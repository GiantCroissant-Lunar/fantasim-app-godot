namespace FantaSim.App.Remote;

public interface IMainThreadDispatcher
{
    Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default);
}

public sealed class InlineMainThreadDispatcher : IMainThreadDispatcher
{
    public Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        return action();
    }
}
