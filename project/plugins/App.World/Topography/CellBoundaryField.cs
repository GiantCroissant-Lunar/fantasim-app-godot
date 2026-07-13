using System;
using System.Collections.Generic;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using UnifyMaths;

namespace FantaSim.App.World.Topography;

/// <summary>
/// Builds the per-cell boundary field (P4): for each cell, the great-circle distance to its nearest typed
/// boundary arc, that boundary's type, and — for convergent boundaries — which side the cell is on
/// (subducting vs overriding), resolved from the polarity map. All sphere math uses UnifyMaths
/// <see cref="Vector3D"/> with the <c>acos(clamp(dot))</c> idiom; no hand-rolled spherical math.
/// </summary>
public static class CellBoundaryField
{
    private const double SharedVertexDotTolerance = 1e-10;
    private const double TransformPhaseSpanPoints = 256.0;
    private static readonly Vector3D TransformPhaseAxis =
        new Vector3D(1.0, Math.Sqrt(2.0), Math.Sqrt(3.0)).Normalize();

    /// <summary>
    /// One <see cref="CellBoundarySample"/> per cell (indexed by cell order in <paramref name="cells"/>).
    /// Cells on a plate that is not one of the nearest arc's two plates still get a sample (distance/type)
    /// but <see cref="BoundaryProfileShape.Contribution"/> zeros them out. When there are no arcs, every
    /// sample has <see cref="CellBoundarySample.Found"/> = false.
    /// </summary>
    public static IReadOnlyList<CellBoundarySample> Build(
        IReadOnlyList<GlobeCell> cells,
        IReadOnlyList<PlateBoundaryArc> arcs,
        IReadOnlyDictionary<(int PlateA, int PlateB), ConvergentBoundaryPolarity> polarity)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(arcs);
        ArgumentNullException.ThrowIfNull(polarity);

        // Pre-convert arc points to Vector3D once (each arc's polyline is reused across all cells).
        var arcVecs = new Vector3D[arcs.Count][];
        for (int a = 0; a < arcs.Count; a++)
        {
            var pts = arcs[a].Points;
            arcVecs[a] = new Vector3D[pts.Count];
            for (int i = 0; i < pts.Count; i++)
                arcVecs[a][i] = new Vector3D(pts[i].X, pts[i].Y, pts[i].Z);
        }

