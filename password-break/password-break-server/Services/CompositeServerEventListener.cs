namespace password_break_server.Services;

public class CompositeServerEventListener : IServerEventListener
{
    private readonly IServerEventListener[] _listeners;
    private readonly ILogger<CompositeServerEventListener> _logger;

    public CompositeServerEventListener(
        IEnumerable<IServerEventListener> listeners,
        ILogger<CompositeServerEventListener> logger)
    {
        _listeners = listeners.ToArray();
        _logger = logger;
    }

    private void Dispatch(Action<IServerEventListener> action)
    {
        foreach (var l in _listeners)
        {
            try { action(l); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Listener {Listener} threw", l.GetType().Name);
            }
        }
    }

    public void ClientConnected(string clientId, string ip)
        => Dispatch(l => l.ClientConnected(clientId, ip));

    public void ClientDisconnected(string clientId)
        => Dispatch(l => l.ClientDisconnected(clientId));

    public void LogTaskAssigned(string clientId, string taskId, long startIndex, long endIndex)
        => Dispatch(l => l.LogTaskAssigned(clientId, taskId, startIndex, endIndex));

    public void LogTaskCompleted(string taskId)
        => Dispatch(l => l.LogTaskCompleted(taskId));

    public void LogTaskRequeued(string taskId)
        => Dispatch(l => l.LogTaskRequeued(taskId));

    public void LogFound(string password, string hash)
        => Dispatch(l => l.LogFound(password, hash));
}
