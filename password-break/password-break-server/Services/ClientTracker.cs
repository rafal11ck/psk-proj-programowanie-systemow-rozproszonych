using System.Collections.Concurrent;

namespace password_break_server.Services;

public class ClientTracker : IClientTracker
{
    private readonly ConcurrentDictionary<string, (DateTime LastSeen, string Ip)> _clients = new();

    public void Connect(string clientId, string ip) =>
        _clients[clientId] = (DateTime.UtcNow, ip);

    public void Disconnect(string clientId) =>
        _clients.TryRemove(clientId, out _);

    public void Heartbeat(string clientId)
    {
        if (_clients.TryGetValue(clientId, out var info))
            _clients[clientId] = (DateTime.UtcNow, info.Ip);
    }

    public DateTime? GetLastSeen(string clientId)
        => _clients.TryGetValue(clientId, out var info) ? info.LastSeen : null;

    public List<string> CleanupStaleClients(int timeoutSeconds)
    {
        var stale = new List<string>();
        foreach (var (clientId, info) in _clients)
        {
            if ((DateTime.UtcNow - info.LastSeen).TotalSeconds > timeoutSeconds)
            {
                _clients.TryRemove(clientId, out _);
                stale.Add(clientId);
            }
        }
        return stale;
    }

    public IReadOnlyList<(string Id, string Ip, DateTime LastSeenUtc)> GetClientStates() =>
        _clients.OrderBy(c => c.Key)
            .Select(c => (c.Key, c.Value.Ip, c.Value.LastSeen))
            .ToList();

    public int Count => _clients.Count;
}
