using System;
using System.Collections.Generic;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using UnifyMaths;

namespace FantaSim.App.World.Topography;

/// <summary>
/// Builds the per-cell boundary field (P4): for each cell, the great-circle distance to its nearest typed
/// boundary arc, that boundary's type, and — for convergent boundaries — which side the cell is on
/// (subducting vs overriding), resolved from the canonical arc. All sphere math uses UnifyMaths
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
        IReadOnlyList<PlateBoundaryArc> arcs)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(arcs);

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

            result[c] = CreateSample(
                centroid,
                cell.PlateId,
                arc,
                arcVecs[bestArc],
                bestPoint,
                distance,
                bestArc);
        }
        return result;
    }

    /// <summary>
    /// Samples the canonical boundary frame at one material-control direction. Unlike
    /// <see cref="Build"/>, this uses the supplied direction's angular distance rather than a
    /// cell-centroid distance, so a boundary corner remains an exact hinge.
    /// </summary>
    public static CellBoundarySample SampleDirection(
        GlobeVec3 direction,
        int plateId,
        IReadOnlyList<PlateBoundaryArc> arcs,
        GlobeVec3? sideHintDirection = null,
        int? preferredArcIndex = null)
    {
        ArgumentNullException.ThrowIfNull(arcs);
        var sampleDirection = Unit(direction);
        if (sampleDirection.Length() < 1e-15 || arcs.Count == 0)
            return NotFound(plateId);

        int bestArc = -1;
        int bestPoint = -1;
        double bestDot = -2.0;
        var arcVecs = new Vector3D[arcs.Count][];
        for (int a = 0; a < arcs.Count; a++)
        {
            if (preferredArcIndex is int preferred && a != preferred)
                continue;

            var points = arcs[a].Points;
            var vectors = new Vector3D[points.Count];
            for (int i = 0; i < points.Count; i++)
                vectors[i] = Unit(points[i]);
            arcVecs[a] = vectors;

            if (arcs[a].Kind == PlateBoundaryKind.Inactive
                || (plateId != arcs[a].PlateA && plateId != arcs[a].PlateB)
                || vectors.Length == 0)
            {
                continue;
            }

            int point = NearestPointIndex(sampleDirection, vectors, out double dot);
            if (dot > bestDot + 1e-15
                || (Math.Abs(dot - bestDot) <= 1e-15
                    && (bestArc < 0 || CompareArcPriority(arcs[a], arcs[bestArc]) < 0)))
            {
                bestArc = a;
                bestPoint = point;
                bestDot = dot;
            }
        }

        if (bestArc < 0)
            return NotFound(plateId);

        Vector3D frameDirection = sideHintDirection is GlobeVec3 sideHint
            ? Unit(sideHint)
            : sampleDirection;
        return CreateSample(
            frameDirection,
            plateId,
            arcs[bestArc],
            arcVecs[bestArc],
            bestPoint,
            Math.Acos(Math.Clamp(bestDot, -1.0, 1.0)),
            bestArc);
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

    private static CellBoundarySample CreateSample(
        Vector3D sampleDirection,
        int plateId,
        PlateBoundaryArc arc,
        IReadOnlyList<Vector3D> arcPoints,
        int pointIndex,
        double distance,
        int boundaryArcIndex)
    {
        int? subductingId = arc.Kind == PlateBoundaryKind.Convergent
            ? arc.SubductingPlateId
            : null;
        bool isCollision = arc.Kind == PlateBoundaryKind.Convergent && arc.IsCollision;
        double signed = distance;
        if (!isCollision && subductingId is int subducting)
        {
            // Preserve a deterministic physical side at the exact hinge.
            signed = plateId == subducting
                ? -Math.Max(distance, double.Epsilon)
                : Math.Max(distance, double.Epsilon);
        }

        var frame = BoundaryFrame(sampleDirection, arcPoints, pointIndex);
        double phase = TransformPhaseCoordinate(arc, arcPoints[pointIndex]);
        return new CellBoundarySample(
            Found: true,
            signed,
            arc.Kind,
            pointIndex,
            phase,
            plateId,
            arc.PlateA,
            arc.PlateB,
            subductingId,
            isCollision)
        {
            BoundaryArcIndex = boundaryArcIndex,
            NearestBoundaryPoint = ToGlobeVec(frame.Point),
            AlongBoundaryDirection = ToGlobeVec(frame.Along),
            AcrossBoundaryDirection = ToGlobeVec(frame.Across),
            AlongBoundaryPhaseCoordinate = phase,
        };
    }

    private static CellBoundarySample NotFound(int plateId)
        => new(
            Found: false,
            0.0,
            PlateBoundaryKind.Inactive,
            0,
            0.0,
            plateId,
            -1,
            -1,
            null,
            IsCollision: false);

    private static (Vector3D Point, Vector3D Along, Vector3D Across) BoundaryFrame(
        Vector3D sampleDirection,
        IReadOnlyList<Vector3D> points,
        int pointIndex)
    {
        var point = points[pointIndex].Normalize();
        int previousIndex = Math.Max(0, pointIndex - 1);
        int nextIndex = Math.Min(points.Count - 1, pointIndex + 1);
        var chord = points[nextIndex] - points[previousIndex];
        var alongCandidate = chord - (point * Vector3D.Dot(chord, point));
        var along = alongCandidate.Length() < 1e-12
            ? default
            : alongCandidate.Normalize();
        if (along.Length() < 1e-12)
        {
            var reference = Math.Abs(point.X) < 0.9
                ? new Vector3D(1.0, 0.0, 0.0)
                : new Vector3D(0.0, 1.0, 0.0);
            along = Vector3D.Cross(reference, point).Normalize();
        }

        var across = Vector3D.Cross(point, along).Normalize();
        var towardCandidate =
            sampleDirection - (point * Vector3D.Dot(sampleDirection, point));
        var towardSample = towardCandidate.Length() < 1e-12
            ? default
            : towardCandidate.Normalize();
        if (towardSample.Length() > 1e-12
            && Vector3D.Dot(across, towardSample) < 0.0)
        {
            across *= -1.0;
        }

        return (point, along, across);
    }

    private static GlobeVec3 ToGlobeVec(Vector3D value)
        => new((float)value.X, (float)value.Y, (float)value.Z);

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
