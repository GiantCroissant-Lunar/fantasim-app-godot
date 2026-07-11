using FantaSim.App.Timeline;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Timeline.Seam;

/// <summary>Which tunnel surface owns the current gesture.</summary>
public enum TunnelGestureKind
{
    None = 0,
    OuterRing = 1,
    InnerRing = 2,
    Wall = 3,
}

/// <summary>Why the provisional fine preview was reset to zero.</summary>
public enum TunnelFineResetReason
{
    FocusChanged = 0,
    BaseTimeChanged = 1,
    Disabled = 2,
    ControllerLost = 3,
    BundleTeardown = 4,
    Disposed = 5,
}

/// <summary>
/// Snapshot the coordinator needs at press time: the current/max tick for outer-dial mapping, the
/// focus index/count for wall snapping, and the focused track's binding plus rail geometry for the
/// fine preview.
/// </summary>
public readonly record struct TunnelGesturePressContext(
    long CurrentTick,
    long MaxTick,
    int FocusIndex,
    int TrackCount,
    TunnelFineTrackBinding FineBinding,
    double FineRailCenterZ,
    double FineRailHalfLength);

/// <summary>
/// One gesture step's observable result: whether it was handled, the owning gesture kind, the
/// accumulated signed angle, the timeline scrub action (outer only), the outer tick mapping, the
/// fine preview (inner only), the carousel snap (wall release only), and an optional fine reset
/// reason.
/// </summary>
public readonly record struct TunnelGestureUpdate(
    bool Handled,
    TunnelGestureKind Gesture,
    double AccumulatedDegrees,
    TimelineScrubAction ScrubAction,
    TunnelOuterTickMapping? OuterTick,
    TunnelFinePreview? FinePreview,
    TunnelCorridorLayout.TunnelCarouselSnap? CarouselSnap,
    TunnelFineResetReason? FineResetReason);

/// <summary>
/// Owns one tunnel gesture at a time and drives the existing <see cref="TimelineScrubCoalescer"/>
/// for the outer dial. Outer motion is coalesced and consumed per frame; release commits the latest
/// accumulated angle exactly once. Inner motion is presentation-only (no scrub action, never
/// <see cref="ITimelineController.PushTick"/>). Wall motion rotates the carousel transiently;
/// release snaps cyclically. Cancel clears ownership without commit. Explicit fine resets return a
/// centered preview with the supplied reason.
/// </summary>
public sealed class TunnelGestureCoordinator
{
    private readonly TimelineScrubCoalescer _coalescer = new();

    private TunnelGestureKind _activeGesture = TunnelGestureKind.None;
    private bool _ownsGesture;
    private double _accumulatedDegrees;

    private long _pressTick;
    private long _maxTick;
    private int _focusIndex;
    private int _trackCount;
    private TunnelFineTrackBinding _fineBinding;
    private double _railCenterZ;
    private double _railHalfLength;
    private TunnelOuterTickMapping? _latestOuter;

    public TunnelGestureKind ActiveGesture => _activeGesture;
    public bool OwnsGesture => _ownsGesture;
    public double AccumulatedDegrees => _accumulatedDegrees;

    public TunnelGestureUpdate Press(TunnelHitRegion hitRegion, TunnelGesturePressContext context)
    {
        if (_ownsGesture)
            return Unhandled();

        var kind = MapHitToGesture(hitRegion);
        if (kind == TunnelGestureKind.None)
            return Unhandled();

        _activeGesture = kind;
        _ownsGesture = true;
        _accumulatedDegrees = 0.0;
        _pressTick = context.CurrentTick;
        _maxTick = context.MaxTick;
        _focusIndex = context.FocusIndex;
        _trackCount = context.TrackCount;
        _fineBinding = context.FineBinding;
        _railCenterZ = context.FineRailCenterZ;
        _railHalfLength = context.FineRailHalfLength;
        _latestOuter = null;

        return kind switch
        {
            TunnelGestureKind.OuterRing => PressOuter(context.CurrentTick),
            TunnelGestureKind.InnerRing => PressInner(),
            TunnelGestureKind.Wall => PressWall(),
            _ => Unhandled(),
        };
    }

