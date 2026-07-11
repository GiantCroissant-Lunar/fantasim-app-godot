using System;
using System.Linq;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// Headless coverage for the two-ring prototype's one-owner gesture coordinator
/// (vault/plans/2026-07-12-rotating-tunnel-two-ring-prototype-plan.md Task 3). Pure Godot-free:
/// press exclusivity, refused owner replacement, per-frame latest-only echo, latest-value release
/// with exactly one commit, cancel without commit, inner non-authority, inactive-inner inert
/// ownership, wall snap, and the fine-preview reset reasons.
/// </summary>
public sealed class TunnelGestureCoordinatorTests
{
    private const long MaxTick = 2_000_000L;
    private const double RailCenterZ = -7.0;
    private const double RailHalfLength = 2.5;
    private static readonly TimelineLadderRung GlobalFallback = TimelineModel.GetLadderRungs().First();

    private static LayerTrackDescriptor Descriptor(string layerId = "geosphere.crust", string rung = "ka") => new(
        SphereId: "geosphere",
        LayerId: layerId,
        StreamId: new LayerTrackStreamId("main", "default", "L0", "world", "default"),
        DisplayName: layerId,
        State: LayerTrackStates.Declared,
        TimeDomain: new LayerTrackTimeDomain(0L, null, rung),
        Content: new LayerTrackContent("filmstrip"),
        Capabilities: new[] { "scrub" },
        SourceRef: layerId);

    private static TunnelGesturePressContext Context(
        long tick = 1_000_000L,
        TunnelFineTrackBinding? binding = null,
        int focusIndex = 0,
        int trackCount = 3)
        => new(
            CurrentTick: tick,
            MaxTick: MaxTick,
            FocusIndex: focusIndex,
            TrackCount: trackCount,
            FineBinding: binding ?? TunnelFinePreviewMapper.Bind(Descriptor(), true, GlobalFallback),
            FineRailCenterZ: RailCenterZ,
            FineRailHalfLength: RailHalfLength);

    // ---- Press exclusivity ----

    [Fact]
    public void Press_NoneHit_IsUnhandled()
    {
        var coord = new TunnelGestureCoordinator();

        var update = coord.Press(TunnelHitRegion.None, Context());

        Assert.False(update.Handled);
        Assert.Equal(TunnelGestureKind.None, update.Gesture);
        Assert.False(coord.OwnsGesture);
    }

    [Fact]
    public void Press_OuterRing_IsHandledAndAppliesPreview()
    {
        var coord = new TunnelGestureCoordinator();

        var update = coord.Press(TunnelHitRegion.OuterRing, Context(tick: 500_000L));

        Assert.True(update.Handled);
        Assert.Equal(TunnelGestureKind.OuterRing, update.Gesture);
        Assert.True(coord.OwnsGesture);
        Assert.True(update.ScrubAction.ShouldApply);
        Assert.Equal(TimelineTickOrigin.ScrubPreview, update.ScrubAction.Origin);
        Assert.Equal(500_000L, update.ScrubAction.Tick);
    }

    [Fact]
    public void Press_InnerRing_IsHandledEvenWhenInactive()
    {
        var coord = new TunnelGestureCoordinator();
        var inactiveBinding = TunnelFinePreviewMapper.Bind(Descriptor(), isActive: false, GlobalFallback);

        var update = coord.Press(TunnelHitRegion.InnerRing, Context(binding: inactiveBinding));

        Assert.True(update.Handled);
        Assert.Equal(TunnelGestureKind.InnerRing, update.Gesture);
        Assert.True(coord.OwnsGesture);
        // Inactive binding yields a centered/inert preview.
        Assert.NotNull(update.FinePreview);
        Assert.Equal(0.0, update.FinePreview!.Value.AccumulatedDegrees, precision: 6);
    }

    [Fact]
    public void Press_Wall_IsHandled()
    {
        var coord = new TunnelGestureCoordinator();

        var update = coord.Press(TunnelHitRegion.Wall, Context());

        Assert.True(update.Handled);
        Assert.Equal(TunnelGestureKind.Wall, update.Gesture);
        Assert.True(coord.OwnsGesture);
        Assert.False(update.ScrubAction.ShouldApply);
    }

    [Fact]
    public void Press_WhileAlreadyOwning_IsRefused()
    {
        var coord = new TunnelGestureCoordinator();
        coord.Press(TunnelHitRegion.OuterRing, Context());

        var second = coord.Press(TunnelHitRegion.Wall, Context());

        Assert.False(second.Handled);
        Assert.Equal(TunnelGestureKind.OuterRing, coord.ActiveGesture);
    }

    // ---- Outer motion + per-frame echo + latest-value release ----

    [Fact]
    public void OuterMotion_StoresAccumulatedAngleAndCoalesces()
    {
        var coord = new TunnelGestureCoordinator();
        coord.Press(TunnelHitRegion.OuterRing, Context(tick: 100_000L));

        var m1 = coord.Motion(180.0);
        var m2 = coord.Motion(90.0);

        Assert.Equal(180.0, m1.AccumulatedDegrees, precision: 6);
        Assert.Equal(270.0, m2.AccumulatedDegrees, precision: 6);
        Assert.NotNull(m2.OuterTick);
        // Motion is coalesced: not applied immediately.
        Assert.False(m2.ScrubAction.ShouldApply);
    }

