using FantaSim.App.World.Composition;

namespace FantaSim.App.Presentation;

/// <summary>
/// Owns scrub-origin heavy-refresh policy: previews debounce through ScrubApplyScheduler,
/// commits flush, standard ticks cancel. Extracted from PlanetPresentationBinder 2026-07-11
/// (vault/plans/2026-07-11-planet-presentation-binder-split-plan.md); D8b's progressive
/// resolution rung ladder lands here.
/// </summary>
internal sealed class ScrubRefreshCoordinator : IDisposable
{
    private readonly ScrubApplyScheduler _scrubApplyScheduler;
    private readonly Action _requestHeavyRefresh;
    private readonly Action<Action> _deferToMainThread;
    private readonly Func<long> _nowMs;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private CancellationTokenSource? _scrubRefreshDelay;
    private bool _disposed;

    public ScrubRefreshCoordinator(
        ScrubApplyScheduler scheduler,
        Action requestHeavyRefresh,
        Action<Action> deferToMainThread,
        Func<long> nowMs,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _scrubApplyScheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _requestHeavyRefresh = requestHeavyRefresh ?? throw new ArgumentNullException(nameof(requestHeavyRefresh));
        _deferToMainThread = deferToMainThread ?? throw new ArgumentNullException(nameof(deferToMainThread));
        _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public void HandleTick(long tick, TimelineTickOrigin origin, bool heavyRefreshRequested)
    {
        switch (origin)
        {
            case TimelineTickOrigin.ScrubPreview:
                ScheduleScrubRestRefresh(tick, heavyRefreshRequested);
                break;
            case TimelineTickOrigin.ScrubCommit:
                FlushScrubRestRefresh(tick);
                if (heavyRefreshRequested)
                    _requestHeavyRefresh();
                break;
            default:
                Cancel();
                if (heavyRefreshRequested)
                    _requestHeavyRefresh();
                break;
        }
    }

    private void ScheduleScrubRestRefresh(long tick, bool heavyRefreshRequested)
    {
        var schedule = _scrubApplyScheduler.RecordPreview(tick, heavyRefreshRequested, _nowMs());
        if (schedule is null)
            return;

        _scrubRefreshDelay?.Cancel();
        _scrubRefreshDelay?.Dispose();
        var cts = new CancellationTokenSource();
        _scrubRefreshDelay = cts;
        var dueInMs = Math.Max(0L, schedule.Value.DueAtMs - _nowMs());
        _ = DelayThenFlushScrubRefreshAsync(schedule.Value.Generation, dueInMs, cts.Token);
    }

    private async Task DelayThenFlushScrubRefreshAsync(int generation, long delayMs, CancellationToken cancellationToken)
    {
        try
        {
            await _delayAsync(TimeSpan.FromMilliseconds(delayMs), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested || _disposed)
            return;

        _deferToMainThread(() => FlushScrubRestRefreshIfDue(generation));
    }

    private void FlushScrubRestRefreshIfDue(int generation)
    {
        if (_disposed)
            return;

        if (_scrubApplyScheduler.ConsumeDue(generation, _nowMs()) is null)
            return;

        _requestHeavyRefresh();
    }

    private void FlushScrubRestRefresh(long tick)
    {
        if (_scrubApplyScheduler.ConsumeCommit(tick) is null)
            return;

        _scrubRefreshDelay?.Cancel();
        _scrubRefreshDelay?.Dispose();
        _scrubRefreshDelay = null;
        _requestHeavyRefresh();
    }

    public void Cancel()
    {
        _scrubApplyScheduler.Clear();
        _scrubRefreshDelay?.Cancel();
        _scrubRefreshDelay?.Dispose();
        _scrubRefreshDelay = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Cancel();
    }
}