    public TunnelGestureUpdate Motion(double signedClockwiseDeltaDegrees)
    {
        if (!_ownsGesture)
            return Unhandled();

        _accumulatedDegrees += signedClockwiseDeltaDegrees;

        return _activeGesture switch
        {
            TunnelGestureKind.OuterRing => MotionOuter(),
            TunnelGestureKind.InnerRing => MotionInner(),
            TunnelGestureKind.Wall => MotionWall(),
            _ => Unhandled(),
        };
    }

    public TunnelGestureUpdate ConsumeFrame()
    {
        if (!_ownsGesture || _activeGesture != TunnelGestureKind.OuterRing)
            return Unhandled();

        var action = _coalescer.ConsumeFrame();
        return new TunnelGestureUpdate(
            Handled: true,
            Gesture: TunnelGestureKind.OuterRing,
            AccumulatedDegrees: _accumulatedDegrees,
            ScrubAction: action,
            OuterTick: _latestOuter,
            FinePreview: null,
            CarouselSnap: null,
            FineResetReason: null);
    }

    public TunnelGestureUpdate Release()
    {
        if (!_ownsGesture)
            return Unhandled();

        var result = _activeGesture switch
        {
            TunnelGestureKind.OuterRing => ReleaseOuter(),
            TunnelGestureKind.InnerRing => ReleaseInner(),
            TunnelGestureKind.Wall => ReleaseWall(),
            _ => Unhandled(),
        };

        ClearOwnership();
        return result;
    }

    public TunnelGestureUpdate Cancel()
    {
        _coalescer.Cancel();
        ClearOwnership();
        return Unhandled();
    }

    public TunnelGestureUpdate ResetFinePreview(
        TunnelFineResetReason reason,
        TunnelFineTrackBinding binding,
        double railCenterZ,
        double railHalfLength)
    {
        var preview = TunnelFinePreviewMapper.Reset(binding, railCenterZ, railHalfLength);
        return new TunnelGestureUpdate(
            Handled: false,
            Gesture: TunnelGestureKind.None,
            AccumulatedDegrees: 0.0,
            ScrubAction: TimelineScrubAction.None,
            OuterTick: null,
            FinePreview: preview,
            CarouselSnap: null,
            FineResetReason: reason);
    }

    private TunnelGestureUpdate PressOuter(long currentTick)
    {
        var action = _coalescer.Press(currentTick);
        return new TunnelGestureUpdate(
            Handled: true,
            Gesture: TunnelGestureKind.OuterRing,
            AccumulatedDegrees: 0.0,
            ScrubAction: action,
            OuterTick: null,
            FinePreview: null,
            CarouselSnap: null,
            FineResetReason: null);
    }

    private TunnelGestureUpdate PressInner()
    {
        var preview = TunnelFinePreviewMapper.Map(_fineBinding, 0.0, _railCenterZ, _railHalfLength);
        return new TunnelGestureUpdate(
            Handled: true,
            Gesture: TunnelGestureKind.InnerRing,
            AccumulatedDegrees: 0.0,
            ScrubAction: TimelineScrubAction.None,
            OuterTick: null,
            FinePreview: preview,
            CarouselSnap: null,
            FineResetReason: null);
    }

    private TunnelGestureUpdate PressWall()
        => new(
            Handled: true,
            Gesture: TunnelGestureKind.Wall,
            AccumulatedDegrees: 0.0,
            ScrubAction: TimelineScrubAction.None,
            OuterTick: null,
            FinePreview: null,
            CarouselSnap: null,
            FineResetReason: null);

