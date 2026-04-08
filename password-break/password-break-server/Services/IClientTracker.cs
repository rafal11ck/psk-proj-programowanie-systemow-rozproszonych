namespace password_break_server.Services;

public interface IClientTracker
{
    void Connect(string clientId, string ip);
    void Disconnect(string clientId);
    void Heartbeat(string clientId);
    DateTime? GetLastSeen(string clientId);
    List<string> CleanupStaleClients(int timeoutSeconds);
    IReadOnlyList<(string Id, string Ip, DateTime LastSeenUtc)> GetClientStates();
    int Count { get; }
}
