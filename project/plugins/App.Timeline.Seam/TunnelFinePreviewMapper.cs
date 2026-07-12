using System;
using FantaSim.App.Timeline;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// The focused track's binding for the inner ring (vault/plans/2026-07-12-rotating-tunnel-two-ring-
/// prototype-plan.md Task 3). Owner label degrades to "No track" for an empty focus; CanAdjust
/// requires a descriptor, a resolved rung, and an active regime.
/// </summary>
public readonly record struct TunnelFineTrackBinding(
    LayerTrackDescriptor? Descriptor,
    TimelineLadderRung? Rung,
    bool IsActive)
{
    public string OwnerLabel => Descriptor?.DisplayName ?? "No track";
    public bool CanAdjust => Descriptor is not null && Rung is not null && IsActive;
}

/// <summary>
/// The provisional fine-preview record: accumulated clamped angle, rung-unit fraction, raw tick
/// quantity, the optional integral tick delta (set only when the quantity is whole), and the axial
/// cursor Z. IsFractionalPresentation is true when the binding can adjust but the quantity is not a
/// whole number of ticks.
/// </summary>
public readonly record struct TunnelFinePreview(
    TunnelFineTrackBinding Binding,
    double AccumulatedDegrees,
    double RungUnits,
    double RawTickQuantity,
    long? IntegralTickDelta,
    double CursorZ)
{
    public bool IsFractionalPresentation => Binding.CanAdjust && IntegralTickDelta is null;
}

/// <summary>
/// Pure Godot-free seam for the inner ring's provisional fine preview. Clamps accumulated angle to
/// ±360°, computes rung-unit and raw-tick quantities from the focused descriptor's real rung, and
/// positions the axial cursor toward the throat for positive (clockwise) motion. An inactive or
/// missing binding stays centered and zero; a fractional or sub-tick quantity never rounds into an
/// integral tick delta.
/// </summary>
public static class TunnelFinePreviewMapper
{
    public const double MaxAbsoluteDegrees = 360d;
    private const double WholeTickTolerance = 1e-6;

    /// <summary>Resolves the descriptor's rung against the canonical ladder with fallback.</summary>
    public static TunnelFineTrackBinding Bind(
        LayerTrackDescriptor? descriptor,
        bool isActive,
        TimelineLadderRung globalFallback)
    {
        if (descriptor is null)
            return new TunnelFineTrackBinding(null, null, false);

        var rung = TunnelCorridorLayout.ResolveCorridorRung(descriptor.TimeDomain.Rung, globalFallback);
        return new TunnelFineTrackBinding(descriptor, rung, isActive);
    }

    /// <summary>
    /// Maps an accumulated signed angle to a fine preview. Accumulated angle is clamped to
    /// ±360°; RungUnits = degrees/360; RawTickQuantity = RungUnits * UnitTicks; CursorZ =
    /// centerZ - RungUnits * abs(halfLength) (positive/clockwise moves toward the throat). An
    /// inactive or missing binding returns a centered/zero preview. IntegralTickDelta is set only
    /// when RawTickQuantity is whole within tolerance.
    /// </summary>
    public static TunnelFinePreview Map(
        TunnelFineTrackBinding binding,
        double accumulatedDegrees,
        double railCenterZ,
        double railHalfLength)
    {
        if (!double.IsFinite(accumulatedDegrees))
            throw new ArgumentOutOfRangeException(
                nameof(accumulatedDegrees),
                accumulatedDegrees,
                "Accumulated angle must be finite; a non-finite value indicates an upstream gesture defect.");

        if (!binding.CanAdjust || binding.Rung is not { } rung)
            return ZeroedPreview(binding, railCenterZ);

        var clamped = Math.Clamp(accumulatedDegrees, -MaxAbsoluteDegrees, MaxAbsoluteDegrees);
        var rungUnits = clamped / 360.0;
        var rawTickQuantity = rungUnits * rung.UnitTicks;
        var cursorZ = railCenterZ - rungUnits * Math.Abs(railHalfLength);
        var integral = TryIntegralTickDelta(rawTickQuantity);

        return new TunnelFinePreview(
            Binding: binding,
            AccumulatedDegrees: clamped,
            RungUnits: rungUnits,
            RawTickQuantity: rawTickQuantity,
            IntegralTickDelta: integral,
            CursorZ: cursorZ);
    }

    /// <summary>Returns a centered/zero preview for the binding (focus/base/disable/reload reset).</summary>
    public static TunnelFinePreview Reset(
        TunnelFineTrackBinding binding,
        double railCenterZ,
        double railHalfLength)
        => ZeroedPreview(binding, railCenterZ);

    private static TunnelFinePreview ZeroedPreview(TunnelFineTrackBinding binding, double railCenterZ)
        => new(
            Binding: binding,
            AccumulatedDegrees: 0.0,
            RungUnits: 0.0,
            RawTickQuantity: 0.0,
            IntegralTickDelta: binding.CanAdjust ? 0L : null,
            CursorZ: railCenterZ);

    private static long? TryIntegralTickDelta(double rawTickQuantity)
    {
        if (!double.IsFinite(rawTickQuantity))
            return null;

        var rounded = Math.Round(rawTickQuantity, MidpointRounding.AwayFromZero);
        // double cannot represent Int64.MaxValue distinctly from 2^63. Treat that positive bound
        // as exclusive while retaining the exactly representable Int64.MinValue lower bound.
        if (rounded < -9_223_372_036_854_775_808d || rounded >= 9_223_372_036_854_775_808d)
            return null;

        var distance = Math.Abs(rawTickQuantity - rounded);
        // Scale the tolerance only below one tick. This preserves tiny floating noise at exact
        // whole coarse quantities without letting magnitude make arbitrary coarse fractions whole.
        var tolerance = WholeTickTolerance * Math.Min(1d, Math.Abs(rawTickQuantity));
        if (distance <= tolerance)
            return (long)rounded;

        return null;
    }
}
