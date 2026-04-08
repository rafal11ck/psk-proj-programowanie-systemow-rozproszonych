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

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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

        foreach (var c in _clientTracker.GetClientStates())
        {
            snapshot.Clients.Add(new ClientInfo
            {
                ClientId = c.Id,
                Ip = c.Ip,
                LastSeenUnixMs = new DateTimeOffset(c.LastSeenUtc, TimeSpan.Zero).ToUnixTimeMilliseconds()
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
                StartedAtUnixMs = new DateTimeOffset(t.StartedAt, TimeSpan.Zero).ToUnixTimeMilliseconds()
            });
        }

        return snapshot;
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
            ClientConnected = new ClientConnectedEvent
            {
                ClientId = clientId,
                Ip = ip,
                LastSeenUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }
        });

    public void ClientDisconnected(string clientId)
        => Broadcast(new MonitorEvent
        {
            ClientDisconnected = new ClientDisconnectedEvent { ClientId = clientId }
        });

    public void ClientHeartbeat(string clientId)
        => Broadcast(new MonitorEvent
        {
            ClientHeartbeat = new ClientHeartbeatEvent
            {
                ClientId = clientId,
                LastSeenUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }
        });

    public void LogTaskAssigned(string clientId, string taskId, long startIndex, long endIndex)
        => Broadcast(new MonitorEvent
        {
            TaskAssigned = new TaskAssignedEvent
            {
                ClientId = clientId, TaskId = taskId,
                StartIndex = startIndex, EndIndex = endIndex,
                StartedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
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

    public void Dispose() { }
}
