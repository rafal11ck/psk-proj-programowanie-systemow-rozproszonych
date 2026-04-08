using Grpc.Core;

namespace password_break_server.Services;

public class MonitorGrpcService : Monitor.MonitorBase
{
    private readonly MonitorEventBroadcaster _broadcaster;

    public MonitorGrpcService(MonitorEventBroadcaster broadcaster)
    {
        _broadcaster = broadcaster;
    }

    public override async Task Subscribe(
        SubscribeRequest request,
        IServerStreamWriter<MonitorEvent> responseStream,
        ServerCallContext context)
    {
        var reader = _broadcaster.Subscribe();
        try
        {
            var snapshot = _broadcaster.BuildSnapshot();
            await responseStream.WriteAsync(new MonitorEvent { Snapshot = snapshot });

            await foreach (var evt in reader.ReadAllAsync(context.CancellationToken))
                await responseStream.WriteAsync(evt);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // klient rozłączył się — normalne zakończenie streamu
        }
        finally
        {
            _broadcaster.Unsubscribe(reader);
        }
    }
}
