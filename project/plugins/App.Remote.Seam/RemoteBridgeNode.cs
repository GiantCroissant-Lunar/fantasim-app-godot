using System.Collections.Concurrent;
using Godot;

namespace FantaSim.App.Remote.Seam;

public partial class RemoteBridgeNode : Node, FantaSim.App.Remote.IMainThreadDispatcher
{
    private const int MaxItemsPerFrame = 16;

    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly GodotFrameSynchronizationContext _syncContext;

    public RemoteBridgeNode()
    {
        Name = "RemoteBridge";
        _syncContext = new GodotFrameSynchronizationContext(_queue);
    }

    public Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<T>(cancellationToken);

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        _queue.Enqueue(() =>
        {
            _ = RunOnFrameContextAsync(action, tcs, registration, cancellationToken);
        });

        return tcs.Task;
    }

    private async Task RunOnFrameContextAsync<T>(
        Func<Task<T>> action,
        TaskCompletionSource<T> tcs,
        CancellationTokenRegistration registration,
        CancellationToken cancellationToken)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_syncContext);
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
            SynchronizationContext.SetSynchronizationContext(previous);
            registration.Dispose();
        }
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

    private sealed class GodotFrameSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<Action> _queue;

        public GodotFrameSynchronizationContext(ConcurrentQueue<Action> queue)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            _queue.Enqueue(() =>
            {
                var previous = Current;
                SetSynchronizationContext(this);
                try
                {
                    d(state);
                }
                finally
                {
                    SetSynchronizationContext(previous);
                }
            });
        }
    }
}
