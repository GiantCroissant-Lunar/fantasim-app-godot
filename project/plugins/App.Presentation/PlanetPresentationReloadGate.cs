namespace FantaSim.App.Presentation;

internal sealed class PlanetPresentationReloadGate
{
    private bool _pending;
    private bool _scheduled;

    public bool IsPending => _pending;

    public void MarkRuntimeChanging()
    {
        _pending = true;
        _scheduled = false;
    }

    public bool TryScheduleDeferredAttempt()
    {
        if (!_pending || _scheduled)
            return false;

        _scheduled = true;
        return true;
    }

    public bool CompleteDeferredAttempt(bool runtimeChangeInProgress)
    {
        _scheduled = false;
        return _pending && !runtimeChangeInProgress;
    }

    public void MarkMounted()
    {
        _pending = false;
        _scheduled = false;
    }
}
