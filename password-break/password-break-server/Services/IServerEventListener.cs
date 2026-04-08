namespace password_break_server.Services;

public interface IServerEventListener
{
    void ClientConnected(string clientId, string ip);
    void ClientDisconnected(string clientId);
    void ClientHeartbeat(string clientId);
    void LogTaskAssigned(string clientId, string taskId, long startIndex, long endIndex);
    void LogTaskCompleted(string taskId);
    void LogTaskRequeued(string taskId);
    void LogFound(string password, string hash);
}
