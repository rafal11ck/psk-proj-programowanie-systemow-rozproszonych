using password_break_server.Models;

namespace password_break_server.Services;

/// <summary>
/// Cyklicznie sprawdza taski w stanie InProgress i jeśli któryś leci dłużej
/// niż <see cref="PasswordBreakConfig.TaskTimeoutSeconds"/>, zwraca go do
/// kolejki. Chroni przed klientem, który TCP-owo żyje (heartbeaty lecą),
/// ale zaciął się na konkretnym chunk'u.
/// </summary>
public class TaskTimeoutChecker : BackgroundService
{
    private readonly ITaskManager _taskManager;
    private readonly IServerEventListener _events;
    private readonly PasswordBreakConfig _config;
    private readonly ILogger<TaskTimeoutChecker> _logger;

    public TaskTimeoutChecker(
        ITaskManager taskManager,
        IServerEventListener events,
        PasswordBreakConfig config,
        ILogger<TaskTimeoutChecker> logger)
    {
        _taskManager = taskManager;
        _events = events;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_config.TaskTimeoutSeconds <= 0)
        {
            _logger.LogInformation("Task timeout disabled (TaskTimeoutSeconds <= 0)");
            return;
        }

        // sprawdzaj co 1/3 timeout, ale nie częściej niż co 1s i nie rzadziej niż co 30s
        var interval = TimeSpan.FromSeconds(Math.Clamp(_config.TaskTimeoutSeconds / 3.0, 1, 30));

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }

            var requeued = _taskManager.RequeueExpiredTasks(_config.TaskTimeoutSeconds);
            foreach (var taskId in requeued)
                _events.LogTaskRequeued(taskId);
        }
    }
}
