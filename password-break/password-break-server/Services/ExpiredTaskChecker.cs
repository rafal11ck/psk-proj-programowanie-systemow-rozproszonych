using password_break_server.Models;

namespace password_break_server.Services;

public class ExpiredTaskChecker : BackgroundService
{
    private readonly TaskManager _taskManager;
    private readonly IServerEventListener _events;
    private readonly ClientTracker _clientTracker;
    private readonly PasswordBreakConfig _config;

    public ExpiredTaskChecker(
        TaskManager taskManager,
        IServerEventListener events,
        ClientTracker clientTracker,
        PasswordBreakConfig config)
    {
        _taskManager = taskManager;
        _events = events;
        _clientTracker = clientTracker;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_config.HeartbeatTimeoutSeconds / 3.0), stoppingToken);
            var staleClients = _clientTracker.CleanupStaleClients(_config.HeartbeatTimeoutSeconds);

            foreach (var clientId in staleClients)
            {
                _events.ClientDisconnected(clientId);
                var requeued = _taskManager.RequeueClientTasks(clientId);
                foreach (var taskId in requeued)
                    _events.LogTaskRequeued(taskId);
            }
        }
    }
}
