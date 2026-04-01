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
                    Credentials = ChannelCredentials.Insecure
                });

                var client = new PasswordBreaker.PasswordBreakerClient(channel);
                _currentCall = client.Connect();
                _isConnected = true;

                var heartbeatTask = HeartbeatLoop(_cts.Token);
                var receiverTask = ReceiverLoop(_currentCall, _cts.Token);

                await _currentCall.RequestStream.WriteAsync(new ClientMessage { Ready = new Ready() });
                Console.WriteLine("[CLIENT] Connected, waiting for tasks... (Ctrl+C to stop)");

                await Task.WhenAny(heartbeatTask, receiverTask);

                _isConnected = false;

                try
                {
                    await _currentCall.RequestStream.CompleteAsync();
                }
                catch (Exception)
                {
                    // Stream may already be closed
                }

                channel.ShutdownAsync().Wait(1000);
            }
            catch (Exception ex) when (!_cts.Token.IsCancellationRequested)
            {
                _isConnected = false;
                _currentCall = null;
                Console.WriteLine($"[CLIENT] Connection lost: {ex.Message}");
                Console.WriteLine("[CLIENT] Reconnecting in 5 seconds...");

                try
                {
                    await Task.Delay(5000, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        Console.WriteLine("[CLIENT] Shutdown complete");
    }

    private async Task ProcessAndSendResult(HashTask task, CancellationToken ct)
    {
        if (_currentCall == null) return;

        var hashes = task.DataCase switch
        {
            HashTask.DataOneofCase.BruteForce =>
                HashWorker.ProcessBruteForce(
                    task.BruteForce.Charset,
                    task.BruteForce.Length,
                    task.BruteForce.StartIndex,
                    task.BruteForce.EndIndex),

            HashTask.DataOneofCase.Words =>
                HashWorker.ProcessWords(task.Words.Words),
            _ => []
        };

        var result = new Result { TaskId = task.TaskId };
        var count = 0;

        foreach (var (password, hash) in hashes)
        {
            result.Hashes.Add(new PasswordHash { Password = password, Hash = hash });
            count++;
        }

        try
        {
            await _currentCall.RequestStream.WriteAsync(new ClientMessage { Result = result }, ct);
            Console.WriteLine($"[SENT] {count} hashes for task {task.TaskId}");
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
            Console.WriteLine("[CLIENT] No tasks available, retrying in 10 seconds...");
            await Task.Delay(10000, ct);
            if (_currentCall != null && _isConnected)
            {
                await _currentCall.RequestStream.WriteAsync(new ClientMessage { Ready = new Ready() }, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to request task: {ex.Message}");
        }
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(5000, ct);
                if (_currentTaskId != null && _currentCall != null && _isConnected)
                {
                    await _currentCall.RequestStream.WriteAsync(new ClientMessage
                    {
                        Heartbeat = new Heartbeat { TaskId = _currentTaskId }
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
                    case ServerMessage.MessageOneofCase.HashTask:
                        Console.WriteLine($"[TASK] Received: {message.HashTask.TaskId}");
                        _currentTaskId = message.HashTask.TaskId;
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
