using Grpc.Core;
using password_break_server.Models;

namespace password_break_server.Services;

public class PasswordBreakerService : PasswordBreaker.PasswordBreakerBase
{
    private readonly TaskManager _taskManager;
    private readonly HashStorage _hashStorage;
    private readonly ILogger<PasswordBreakerService> _logger;

    public PasswordBreakerService(TaskManager taskManager, HashStorage hashStorage, ILogger<PasswordBreakerService> logger)
    {
        _taskManager = taskManager;
        _hashStorage = hashStorage;
        _logger = logger;
    }

    public override async Task Connect(
        IAsyncStreamReader<ClientMessage> requestStream,
        IServerStreamWriter<ServerMessage> responseStream,
        ServerCallContext context)
    {
        var clientId = Guid.NewGuid().ToString("N")[..8];
        string? currentTaskId = null;

        _logger.LogInformation("[{ClientId}] Client connected", clientId);

        try
        {
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                switch (message.MessageCase)
                {
                    case ClientMessage.MessageOneofCase.Ready:
                        currentTaskId = await SendNextTask(responseStream, clientId);
                        break;

                    case ClientMessage.MessageOneofCase.Result:
                        var hashCount = message.Result.Hashes.Count;
                        _hashStorage.StoreBatch(message.Result.Hashes.Select(h => (h.Password, h.Hash)));
                        _taskManager.MarkCompleted(message.Result.TaskId);
                        var (completed, total) = _taskManager.GetProgress();
                        _logger.LogInformation(
                            "[{ClientId}] Task {TaskId} completed: {HashCount} hashes | Progress: {Completed}/{Total} tasks ({Percent}%) | Total hashes stored: {StoredCount}",
                            clientId, message.Result.TaskId, hashCount, completed, total,
                            total > 0 ? completed * 100 / total : 0, _hashStorage.Count);
                        currentTaskId = await SendNextTask(responseStream, clientId);
                        break;

                    case ClientMessage.MessageOneofCase.Heartbeat:
                        _taskManager.TouchTask(message.Heartbeat.TaskId);
                        break;
                }
            }
        }
        finally
        {
            if (currentTaskId != null)
            {
                _logger.LogWarning("[{ClientId}] Disconnected with task {TaskId} in progress, re-queuing", clientId, currentTaskId);
                _taskManager.MarkPending(currentTaskId);
            }
            else
            {
                _logger.LogInformation("[{ClientId}] Client disconnected", clientId);
            }
        }
    }

    private async Task<string?> SendNextTask(IServerStreamWriter<ServerMessage> responseStream, string clientId)
    {
        _taskManager.CheckExpiredTasks();
        var task = _taskManager.GetNextTask(clientId);

        if (task == null)
        {
            _hashStorage.SaveToFile("results.csv");
            _logger.LogInformation("[{ClientId}] No more tasks, results saved to results.csv ({Count} hashes)", clientId, _hashStorage.Count);
            await responseStream.WriteAsync(new ServerMessage
            {
                Ack = new Ack { Message = "No more tasks" }
            });
            return null;
        }

        var hashTask = new HashTask { TaskId = task.TaskId };

        if (task.TaskData is List<string> words)
        {
            hashTask.Words = new WordList();
            hashTask.Words.Words.AddRange(words);
        }
        else if (task.TaskData is BruteForceTaskData bfData)
        {
            hashTask.BruteForce = new BruteForceRange
            {
                Charset = bfData.CharSet,
                Length = bfData.Length,
                StartIndex = bfData.StartIndex,
                EndIndex = bfData.EndIndex
            };
        }

        await responseStream.WriteAsync(new ServerMessage { HashTask = hashTask });
        return task.TaskId;
    }
}
