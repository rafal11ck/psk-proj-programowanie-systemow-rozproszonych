using Grpc.Core;
using password_break_server.Models;

namespace password_break_server.Services;

public class PasswordBreakerService : PasswordBreaker.PasswordBreakerBase
{
    private readonly TaskManager _taskManager;
    private readonly FoundPasswords _foundPasswords;
    private readonly PasswordBreakConfig _config;
    private readonly IServerEventListener _events;

    public PasswordBreakerService(
        TaskManager taskManager,
        FoundPasswords foundPasswords,
        PasswordBreakConfig config,
        IServerEventListener events)
    {
        _taskManager = taskManager;
        _foundPasswords = foundPasswords;
        _config = config;
        _events = events;
    }

    public override async Task Connect(
        IAsyncStreamReader<ClientMessage> requestStream,
        IServerStreamWriter<ServerMessage> responseStream,
        ServerCallContext context)
    {
        var clientId = Guid.NewGuid().ToString("N")[..8];
        var clientIp = context.Peer ?? "unknown";
        var currentTaskId = (string?)null;

        _events.ClientConnected(clientId, clientIp);

        try
        {
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                currentTaskId = await HandleMessage(message, responseStream, clientId, currentTaskId);
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
        finally
        {
            await Cleanup(clientId, currentTaskId);
        }
    }

    private async Task<string?> HandleMessage(
        ClientMessage message,
        IServerStreamWriter<ServerMessage> responseStream,
        string clientId,
        string? currentTaskId)
    {
        return message.MessageCase switch
        {
            ClientMessage.MessageOneofCase.Hello => await HandleHello(responseStream),
            ClientMessage.MessageOneofCase.Ready => await SendTask(responseStream, clientId),
            ClientMessage.MessageOneofCase.Result => await HandleResult(message.Result, responseStream, clientId),
            ClientMessage.MessageOneofCase.Heartbeat => HandleHeartbeat(message.Heartbeat, clientId),
            _ => currentTaskId
        };
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
        var task = _taskManager.GetNextTask(clientId);
        
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

        await responseStream.WriteAsync(new ServerMessage { HashTask = hashTask });
        _events.LogTaskAssigned(clientId, task.TaskId);
        
        return task.TaskId;
    }

    private async Task<string?> HandleResult(
        Result result,
        IServerStreamWriter<ServerMessage> responseStream,
        string clientId)
    {
        StoreResults(result.Found);
        _taskManager.MarkCompleted(result.TaskId);
        _events.LogTaskCompleted(result.TaskId);

        if (_foundPasswords.AllFound)
        {
            if (!_foundPasswords.Saved)
                _foundPasswords.SaveToFile("results.csv");

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

    private string? HandleHeartbeat(Heartbeat heartbeat, string clientId)
    {
        _events.ClientHeartbeat(clientId);
        return null;
    }

    private Task Cleanup(string clientId, string? currentTaskId)
    {
        _events.ClientDisconnected(clientId);

        if (currentTaskId != null)
        {
            _taskManager.MarkPending(currentTaskId);
            _events.LogTaskRequeued(currentTaskId);
        }

        return Task.CompletedTask;
    }
}