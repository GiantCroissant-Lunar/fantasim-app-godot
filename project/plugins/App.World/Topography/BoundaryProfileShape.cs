using System;

namespace FantaSim.App.World.Topography;

/// <summary>
/// Pure boundary-profile elevation functions (P4). Given a cell's <see cref="CellBoundarySample"/> and the
/// world's <see cref="BoundaryProfileParameters"/>, returns the elevation contribution that shapes the crust
/// view's topography at plate boundaries — added on top of <c>CellElevationSystem.Derive</c>. All sphere
/// geometry is resolved upstream (<see cref="CellBoundaryField"/>); this is pure scalar math, fully unit-testable.
///
/// <para>Shapes (Earth-like defaults in <see cref="BoundaryProfileParameters.Default"/>):</para>
/// <list type="bullet">
/// <item><b>Convergent subduction (asymmetric):</b> deep trench on the subducting (down-going) side tapering
/// from the boundary; uplift volcanic arc set back onto the overriding side.</item>
/// <item><b>Convergent collision (symmetric):</b> symmetric uplift band (continent–continent; no subduction).</item>
/// <item><b>Divergent (symmetric):</b> broad swell rising toward the boundary with a narrow axial rift notch
/// at it (the crest sits below the flanks). Far-field subsidence is already handled by crust-age deepening.</item>
/// <item><b>Transform:</b> small-amplitude narrow band of linear scarps oscillating along the boundary.</item>
/// </list>
/// </summary>
public static class BoundaryProfileShape
{
    /// <summary>
    /// The boundary-profile elevation contribution for one cell. Zero when the cell has no boundary
    /// (<see cref="CellBoundarySample.Found"/> is false), when the cell's plate is not one of the arc's two
    /// plates, or when all amplitudes are zero (<see cref="BoundaryProfileParameters.Zero"/>).
    /// </summary>
    public static double Contribution(in CellBoundarySample s, in BoundaryProfileParameters p)
    {
        if (!s.Found) return 0.0;
        if (s.CellPlateId != s.ArcPlateA && s.CellPlateId != s.ArcPlateB) return 0.0;

        return s.Kind switch
        {
            PlateBoundaryKind.Convergent => s.IsCollision
                ? ConvergentCollision(s.SignedDistanceRad, p)
                : ConvergentSubduction(s.SignedDistanceRad, p),
            PlateBoundaryKind.Divergent => Divergent(s.SignedDistanceRad, p),
            PlateBoundaryKind.Transform => Transform(s.SignedDistanceRad, s.TransformPhaseCoordinate, p),
            _ => 0.0,
        };
    }

    // Asymmetric trench (subducting side, signed distance <= 0) + arc (overriding side, signed distance > 0).
    // The trench is deepest at the boundary and tapers across TrenchHalfWidth; the arc peaks at ArcSetback
    // onto the overriding plate and tapers across ArcHalfWidth.
    private static double ConvergentSubduction(double signed, in BoundaryProfileParameters p)
    {
        if (signed <= 0.0)
        {
            double d = -signed;
            return p.ConvergentTrenchDepth * Falloff(d, p.ConvergentTrenchHalfWidthRad);
        }

        double u = signed - p.ConvergentArcSetbackRad;
        return p.ConvergentArcHeight * Falloff(u, p.ConvergentArcHalfWidthRad);
    }

    // Symmetric collision uplift (both sides identical): peaks at the boundary, tapers across CollisionHalfWidth.
    private static double ConvergentCollision(double signed, in BoundaryProfileParameters p)
        => p.ConvergentCollisionHeight * Falloff(Math.Abs(signed), p.ConvergentCollisionHalfWidthRad);

    // Symmetric swell (broad) + axial rift notch (narrow, negative). At the axis the notch subtracts from the
    // swell crest so the spreading centre sits below the flanks.
    private static double Divergent(double signed, in BoundaryProfileParameters p)
    {
        double d = Math.Abs(signed);
        return p.DivergentSwellHeight * Falloff(d, p.DivergentSwellHalfWidthRad)
             + p.DivergentRiftNotchDepth * Falloff(d, p.DivergentRiftHalfWidthRad);
    }

    // Narrow-band scarps oscillating along the boundary (cosine over a stable world-space phase
    // coordinate) tapering across TransformHalfWidth. The period remains expressed in the
    // established pseudo-sample-point unit, but no longer resets for every edge-local visual arc.
    private static double Transform(double signed, double phaseCoordinate, in BoundaryProfileParameters p)
    {
        double d = Math.Abs(signed);
        double taper = Falloff(d, p.TransformHalfWidthRad);
        if (taper == 0.0) return 0.0;

        double phase = 2.0 * Math.PI * phaseCoordinate / p.TransformScarpPeriodPoints;
        return p.TransformScarpAmplitude * Math.Cos(phase) * taper;
    }

    // Smooth cosine falloff: 1 at distance 0, 0 at distance halfWidth, C1-continuous, zero outside the band.
    // halfWidth must be positive (BoundaryProfileParameters.Zero keeps widths non-zero for this reason).
    private static double Falloff(double distance, double halfWidth)
    {
        if (halfWidth <= 0.0) return 0.0;
        if (Math.Abs(distance) >= halfWidth) return 0.0;
        return 0.5 * (1.0 + Math.Cos(Math.PI * distance / halfWidth));
    }
}
