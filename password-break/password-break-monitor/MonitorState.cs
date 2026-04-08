namespace password_break_monitor;

public record ClientView(string ClientId, string Ip, int SecondsAgo);
public record TaskView(string TaskId, string ClientId, long StartIndex, long EndIndex, int ElapsedSeconds);

public class MonitorStateSnapshot
{
    public int CompletedTasks { get; init; }
    public int TotalTasks { get; init; }
    public int PendingTasks { get; init; }
    public int FoundCount { get; init; }
    public int RemainingCount { get; init; }
    public bool AllFound { get; init; }
    public bool Saved { get; init; }
    public string AttackMode { get; init; } = "";
    public List<ClientView> Clients { get; init; } = [];
    public List<TaskView> ActiveTasks { get; init; } = [];
    public List<string> LogLines { get; init; } = [];
    public bool Connected { get; init; }

    public int TargetTotal => Math.Max(1, FoundCount + RemainingCount);
    public int InProgress => TotalTasks - CompletedTasks - PendingTasks;
    public float TaskFraction => TotalTasks > 0 ? (float)CompletedTasks / TotalTasks : 0f;
    public float FoundFraction => TargetTotal > 0 ? (float)FoundCount / TargetTotal : 0f;
}

internal class ClientEntry
{
    public string Ip = "";
    public DateTime LastSeenUtc;
}

internal class TaskEntry
{
    public string ClientId = "";
    public long StartIndex;
    public long EndIndex;
    public DateTime StartedAtUtc;
}

public class MonitorState
{
    private readonly Lock _lock = new();
    private int _completedTasks;
    private int _totalTasks;
    private int _pendingTasks;
    private int _foundCount;
    private int _remainingCount;
    private bool _allFound;
    private bool _saved;
    private string _attackMode = "";
    private readonly Dictionary<string, ClientEntry> _clients = new();
    private readonly Dictionary<string, TaskEntry> _tasks = new();
    private readonly List<string> _logLines = [];
    private bool _connected;

    private const int MaxLogLines = 500;

    public void SetConnected(bool connected)
    {
        lock (_lock) _connected = connected;
    }

    private static DateTime FromUnixMs(long ms)
        => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

    public void ApplySnapshot(StateSnapshot snapshot)
    {
        lock (_lock)
        {
            _completedTasks = snapshot.CompletedTasks;
            _totalTasks = snapshot.TotalTasks;
            _pendingTasks = snapshot.PendingTasks;
            _foundCount = snapshot.FoundCount;
            _remainingCount = snapshot.RemainingCount;
            _allFound = snapshot.AllFound;
            _saved = snapshot.Saved;
            _attackMode = snapshot.AttackMode;

            _clients.Clear();
            foreach (var c in snapshot.Clients)
            {
                _clients[c.ClientId] = new ClientEntry
                {
                    Ip = c.Ip,
                    LastSeenUtc = FromUnixMs(c.LastSeenUnixMs)
                };
            }

            _tasks.Clear();
            foreach (var t in snapshot.ActiveTasks)
            {
                _tasks[t.TaskId] = new TaskEntry
                {
                    ClientId = t.ClientId,
                    StartIndex = t.StartIndex,
                    EndIndex = t.EndIndex,
                    StartedAtUtc = FromUnixMs(t.StartedAtUnixMs)
                };
            }
        }
    }

    public void OnClientConnected(string clientId, string ip, long lastSeenUnixMs)
    {
        lock (_lock)
            _clients[clientId] = new ClientEntry { Ip = ip, LastSeenUtc = FromUnixMs(lastSeenUnixMs) };
    }

    public void OnClientDisconnected(string clientId)
    {
        lock (_lock) _clients.Remove(clientId);
    }

    public void OnClientHeartbeat(string clientId, long lastSeenUnixMs)
    {
        lock (_lock)
        {
            if (_clients.TryGetValue(clientId, out var c))
                c.LastSeenUtc = FromUnixMs(lastSeenUnixMs);
        }
    }

    public void OnTaskAssigned(string taskId, string clientId, long startIndex, long endIndex, long startedAtUnixMs)
    {
        lock (_lock)
        {
            _tasks[taskId] = new TaskEntry
            {
                ClientId = clientId,
                StartIndex = startIndex,
                EndIndex = endIndex,
                StartedAtUtc = FromUnixMs(startedAtUnixMs)
            };
            if (_pendingTasks > 0) _pendingTasks--;
        }
    }

    public void OnTaskCompleted(string taskId)
    {
        lock (_lock)
        {
            if (_tasks.Remove(taskId))
                _completedTasks++;
        }
    }

    public void OnTaskRequeued(string taskId)
    {
        lock (_lock)
        {
            if (_tasks.Remove(taskId))
                _pendingTasks++;
        }
    }

    public void OnPasswordFound()
    {
        lock (_lock)
        {
            _foundCount++;
            if (_remainingCount > 0) _remainingCount--;
            _allFound = _remainingCount == 0;
        }
    }

    public void AddLog(string message)
    {
        lock (_lock)
        {
            _logLines.Add($"{DateTime.Now:HH:mm:ss} {message}");
            while (_logLines.Count > MaxLogLines)
                _logLines.RemoveAt(0);
        }
    }

    public MonitorStateSnapshot GetSnapshot()
    {
        var now = DateTime.UtcNow;
        lock (_lock)
        {
            var clients = _clients
                .OrderBy(kv => kv.Key)
                .Select(kv => new ClientView(
                    kv.Key,
                    kv.Value.Ip,
                    (int)(now - kv.Value.LastSeenUtc).TotalSeconds))
                .ToList();

            var tasks = _tasks
                .OrderBy(kv => kv.Value.ClientId)
                .Select(kv => new TaskView(
                    kv.Key,
                    kv.Value.ClientId,
                    kv.Value.StartIndex,
                    kv.Value.EndIndex,
                    (int)(now - kv.Value.StartedAtUtc).TotalSeconds))
                .ToList();

            return new MonitorStateSnapshot
            {
                CompletedTasks = _completedTasks,
                TotalTasks = _totalTasks,
                PendingTasks = _pendingTasks,
                FoundCount = _foundCount,
                RemainingCount = _remainingCount,
                AllFound = _allFound,
                Saved = _saved,
                AttackMode = _attackMode,
                Clients = clients,
                ActiveTasks = tasks,
                LogLines = [.. _logLines],
                Connected = _connected
            };
        }
    }
}
