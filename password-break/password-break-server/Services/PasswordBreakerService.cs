using Grpc.Core;
using password_break_server.Models;

namespace password_break_server.Services;

public class PasswordBreakerService : PasswordBreaker.PasswordBreakerBase
{
    private readonly TaskManager _taskManager;
    private readonly FoundPasswords _foundPasswords;
    private readonly PasswordBreakConfig _config;
    private readonly IServerEventListener _events;
    private readonly ClientTracker _clientTracker;
    private readonly ServerRunState _runState;
    private readonly ExperimentRunManager _runManager;
    private readonly object _metricsLock = new();
    private readonly string _serverMachineName = Environment.MachineName;

    public PasswordBreakerService(
        TaskManager taskManager,
        FoundPasswords foundPasswords,
        PasswordBreakConfig config,
        IServerEventListener events,
        ClientTracker clientTracker,
        ServerRunState runState,
        ExperimentRunManager runManager)
    {
        _taskManager = taskManager;
        _foundPasswords = foundPasswords;
        _config = config;
        _events = events;
        _clientTracker = clientTracker;
        _runState = runState;
        _runManager = runManager;
    }

    public override async Task Connect(
        IAsyncStreamReader<ClientMessage> requestStream,
        IServerStreamWriter<ServerMessage> responseStream,
        ServerCallContext context)
    {
        var clientId = Guid.NewGuid().ToString("N")[..8];
        var clientIp = context.Peer ?? "unknown";
        var currentTaskId = (string?)null;

        _clientTracker.Connect(clientId, clientIp);
        _events.ClientConnected(clientId, clientIp);

        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var watchdog = StartHeartbeatWatchdog(clientId, streamCts);

        try
        {
            await foreach (var message in requestStream.ReadAllAsync(streamCts.Token))
            {
                currentTaskId = await HandleMessage(message, responseStream, clientId, currentTaskId);
            }
        }
        catch (IOException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            streamCts.Cancel();

            try
            {
                await watchdog;
            }
            catch
            {
            }

            await Cleanup(clientId, currentTaskId);
        }
    }

