using FantaSim.App.World.Composition;

namespace FantaSim.App.Presentation;

/// <summary>
/// Owns scrub-origin refresh policy: previews fire an immediate low-rung refresh, rest starts a
/// climb up the rung ladder, commits/standard cancel and request full. Extracted from
/// PlanetPresentationBinder 2026-07-11 (vault/plans/2026-07-11-planet-presentation-binder-split-plan.md);
/// D8b progressive-resolution rung ladder
/// (vault/plans/2026-07-11-d8b-progressive-resolution-slice1-plan.md): LowRung/MidRung mirror
/// Service.ResolveFilmstripFrequency's 2/3 ladder — no parallel LOD system.
/// </summary>
internal sealed class ScrubRefreshCoordinator : IDisposable
{
    internal const int LowRung = 2;
    internal const int MidRung = 3;

    private readonly ScrubApplyScheduler _scrubApplyScheduler;
    private readonly Action<int?> _requestRefresh;
    private readonly Action<Action> _deferToMainThread;
    private readonly Func<long> _nowMs;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private CancellationTokenSource? _restCts;
    private CancellationTokenSource? _climbCts;
    private bool _climbRunning;
    private bool _disposed;

    public ScrubRefreshCoordinator(
        ScrubApplyScheduler scheduler,
        Action<int?> requestRefresh,
        Action<Action> deferToMainThread,
        Func<long> nowMs,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _scrubApplyScheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _requestRefresh = requestRefresh ?? throw new ArgumentNullException(nameof(requestRefresh));
        _deferToMainThread = deferToMainThread ?? throw new ArgumentNullException(nameof(deferToMainThread));
        _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public void HandleTick(long tick, TimelineTickOrigin origin, bool heavyRefreshRequested)
    {
        switch (origin)
        {
            case TimelineTickOrigin.ScrubPreview:
                // A new scrub supersedes an in-progress climb (D8b directive 2).
                CancelClimb();
                ScheduleScrubRestRefresh(tick, heavyRefreshRequested);
                break;
            case TimelineTickOrigin.ScrubCommit:
                CancelClimb();
                var hadPending = FlushScrubRestRefresh(tick);
                if (hadPending || heavyRefreshRequested)
                    _requestRefresh(null);
                break;
            default:
                // Standard + heavy = genuinely new content: supersede all scrub state and go
                // straight to full. Standard + NO heavy is a scrub-state NO-OP: every refresh's
                // document apply echoes such a tick at the same playhead (face SeekTo echo,
                // PlanetTimelineController.UpdateFrom -> PushTick), and cancelling here wiped
                // the pending rest after each preview's low-rung refresh — the climb never ran
                // and the planet stranded below full resolution (D8b gate, 2026-07-11).
                if (heavyRefreshRequested)
                {
                    Cancel();
                    _requestRefresh(null);
                }
                break;
        }
    }

    /// <summary>
    /// True while a scrub interaction owns the refresh pipeline: a rest flush is pending or the
    /// rung climb is running. The binder's generation-changed subscription checks this so that
    /// generation completions CAUSED BY a scrub's own low-rung fetch do not chase a
    /// full-frequency refresh mid-scrub — the climb re-binds at full when the hand rests.
    /// </summary>
    internal bool IsScrubActive => _scrubApplyScheduler.HasPending || _climbRunning;

    private void ScheduleScrubRestRefresh(long tick, bool heavyRefreshRequested)
    {
        if (heavyRefreshRequested)
            _requestRefresh(LowRung);

        var schedule = _scrubApplyScheduler.RecordPreview(tick, heavyRefreshRequested, _nowMs());
        if (schedule is null)
            return;

        _restCts?.Cancel();
        _restCts?.Dispose();
        var cts = new CancellationTokenSource();
        _restCts = cts;
        var dueInMs = Math.Max(0L, schedule.Value.DueAtMs - _nowMs());
        _ = DelayThenClimbAsync(schedule.Value.Generation, dueInMs, cts.Token);
    }

    private async Task DelayThenClimbAsync(int generation, long delayMs, CancellationToken cancellationToken)
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

        _deferToMainThread(() => StartClimbIfDue(generation));
    }

    private void StartClimbIfDue(int generation)
    {
        if (_disposed)
            return;

        if (_scrubApplyScheduler.ConsumeDue(generation, _nowMs()) is null)
            return;

        _climbCts?.Cancel();
        _climbCts?.Dispose();
        var climbCts = new CancellationTokenSource();
        _climbCts = climbCts;
        _climbRunning = true;
        // Capture the token BEFORE invoking the callback: a re-entrant HandleTick inside
        // _requestRefresh (any consumer feedback) can CancelClimb() and dispose climbCts, after
        // which climbCts.Token throws ObjectDisposedException. A captured token stays readable.
        var climbToken = climbCts.Token;
        // The first climb step fires AT the rest flush (plan: "invoke requestRefresh(MidRung),
        // then after each subsequent restDelayMs interval the next higher rung") — we are already
        // on the main thread here, deferred by DelayThenClimbAsync. Only the remaining rungs wait.
        _requestRefresh(MidRung);
        if (!climbToken.IsCancellationRequested)
            _ = ClimbAsync(climbToken);
    }

    private async Task ClimbAsync(CancellationToken cancellationToken)
    {
        var rungs = new int?[] { null };
        foreach (var rung in rungs)
        {
            try
            {
                await _delayAsync(TimeSpan.FromMilliseconds(_scrubApplyScheduler.RestDelayMs), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested || _disposed)
                return;

            var capturedRung = rung;
            _deferToMainThread(() =>
            {
                if (cancellationToken.IsCancellationRequested || _disposed)
                    return;
                _requestRefresh(capturedRung);
            });
        }

        // Natural completion: drop the scrub-active claim AFTER the final (full) refresh request
        // has been queued — deferred FIFO keeps IsScrubActive true until full is in flight. A
        // cancelled climb clears the flag in CancelClimb instead (this action self-gates).
        _deferToMainThread(() =>
        {
            if (!cancellationToken.IsCancellationRequested)
                _climbRunning = false;
        });
    }

    private bool FlushScrubRestRefresh(long tick)
    {
        if (_scrubApplyScheduler.ConsumeCommit(tick) is null)
            return false;

        _restCts?.Cancel();
        _restCts?.Dispose();
        _restCts = null;
        return true;
    }

    private void CancelClimb()
    {
        _climbCts?.Cancel();
        _climbCts?.Dispose();
        _climbCts = null;
        _climbRunning = false;
    }

    public void Cancel()
    {
        _scrubApplyScheduler.Clear();
        _restCts?.Cancel();
        _restCts?.Dispose();
        _restCts = null;
        CancelClimb();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Cancel();
    }
}