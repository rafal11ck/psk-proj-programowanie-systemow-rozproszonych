namespace password_break_monitor;

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
    public List<ClientInfo> Clients { get; init; } = [];
    public List<ActiveTask> ActiveTasks { get; init; } = [];
    public List<string> LogLines { get; init; } = [];
    public bool Connected { get; init; }

    public int TargetTotal => Math.Max(1, FoundCount + RemainingCount);
    public int InProgress => TotalTasks - CompletedTasks - PendingTasks;
    public float TaskFraction => TotalTasks > 0 ? (float)CompletedTasks / TotalTasks : 0f;
    public float FoundFraction => TargetTotal > 0 ? (float)FoundCount / TargetTotal : 0f;
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
    private List<ClientInfo> _clients = [];
    private List<ActiveTask> _activeTasks = [];
    private readonly List<string> _logLines = [];
    private bool _connected;

    private const int MaxLogLines = 500;

    public void SetConnected(bool connected)
    {
        lock (_lock) _connected = connected;
    }

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
            _clients = [.. snapshot.Clients];
            _activeTasks = [.. snapshot.ActiveTasks];
        }
    }

    public void OnClientConnected(string clientId, string ip)
    {
        lock (_lock)
        {
            _clients.RemoveAll(c => c.ClientId == clientId);
            _clients.Add(new ClientInfo { ClientId = clientId, Ip = ip, SecondsAgo = 0, TimeoutRemaining = 0 });
        }
    }

    public void OnClientDisconnected(string clientId)
    {
        lock (_lock)
            _clients.RemoveAll(c => c.ClientId == clientId);
    }

    public void OnTaskAssigned(string taskId, string clientId, long startIndex, long endIndex)
    {
        lock (_lock)
        {
            _activeTasks.Add(new ActiveTask
            {
                TaskId = taskId, ClientId = clientId,
                StartIndex = startIndex, EndIndex = endIndex,
                ElapsedSeconds = 0
            });
            if (_pendingTasks > 0) _pendingTasks--;
        }
    }

    public void OnTaskCompleted(string taskId)
    {
        lock (_lock)
        {
            _activeTasks.RemoveAll(t => t.TaskId == taskId);
            _completedTasks++;
        }
    }

    public void OnTaskRequeued(string taskId)
    {
        lock (_lock)
        {
            _activeTasks.RemoveAll(t => t.TaskId == taskId);
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
        lock (_lock)
        {
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
                Clients = [.. _clients],
                ActiveTasks = [.. _activeTasks],
                LogLines = [.. _logLines],
                Connected = _connected
            };
        }
    }
}
