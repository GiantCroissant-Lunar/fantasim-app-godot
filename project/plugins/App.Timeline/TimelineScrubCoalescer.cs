using FantaSim.App.World.Composition;

namespace FantaSim.App.Timeline;

public enum TimelineScrubActionKind
{
    None = 0,
    Apply = 1,
}

public readonly record struct TimelineScrubAction(
    TimelineScrubActionKind Kind,
    long Tick,
    TimelineTickOrigin Origin)
{
    public static TimelineScrubAction None => new(TimelineScrubActionKind.None, 0L, TimelineTickOrigin.Standard);
    public bool ShouldApply => Kind == TimelineScrubActionKind.Apply;
}

public sealed class TimelineScrubCoalescer
{
    private long? _pendingTick;
    private bool _dragging;

    public bool IsDragging => _dragging;
    public long? PendingTick => _pendingTick;

    public TimelineScrubAction Press(long tick)
    {
        _dragging = true;
        _pendingTick = null;
        return new TimelineScrubAction(TimelineScrubActionKind.Apply, tick, TimelineTickOrigin.ScrubPreview);
    }

    public TimelineScrubAction Motion(long tick)
    {
        if (!_dragging)
            return new TimelineScrubAction(TimelineScrubActionKind.Apply, tick, TimelineTickOrigin.ScrubPreview);

        _pendingTick = tick;
        return TimelineScrubAction.None;
    }

    public TimelineScrubAction ConsumeFrame()
    {
        if (_pendingTick is not { } tick)
            return TimelineScrubAction.None;

        _pendingTick = null;
        return new TimelineScrubAction(TimelineScrubActionKind.Apply, tick, TimelineTickOrigin.ScrubPreview);
    }

    public TimelineScrubAction Release(long tick)
    {
        _dragging = false;
        _pendingTick = null;
        return new TimelineScrubAction(TimelineScrubActionKind.Apply, tick, TimelineTickOrigin.ScrubCommit);
    }

    public void Cancel()
    {
        _dragging = false;
        _pendingTick = null;
    }
}