    private Task StartHeartbeatWatchdog(string clientId, CancellationTokenSource streamCts)
    {
        var timeoutSeconds = _config.HeartbeatTimeoutSeconds;

        if (timeoutSeconds <= 0)
            return Task.CompletedTask;

        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds / 4.0));

        return Task.Run(async () =>
        {
            while (!streamCts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(pollInterval, streamCts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var lastSeen = _clientTracker.GetLastSeen(clientId);

                if (lastSeen == null)
                    return;

                var ago = (DateTime.UtcNow - lastSeen.Value).TotalSeconds;

                if (ago > timeoutSeconds)
                {
                    streamCts.Cancel();
                    return;
                }
            }
        });
    }

    private async Task<string?> HandleMessage(
        ClientMessage message,
        IServerStreamWriter<ServerMessage> responseStream,
        string clientId,
        string? currentTaskId)
    {
        switch (message.MessageCase)
        {
            case ClientMessage.MessageOneofCase.Hello:
                await HandleHello(responseStream);
                return currentTaskId;

            case ClientMessage.MessageOneofCase.Ready:
                return await SendTask(responseStream, clientId);

            case ClientMessage.MessageOneofCase.Result:
                return await HandleResult(message.Result, responseStream, clientId);

            case ClientMessage.MessageOneofCase.Heartbeat:
                HandleHeartbeat(clientId);
                return currentTaskId;

            default:
                return currentTaskId;
        }
    }

    private async Task<string?> HandleHello(IServerStreamWriter<ServerMessage> responseStream)
    {
        var config = new Config
        {
            ChunkSize = _config.ChunkSize,
            WordlistTimestamp = _taskManager.GetWordListTimestamp(),
            HeartbeatIntervalSeconds = _config.HeartbeatIntervalSeconds
        };

        config.TargetHashes.AddRange(_config.TargetHashes);

        switch (_config.AttackMode)
        {
            case "dictionary":
                config.Dictionary = new DictionaryConfig { WordlistPath = _config.WordListPath ?? "" };
                break;

            default:
                config.BruteForce = new BruteForceConfig
                {
                    Charset = _config.CharSet,
                    MinLength = _config.MinLength,
                    MaxLength = _config.MaxLength
                };
                break;
        }

        await responseStream.WriteAsync(new ServerMessage { Config = config });
        return null;
    }

    private async Task<string?> SendTask(IServerStreamWriter<ServerMessage> responseStream, string clientId)
    {
        if (!_runState.IsRunning)
        {
            await responseStream.WriteAsync(new ServerMessage
            {
                Ack = new Ack { Message = "Waiting for server start" }
            });

            return null;
        }

        var run = _runManager.Current;

        if (run == null || !run.IsOpen)
        {
            await responseStream.WriteAsync(new ServerMessage
            {
                Ack = new Ack { Message = "Run is not active" }
            });

            return null;
        }

        var task = _taskManager.GetNextTask(clientId, run.Sequence);

        if (task == null)
        {
            await responseStream.WriteAsync(new ServerMessage
            {
                Ack = new Ack { Message = "No more tasks" }
            });

            return null;
        }

        var hashTask = new HashTask
        {
            TaskId = task.TaskId,
            StartIndex = task.StartIndex,
            EndIndex = task.EndIndex
        };

        hashTask.TargetHashes.AddRange(_taskManager.TargetHashes);

        task.SentAtUtc = DateTime.UtcNow;

        await responseStream.WriteAsync(new ServerMessage { HashTask = hashTask });

        _events.LogTaskAssigned(clientId, task.TaskId, task.StartIndex, task.EndIndex);

        return task.TaskId;
    }

    private async Task<string?> HandleResult(
        Result result,
        IServerStreamWriter<ServerMessage> responseStream,
        string clientId)
    {
        var task = _taskManager.GetTask(result.TaskId);
        var t4Utc = DateTime.UtcNow;
        var run = _runManager.Current;

        if (task == null || run == null || !run.IsOpen || task.RunSequence != run.Sequence)
        {
            await responseStream.WriteAsync(new ServerMessage
            {
                Ack = new Ack { Message = "Result ignored - run is not active" }
            });

            return null;
        }

        SaveMetrics(task, clientId, result, t4Utc, run);

        StoreResults(result.Found);

        if (result.Found.Count > 0)
        {
            _foundPasswords.SaveToFile(run.ResultsFilePath);
        }

        _taskManager.MarkCompleted(result.TaskId);
        _events.LogTaskCompleted(result.TaskId);

        if (_foundPasswords.AllFound)
        {
            _foundPasswords.SaveToFile(run.ResultsFilePath);

            await responseStream.WriteAsync(new ServerMessage
            {
                Ack = new Ack { Message = "All passwords found" }
            });

            return null;
        }

        return await SendTask(responseStream, clientId);
    }

    private void StoreResults(IEnumerable<FoundPassword> found)
    {
        var entries = found.Select(f => (f.Password, f.Hash));
        _foundPasswords.StoreFound(entries);

        foreach (var f in found)
            _events.LogFound(f.Password, f.Hash);
    }

    private void HandleHeartbeat(string clientId)
    {
        _clientTracker.Heartbeat(clientId);
        _events.ClientHeartbeat(clientId);
    }

    private Task Cleanup(string clientId, string? currentTaskId)
    {
        _clientTracker.Disconnect(clientId);
        _events.ClientDisconnected(clientId);

        if (currentTaskId != null)
        {
            _taskManager.MarkPending(currentTaskId);
            _events.LogTaskRequeued(currentTaskId);
        }

        return Task.CompletedTask;
    }

    private void SaveMetrics(TaskInfo task, string clientId, Result result, DateTime t4Utc, ExperimentRun run)
    {
        var totalTimeMs = Math.Max(1, (long)(t4Utc - task.SentAtUtc).TotalMilliseconds);
        var computeTimeMs = Math.Max(1, result.ComputeTimeMs);
        var communicationTimeMs = Math.Max(0, totalTimeMs - computeTimeMs);

        var candidatesCount = task.EndIndex - task.StartIndex + 1;
        var throughput = candidatesCount / (computeTimeMs / 1000.0);
        var connectedClientsAtResult = _clientTracker.Count;

        var line = string.Join(",",
            run.RunId,
            run.ExperimentName,
            run.Sequence,
            run.StartedAtUtc.ToString("O"),
            run.PausedAtUtc?.ToString("O") ?? "",
            _serverMachineName,
            _config.AttackMode,
            _config.ChunkSize,
            _config.MinLength,
            _config.MaxLength,
            _config.CharSet.Length,
            _config.TargetHashes.Count,
            _config.ClientsCount,
            run.ConnectedClientsAtStart,
            connectedClientsAtResult,
            _config.ClientThreads,
            task.TaskId,
            clientId,
            task.StartIndex,
            task.EndIndex,
            candidatesCount,
            computeTimeMs,
            communicationTimeMs,
            totalTimeMs,
            throughput.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            result.Found.Count,
            task.SentAtUtc.ToString("O"),
            t4Utc.ToString("O"));

        lock (_metricsLock)
        {
            File.AppendAllText(run.MetricsFilePath, line + Environment.NewLine);
        }
    }
}
