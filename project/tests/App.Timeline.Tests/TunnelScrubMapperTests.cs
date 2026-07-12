using System;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// Headless coverage for the two-ring prototype's outer-dial mapping and hit arbitration
/// (vault/plans/2026-07-12-rotating-tunnel-two-ring-prototype-plan.md Task 1). Pure Godot-free
/// seam: one accumulated clockwise revolution maps to exactly one canonical kb, rounding happens
/// once with AwayFromZero before clamping, and ring/wall hits are exclusive. The kb rung is
/// resolved from <see cref="TimelineModel.GetLadderRungs"/> exactly as production does, never
/// reconstructed.
/// </summary>
public sealed class TunnelScrubMapperTests
{
    private static TimelineLadderRung Kb() =>
        TimelineModel.GetLadderRungs().Single(r => r.Symbol == TunnelScrubMapper.OuterRungSymbol);

    // The accumulated angle that produces a desired raw tick delta, given the real kb UnitTicks.
    private static double AngleForRawTicks(double rawTicks)
        => rawTicks * 360.0 / Kb().UnitTicks;

    // ---- ResolveOuterRung ----

    [Fact]
    public void ResolveOuterRung_ReturnsTimelineModelsKbEntry()
    {
        var rung = TunnelScrubMapper.ResolveOuterRung();

        Assert.Equal(TunnelScrubMapper.OuterRungSymbol, rung.Symbol);
        var expected = Kb();
        Assert.Equal(expected.UnitTicks, rung.UnitTicks);
    }

    // ---- CanonicalPhaseDegrees ----

