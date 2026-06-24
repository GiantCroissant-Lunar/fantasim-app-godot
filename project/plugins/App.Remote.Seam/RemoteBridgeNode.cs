using System.Collections.Concurrent;
using Godot;

namespace FantaSim.App.Remote.Seam;

public partial class RemoteBridgeNode : Node, FantaSim.App.Remote.IMainThreadDispatcher
{
    private const int MaxItemsPerFrame = 16;

    private readonly ConcurrentQueue<Action> _queue = new();

    public RemoteBridgeNode()
    {
        Name = "RemoteBridge";
    }

    public Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<T>(cancellationToken);

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        _queue.Enqueue(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await action();
                tcs.TrySetResult(result);
            }
            catch (OperationCanceledException ex)
            {
                tcs.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                registration.Dispose();
            }
        });

        return tcs.Task;
    }

    public override void _Process(double delta)
    {
        var processed = 0;
        while (processed < MaxItemsPerFrame && _queue.TryDequeue(out var action))
        {
            action();
            processed++;
        }
    }
}
