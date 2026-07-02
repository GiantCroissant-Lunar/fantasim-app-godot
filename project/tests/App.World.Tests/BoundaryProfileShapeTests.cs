using FantaSim.App.World;
using FantaSim.App.World.Topography;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Pins the SHAPE of each boundary-profile function (<see cref="BoundaryProfileShape.Contribution"/>): the
/// asymmetry of convergent subduction (trench vs arc), the symmetric swell+rift-notch of divergent, the
/// small oscillating amplitude of transform, and the zero-contribution guards.
/// </summary>
public sealed class BoundaryProfileShapeTests
{
    private static readonly BoundaryProfileParameters P = BoundaryProfileParameters.Default;

    private static CellBoundarySample Sample(double signed, PlateBoundaryKind kind,
        int cellPlate = 0, int arcA = 0, int arcB = 1, int pointIdx = 0,
        int? subducting = null, bool collision = false) =>
        new(true, signed, kind, pointIdx, cellPlate, arcA, arcB, subducting, collision);

    // ── Convergent subduction (asymmetric) ────────────────────────────────────────────────────

    [Fact]
    public void Trench_dips_negative_on_subducting_side_and_peaks_at_boundary()
    {
        // At the boundary (signed=0) on the subducting side the trench is full depth (-2000).
        double atBoundary = BoundaryProfileShape.Contribution(
            Sample(-0.0, PlateBoundaryKind.Convergent, cellPlate: 1, arcA: 0, arcB: 1, subducting: 1), P);
        Assert.Equal(P.ConvergentTrenchDepth, atBoundary, 9);

        // Slightly onto the subducting side it is still strongly negative.
        double near = BoundaryProfileShape.Contribution(
            Sample(-0.02, PlateBoundaryKind.Convergent, cellPlate: 1, arcA: 0, arcB: 1, subducting: 1), P);
        Assert.True(near < -1000.0, $"expected deep trench near boundary, got {near}");

        // Past the trench half-width it returns to zero.
        double beyond = BoundaryProfileShape.Contribution(
            Sample(-P.ConvergentTrenchHalfWidthRad - 0.01, PlateBoundaryKind.Convergent, cellPlate: 1, arcA: 0, arcB: 1, subducting: 1), P);
        Assert.Equal(0.0, beyond, 9);
    }

    [Fact]
    public void Arc_peaks_at_setback_on_overriding_side()
    {
        // On the overriding side the arc peaks at the setback distance, not at the boundary.
        double atSetback = BoundaryProfileShape.Contribution(
            Sample(P.ConvergentArcSetbackRad, PlateBoundaryKind.Convergent, cellPlate: 0, arcA: 0, arcB: 1, subducting: 1), P);
        Assert.Equal(P.ConvergentArcHeight, atSetback, 9);

        // Right at the boundary on the overriding side the arc has not started (arc is set back).
        double atBoundary = BoundaryProfileShape.Contribution(
            Sample(0.001, PlateBoundaryKind.Convergent, cellPlate: 0, arcA: 0, arcB: 1, subducting: 1), P);
        Assert.True(atBoundary < P.ConvergentArcHeight * 0.1, $"arc should be near-zero before setback, got {atBoundary}");
    }

    [Fact]
    public void Arc_tapers_to_zero_outside_arc_half_width()
    {
        double beyond = BoundaryProfileShape.Contribution(
            Sample(P.ConvergentArcSetbackRad + P.ConvergentArcHalfWidthRad + 0.01,
                   PlateBoundaryKind.Convergent, cellPlate: 0, arcA: 0, arcB: 1, subducting: 1), P);
        Assert.Equal(0.0, beyond, 9);
    }

    [Fact]
    public void Convergent_subduction_is_asymmetric_trench_negative_arc_positive()
    {
        // The trench side is negative; the arc side (at setback) is positive — the signature asymmetry.
        double trench = BoundaryProfileShape.Contribution(
            Sample(-0.01, PlateBoundaryKind.Convergent, cellPlate: 1, arcA: 0, arcB: 1, subducting: 1), P);
        double arc = BoundaryProfileShape.Contribution(
            Sample(P.ConvergentArcSetbackRad, PlateBoundaryKind.Convergent, cellPlate: 0, arcA: 0, arcB: 1, subducting: 1), P);
        Assert.True(trench < 0.0, $"trench must be negative, got {trench}");
        Assert.True(arc > 0.0, $"arc must be positive, got {arc}");
    }

    // ── Convergent collision (symmetric) ──────────────────────────────────────────────────────

    [Fact]
    public void Collision_convergent_is_symmetric_uplift_peaking_at_boundary()
    {
        double atBoundary = BoundaryProfileShape.Contribution(
            Sample(0.0, PlateBoundaryKind.Convergent, cellPlate: 0, arcA: 0, arcB: 1, collision: true), P);
        Assert.Equal(P.ConvergentCollisionHeight, atBoundary, 9);

        // Symmetric: same magnitude on both sides at equal distance.
        double left = BoundaryProfileShape.Contribution(
            Sample(-0.03, PlateBoundaryKind.Convergent, cellPlate: 1, arcA: 0, arcB: 1, collision: true), P);
        double right = BoundaryProfileShape.Contribution(
            Sample(0.03, PlateBoundaryKind.Convergent, cellPlate: 0, arcA: 0, arcB: 1, collision: true), P);
        Assert.Equal(left, right, 9);
        Assert.True(left > 0.0 && left < P.ConvergentCollisionHeight);
    }