    private TunnelGestureUpdate MotionOuter()
    {
        var mapping = TunnelScrubMapper.MapOuterAngleToTick(_accumulatedDegrees, _pressTick, _maxTick);
        _latestOuter = mapping;
        var action = _coalescer.Motion(mapping.ClampedTargetTick);
        return new TunnelGestureUpdate(
            Handled: true,
            Gesture: TunnelGestureKind.OuterRing,
            AccumulatedDegrees: _accumulatedDegrees,
            ScrubAction: action,
            OuterTick: mapping,
            FinePreview: null,
            CarouselSnap: null,
            FineResetReason: null);
    }

    private TunnelGestureUpdate MotionInner()
    {
        var preview = TunnelFinePreviewMapper.Map(_fineBinding, _accumulatedDegrees, _railCenterZ, _railHalfLength);
        return new TunnelGestureUpdate(
            Handled: true,
            Gesture: TunnelGestureKind.InnerRing,
            AccumulatedDegrees: _accumulatedDegrees,
            ScrubAction: TimelineScrubAction.None,
            OuterTick: null,
            FinePreview: preview,
            CarouselSnap: null,
            FineResetReason: null);
    }

    private TunnelGestureUpdate MotionWall()
        => new(
            Handled: true,
            Gesture: TunnelGestureKind.Wall,
            AccumulatedDegrees: _accumulatedDegrees,
            ScrubAction: TimelineScrubAction.None,
            OuterTick: null,
            FinePreview: null,
            CarouselSnap: null,
            FineResetReason: null);

    private TunnelGestureUpdate ReleaseOuter()
    {
        var mapping = TunnelScrubMapper.MapOuterAngleToTick(_accumulatedDegrees, _pressTick, _maxTick);
        _latestOuter = mapping;
        var action = _coalescer.Release(mapping.ClampedTargetTick);
        return new TunnelGestureUpdate(
            Handled: true,
            Gesture: TunnelGestureKind.OuterRing,
            AccumulatedDegrees: _accumulatedDegrees,
            ScrubAction: action,
            OuterTick: mapping,
            FinePreview: null,
            CarouselSnap: null,
            FineResetReason: null);
    }

    private TunnelGestureUpdate ReleaseInner()
    {
        var preview = TunnelFinePreviewMapper.Map(_fineBinding, _accumulatedDegrees, _railCenterZ, _railHalfLength);
        return new TunnelGestureUpdate(
            Handled: true,
            Gesture: TunnelGestureKind.InnerRing,
            AccumulatedDegrees: _accumulatedDegrees,
            ScrubAction: TimelineScrubAction.None,
            OuterTick: null,
            FinePreview: preview,
            CarouselSnap: null,
            FineResetReason: null);
    }

    private TunnelGestureUpdate ReleaseWall()
    {
        var snap = TunnelCorridorLayout.SnapFocus(_focusIndex, _trackCount, _accumulatedDegrees);
        return new TunnelGestureUpdate(
            Handled: true,
            Gesture: TunnelGestureKind.Wall,
            AccumulatedDegrees: _accumulatedDegrees,
            ScrubAction: TimelineScrubAction.None,
            OuterTick: null,
            FinePreview: null,
            CarouselSnap: snap,
            FineResetReason: null);
    }

    private void ClearOwnership()
    {
        _activeGesture = TunnelGestureKind.None;
        _ownsGesture = false;
        _accumulatedDegrees = 0.0;
        _latestOuter = null;
    }

    private static TunnelGestureUpdate Unhandled()
        => new(
            Handled: false,
            Gesture: TunnelGestureKind.None,
            AccumulatedDegrees: 0.0,
            ScrubAction: TimelineScrubAction.None,
            OuterTick: null,
            FinePreview: null,
            CarouselSnap: null,
            FineResetReason: null);

    private static TunnelGestureKind MapHitToGesture(TunnelHitRegion region) => region switch
    {
        TunnelHitRegion.OuterRing => TunnelGestureKind.OuterRing,
        TunnelHitRegion.InnerRing => TunnelGestureKind.InnerRing,
        TunnelHitRegion.Wall => TunnelGestureKind.Wall,
        _ => TunnelGestureKind.None,
    };
}
