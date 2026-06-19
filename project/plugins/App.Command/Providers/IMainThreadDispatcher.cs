namespace FantaSim.App.Command.Providers;

/// <summary>
/// Marshals command handler invocations onto the host's main thread when required by the
/// runtime (e.g. Godot main loop). The default <see cref="ImmediateMainThreadDispatcher"/>
/// executes inline and is used in tests and headless hosts.
/// </summary>
public interface IMainThreadDispatcher
{
    Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default);
}

public sealed class ImmediateMainThreadDispatcher : IMainThreadDispatcher
{
    public Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return action();
    }
}