using System.Threading.Channels;
using password_break_server.Models;

namespace password_break_server.Services;

public class MonitorEventBroadcaster : IServerEventListener, IHostedService, IDisposable
{
    private readonly ITaskManager _taskManager;
    private readonly IFoundPasswords _foundPasswords;
    private readonly IClientTracker _clientTracker;
    private readonly PasswordBreakConfig _config;
    private readonly List<Channel<MonitorEvent>> _subscribers = [];
    private readonly Lock _lock = new();
    private Timer? _snapshotTimer;

    public MonitorEventBroadcaster(
        ITaskManager taskManager,
        IFoundPasswords foundPasswords,
        IClientTracker clientTracker,
        PasswordBreakConfig config)
    {
        _taskManager = taskManager;
        _foundPasswords = foundPasswords;
        _clientTracker = clientTracker;
        _config = config;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _snapshotTimer = new Timer(_ => BroadcastSnapshot(), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _snapshotTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public ChannelReader<MonitorEvent> Subscribe()
    {
        var channel = Channel.CreateBounded<MonitorEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        lock (_lock) _subscribers.Add(channel);
        return channel.Reader;
    }

    public void Unsubscribe(ChannelReader<MonitorEvent> reader)
    {
        lock (_lock)
            _subscribers.RemoveAll(ch => ch.Reader == reader);
    }

    public StateSnapshot BuildSnapshot()
    {
        var (completed, total, pending) = _taskManager.GetProgress();
        var snapshot = new StateSnapshot
        {
            CompletedTasks = completed,
            TotalTasks = total,
            PendingTasks = pending,
            FoundCount = _foundPasswords.FoundCount,
            RemainingCount = _foundPasswords.RemainingCount,
            AllFound = _foundPasswords.AllFound,
            Saved = _foundPasswords.Saved,
            AttackMode = _config.AttackMode
        };

        foreach (var c in _clientTracker.GetClientStates(_config.HeartbeatTimeoutSeconds))
        {
            snapshot.Clients.Add(new ClientInfo
            {
                ClientId = c.Id,
                Ip = c.Ip,
                SecondsAgo = c.Ago,
                TimeoutRemaining = c.Timeout
            });
        }

        foreach (var t in _taskManager.GetActiveTasks())
        {
            snapshot.ActiveTasks.Add(new ActiveTask
            {
                TaskId = t.TaskId,
                ClientId = t.ClientId ?? "",
                StartIndex = t.StartIndex,
                EndIndex = t.EndIndex,
                ElapsedSeconds = (int)(DateTime.UtcNow - t.StartedAt).TotalSeconds
            });
        }

        return snapshot;
    }

    private void BroadcastSnapshot()
    {
        lock (_lock)
        {
            if (_subscribers.Count == 0) return;
        }
        Broadcast(new MonitorEvent { Snapshot = BuildSnapshot() });
    }

    private void Broadcast(MonitorEvent evt)
    {
        lock (_lock)
        {
            foreach (var ch in _subscribers)
                ch.Writer.TryWrite(evt);
        }
    }

    public void ClientConnected(string clientId, string ip)
        => Broadcast(new MonitorEvent
        {
            ClientConnected = new ClientConnectedEvent { ClientId = clientId, Ip = ip }
        });

    public void ClientDisconnected(string clientId)
        => Broadcast(new MonitorEvent
        {
            ClientDisconnected = new ClientDisconnectedEvent { ClientId = clientId }
        });

    public void LogTaskAssigned(string clientId, string taskId, long startIndex, long endIndex)
        => Broadcast(new MonitorEvent
        {
            TaskAssigned = new TaskAssignedEvent
            {
                ClientId = clientId, TaskId = taskId,
                StartIndex = startIndex, EndIndex = endIndex
            }
        });

    public void LogTaskCompleted(string taskId)
        => Broadcast(new MonitorEvent
        {
            TaskCompleted = new TaskCompletedEvent { TaskId = taskId }
        });

    public void LogTaskRequeued(string taskId)
        => Broadcast(new MonitorEvent
        {
            TaskRequeued = new TaskRequeuedEvent { TaskId = taskId }
        });

    public void LogFound(string password, string hash)
        => Broadcast(new MonitorEvent
        {
            PasswordFound = new PasswordFoundEvent { Password = password, Hash = hash }
        });

    public void Dispose()
    {
        _snapshotTimer?.Dispose();
    }
}
