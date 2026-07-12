using System.Threading;

namespace FantaSim.App.Timeline.Seam;

internal readonly record struct TunnelFineRequestKey(
    string SphereId,
    string LayerId,
    long SampleTick,
    long Bucket,
    string ViewRung,
    int GraphRevision,
    int MountGeneration,
    long Epoch);

internal static class TunnelFineCompletionPolicy
{
    internal static bool CanApply(
        TunnelFineRequestKey completed,
        TunnelFineRequestKey? expected,
        bool cancellationRequested,
        int capturedLaneEpoch,
        int currentLaneEpoch)
        => !cancellationRequested
           && capturedLaneEpoch == currentLaneEpoch
           && expected == completed;
}

internal enum TunnelFineScheduleAction
{
    None,
    Start,
    CancelActive,
    Wait,
}

internal readonly record struct TunnelFineScheduleDecision(
    TunnelFineScheduleAction Action,
    TunnelFineRequestKey? Key,
    long DueAtMs);

/// <summary>Single-active latest-wins scheduler for presentation-only fine inspection. Reset and
/// replacement cancel the active token but never clear the active key until its provider unwinds.</summary>
internal sealed class TunnelFineRequestScheduler
{
    internal const long MinimumStartIntervalMs = 100L;

    private TunnelFineRequestKey? _active;
    private TunnelFineRequestKey? _pending;
    private CancellationTokenSource? _activeCts;
    private long _nextStartAtMs;

    internal CancellationToken ActiveToken => _activeCts?.Token ?? CancellationToken.None;

    internal TunnelFineScheduleDecision Offer(TunnelFineRequestKey key, long nowMs)
    {
        if (_active == key || _pending == key)
            return new(TunnelFineScheduleAction.None, null, _nextStartAtMs);

        if (_active is not null)
        {
            _pending = key;
            _activeCts?.Cancel();
            return new(TunnelFineScheduleAction.CancelActive, null, _nextStartAtMs);
        }

        if (nowMs < _nextStartAtMs)
        {
            _pending = key;
            return new(TunnelFineScheduleAction.Wait, null, _nextStartAtMs);
        }

        return Start(key, nowMs);
    }

    internal TunnelFineScheduleDecision ActiveFinished(
        TunnelFineRequestKey completedKey,
        long nowMs)
    {
        if (_active != completedKey)
            return new(TunnelFineScheduleAction.None, null, _nextStartAtMs);

        _active = null;
        _activeCts?.Dispose();
        _activeCts = null;
        return TakeDue(nowMs);
    }

    /// <summary>Completes the exact active key while deliberately abandoning all pending work.
    /// Used when the owning face/mount is gone, so a Start decision could not be serviced.</summary>
    internal bool AbandonFinished(TunnelFineRequestKey completedKey)
    {
        if (_active != completedKey)
            return false;

        _pending = null;
        _active = null;
        _activeCts?.Dispose();
        _activeCts = null;
        return true;
    }

    internal TunnelFineScheduleDecision TakeDue(long nowMs)
    {
        if (_active is not null || _pending is null)
            return new(TunnelFineScheduleAction.None, null, _nextStartAtMs);

        if (nowMs < _nextStartAtMs)
            return new(TunnelFineScheduleAction.Wait, null, _nextStartAtMs);

        var key = _pending.Value;
        _pending = null;
        return Start(key, nowMs);
    }

    internal bool Reset()
    {
        var cancel = _active is not null;
        _activeCts?.Cancel();
        _pending = null;
        return cancel;
    }

    private TunnelFineScheduleDecision Start(TunnelFineRequestKey key, long nowMs)
    {
        _active = key;
        _activeCts = new CancellationTokenSource();
        _nextStartAtMs = checked(nowMs + MinimumStartIntervalMs);
        return new(TunnelFineScheduleAction.Start, key, _nextStartAtMs);
    }
}
