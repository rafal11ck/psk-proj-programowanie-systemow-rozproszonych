using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace password_break_client;

public class GrpcClient : IAsyncDisposable
{
    private readonly string _serverUrl;
    private readonly IWordlistManager _wordlistManager;
    private readonly ILogger<GrpcClient> _logger;
    private readonly int? _maxDegreeOfParallelism;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string? _currentTaskId;
    private volatile bool _isConnected;
    private volatile int _heartbeatIntervalMs = 15000;
    private AsyncDuplexStreamingCall<ClientMessage, ServerMessage>? _currentCall;
    private IClientAttackStrategy? _attackStrategy;
    private HashSet<string> _targetHashes = new(StringComparer.OrdinalIgnoreCase);

    public GrpcClient(
        string serverUrl,
        IWordlistManager wordlistManager,
        ILogger<GrpcClient> logger,
        int? maxDegreeOfParallelism = null)
    {
        _serverUrl = serverUrl;
        _wordlistManager = wordlistManager;
        _logger = logger;
        _maxDegreeOfParallelism = maxDegreeOfParallelism;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _cts.Cancel();
        };
    }

    public async Task RunAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await ConnectAndProcess();
            }
            catch (Exception ex) when (!_cts.Token.IsCancellationRequested)
            {
                _logger.LogWarning("Connection lost: {Message}", ex.Message);
            }
            finally
            {
                ResetState();
            }

            if (_cts.Token.IsCancellationRequested) break;

            _logger.LogInformation("Reconnecting in {Seconds}s...", _heartbeatIntervalMs / 1000);
            try { await Task.Delay(_heartbeatIntervalMs, _cts.Token); }
            catch (OperationCanceledException) { }
        }

        _logger.LogInformation("Shutdown complete");
    }

    private async Task ConnectAndProcess()
    {
        _logger.LogInformation("Connecting to {ServerUrl}...", _serverUrl);

        using var channel = GrpcChannel.ForAddress(_serverUrl, new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            HttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                ConnectTimeout = TimeSpan.FromSeconds(10),
            }
        });

        var client = new PasswordBreaker.PasswordBreakerClient(channel);
        _currentCall = client.Connect();
        _isConnected = true;

        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var ct = connectionCts.Token;

        var heartbeatTask = HeartbeatLoop(ct);
        var receiverTask = ReceiverLoop(_currentCall, ct);

        try
        {
            var localTimestamp = _wordlistManager.GetLocalTimestamp();
            await SendMessageAsync(new ClientMessage
            {
                Hello = new Hello { WordlistTimestamp = localTimestamp }
            }, ct);

            _logger.LogInformation("Connected, sending hello... (Ctrl+C to stop)");

            var completed = await Task.WhenAny(heartbeatTask, receiverTask);

            if (completed == receiverTask)
                await receiverTask;

            if (completed == heartbeatTask)
                await heartbeatTask;
        }
        finally
        {
            _isConnected = false;
            await connectionCts.CancelAsync();

            try { await _currentCall.RequestStream.CompleteAsync(); } catch { }
        }
    }

    private void ResetState()
    {
        _isConnected = false;
        _currentCall = null;
        _attackStrategy = null;
        _currentTaskId = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _cts.Dispose();
        _writeLock.Dispose();
    }

    private async Task ProcessAndSendResult(HashTask task, CancellationToken ct)
    {
        if (_currentCall == null || _attackStrategy == null) return;

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var found = _attackStrategy.Process(task.StartIndex, task.EndIndex, _targetHashes, ct);
            stopwatch.Stop();

            ct.ThrowIfCancellationRequested();

            var result = new Result
            {
                TaskId = task.TaskId,
                ComputeTimeMs = stopwatch.ElapsedMilliseconds
            };

            foreach (var (password, hash) in found)
            {
                result.Found.Add(new FoundPassword
                {
                    Password = password,
                    Hash = hash
                });
            }

            await SendMessageAsync(new ClientMessage { Result = result }, ct);
            _currentTaskId = null;
            Console.WriteLine($"[SENT] Task {task.TaskId}: found {found.Count} password(s)");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[CANCELLED] Task {task.TaskId} interrupted - result not sent");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process/send result for task {TaskId}", task.TaskId);
            throw;
        }
    }

    private async Task RequestTaskAfterDelay(CancellationToken ct)
    {
        try
        {
            Console.WriteLine("[CLIENT] Waiting for next task...");
            await Task.Delay(_heartbeatIntervalMs, ct);
            await SendMessageAsync(new ClientMessage { Ready = new Ready() }, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request next task");
            throw;
        }
    }

    private async Task SendMessageAsync(ClientMessage message, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            if (_currentCall != null && _isConnected)
                await _currentCall.RequestStream.WriteAsync(message, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_heartbeatIntervalMs, ct);

                if (_currentCall != null && _isConnected)
                {
                    var taskId = _currentTaskId;
                    if (taskId != null)
                    {
                        Console.WriteLine($"[HEARTBEAT] Task {taskId} (interval: {_heartbeatIntervalMs}ms)");
                    }

                    await SendMessageAsync(new ClientMessage
                    {
                        Heartbeat = new Heartbeat()
                    }, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Heartbeat failed: {Message}", ex.Message);
            }
        }
    }

    private async Task<IClientAttackStrategy> CreateStrategy(Config config)
    {
        switch (config.AttackConfigCase)
        {
            case Config.AttackConfigOneofCase.Dictionary:
                _logger.LogInformation("Mode: dictionary, Targets: {Count}", config.TargetHashes.Count);

                var localTimestamp = _wordlistManager.GetLocalTimestamp();
                if (localTimestamp != config.WordlistTimestamp)
                {
                    try
                    {
                        await _wordlistManager.DownloadAsync(_serverUrl);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Could not download wordlist: {Message}", ex.Message);
                    }
                }

                var wordList = _wordlistManager.Load();
                return new DictionaryClientStrategy(wordList, _maxDegreeOfParallelism);

            case Config.AttackConfigOneofCase.BruteForce:
                _logger.LogInformation(
                    "Mode: bruteforce, Charset: {Charset}, Length: {Min}-{Max}, Targets: {Count}",
                    config.BruteForce.Charset,
                    config.BruteForce.MinLength,
                    config.BruteForce.MaxLength,
                    config.TargetHashes.Count);

                return new BruteForceClientStrategy(
                    config.BruteForce.Charset,
                    config.BruteForce.MinLength,
                    config.BruteForce.MaxLength,
                    _maxDegreeOfParallelism);
            default:
                throw new InvalidOperationException($"Unknown attack config: {config.AttackConfigCase}");
        }
    }

    private async Task ReceiverLoop(AsyncDuplexStreamingCall<ClientMessage, ServerMessage> call, CancellationToken ct)
    {
        try
        {
            await foreach (var message in call.ResponseStream.ReadAllAsync(ct))
            {
                switch (message.MessageCase)
                {
                    case ServerMessage.MessageOneofCase.Config:
                        var config = message.Config;
                        _targetHashes = new HashSet<string>(config.TargetHashes, StringComparer.OrdinalIgnoreCase);

                        if (config.HeartbeatIntervalSeconds > 0)
                            _heartbeatIntervalMs = config.HeartbeatIntervalSeconds * 1000;

                        _attackStrategy = await CreateStrategy(config);
                        await SendMessageAsync(new ClientMessage { Ready = new Ready() }, ct);
                        break;

                    case ServerMessage.MessageOneofCase.HashTask:
                        Console.WriteLine($"[TASK] Received: {message.HashTask.TaskId} (range {message.HashTask.StartIndex}-{message.HashTask.EndIndex})");
                        _currentTaskId = message.HashTask.TaskId;
                        _targetHashes = new HashSet<string>(message.HashTask.TargetHashes, StringComparer.OrdinalIgnoreCase);
                        await ProcessAndSendResult(message.HashTask, ct);
                        break;

                    case ServerMessage.MessageOneofCase.Ack:
                        _logger.LogInformation("Ack: {Message}", message.Ack.Message);
                        await RequestTaskAfterDelay(ct);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Receiver loop failed");
            throw;
        }
    }
}