    [Theory]
    [InlineData(0L, 1_000L, 0.0)]
    [InlineData(250L, 1_000L, -90.0)]
    [InlineData(1_000L, 1_000L, 0.0)]
    [InlineData(2_250L, 1_000L, -90.0)]
    [InlineData(-250L, 1_000L, -270.0)]
    public void CanonicalPhaseDegrees_TracksCanonicalTickModuloUnit(
        long tick,
        long unitTicks,
        double expectedDegrees)
    {
        Assert.Equal(
            expectedDegrees,
            TunnelScrubMapper.CanonicalPhaseDegrees(tick, unitTicks),
            precision: 9);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void CanonicalPhaseDegrees_NonPositiveUnit_IsIdentity(long unitTicks)
    {
        Assert.Equal(0.0, TunnelScrubMapper.CanonicalPhaseDegrees(123L, unitTicks));
    }

    [Fact]
    public void CanonicalPhaseDegrees_UsesTheRealRoundedKbPeriodConsumedByProduction()
    {
        var unitTicks = TimelineModel.SpanTicksForRung(Kb(), units: 1);

        Assert.Equal(100_000_000L, unitTicks);
        Assert.Equal(-180.0,
            TunnelScrubMapper.CanonicalPhaseDegrees(50_000_000L, unitTicks),
            precision: 9);
        Assert.Equal(0.0,
            TunnelScrubMapper.CanonicalPhaseDegrees(100_000_000L, unitTicks),
            precision: 9);
    }

    [Fact]
    public void CanonicalPhaseDegrees_ExternalSeekBackToZero_ClearsPriorPose()
    {
        var prior = TunnelScrubMapper.CanonicalPhaseDegrees(500L, 1_000L);

        var afterSeek = TunnelScrubMapper.CanonicalPhaseDegrees(0L, 1_000L);

        Assert.Equal(-180.0, prior, precision: 9);
        Assert.Equal(0.0, afterSeek, precision: 9);
    }

    // ---- MapOuterAngleToTick ----

    [Fact]
    public void MapOuterAngleToTick_PositiveFullRevolution_AddsExactlyOneKb()
    {
        var kb = Kb();
        var mapping = TunnelScrubMapper.MapOuterAngleToTick(360.0, pressTick: 1_000L, maxTick: long.MaxValue);

        Assert.Equal(360.0, mapping.AccumulatedDegrees);
        Assert.Equal(kb.Symbol, mapping.Rung.Symbol);
        Assert.Equal(kb.UnitTicks, mapping.RawTickDelta);
        Assert.Equal((long)Math.Round(kb.UnitTicks, MidpointRounding.AwayFromZero), mapping.RoundedTickDelta);
        Assert.Equal(1_000L + mapping.RoundedTickDelta, mapping.RoundedTargetTick);
        Assert.Equal(mapping.RoundedTargetTick, mapping.ClampedTargetTick);
    }

    [Fact]
    public void MapOuterAngleToTick_NegativeFullRevolution_SubtractsExactlyOneKb()
    {
        var kb = Kb();
        // pressTick comfortably above one kb so the target stays in range and is not clamped.
        var pressTick = (long)Math.Round(kb.UnitTicks, MidpointRounding.AwayFromZero) * 3L;
        var mapping = TunnelScrubMapper.MapOuterAngleToTick(-360.0, pressTick: pressTick, maxTick: long.MaxValue);

        Assert.Equal(-kb.UnitTicks, mapping.RawTickDelta);
        Assert.Equal(-(long)Math.Round(kb.UnitTicks, MidpointRounding.AwayFromZero), mapping.RoundedTickDelta);
        Assert.Equal(pressTick + mapping.RoundedTickDelta, mapping.RoundedTargetTick);
        Assert.Equal(mapping.RoundedTargetTick, mapping.ClampedTargetTick);
    }

    [Fact]
    public void MapOuterAngleToTick_FractionalAndMultipleRevolutions_MapProportionally()
    {
        var kb = Kb();

        var half = TunnelScrubMapper.MapOuterAngleToTick(180.0, 0L, long.MaxValue);
        Assert.Equal(0.5 * kb.UnitTicks, half.RawTickDelta, precision: 9);

        // Three revolutions: raw delta = 3 kb units.
        var triple = TunnelScrubMapper.MapOuterAngleToTick(360.0 * 3.0, 0L, long.MaxValue);
        Assert.Equal(3.0 * kb.UnitTicks, triple.RawTickDelta, precision: 6);
        Assert.Equal((long)Math.Round(3.0 * kb.UnitTicks, MidpointRounding.AwayFromZero), triple.RoundedTickDelta);
    }

    [Fact]
    public void MapOuterAngleToTick_PositiveAndNegativeMidpoints_RoundSymmetricallyAwayFromZero()
    {
        // Angles chosen so rawTickDelta is exactly +0.5 and -0.5 regardless of kb.UnitTicks.
        var positive = TunnelScrubMapper.MapOuterAngleToTick(AngleForRawTicks(0.5), 0L, long.MaxValue);
        var negative = TunnelScrubMapper.MapOuterAngleToTick(AngleForRawTicks(-0.5), 0L, long.MaxValue);

        // AwayFromZero: +0.5 -> 1, -0.5 -> -1 (symmetric, not toward zero).
        Assert.Equal(1L, positive.RoundedTickDelta);
        Assert.Equal(-1L, negative.RoundedTickDelta);
        Assert.Equal(1L, positive.RoundedTargetTick);
        Assert.Equal(-1L, negative.RoundedTargetTick);
    }

    [Fact]
    public void MapOuterAngleToTick_RoundsBeforeClamping()
    {
        // maxTick = 0 so any positive rounded target must clamp to 0. The RoundedTargetTick still
        // records the pre-clamp rounded value (1), proving the round happened before the clamp.
        var mapping = TunnelScrubMapper.MapOuterAngleToTick(AngleForRawTicks(0.6), pressTick: 0L, maxTick: 0L);

        Assert.Equal(1L, mapping.RoundedTickDelta);
        Assert.Equal(1L, mapping.RoundedTargetTick);
        Assert.Equal(0L, mapping.ClampedTargetTick);
    }

    [Fact]
    public void MapOuterAngleToTick_ClampsAtBothTimelineBounds()
    {
        // Upper bound: a large positive angle clamps to maxTick.
        var upper = TunnelScrubMapper.MapOuterAngleToTick(360.0 * 1_000_000.0, pressTick: 0L, maxTick: 42L);
        Assert.Equal(42L, upper.ClampedTargetTick);

        // Lower bound: a large negative angle clamps to 0.
        var lower = TunnelScrubMapper.MapOuterAngleToTick(-360.0 * 1_000_000.0, pressTick: 100L, maxTick: 42L);
        Assert.Equal(0L, lower.ClampedTargetTick);
    }

    [Fact]
    public void MapOuterAngleToTick_PathologicalFiniteAngles_SaturateBeforeFinalClamp()
    {
        // A finite angle so large the raw tick delta overflows long: saturating conversion must
        // cap at long.MaxValue, then the final clamp brings it back to maxTick without overflow.
        var huge = TunnelScrubMapper.MapOuterAngleToTick(1e18, pressTick: 0L, maxTick: 99L);
        Assert.Equal(99L, huge.ClampedTargetTick);
        Assert.True(huge.RoundedTickDelta > 0L);

        var hugeNeg = TunnelScrubMapper.MapOuterAngleToTick(-1e18, pressTick: 5_000L, maxTick: 99L);
        Assert.Equal(0L, hugeNeg.ClampedTargetTick);
        Assert.True(hugeNeg.RoundedTickDelta < 0L);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void MapOuterAngleToTick_NonFiniteAngle_Throws(double angle)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TunnelScrubMapper.MapOuterAngleToTick(angle, pressTick: 0L, maxTick: 100L));
    }

