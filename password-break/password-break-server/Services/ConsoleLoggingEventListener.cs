namespace password_break_server.Services;

public class ConsoleLoggingEventListener : IServerEventListener
{
    private readonly ILogger<ConsoleLoggingEventListener> _logger;

    public ConsoleLoggingEventListener(ILogger<ConsoleLoggingEventListener> logger)
    {
        _logger = logger;
    }

    public void ClientConnected(string clientId, string ip)
        => _logger.LogInformation("Client {ClientId} ({Ip}) connected", clientId, ip);

    public void ClientDisconnected(string clientId)
        => _logger.LogInformation("Client {ClientId} disconnected", clientId);

    public void LogTaskAssigned(string clientId, string taskId, long startIndex, long endIndex)
        => _logger.LogInformation("Task {TaskId} assigned to {ClientId}", taskId, clientId);

    public void LogTaskCompleted(string taskId)
        => _logger.LogInformation("Task {TaskId} completed", taskId);

    public void LogTaskRequeued(string taskId)
        => _logger.LogWarning("Task {TaskId} requeued", taskId);

    public void LogFound(string password, string hash)
        => _logger.LogInformation("Found password: {Password} -> {Hash}", password, hash[..Math.Min(16, hash.Length)]);
}
