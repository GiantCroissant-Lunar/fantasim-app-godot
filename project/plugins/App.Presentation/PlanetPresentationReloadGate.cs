namespace FantaSim.App.Presentation;

internal sealed class PlanetPresentationReloadGate
{
    private readonly object _gate = new();
    private bool _pending;
    private bool _scheduled;

    public bool IsPending
    {
        get
        {
            lock (_gate)
                return _pending;
        }
    }

    public void MarkRuntimeChanging()
    {
        lock (_gate)
        {
            _pending = true;
            _scheduled = false;
        }
    }

    public bool TryScheduleDeferredAttempt()
    {
        lock (_gate)
        {
            if (!_pending || _scheduled)
                return false;

            _scheduled = true;
            return true;
        }
    }

    public bool CompleteDeferredAttempt(bool runtimeChangeInProgress)
    {
        lock (_gate)
        {
            _scheduled = false;
            return _pending && !runtimeChangeInProgress;
        }
    }

    public void MarkMounted()
    {
        lock (_gate)
        {
            _pending = false;
            _scheduled = false;
        }
    }
}