    // ── Divergent (symmetric swell + rift notch) ──────────────────────────────────────────────

    [Fact]
    public void Divergent_swell_plus_rift_notches_at_center_below_flanks()
    {
        // At the axis: swell (full) + rift notch (full) = swell + notch.
        double center = BoundaryProfileShape.Contribution(
            Sample(0.0, PlateBoundaryKind.Divergent, cellPlate: 0, arcA: 0, arcB: 1), P);
        Assert.Equal(P.DivergentSwellHeight + P.DivergentRiftNotchDepth, center, 9);

        // Just past the narrow rift half-width the notch is gone but the swell is near-peak: flanks higher.
        double flank = BoundaryProfileShape.Contribution(
            Sample(P.DivergentRiftHalfWidthRad + 0.001, PlateBoundaryKind.Divergent, cellPlate: 0, arcA: 0, arcB: 1), P);
        Assert.True(flank > center, $"flanks ({flank}) must rise above the rifted axis ({center})");
    }

    [Fact]
    public void Divergent_is_zero_outside_swell_half_width()
    {
        double beyond = BoundaryProfileShape.Contribution(
            Sample(P.DivergentSwellHalfWidthRad + 0.01, PlateBoundaryKind.Divergent, cellPlate: 0, arcA: 0, arcB: 1), P);
        Assert.Equal(0.0, beyond, 9);
    }

    // ── Transform (small oscillating scarps) ──────────────────────────────────────────────────

    [Fact]
    public void Transform_scarp_oscillates_along_arc_and_tapers()
    {
        // At the boundary, scarps oscillate between +amplitude and -amplitude along the polyline index.
        double atZeroPhase = BoundaryProfileShape.Contribution(
            Sample(0.0, PlateBoundaryKind.Transform, cellPlate: 0, arcA: 0, arcB: 1, pointIdx: 0), P);
        double atHalfPeriod = BoundaryProfileShape.Contribution(
            Sample(0.0, PlateBoundaryKind.Transform, cellPlate: 0, arcA: 0, arcB: 1, pointIdx: (int)(P.TransformScarpPeriodPoints / 2)), P);
        Assert.Equal(P.TransformScarpAmplitude, atZeroPhase, 6);
        Assert.Equal(-P.TransformScarpAmplitude, atHalfPeriod, 6);

        // Past the transform half-width it is zero.
        double beyond = BoundaryProfileShape.Contribution(
            Sample(P.TransformHalfWidthRad + 0.01, PlateBoundaryKind.Transform, cellPlate: 0, arcA: 0, arcB: 1), P);
        Assert.Equal(0.0, beyond, 9);
    }

    [Fact]
    public void Transform_amplitude_is_small_relative_to_convergent_and_divergent()
    {
        // The locked design: transform scarps are subtle vs the convergent trench/arc and divergent swell.
        Assert.True(P.TransformScarpAmplitude < -P.ConvergentTrenchDepth);
        Assert.True(P.TransformScarpAmplitude < P.ConvergentArcHeight);
        Assert.True(P.TransformScarpAmplitude < P.DivergentSwellHeight);
    }

    // ── Guards ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Zero_parameters_yield_zero_contribution()
    {
        var z = BoundaryProfileParameters.Zero;
        Assert.Equal(0.0, BoundaryProfileShape.Contribution(
            Sample(-0.01, PlateBoundaryKind.Convergent, cellPlate: 1, arcA: 0, arcB: 1, subducting: 1), z), 9);
        Assert.Equal(0.0, BoundaryProfileShape.Contribution(
            Sample(0.0, PlateBoundaryKind.Divergent, cellPlate: 0, arcA: 0, arcB: 1), z), 9);
        Assert.Equal(0.0, BoundaryProfileShape.Contribution(
            Sample(0.0, PlateBoundaryKind.Transform, cellPlate: 0, arcA: 0, arcB: 1), z), 9);
    }

    [Fact]
    public void Contribution_is_zero_when_cell_plate_not_in_arc_plates()
    {
        // The cell belongs to neither plate of the arc — no boundary-profile contribution.
        double c = BoundaryProfileShape.Contribution(
            Sample(0.0, PlateBoundaryKind.Convergent, cellPlate: 5, arcA: 0, arcB: 1, subducting: 1), P);
        Assert.Equal(0.0, c, 9);
    }

    [Fact]
    public void Contribution_is_zero_when_not_found()
    {
        var notFound = new CellBoundarySample(false, 0.0, PlateBoundaryKind.Convergent, 0, 0, 0, 1, null, false);
        Assert.Equal(0.0, BoundaryProfileShape.Contribution(notFound, P), 9);
    }
}
