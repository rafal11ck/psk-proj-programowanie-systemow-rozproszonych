using password_break_server.Models;

namespace password_break_server.Services;

public class TaskManager
{
    private readonly PasswordBreakConfig _config;
    private List<string> _wordList = [];
    private readonly Dictionary<string, TaskInfo> _tasks = new();
    private readonly Queue<string> _pendingTasks = new();
    private readonly Lock _lock = new();

    public TaskManager(PasswordBreakConfig config)
    {
        _config = config;
        LoadWordList();
        InitializeTasks();
    }

    private void LoadWordList()
    {
        if (_config.AttackMode != "dictionary" || string.IsNullOrEmpty(_config.WordListPath))
            return;

        if (File.Exists(_config.WordListPath))
        {
            _wordList = File.ReadAllLines(_config.WordListPath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .ToList();
        }
    }

    private void InitializeTasks()
    {
        if (_config.AttackMode == "dictionary")
            InitializeDictionaryTasks();
        else
            InitializeBruteForceTasks();
    }

    private void InitializeDictionaryTasks()
    {
        for (var i = 0; i < _wordList.Count; i += _config.ChunkSize)
        {
            var chunk = _wordList.Skip(i).Take(_config.ChunkSize).ToList();
            var taskId = $"dict_{i / _config.ChunkSize}";
            _tasks[taskId] = new TaskInfo { TaskId = taskId, TaskData = chunk };
            _pendingTasks.Enqueue(taskId);
        }
    }

    private void InitializeBruteForceTasks()
    {
        var taskId = 0L;
        for (var length = _config.MinLength; length <= _config.MaxLength; length++)
        {
            var total = (long)Math.Pow(_config.CharSet.Length, length);
            for (var start = 0L; start < total; start += _config.ChunkSize)
            {
                var end = Math.Min(start + _config.ChunkSize - 1, total - 1);
                var id = $"bf_{length}_{taskId++}";
                _tasks[id] = new TaskInfo
                {
                    TaskId = id,
                    TaskData = new BruteForceTaskData
                    {
                        CharSet = _config.CharSet,
                        Length = length,
                        StartIndex = start,
                        EndIndex = end
                    }
                };
                _pendingTasks.Enqueue(id);
            }
        }
    }

    public TaskInfo? GetNextTask(string clientId)
    {
        lock (_lock)
        {
            while (_pendingTasks.Count > 0)
            {
                var taskId = _pendingTasks.Dequeue();
                var task = _tasks[taskId];
                
                if (task.Status != "pending" && !IsExpired(task)) continue;
                
                task.Status = "in_progress";
                task.ClientId = clientId;
                task.AssignedAt = DateTime.UtcNow;
                return task;
            }
            return null;
        }
    }

    public void MarkCompleted(string taskId)
    {
        lock (_lock)
        {
            if (_tasks.TryGetValue(taskId, out var task))
                task.Status = "completed";
        }
    }

    public void MarkPending(string taskId)
    {
        lock (_lock)
        {
            if (_tasks.TryGetValue(taskId, out var task) && task.Status == "in_progress")
            {
                task.Status = "pending";
                task.ClientId = null;
                _pendingTasks.Enqueue(taskId);
            }
        }
    }

    public void TouchTask(string taskId)
    {
        lock (_lock)
        {
            if (_tasks.TryGetValue(taskId, out var task) && task.Status == "in_progress")
            {
                task.AssignedAt = DateTime.UtcNow;
            }
        }
    }

    private bool IsExpired(TaskInfo task)
    {
        return task.Status == "in_progress" && 
               (DateTime.UtcNow - task.AssignedAt).TotalSeconds > _config.HeartbeatTimeoutSeconds;
    }

    public void CheckExpiredTasks()
    {
        lock (_lock)
        {
            foreach (var task in _tasks.Values.Where(t => IsExpired(t)))
            {
                task.Status = "pending";
                task.ClientId = null;
                _pendingTasks.Enqueue(task.TaskId);
            }
        }
    }

    public (int Completed, int Total) GetProgress()
    {
        lock (_lock)
        {
            return (_tasks.Values.Count(t => t.Status == "completed"), _tasks.Count);
        }
    }
}