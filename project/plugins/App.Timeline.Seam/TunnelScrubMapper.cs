using System;
using FantaSim.App.Timeline;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Pure Godot-free seam for the two-ring prototype's outer-dial mapping and hit arbitration
/// (vault/plans/2026-07-12-rotating-tunnel-two-ring-prototype-plan.md Task 1). One accumulated
/// clockwise revolution maps to exactly one canonical kb; rounding happens once with
/// <see cref="MidpointRounding.AwayFromZero"/> before the final clamp; ring/wall hits are
/// exclusive. The kb rung is resolved from <see cref="TimelineModel.GetLadderRungs"/> exactly as
/// production does.
/// </summary>
public enum TunnelHitRegion
{
    None = 0,
    OuterRing = 1,
    InnerRing = 2,
    Wall = 3,
}

/// <summary>
/// The full outer-dial evidence record emitted during a gesture and in the structured log: the
/// accumulated signed angle, the resolved kb rung, the raw proportional tick quantity, the
/// once-rounded tick delta, the rounded target (pre-clamp), and the final clamped target tick.
/// </summary>
public readonly record struct TunnelOuterTickMapping(
    double AccumulatedDegrees,
    TimelineLadderRung Rung,
    double RawTickDelta,
    long RoundedTickDelta,
    long RoundedTargetTick,
    long ClampedTargetTick);

public static class TunnelScrubMapper
{
    /// <summary>The canonical coarse-time rung one outer revolution advances by.</summary>
    public const string OuterRungSymbol = "kb";

    /// <summary>Resolves the real kb entry from <see cref="TimelineModel.GetLadderRungs"/>; fails
    /// loudly if the canonical ladder lacks kb rather than substituting another unit.</summary>
    public static TimelineLadderRung ResolveOuterRung()
    {
        foreach (var rung in TimelineModel.GetLadderRungs())
        {
            if (rung.Symbol == OuterRungSymbol)
                return rung;
        }

        throw new InvalidOperationException(
            $"The canonical timeline ladder has no '{OuterRungSymbol}' rung; the outer dial cannot map revolutions to canonical ticks.");
    }

    /// <summary>
    /// Returns the deterministic visual phase for a canonical tick within one unit period. A full
    /// positive period returns to zero; negative ticks wrap into the same clockwise-negative range.
    /// </summary>
    public static double CanonicalPhaseDegrees(long tick, long unitTicks)
    {
        if (unitTicks <= 0L)
            return 0d;

        var remainder = tick % unitTicks;
        if (remainder < 0L)
            remainder += unitTicks;
        return -360d * remainder / unitTicks;
    }

    /// <summary>
    /// Maps an accumulated signed clockwise angle to a clamped target tick. The mapping is
    /// <c>pressTick + RoundAwayFromZero(accumulatedDegrees / 360 * kb.UnitTicks)</c>, rounded exactly
    /// once before clamping to <c>[0, max(0, maxTick)]</c>. Non-finite angles throw; pathological
    /// finite angles saturate at <see cref="long.MaxValue"/>/<see cref="long.MinValue"/> before the
    /// final clamp so no cast produces false evidence.
    /// </summary>
    public static TunnelOuterTickMapping MapOuterAngleToTick(double accumulatedDegrees, long pressTick, long maxTick)
    {
        if (!double.IsFinite(accumulatedDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(accumulatedDegrees), accumulatedDegrees,
                "Accumulated outer-dial angle must be a finite number of degrees.");
        }

        var rung = ResolveOuterRung();
        var rawTickDelta = accumulatedDegrees / 360.0 * rung.UnitTicks;
        var roundedDelta = SaturatingRoundAwayFromZero(rawTickDelta);
        var roundedTarget = SaturatingAdd(pressTick, roundedDelta);
        var ceiling = Math.Max(0L, maxTick);
        var clampedTarget = Math.Clamp(roundedTarget, 0L, ceiling);

        return new TunnelOuterTickMapping(
            AccumulatedDegrees: accumulatedDegrees,
            Rung: rung,
            RawTickDelta: rawTickDelta,
            RoundedTickDelta: roundedDelta,
            RoundedTargetTick: roundedTarget,
            ClampedTargetTick: clampedTarget);
    }

    /// <summary>
    /// Normalizes a pointer-angle delta (degrees) into <c>[-180, +180]</c> and negates it so
    /// clockwise motion is positive. <paramref name="previousPointerDegrees"/> and
    /// <paramref name="currentPointerDegrees"/> are raw <c>atan2(y, x)</c> values in a Y-up local
    /// frame (Godot mount-local XY), where increasing angle is counter-clockwise; the negation
    /// converts that to the product contract's clockwise-positive convention. Non-finite inputs
    /// throw.
    /// </summary>
    public static double NormalizeClockwiseDeltaDegrees(double previousPointerDegrees, double currentPointerDegrees)
    {
        if (!double.IsFinite(previousPointerDegrees) || !double.IsFinite(currentPointerDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(previousPointerDegrees),
                $"Pointer angles must be finite (previous={previousPointerDegrees}, current={currentPointerDegrees}).");
        }

        var rawDelta = currentPointerDegrees - previousPointerDegrees;
        var unwrapped = WrapToPlusMinus180(rawDelta);
        return -unwrapped;
    }

    /// <summary>
    /// Resolves the exclusive hit region for a press: a single ring wins over its backing wall;
    /// both rings hit at once is ambiguous and returns <see cref="TunnelHitRegion.None"/>; nothing
    /// hit returns <see cref="TunnelHitRegion.None"/>.
    /// </summary>
    public static TunnelHitRegion ResolveHitRegion(bool outerRingHit, bool innerRingHit, bool wallHit)
    {
        if (outerRingHit && innerRingHit)
            return TunnelHitRegion.None;

        if (outerRingHit)
            return TunnelHitRegion.OuterRing;

        if (innerRingHit)
            return TunnelHitRegion.InnerRing;

        if (wallHit)
            return TunnelHitRegion.Wall;

        return TunnelHitRegion.None;
    }

    private static double WrapToPlusMinus180(double degrees)
    {
        // Maps any finite angle into [-180, +180] so a branch-cut crossing is reported as the small
        // signed step, not a full-revolution jump: e.g. +358 -> -2, -358 -> +2.
        return ((degrees + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
    }

    // Rounds once with AwayFromZero, saturating at the long bounds so a pathological finite raw
    // delta cannot overflow the cast into false evidence. The caller has already rejected NaN/inf.
    private static long SaturatingRoundAwayFromZero(double value)
    {
        if (value >= long.MaxValue)
            return long.MaxValue;
        if (value <= long.MinValue)
            return long.MinValue;
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static long SaturatingAdd(long a, long b)
    {
        if (b > 0 && a > long.MaxValue - b)
            return long.MaxValue;
        if (b < 0 && a < long.MinValue - b)
            return long.MinValue;
        return a + b;
    }
}