        var result = new CellBoundarySample[cells.Count];
        for (int c = 0; c < cells.Count; c++)
        {
            var cell = cells[c];
            var centroid = Centroid(cell);

            if (arcs.Count == 0)
            {
                result[c] = new CellBoundarySample(Found: false, 0.0, PlateBoundaryKind.Inactive,
                    0, 0.0, cell.PlateId, -1, -1, null, IsCollision: false);
                continue;
            }

            // Nearest arc point = max dot product (cosine of the angular distance). Only consider arcs
            // whose plate pair includes this cell's plate: a cell's boundary-profile comes from its OWN
            // plate's boundaries, not a geometrically-near boundary between two other plates.
            int bestArc = -1, bestPoint = -1;
            double bestDot = -2.0;
            bool bestArcIsCellEdge = false;
            for (int a = 0; a < arcs.Count; a++)
            {
                if (arcs[a].Kind == PlateBoundaryKind.Inactive) continue;
                if (cell.PlateId != arcs[a].PlateA && cell.PlateId != arcs[a].PlateB) continue;

                // Production arcs are edge-local: when their endpoints are the two endpoints of
                // one of this triangular cell's edges, the finite cell footprint touches that
                // boundary exactly. Prefer that topological fact over centroid proximity so an
                // unrelated nearby segment cannot steal the sample. At a junction multiple
                // incident edges can be exact zero-distance ties; resolve them semantically and
                // independently of input order: Convergent > Divergent > Transform > Inactive,
                // then normalized plate pair, then stable endpoint geometry.
                if (ArcMatchesCellEdge(cell, arcs[a].Points))
                {
                    int candidatePoint = NearestPointIndex(centroid, arcVecs[a], out double candidateDot);
                    if (!bestArcIsCellEdge || CompareArcPriority(arcs[a], arcs[bestArc]) < 0)
                    {
                        bestArc = a;
                        bestPoint = candidatePoint;
                        bestDot = candidateDot;
                        bestArcIsCellEdge = true;
                    }
                    continue;
                }

                if (bestArcIsCellEdge)
                    continue;

                var vecs = arcVecs[a];
                for (int i = 0; i < vecs.Length; i++)
                {
                    double dot = Vector3D.Dot(centroid, vecs[i]);
                    if (dot > bestDot + 1e-15
                        || (Math.Abs(dot - bestDot) <= 1e-15
                            && (bestArc < 0 || CompareArcPriority(arcs[a], arcs[bestArc]) < 0)))
                    {
                        bestDot = dot;
                        bestArc = a;
                        bestPoint = i;
                    }
                }
            }

            if (bestArc < 0)
            {
                // Interior cell (no boundary of its own plate nearby): no profile contribution.
                result[c] = new CellBoundarySample(Found: false, 0.0, PlateBoundaryKind.Inactive,
                    0, 0.0, cell.PlateId, -1, -1, null, IsCollision: false);
                continue;
            }

            var arc = arcs[bestArc];
            // An actual shared-edge cell has zero footprint-to-boundary distance. Every other cell
            // keeps the conservative centroid-to-arc distance; this is the interior guard that
            // prevents a coarse cell's nearest (possibly unrelated) edge from widening the authored
            // profile band.
            double distance = bestArcIsCellEdge
                ? 0.0
                : Math.Acos(Math.Clamp(bestDot, -1.0, 1.0));

            // Resolve side for convergent boundaries; symmetric kinds carry a non-negative distance.
            int? subductingId = null;
            bool isCollision = false;
            double signed = distance;

            if (arc.Kind == PlateBoundaryKind.Convergent)
            {
                var key = arc.PlateA <= arc.PlateB ? (arc.PlateA, arc.PlateB) : (arc.PlateB, arc.PlateA);
                if (polarity.TryGetValue(key, out var pol))
                {
                    subductingId = pol.SubductingPlateId;
                    isCollision = pol.IsCollision;
                    // Collision is symmetric (uplift on both sides); subduction is asymmetric (subducting side negative).
                    if (!isCollision)
                    {
                        // Keep the physical side unambiguous when exact edge membership lands
                        // exactly on the boundary: +epsilon selects the overriding arc branch;
                        // -epsilon selects the subducting trench branch.
                        signed = cell.PlateId == pol.SubductingPlateId
                            ? -Math.Max(distance, double.Epsilon)
                            : Math.Max(distance, double.Epsilon);
                    }
                }
            }

            result[c] = new CellBoundarySample(
                Found: true,
                signed,
                arc.Kind,
                bestPoint,
                arc.Kind == PlateBoundaryKind.Transform
                    ? TransformPhaseCoordinate(arc, arcVecs[bestArc][bestPoint])
                    : 0.0,
                cell.PlateId,
                arc.PlateA,
                arc.PlateB,
                subductingId,
                isCollision);
        }
        return result;
    }

    /// <summary>
    /// Deterministic continuous world-space pseudo-sample coordinate for transform-profile phase.
    /// The fixed irrational axis avoids coordinate seams; normalized plate-pair offset prevents all
    /// transform systems from sharing identical bands. Reversing or splitting an arc leaves the
    /// coordinate at a physical point unchanged.
    /// </summary>
    internal static double TransformPhaseCoordinate(PlateBoundaryArc arc, Vector3D point)
    {
        ArgumentNullException.ThrowIfNull(arc);
        double length = point.Length();
        var unit = length < 1e-15 ? new Vector3D(1.0, 0.0, 0.0) : point * (1.0 / length);
        int plateA = Math.Min(arc.PlateA, arc.PlateB);
        int plateB = Math.Max(arc.PlateA, arc.PlateB);
        uint pairHash = unchecked(((uint)plateA * 73_856_093u) ^ ((uint)plateB * 19_349_663u));
        double pairOffset = pairHash % (uint)TransformPhaseSpanPoints;
        double spatial = 0.5 * TransformPhaseSpanPoints
            * (Math.Clamp(Vector3D.Dot(unit, TransformPhaseAxis), -1.0, 1.0) + 1.0);
        return pairOffset + spatial;
    }

    private static Vector3D Centroid(GlobeCell cell)
    {
        var v = new Vector3D(cell.C0.X + cell.C1.X + cell.C2.X,
                             cell.C0.Y + cell.C1.Y + cell.C2.Y,
                             cell.C0.Z + cell.C1.Z + cell.C2.Z);
        double len = v.Length();
        return len < 1e-15 ? new Vector3D(1, 0, 0) : v * (1.0 / len);
    }

    private static int NearestPointIndex(Vector3D centroid, IReadOnlyList<Vector3D> points, out double bestDot)
    {
        int bestPoint = -1;
        bestDot = -2.0;
        for (int i = 0; i < points.Count; i++)
        {
            double dot = Vector3D.Dot(centroid, points[i]);
            if (dot <= bestDot)
                continue;

            bestDot = dot;
            bestPoint = i;
        }
        return bestPoint;
    }

    private static bool ArcMatchesCellEdge(GlobeCell cell, IReadOnlyList<GlobeVec3> points)
    {
        if (points.Count < 2)
            return false;

        var corners = new[] { Unit(cell.C0), Unit(cell.C1), Unit(cell.C2) };
        var first = Unit(points[0]);
        var last = Unit(points[^1]);
        for (int edge = 0; edge < corners.Length; edge++)
        {
            var a = corners[edge];
            var b = corners[(edge + 1) % corners.Length];
            if ((SameDirection(first, a) && SameDirection(last, b))
                || (SameDirection(first, b) && SameDirection(last, a)))
                return true;
        }

        return false;
    }

    private static bool SameDirection(Vector3D a, Vector3D b) =>
        Vector3D.Dot(a, b) >= 1.0 - SharedVertexDotTolerance;

    private static int CompareArcPriority(PlateBoundaryArc left, PlateBoundaryArc right)
    {
        int comparison = KindPriority(left.Kind).CompareTo(KindPriority(right.Kind));
        if (comparison != 0)
            return comparison;

        int leftA = Math.Min(left.PlateA, left.PlateB);
        int leftB = Math.Max(left.PlateA, left.PlateB);
        int rightA = Math.Min(right.PlateA, right.PlateB);
        int rightB = Math.Max(right.PlateA, right.PlateB);
        comparison = leftA.CompareTo(rightA);
        if (comparison != 0) return comparison;
        comparison = leftB.CompareTo(rightB);
        if (comparison != 0) return comparison;

        var leftFirst = left.Points.Count > 0 ? left.Points[0] : default;
        var leftLast = left.Points.Count > 0 ? left.Points[^1] : default;
        var rightFirst = right.Points.Count > 0 ? right.Points[0] : default;
        var rightLast = right.Points.Count > 0 ? right.Points[^1] : default;
        CanonicalizeEndpoints(ref leftFirst, ref leftLast);
        CanonicalizeEndpoints(ref rightFirst, ref rightLast);
        comparison = ComparePoint(leftFirst, rightFirst);
        return comparison != 0 ? comparison : ComparePoint(leftLast, rightLast);
    }

    private static int KindPriority(PlateBoundaryKind kind) => kind switch
    {
        PlateBoundaryKind.Convergent => 0,
        PlateBoundaryKind.Divergent => 1,
        PlateBoundaryKind.Transform => 2,
        _ => 3,
    };

    private static void CanonicalizeEndpoints(ref GlobeVec3 first, ref GlobeVec3 last)
    {
        if (ComparePoint(first, last) <= 0)
            return;
        (first, last) = (last, first);
    }

    private static int ComparePoint(GlobeVec3 left, GlobeVec3 right)
    {
        int comparison = left.X.CompareTo(right.X);
        if (comparison != 0) return comparison;
        comparison = left.Y.CompareTo(right.Y);
        return comparison != 0 ? comparison : left.Z.CompareTo(right.Z);
    }

    private static Vector3D Unit(GlobeVec3 point)
    {
        var vector = new Vector3D(point.X, point.Y, point.Z);
        double length = vector.Length();
        if (length < 1e-15)
            return new Vector3D(0, 0, 0);

        return vector * (1.0 / length);
    }
}
