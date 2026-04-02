namespace password_break_server.Services;

public interface IClientTracker
{
    void Connect(string clientId, string ip);
    void Disconnect(string clientId);
    void Heartbeat(string clientId);
    List<string> CleanupStaleClients(int timeoutSeconds);
    IReadOnlyList<(string Id, string Ip, int Ago, int Timeout)> GetClientStates(int heartbeatTimeout);
    int Count { get; }
}
