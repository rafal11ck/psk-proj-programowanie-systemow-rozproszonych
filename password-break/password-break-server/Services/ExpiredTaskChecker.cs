namespace password_break_server.Services;

public class ExpiredTaskChecker : BackgroundService
{
    private readonly TaskManager _taskManager;

    public ExpiredTaskChecker(TaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            _taskManager.CheckExpiredTasks();
        }
    }
}
