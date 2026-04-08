using password_break_server.Models;

namespace password_break_server.Services;

public class TaskManager : ITaskManager
{
    private readonly PasswordBreakConfig _config;
    private readonly FoundPasswords _foundPasswords;
    private readonly Dictionary<string, TaskInfo> _tasks = new();
    private readonly Queue<string> _pendingTasks = new();
    private readonly Lock _lock = new();
    private readonly List<string> _wordList = [];

    public IReadOnlyList<string> TargetHashes => _config.TargetHashes;
    public IReadOnlyList<string> WordList => _wordList;
    public string? WordListPath => _config.WordListPath;

    public TaskManager(PasswordBreakConfig config, FoundPasswords foundPasswords)
    {
        _config = config;
        _foundPasswords = foundPasswords;
        LoadWordList();
        InitializeTasks();
    }

    private void LoadWordList()
    {
        if (_config.AttackMode != "dictionary") return;
        if (string.IsNullOrEmpty(_config.WordListPath) || !File.Exists(_config.WordListPath)) return;
        _wordList.AddRange(File.ReadAllLines(_config.WordListPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim()));
    }

    private void InitializeTasks()
    {
        var totalItems = _config.AttackMode switch
        {
            "dictionary" => _wordList.Count,
            _ => CalculateBruteForceTotal()
        };

        for (var start = 0L; start < totalItems; start += _config.ChunkSize)
        {
            var end = Math.Min(start + _config.ChunkSize - 1, totalItems - 1);
            var taskId = $"{start}_{end}";
            
            _tasks[taskId] = new TaskInfo
            {
                TaskId = taskId,
                StartIndex = start,
                EndIndex = end
            };
            _pendingTasks.Enqueue(taskId);
        }
    }

    private long CalculateBruteForceTotal()
    {
        long total = 0;
        for (var length = _config.MinLength; length <= _config.MaxLength; length++)
            total += (long)Math.Pow(_config.CharSet.Length, length);
        return total;
    }

    public TaskInfo? GetNextTask(string clientId)
    {
        lock (_lock)
        {
            if (_foundPasswords.AllFound)
                return null;

            if (_pendingTasks.Count == 0)
                return null;

            var taskId = _pendingTasks.Dequeue();
            var task = _tasks[taskId];

            task.Status = HashTaskStatus.InProgress;
            task.ClientId = clientId;
            task.StartedAt = DateTime.UtcNow;
            return task;
        }
    }

    public void MarkCompleted(string taskId)
    {
        lock (_lock)
        {
            if (_tasks.TryGetValue(taskId, out var task))
                task.Status = HashTaskStatus.Completed;
        }
    }

    public void MarkPending(string taskId)
    {
        lock (_lock)
        {
            if (_tasks.TryGetValue(taskId, out var task) && task.Status == HashTaskStatus.InProgress)
            {
                task.Status = HashTaskStatus.Pending;
                task.ClientId = null;
                _pendingTasks.Enqueue(taskId);
            }
        }
    }

    public void ClearPending()
    {
        lock (_lock)
            _pendingTasks.Clear();
    }

    public List<string> RequeueClientTasks(string clientId)
    {
        lock (_lock)
        {
            var tasks = _tasks.Values
                .Where(t => t.Status == HashTaskStatus.InProgress && t.ClientId == clientId)
                .ToList();

            foreach (var task in tasks)
            {
                task.Status = HashTaskStatus.Pending;
                task.ClientId = null;
                _pendingTasks.Enqueue(task.TaskId);
            }

            return tasks.Select(t => t.TaskId).ToList();
        }
    }

    public (int Completed, int Total, int Pending) GetProgress()
    {
        lock (_lock)
        {
            return (
                _tasks.Values.Count(t => t.Status == HashTaskStatus.Completed),
                _tasks.Count,
                _pendingTasks.Count
            );
        }
    }

    public List<TaskInfo> GetActiveTasks()
    {
        lock (_lock)
        {
            return _tasks.Values
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
}
