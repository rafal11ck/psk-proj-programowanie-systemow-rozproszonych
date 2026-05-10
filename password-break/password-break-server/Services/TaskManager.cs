using password_break_server.Models;

namespace password_break_server.Services;

public class TaskManager : ITaskManager
{
    private readonly PasswordBreakConfig _config;
    private readonly FoundPasswords _foundPasswords;
    private readonly Dictionary<string, TaskInfo> _activeTasks = new();
    private readonly Queue<TaskInfo> _pendingTasks = new();
    private readonly Lock _lock = new();
    private readonly List<string> _wordList = [];

    private long _nextStartIndex;
    private long _totalItems;
    private long _completedTasks;

    public IReadOnlyList<string> TargetHashes => _config.TargetHashes;
    public IReadOnlyList<string> WordList => _wordList;
    public string? WordListPath => _config.WordListPath;

    public TaskManager(PasswordBreakConfig config, FoundPasswords foundPasswords)
    {
        _config = config;
        _foundPasswords = foundPasswords;

        LoadWordList();
        InitializeGlobalWorkSpace();
    }

    private void InitializeGlobalWorkSpace()
    {
        _nextStartIndex = 0;
        _completedTasks = 0;

        _activeTasks.Clear();
        _pendingTasks.Clear();

        _totalItems = _config.AttackMode switch
        {
            "dictionary" => _wordList.Count,
            _ => CalculateBruteForceTotal()
        };
    }

    private void LoadWordList()
    {
        _wordList.Clear();

        if (_config.AttackMode != "dictionary")
            return;

        if (string.IsNullOrEmpty(_config.WordListPath) || !File.Exists(_config.WordListPath))
            return;

        _wordList.AddRange(File.ReadAllLines(_config.WordListPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim()));
    }

    private long CalculateBruteForceTotal()
    {
        long total = 0;

        for (var length = _config.MinLength; length <= _config.MaxLength; length++)
            total += (long)Math.Pow(_config.CharSet.Length, length);

        return total;
    }

    public TaskInfo? GetNextTask(string clientId, int runSequence)
    {
        lock (_lock)
        {
            if (_foundPasswords.AllFound)
                return null;

            var task = GetReusablePendingTaskForRun(runSequence) ?? CreateNextTask(runSequence);

            if (task == null)
                return null;

            var now = DateTime.UtcNow;

            task.Status = HashTaskStatus.InProgress;
            task.ClientId = clientId;
            task.StartedAt = now;
            task.SentAtUtc = now;

            _activeTasks[task.TaskId] = task;

            return task;
        }
    }

    private TaskInfo? GetReusablePendingTaskForRun(int runSequence)
    {
        while (_pendingTasks.Count > 0)
        {
            var task = _pendingTasks.Dequeue();

            if (task.RunSequence == runSequence)
                return task;
        }

        return null;
    }

    private TaskInfo? CreateNextTask(int runSequence)
    {
        if (_nextStartIndex >= _totalItems)
            return null;

        var start = _nextStartIndex;
        var end = Math.Min(start + _config.ChunkSize - 1, _totalItems - 1);

        _nextStartIndex = end + 1;

        return new TaskInfo
        {
            TaskId = $"run{runSequence}_{start}_{end}",
            RunSequence = runSequence,
            Status = HashTaskStatus.Pending,
            StartIndex = start,
            EndIndex = end
        };
    }

    public TaskInfo? GetTask(string taskId)
    {
        lock (_lock)
        {
            return _activeTasks.TryGetValue(taskId, out var task) ? task : null;
        }
    }

    public void MarkCompleted(string taskId)
    {
        lock (_lock)
        {
            if (_activeTasks.TryGetValue(taskId, out var task))
            {
                task.Status = HashTaskStatus.Completed;
                _activeTasks.Remove(taskId);
                _completedTasks++;
            }
        }
    }

    public void MarkPending(string taskId)
    {
        lock (_lock)
        {
            if (_activeTasks.TryGetValue(taskId, out var task) && task.Status == HashTaskStatus.InProgress)
            {
                _activeTasks.Remove(taskId);

                task.Status = HashTaskStatus.Pending;
                task.ClientId = null;

                _pendingTasks.Enqueue(task);
            }
        }
    }

    public List<string> RequeueClientTasks(string clientId)
    {
        lock (_lock)
        {
            var tasks = _activeTasks.Values
                .Where(t => t.Status == HashTaskStatus.InProgress && t.ClientId == clientId)
                .ToList();

            foreach (var task in tasks)
            {
                _activeTasks.Remove(task.TaskId);

                task.Status = HashTaskStatus.Pending;
                task.ClientId = null;

                _pendingTasks.Enqueue(task);
            }

            return tasks.Select(t => t.TaskId).ToList();
        }
    }

    public List<string> RequeueExpiredTasks(int timeoutSeconds)
    {
        if (timeoutSeconds <= 0)
            return [];

        var now = DateTime.UtcNow;

        lock (_lock)
        {
            var expired = _activeTasks.Values
                .Where(t => t.Status == HashTaskStatus.InProgress
                            && (now - t.StartedAt).TotalSeconds > timeoutSeconds)
                .ToList();

            foreach (var task in expired)
            {
                _activeTasks.Remove(task.TaskId);

                task.Status = HashTaskStatus.Pending;
                task.ClientId = null;

                _pendingTasks.Enqueue(task);
            }

            return expired.Select(t => t.TaskId).ToList();
        }
    }

    public (int Completed, int Total, int Pending) GetProgress()
    {
        lock (_lock)
        {
            var totalTasks = GetTotalTaskCount();
            var generatedTasks = _completedTasks + _activeTasks.Count + _pendingTasks.Count;
            var notGeneratedYet = Math.Max(0, totalTasks - generatedTasks);
            var pending = _pendingTasks.Count + notGeneratedYet;

            return (
                ClampToInt(_completedTasks),
                ClampToInt(totalTasks),
                ClampToInt(pending)
            );
        }
    }

    public List<TaskInfo> GetActiveTasks()
    {
        lock (_lock)
        {
            return _activeTasks.Values
                .Where(t => t.Status == HashTaskStatus.InProgress)
                .OrderBy(t => t.ClientId)
                .ToList();
        }
    }

    public long GetWordListTimestamp()
    {
        if (string.IsNullOrEmpty(_config.WordListPath) || !File.Exists(_config.WordListPath))
            return 0;

        return new FileInfo(_config.WordListPath).LastWriteTimeUtc.Ticks;
    }

    private long GetTotalTaskCount()
    {
        if (_totalItems <= 0)
            return 0;

        return (_totalItems + _config.ChunkSize - 1) / _config.ChunkSize;
    }

    private static int ClampToInt(long value)
    {
        if (value > int.MaxValue)
            return int.MaxValue;

        if (value < int.MinValue)
            return int.MinValue;

        return (int)value;
    }
}