    [Fact]
    public void ConsumeFrame_ReturnsLatestPreviewOnce()
    {
        var coord = new TunnelGestureCoordinator();
        coord.Press(TunnelHitRegion.OuterRing, Context(tick: 100_000L));
        coord.Motion(120.0);
        coord.Motion(60.0);

        var frame1 = coord.ConsumeFrame();
        var frame2 = coord.ConsumeFrame();

        Assert.True(frame1.ScrubAction.ShouldApply);
        Assert.Equal(180.0, frame1.AccumulatedDegrees, precision: 6);
        Assert.False(frame2.ScrubAction.ShouldApply);
    }

    [Fact]
    public void Release_CommitsLatestAccumulatedAngle_EvenIfMotionNotConsumed()
    {
        var coord = new TunnelGestureCoordinator();
        coord.Press(TunnelHitRegion.OuterRing, Context(tick: 100_000L));
        coord.Motion(200.0);
        // Do NOT ConsumeFrame: the pending motion is still queued.

        var release = coord.Release();

        Assert.True(release.ScrubAction.ShouldApply);
        Assert.Equal(TimelineTickOrigin.ScrubCommit, release.ScrubAction.Origin);
        // The commit must equal the latest mapping's clamped target, not a stale cached tick.
        var expected = TunnelScrubMapper.MapOuterAngleToTick(200.0, 100_000L, MaxTick);
        Assert.Equal(expected.ClampedTargetTick, release.ScrubAction.Tick);
        Assert.False(coord.OwnsGesture);
    }

    [Fact]
    public void Release_SecondRelease_IsUnhandledNoCommit()
    {
        var coord = new TunnelGestureCoordinator();
        coord.Press(TunnelHitRegion.OuterRing, Context(tick: 100_000L));
        coord.Release();

        var second = coord.Release();

        Assert.False(second.Handled);
        Assert.False(second.ScrubAction.ShouldApply);
    }

    [Fact]
    public void Motion_WithoutPress_IsUnhandled()
    {
        var coord = new TunnelGestureCoordinator();

        var update = coord.Motion(50.0);

        Assert.False(update.Handled);
        Assert.Equal(0.0, update.AccumulatedDegrees);
    }

    // ---- Inner non-authority ----

    [Fact]
    public void InnerMotion_ReturnsFinePreviewOnly_NoScrubAction()
    {
        var coord = new TunnelGestureCoordinator();
        coord.Press(TunnelHitRegion.InnerRing, Context());

        var motion = coord.Motion(90.0);
        var release = coord.Release();

        Assert.False(motion.ScrubAction.ShouldApply);
        Assert.NotNull(motion.FinePreview);
        Assert.Equal(90.0, motion.FinePreview!.Value.AccumulatedDegrees, precision: 6);
        Assert.False(release.ScrubAction.ShouldApply);
        Assert.False(coord.OwnsGesture);
    }

    // ---- Wall snap ----

    [Fact]
    public void WallRelease_ReturnsCarouselSnap_NoScrubAction()
    {
        var coord = new TunnelGestureCoordinator();
        coord.Press(TunnelHitRegion.Wall, Context(focusIndex: 0, trackCount: 5));
        coord.Motion(16.0); // past the 15deg threshold -> StepDelta 1.

        var release = coord.Release();

        Assert.True(release.Handled);
        Assert.NotNull(release.CarouselSnap);
        Assert.Equal(1L, release.CarouselSnap!.Value.StepDelta);
        Assert.Equal(1, release.CarouselSnap.Value.FocusIndex);
        Assert.False(release.ScrubAction.ShouldApply);
        Assert.False(coord.OwnsGesture);
    }

    // ---- Cancel ----

    [Fact]
    public void Cancel_ClearsOwnershipWithoutCommit()
    {
        var coord = new TunnelGestureCoordinator();
        coord.Press(TunnelHitRegion.OuterRing, Context(tick: 100_000L));
        coord.Motion(200.0);

        var cancel = coord.Cancel();

        Assert.False(cancel.Handled);
        Assert.False(cancel.ScrubAction.ShouldApply);
        Assert.False(coord.OwnsGesture);
        Assert.Equal(0.0, coord.AccumulatedDegrees);
    }

    // ---- Fine reset reasons ----

    [Theory]
    [InlineData(TunnelFineResetReason.FocusChanged)]
    [InlineData(TunnelFineResetReason.BaseTimeChanged)]
    [InlineData(TunnelFineResetReason.Disabled)]
    [InlineData(TunnelFineResetReason.ControllerLost)]
    [InlineData(TunnelFineResetReason.BundleTeardown)]
    [InlineData(TunnelFineResetReason.Disposed)]
    public void ResetFinePreview_ReturnsCenteredPreviewWithReason(TunnelFineResetReason reason)
    {
        var coord = new TunnelGestureCoordinator();
        var binding = TunnelFinePreviewMapper.Bind(Descriptor(), true, GlobalFallback);

        var reset = coord.ResetFinePreview(reason, binding, RailCenterZ, RailHalfLength);

        Assert.Equal(reason, reset.FineResetReason);
        Assert.NotNull(reset.FinePreview);
        Assert.Equal(0.0, reset.FinePreview!.Value.AccumulatedDegrees, precision: 6);
        Assert.Equal(RailCenterZ, reset.FinePreview.Value.CursorZ, precision: 6);
    }
}
