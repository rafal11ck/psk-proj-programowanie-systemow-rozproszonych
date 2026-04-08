using Grpc.Core;
using Grpc.Net.Client;

namespace password_break_monitor;

public class GrpcMonitorClient : IDisposable
{
    private readonly string _serverUrl;
    private readonly MonitorState _state;
    private readonly Action _onUpdate;
    private GrpcChannel? _channel;

    public GrpcMonitorClient(string serverUrl, MonitorState state, Action onUpdate)
    {
        _serverUrl = serverUrl;
        _state = state;
        _onUpdate = onUpdate;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(10);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _state.SetConnected(false);
                _state.AddLog($"Connecting to {_serverUrl}...");
                _onUpdate();

                _channel = GrpcChannel.ForAddress(_serverUrl);
                var client = new Monitor.MonitorClient(_channel);

                using var call = client.Subscribe(new SubscribeRequest(), cancellationToken: ct);

                _state.SetConnected(true);
                _state.AddLog("Connected");
                _onUpdate();
                delay = TimeSpan.FromSeconds(1);

                await foreach (var evt in call.ResponseStream.ReadAllAsync(ct))
                {
                    ProcessEvent(evt);
                    _onUpdate();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (RpcException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _state.SetConnected(false);
                _state.AddLog($"Disconnected: {ex.Message}");
                _onUpdate();
            }
            finally
            {
                _channel?.Dispose();
                _channel = null;
            }

            if (!ct.IsCancellationRequested)
            {
                _state.AddLog($"Reconnecting in {delay.TotalSeconds:0}s...");
                _onUpdate();
                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
            }
        }
    }

    private void ProcessEvent(MonitorEvent evt)
    {
        switch (evt.EventCase)
        {
            case MonitorEvent.EventOneofCase.Snapshot:
                _state.ApplySnapshot(evt.Snapshot);
                break;
            case MonitorEvent.EventOneofCase.ClientConnected:
                _state.OnClientConnected(evt.ClientConnected.ClientId, evt.ClientConnected.Ip, evt.ClientConnected.LastSeenUnixMs);
                _state.AddLog($"+ Client {evt.ClientConnected.ClientId} ({evt.ClientConnected.Ip}) connected");
                break;
            case MonitorEvent.EventOneofCase.ClientDisconnected:
                _state.OnClientDisconnected(evt.ClientDisconnected.ClientId);
                _state.AddLog($"- Client {evt.ClientDisconnected.ClientId} disconnected");
                break;
            case MonitorEvent.EventOneofCase.ClientHeartbeat:
                _state.OnClientHeartbeat(evt.ClientHeartbeat.ClientId, evt.ClientHeartbeat.LastSeenUnixMs);
                break;
            case MonitorEvent.EventOneofCase.TaskAssigned:
                _state.OnTaskAssigned(evt.TaskAssigned.TaskId, evt.TaskAssigned.ClientId,
                    evt.TaskAssigned.StartIndex, evt.TaskAssigned.EndIndex, evt.TaskAssigned.StartedAtUnixMs);
                _state.AddLog($"> Task {evt.TaskAssigned.TaskId} -> {evt.TaskAssigned.ClientId}");
                break;
            case MonitorEvent.EventOneofCase.TaskCompleted:
                _state.OnTaskCompleted(evt.TaskCompleted.TaskId);
                _state.AddLog($"v Task {evt.TaskCompleted.TaskId} done");
                break;
            case MonitorEvent.EventOneofCase.TaskRequeued:
                _state.OnTaskRequeued(evt.TaskRequeued.TaskId);
                _state.AddLog($"~ Task {evt.TaskRequeued.TaskId} requeued");
                break;
            case MonitorEvent.EventOneofCase.PasswordFound:
                _state.OnPasswordFound();
                _state.AddLog($"* Found: {evt.PasswordFound.Password} -> {evt.PasswordFound.Hash[..Math.Min(16, evt.PasswordFound.Hash.Length)]}...");
                break;
        }
    }

    public void Dispose()
    {
        _channel?.Dispose();
    }
}