    // ---- NormalizeClockwiseDeltaDegrees ----

    [Fact]
    public void NormalizeClockwiseDeltaDegrees_UnwrapsBothDirectionsAcrossBranchCut()
    {
        // previous=-179, current=179: the pointer crossed the +X axis by +2deg, not -358deg.
        Assert.Equal(2.0, TunnelScrubMapper.NormalizeClockwiseDeltaDegrees(-179.0, 179.0));
        // Reverse crossing: 179 -> -179 is -2deg.
        Assert.Equal(-2.0, TunnelScrubMapper.NormalizeClockwiseDeltaDegrees(179.0, -179.0));
    }

    [Fact]
    public void NormalizeClockwiseDeltaDegrees_ClockwiseIsPositive()
    {
        // In screen coordinates increasing angle is counter-clockwise (math convention) while the
        // product contract says clockwise is positive, so the delta is negated.
        Assert.Equal(-10.0, TunnelScrubMapper.NormalizeClockwiseDeltaDegrees(0.0, 10.0));
        Assert.Equal(10.0, TunnelScrubMapper.NormalizeClockwiseDeltaDegrees(10.0, 0.0));
    }

    [Theory]
    [InlineData(double.NaN, 0.0)]
    [InlineData(0.0, double.NaN)]
    [InlineData(double.PositiveInfinity, 1.0)]
    public void NormalizeClockwiseDeltaDegrees_NonFiniteInputThrows(double previous, double current)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TunnelScrubMapper.NormalizeClockwiseDeltaDegrees(previous, current));
    }

    // ---- ResolveHitRegion ----

    [Fact]
    public void ResolveHitRegion_EachSingleCandidate_IsExclusive()
    {
        Assert.Equal(TunnelHitRegion.OuterRing, TunnelScrubMapper.ResolveHitRegion(outerRingHit: true, innerRingHit: false, wallHit: false));
        Assert.Equal(TunnelHitRegion.InnerRing, TunnelScrubMapper.ResolveHitRegion(outerRingHit: false, innerRingHit: true, wallHit: false));
        Assert.Equal(TunnelHitRegion.Wall, TunnelScrubMapper.ResolveHitRegion(outerRingHit: false, innerRingHit: false, wallHit: true));
        Assert.Equal(TunnelHitRegion.None, TunnelScrubMapper.ResolveHitRegion(outerRingHit: false, innerRingHit: false, wallHit: false));
    }

    [Fact]
    public void ResolveHitRegion_RingWinsOverBackingWall()
    {
        // A ring sits in front of the wall at the same angular position; the ring wins.
        Assert.Equal(TunnelHitRegion.OuterRing, TunnelScrubMapper.ResolveHitRegion(true, false, true));
        Assert.Equal(TunnelHitRegion.InnerRing, TunnelScrubMapper.ResolveHitRegion(false, true, true));
    }

    [Fact]
    public void ResolveHitRegion_AmbiguousRings_ReturnsNone()
    {
        // Both rings hit at once is ambiguous -- refuse to own the gesture.
        Assert.Equal(TunnelHitRegion.None, TunnelScrubMapper.ResolveHitRegion(true, true, false));
        Assert.Equal(TunnelHitRegion.None, TunnelScrubMapper.ResolveHitRegion(true, true, true));
    }
}
