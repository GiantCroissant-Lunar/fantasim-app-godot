using System.Collections.Generic;
using System.Linq;
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

        var forcingArcs = new Dictionary<BoundaryGroupKey, List<IReadOnlyList<GlobeVec3>>>();
        foreach (var arc in arcs)
        {
            var kind = MapKind(arc.Kind);
            if (kind is null)
                continue; // Transform/Inactive: no forcing model.

            var points = arc.Points;
            if (points is null || points.Count < 2)
                continue;

            var key = new BoundaryGroupKey(
                System.Math.Min(arc.PlateA, arc.PlateB),
                System.Math.Max(arc.PlateA, arc.PlateB),
                kind.Value);
            if (!forcingArcs.TryGetValue(key, out var group))
            {
                group = new List<IReadOnlyList<GlobeVec3>>();
                forcingArcs.Add(key, group);
            }
            group.Add(points);
        }

        var segments = new List<BoundarySegmentHistory>();
        foreach (var group in forcingArcs
                     .OrderBy(entry => entry.Key.PlateA)
                     .ThenBy(entry => entry.Key.PlateB)
                     .ThenBy(entry => entry.Key.Kind))
        {
            foreach (var polyline in CoalesceConnectedArcs(group.Value))
            {
                AppendChunkedSegments(
                    polyline,
                    group.Key.Kind,
                    plateOnsetTick,
                    relativeRateRadPerTick,
                    segments);
            }
        }

        return new PlateBoundaryHistory(segments);
    }

    /// <summary>
    /// Recovers mantle-scale boundary polylines from the presentation seam's edge-local records.
    /// Only records with an exactly shared tessellation endpoint are joined, so equal plate-pair
    /// metadata can never bridge disconnected frontier components. Degree-two vertices continue a
    /// chain; a junction (degree other than two) terminates it so unrelated branches are not paired
    /// through an arbitrary turn. Input order and per-record orientation do not affect the result.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<GlobeVec3>> CoalesceConnectedArcs(
        IReadOnlyList<IReadOnlyList<GlobeVec3>> source)
    {
        var edges = source
            .Where(points => points.Count >= 2 && points[0] != points[^1])
            .Select(points => new BoundaryEdge(points, points[0], points[^1]))
            .ToArray();
        if (edges.Length == 0)
            return System.Array.Empty<IReadOnlyList<GlobeVec3>>();
        if (edges.Length == 1)
            return new[] { edges[0].Points };

        var incidentByEndpoint = new Dictionary<GlobeVec3, List<int>>();
        for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
        {
            AddIncident(incidentByEndpoint, edges[edgeIndex].Start, edgeIndex);
            AddIncident(incidentByEndpoint, edges[edgeIndex].End, edgeIndex);
        }

        var orderedEdges = Enumerable.Range(0, edges.Length).ToList();
        orderedEdges.Sort((left, right) => CompareEdges(edges[left], edges[right]));

        var visited = new bool[edges.Length];
        var result = new List<IReadOnlyList<GlobeVec3>>(edges.Length);

        // Open paths and paths ending at a junction. Stopping at degree != 2 prevents an arbitrary
        // branch-to-branch shortcut from becoming one mantle segment.
        foreach (int edgeIndex in orderedEdges)
        {
            if (visited[edgeIndex])
                continue;
            var edge = edges[edgeIndex];
            int startDegree = incidentByEndpoint[edge.Start].Count;
            int endDegree = incidentByEndpoint[edge.End].Count;
            if (startDegree == 2 && endDegree == 2)
                continue; // Closed degree-two component; handled below.

            var start = SelectStart(edge, startDegree, endDegree);
            result.Add(WalkPath(edgeIndex, start, edges, incidentByEndpoint, visited));
        }

        // Any remaining component is a closed degree-two loop. Pick a stable edge and endpoint;
        // WalkPath returns the repeated start endpoint so the chunker can preserve the closure.
        foreach (int edgeIndex in orderedEdges)
        {
            if (visited[edgeIndex])
                continue;
            var edge = edges[edgeIndex];
            var start = ComparePoints(edge.Start, edge.End) <= 0 ? edge.Start : edge.End;
            result.Add(WalkPath(edgeIndex, start, edges, incidentByEndpoint, visited));
        }

        return result;
    }

    private static IReadOnlyList<GlobeVec3> WalkPath(
        int firstEdgeIndex,
        GlobeVec3 start,
        IReadOnlyList<BoundaryEdge> edges,
        IReadOnlyDictionary<GlobeVec3, List<int>> incidentByEndpoint,
        bool[] visited)
    {
        var points = new List<GlobeVec3>();
        int edgeIndex = firstEdgeIndex;
        var current = start;

        while (!visited[edgeIndex])
        {
            var edge = edges[edgeIndex];
            bool forward = edge.Start == current;
            var next = forward ? edge.End : edge.Start;
            AppendOriented(points, edge.Points, forward);
            visited[edgeIndex] = true;

            var incident = incidentByEndpoint[next];
            if (incident.Count != 2)
                break;

            int nextEdge = -1;
            foreach (int candidate in incident)
            {
                if (visited[candidate])
                    continue;
                if (nextEdge < 0 || CompareEdges(edges[candidate], edges[nextEdge]) < 0)
                    nextEdge = candidate;
            }
            if (nextEdge < 0)
                break;

            current = next;
            edgeIndex = nextEdge;
        }

        return points;
    }

    private static void AppendOriented(
        List<GlobeVec3> destination,
        IReadOnlyList<GlobeVec3> source,
        bool forward)
    {
        if (forward)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (destination.Count == 0 || destination[^1] != source[i])
                    destination.Add(source[i]);
            }
            return;
        }

        for (int i = source.Count - 1; i >= 0; i--)
        {
            if (destination.Count == 0 || destination[^1] != source[i])
                destination.Add(source[i]);
        }
    }

    private static GlobeVec3 SelectStart(BoundaryEdge edge, int startDegree, int endDegree)
    {
        if (startDegree != 2 && endDegree == 2)
            return edge.Start;
        if (endDegree != 2 && startDegree == 2)
            return edge.End;
        return ComparePoints(edge.Start, edge.End) <= 0 ? edge.Start : edge.End;
    }

    private static void AddIncident(
        Dictionary<GlobeVec3, List<int>> incidentByEndpoint,
        GlobeVec3 endpoint,
        int edgeIndex)
    {
        if (!incidentByEndpoint.TryGetValue(endpoint, out var incident))
        {
            incident = new List<int>();
            incidentByEndpoint.Add(endpoint, incident);
        }
        incident.Add(edgeIndex);
    }

    private static int CompareEdges(BoundaryEdge left, BoundaryEdge right)
    {
        var leftMin = ComparePoints(left.Start, left.End) <= 0 ? left.Start : left.End;
        var rightMin = ComparePoints(right.Start, right.End) <= 0 ? right.Start : right.End;
        int comparison = ComparePoints(leftMin, rightMin);
        if (comparison != 0)
            return comparison;

        var leftMax = leftMin == left.Start ? left.End : left.Start;
        var rightMax = rightMin == right.Start ? right.End : right.Start;
        return ComparePoints(leftMax, rightMax);
    }

    private static int ComparePoints(GlobeVec3 left, GlobeVec3 right)
    {
        int comparison = left.X.CompareTo(right.X);
        if (comparison != 0) return comparison;
        comparison = left.Y.CompareTo(right.Y);
        return comparison != 0 ? comparison : left.Z.CompareTo(right.Z);
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
        if (points.Count > 2 && points[0] == points[^1])
        {
            // A closed boundary whose total diameter is below the angular cap would otherwise be
            // reduced to one degenerate start==end segment. Split it at its farthest sampled point
            // and chunk both halves, preserving the complete loop without an antipodal shortcut.
            var origin = ToUnitVector3D(points[0]);
            int split = 1;
            double lowestDot = Vector3D.Dot(origin, ToUnitVector3D(points[split]));
            for (int i = 2; i < points.Count - 1; i++)
            {
                double dot = Vector3D.Dot(origin, ToUnitVector3D(points[i]));
                if (dot < lowestDot)
                {
                    lowestDot = dot;
                    split = i;
                }
            }

            AppendChunkedSegments(points.Take(split + 1).ToArray(), kind, plateOnsetTick, relativeRateRadPerTick, segments);
            AppendChunkedSegments(points.Skip(split).ToArray(), kind, plateOnsetTick, relativeRateRadPerTick, segments);
            return;
        }

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

    private readonly record struct BoundaryGroupKey(int PlateA, int PlateB, PlateHistoryKind Kind);

    private sealed record BoundaryEdge(
        IReadOnlyList<GlobeVec3> Points,
        GlobeVec3 Start,
        GlobeVec3 End);
}
