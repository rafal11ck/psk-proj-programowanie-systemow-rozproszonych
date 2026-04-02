using Grpc.Core;
using Grpc.Net.Client;
using password_break_server;

namespace password_break_client;

public class GrpcClient
{
    private readonly string _serverUrl;
    private readonly CancellationTokenSource _cts = new();
    private string? _currentTaskId;
    private AsyncDuplexStreamingCall<ClientMessage, ServerMessage>? _currentCall;
    private bool _isConnected;
    private List<string> _wordList = [];
    private bool _isDictionary;
    private string _charSet = "";
    private int _minLength;
    private int _maxLength;
    private int _chunkSize;
    private int _heartbeatIntervalMs = 15000;
    private HashSet<string> _targetHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public GrpcClient(string serverUrl)
    {
        _serverUrl = serverUrl;

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
                Console.WriteLine($"[CLIENT] Connecting to {_serverUrl}...");

                var channel = GrpcChannel.ForAddress(_serverUrl, new GrpcChannelOptions
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
                _currentTaskId = null;

                var heartbeatTask = HeartbeatLoop(_cts.Token);
                var receiverTask = ReceiverLoop(_currentCall, _cts.Token);

                var localTimestamp = GetLocalWordListTimestamp();
                await SendMessageAsync(new ClientMessage
                {
                    Hello = new Hello { WordlistTimestamp = localTimestamp }
                }, _cts.Token);
                Console.WriteLine("[CLIENT] Connected, sending hello... (Ctrl+C to stop)");

                await Task.WhenAny(heartbeatTask, receiverTask);

                _isConnected = false;

                try
                {
                    await _currentCall.RequestStream.CompleteAsync();
                }
                catch (Exception)
                {
                }

                channel.ShutdownAsync().Wait(1000);
            }
            catch (Exception ex) when (!_cts.Token.IsCancellationRequested)
            {
                _isConnected = false;
                _currentCall = null;
                _currentTaskId = null;
                _wordList = [];
                Console.WriteLine($"[CLIENT] Connection lost: {ex.Message}");
                Console.WriteLine($"[CLIENT] Reconnecting in {_heartbeatIntervalMs / 1000} seconds...");

                try
                {
                    await Task.Delay(_heartbeatIntervalMs, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        Console.WriteLine("[CLIENT] Shutdown complete");
    }

    private long GetLocalWordListTimestamp()
    {
        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wordlist.txt");
        if (!File.Exists(localPath))
            return 0;
        
        return new FileInfo(localPath).LastWriteTimeUtc.Ticks;
    }

    private async Task DownloadWordList(string serverUrl)
    {
        try
        {
            var uri = new Uri(serverUrl);
            var wordlistUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}/wordlist";
            
            using var httpClient = new HttpClient();
            var content = await httpClient.GetStringAsync(wordlistUrl);
            
            var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wordlist.txt");
            await File.WriteAllTextAsync(localPath, content);
            
            Console.WriteLine($"[CLIENT] Downloaded wordlist ({content.Split('\n').Length} words)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Could not download wordlist: {ex.Message}");
        }
    }

    private void LoadWordList()
    {
        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wordlist.txt");
        if (!File.Exists(localPath))
        {
            Console.WriteLine($"[WARN] Wordlist not found: {localPath}");
            return;
        }

        _wordList = File.ReadAllLines(localPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .ToList();
        Console.WriteLine($"[CLIENT] Loaded wordlist ({_wordList.Count} words)");
    }

    private async Task ProcessAndSendResult(HashTask task, CancellationToken ct)
    {
        if (_currentCall == null) return;

        var found = _isDictionary
            ? HashWorker.ProcessDictionary(_wordList, task.StartIndex, task.EndIndex, _targetHashes)
            : HashWorker.ProcessBruteForce(_charSet, _minLength, _maxLength, task.StartIndex, task.EndIndex, _targetHashes);

        var result = new Result { TaskId = task.TaskId };
        var count = 0;

        foreach (var (password, hash) in found)
        {
            result.Found.Add(new FoundPassword { Password = password, Hash = hash });
            count++;
        }

        try
        {
            await SendMessageAsync(new ClientMessage { Result = result }, ct);
            Console.WriteLine($"[SENT] Task {task.TaskId}: found {count} password(s)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to send result: {ex.Message}");
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
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to request task: {ex.Message}");
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
                        Heartbeat = new Heartbeat { TaskId = taskId ?? "" }
                    }, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Heartbeat failed: {ex.Message}");
            }
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
                        _chunkSize = config.ChunkSize;
                        _targetHashes = new HashSet<string>(config.TargetHashes, StringComparer.OrdinalIgnoreCase);
                        if (config.HeartbeatIntervalSeconds > 0)
                            _heartbeatIntervalMs = config.HeartbeatIntervalSeconds * 1000;
                        
                        switch (config.AttackConfigCase)
                        {
                            case Config.AttackConfigOneofCase.Dictionary:
                                _isDictionary = true;
                                Console.WriteLine($"[CONFIG] Mode: dictionary, Targets: {config.TargetHashes.Count}");
                                
                                var localTimestamp = GetLocalWordListTimestamp();
                                if (localTimestamp != config.WordlistTimestamp)
                                {
                                    await DownloadWordList(_serverUrl);
                                }
                                LoadWordList();
                                break;
                            
                            case Config.AttackConfigOneofCase.BruteForce:
                                _isDictionary = false;
                                _charSet = config.BruteForce.Charset;
                                _minLength = config.BruteForce.MinLength;
                                _maxLength = config.BruteForce.MaxLength;
                                Console.WriteLine($"[CONFIG] Mode: bruteforce, Charset: {_charSet}, Length: {_minLength}-{_maxLength}, Targets: {config.TargetHashes.Count}");
                                break;
                            
                            default:
                                Console.WriteLine($"[ERROR] Unknown attack config: {config.AttackConfigCase}");
                                break;
                        }
                        
                        await SendMessageAsync(new ClientMessage { Ready = new Ready() }, ct);
                        break;

                    case ServerMessage.MessageOneofCase.HashTask:
                        Console.WriteLine($"[TASK] Received: {message.HashTask.TaskId} (range {message.HashTask.StartIndex}-{message.HashTask.EndIndex})");
                        _currentTaskId = message.HashTask.TaskId;
                        _targetHashes = new HashSet<string>(message.HashTask.TargetHashes, StringComparer.OrdinalIgnoreCase);
                        await ProcessAndSendResult(message.HashTask, ct);
                        break;

                    case ServerMessage.MessageOneofCase.Ack:
                        Console.WriteLine($"[ACK] {message.Ack.Message}");
                        _currentTaskId = null;
                        await RequestTaskAfterDelay(ct);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Receiver: {ex.Message}");
        }
    }
}