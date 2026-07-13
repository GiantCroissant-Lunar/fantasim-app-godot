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
                    0, cell.PlateId, -1, -1, null, IsCollision: false);
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
                // unrelated nearby segment cannot steal the sample. At a junction two incident
                // edges can both be exact zero-distance ties; this one-sample field deliberately
                // uses the stable input arc order as its tie-break (pinned by a junction test).
                if (ArcMatchesCellEdge(cell, arcs[a].Points))
                {
                    bestArc = a;
                    bestPoint = NearestPointIndex(centroid, arcVecs[a], out bestDot);
                    bestArcIsCellEdge = true;
                    break;
                }

                var vecs = arcVecs[a];
                for (int i = 0; i < vecs.Length; i++)
                {
                    double dot = Vector3D.Dot(centroid, vecs[i]);
                    if (dot > bestDot) { bestDot = dot; bestArc = a; bestPoint = i; }
                }
            }

            if (bestArc < 0)
            {
                // Interior cell (no boundary of its own plate nearby): no profile contribution.
                result[c] = new CellBoundarySample(Found: false, 0.0, PlateBoundaryKind.Inactive,
                    0, cell.PlateId, -1, -1, null, IsCollision: false);
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
                cell.PlateId,
                arc.PlateA,
                arc.PlateB,
                subductingId,
                isCollision);
        }
        return result;
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

    private static Vector3D Unit(GlobeVec3 point)
    {
        var vector = new Vector3D(point.X, point.Y, point.Z);
        double length = vector.Length();
        if (length < 1e-15)
            return new Vector3D(0, 0, 0);

        return vector * (1.0 / length);
    }
}
