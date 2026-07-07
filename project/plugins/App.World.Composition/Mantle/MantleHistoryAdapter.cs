using System.Collections.Generic;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using FantaSim.Geosphere.Asthenosphere.Convection;
using UnifyMaths;

namespace FantaSim.App.World.Composition;

// Mantle x-ray view (M-A), task 1: history adapter.
//
// Maps the presentation document's typed plate-boundary arcs (at the bound tick) into the engine's
// pure-data BoundarySegmentHistory records that MantleAnomalyField consumes. Convergent arcs
// (trench/subduction) map to Convergent segments -> cold slab ribbons sinking beneath them;
// Divergent arcs (ridge/rift) map to Divergent segments -> shallow hot ridge curtains. Transform
// and Inactive arcs contribute no forcing and are dropped (the engine has no transform-fault model).
//
// Segmentation (documented decision): the engine POLYLINE-SAMPLES each segment's great circle
// internally (12 samples per segment, Gaussian sheet width 0.14 rad) and builds a full swept
// ribbon per segment, so the right granularity is FEW, LONG segments — not one per point pair
// (which would multiply ribbon count ~10x for no fidelity gain and make sampling non-interactive;
// measured 17.7s -> ~5s at 40 arcs). Arcs are chunked purely by ANGULAR SPAN: a chunk closes when
// its endpoints would exceed MaxSegmentSpanDot (~69 degrees), which keeps the engine's along-
// segment sample spacing (span/11 <= 0.11 rad) inside the sheet width (no bead gaps) and stays far
// from the engine's antipodal-degeneracy guard. Within a chunk the arc is approximated by the
// great circle through its endpoints (max chord deviation ~ span^2/8 ~ 0.05 rad at the cap).
//
// v1 simplifications (documented for the lead):
//   * ActiveSinceTick = plateOnsetTick for every segment. Slab age (tick - ActiveSinceTick) is the
//     full mobile-plate lifetime, not per-boundary birth; per-arc onset ages are not yet carried on
//     the presentation document (P1 follow-up).
//   * RelativeRateRadPerTick = a documented constant. The volumetric engine field derives slab tip
//     depth from AGE ALONE (age x sink rate, by engine design); the rate is carried for contract
//     completeness and any future rate-scaled amplitude.
// The mapping is pure and deterministic (no wall-clock, no randomness) so it is unit-testable.

/// <summary>
/// Builds an engine <see cref="PlateBoundaryHistory"/> from the presentation document's typed
/// <see cref="PlateBoundaryArc"/>s. Pure, deterministic, Godot-free.
/// </summary>
public static class MantleHistoryAdapter
{
    /// <summary>
    /// Default per-segment convergence/divergence rate (radians per tick). Carried on the input
    /// contract; the volumetric field's slab depth is age-driven (see the class remarks).
    /// </summary>
    public const double DefaultRelativeRateRadPerTick = 4e-8;

    /// <summary>Minimum dot product between a chunk's endpoints — caps a segment's angular span at
    /// ~69 degrees (1.2 rad; see the segmentation note above).</summary>
    public const double MaxSegmentSpanDot = 0.36;

    /// <summary>
    /// Adapts presentation boundary arcs into engine boundary-segment history.
    /// </summary>
    /// <param name="arcs">Typed plate-boundary arcs at the bound tick (may be null/empty -> Empty history).</param>
    /// <param name="plateOnsetTick">Plate onset tick, used as every segment's ActiveSinceTick (v1 simplification).</param>
    /// <param name="relativeRateRadPerTick">Per-segment rate (default <see cref="DefaultRelativeRateRadPerTick"/>).</param>
    /// <returns>An engine <see cref="PlateBoundaryHistory"/> consumable by <c>MantleAnomalyField</c>.</returns>
    public static PlateBoundaryHistory Build(
        IReadOnlyList<PlateBoundaryArc>? arcs,
        long plateOnsetTick,
        double relativeRateRadPerTick = DefaultRelativeRateRadPerTick)
    {
        if (arcs is null || arcs.Count == 0)
            return PlateBoundaryHistory.Empty;

        var segments = new List<BoundarySegmentHistory>();
        foreach (var arc in arcs)
        {
            var kind = MapKind(arc.Kind);
            if (kind is null)
                continue; // Transform/Inactive: no forcing model.

            var points = arc.Points;
            if (points is null || points.Count < 2)
                continue;

            AppendChunkedSegments(points, kind.Value, plateOnsetTick, relativeRateRadPerTick, segments);
        }

        return new PlateBoundaryHistory(segments);
    }

    /// <summary>Walks the arc polyline emitting the longest spans whose endpoints stay within the
    /// angular cap (<see cref="MaxSegmentSpanDot"/>).</summary>
    private static void AppendChunkedSegments(
        IReadOnlyList<GlobeVec3> points,
        PlateHistoryKind kind,
        long plateOnsetTick,
        double relativeRateRadPerTick,
        List<BoundarySegmentHistory> segments)
    {
        int start = 0;
        while (start < points.Count - 1)
        {
            var a = ToUnitVector3D(points[start]);
            int end = start + 1;
            // Greedily extend the span while its endpoints stay within the angular cap.
            while (end + 1 < points.Count
                   && Vector3D.Dot(a, ToUnitVector3D(points[end + 1])) > MaxSegmentSpanDot)
            {
                end++;
            }

            var b = ToUnitVector3D(points[end]);
            if (!IsDegenerate(a, b))
            {
                segments.Add(new BoundarySegmentHistory(
                    A: a,
                    B: b,
                    Kind: kind,
                    ActiveSinceTick: plateOnsetTick,
                    RelativeRateRadPerTick: relativeRateRadPerTick));
            }

            start = end;
        }
    }

    private static PlateHistoryKind? MapKind(PlateBoundaryKind kind) =>
        kind switch
        {
            PlateBoundaryKind.Convergent => PlateHistoryKind.Convergent,
            PlateBoundaryKind.Divergent => PlateHistoryKind.Divergent,
            _ => null,
        };

    private static Vector3D ToUnitVector3D(GlobeVec3 v)
    {
        var p = new Vector3D(v.X, v.Y, v.Z);
        var len = System.Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
        if (len < 1e-15)
            return new Vector3D(0, 0, 0);
        return new Vector3D(p.X / len, p.Y / len, p.Z / len);
    }

    private static bool IsDegenerate(Vector3D a, Vector3D b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return (dx * dx + dy * dy + dz * dz) < 1e-18;
    }
}
