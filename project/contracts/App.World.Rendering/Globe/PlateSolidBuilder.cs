using System;
using System.Collections.Generic;
using System.Globalization;
using FantaSim.App.World.Dto;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Globe.Core;
using FantaSim.Cartography.Shared;
using UnifyMaths;
using UnifyMaths.Numerics;

namespace FantaSim.App.World.Globe;

/// <summary>
/// One plate's closed, watertight SOLID crust slab for the exploded-crust mode (M-B).
/// </summary>
/// <remarks>
/// <para>
/// The solid is a single indexed mesh: <see cref="Positions"/> holds the TOP cap vertices (indices
/// <c>0..N-1</c>) followed by the BOTTOM vertices (indices <c>N..2N-1</c>, same order as top), and
/// <see cref="Triangles"/> is the concatenation of the top triangles (winding unchanged), the bottom
/// triangles (winding reversed so the bottom face's outward normal points toward the globe centre),
/// and the side-wall quad-strip triangles stitched along the plate's rim loops.
/// </para>
/// <para>
/// Because the top cap is watertight (every interior undirected edge is shared by exactly two top
/// triangles), the rim is exactly the set of edges that appear in only ONE top triangle, and the
/// side walls connect each rim edge to its corresponding bottom edge — so every undirected edge in
/// the whole solid is shared by exactly two triangles. The solid is WATERTIGHT BY CONSTRUCTION.
/// </para>
/// <para>
/// This is a PURE, Godot-free value: no engine types, no random, no wall-clock. Two solids built
/// from identical inputs produce bit-equal <see cref="Positions"/> and <see cref="Triangles"/>.
/// </para>
/// </remarks>
public sealed record PlateSolid(
    int PlateId,
    CartesianPoint3[] Positions,
    int[] Triangles)
{
    /// <summary>Number of unique vertices (== <see cref="Positions"/> length).</summary>
    public int VertexCount => Positions.Length;

    /// <summary>Number of triangles (== <see cref="Triangles"/> length / 3).</summary>
    public int TriangleCount => Triangles.Length / 3;
}

/// <summary>
/// One plate's area-weighted unit centroid direction, computed from the BASE unit-sphere cell
/// corners (tick/relief-invariant) so the exploded translation is stable across ticks. Carried
/// alongside the solids so <see cref="PlateSolidBuilder.ApplyExplodedFactor"/> can translate each
/// plate without recomputing the direction.
/// </summary>
public sealed record PlateSolidCentroid(int PlateId, CartesianPoint3 CentroidDirection);

/// <summary>
/// Pure, Godot-free builder for exploded-crust solid slabs (M-B). From each plate's relief-applied
/// <see cref="PlateCap"/> top surface and a per-cell crust THICKNESS field (metres), builds a closed
/// watertight <see cref="PlateSolid"/> per plate, and a pure radial-explode transform that translates
/// each plate solid along its area-weighted centroid direction by <c>factor * maxOffset</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thickness gather.</b> Each plate's per-FACE thickness (face <c>t</c> came from cell
/// <see cref="PlateCap.CellIds"/>[t]) is gathered to per-VERTEX arithmetic means via
/// <see cref="GlobeSurfaceBuilder.GatherVertexHeights"/>, the SAME mean-gather the relief path uses.
/// Shared corners therefore get a single thickness, which keeps the bottom face watertight.
/// </para>
/// <para>
/// <b>Bottom depth is LINEAR in thickness.</b> For each top vertex <c>p</c> with gathered thickness
/// <c>thk_m</c> and the THICKNESS exaggeration <c>exg</c>, the bottom position is
/// <c>q = p - normalize(p) * (thk_m * exg)</c>. No height exponent is applied — thickness is a
/// DEPTH, not an elevation amplitude, so 2x thickness gives 2x radial depth (data-trueness). The
/// thickness exaggeration is an EXPLICIT parameter distinct from the surface relief exaggeration:
/// D3's <c>RadialSectionProfile.CrustThicknessExaggeration</c> (default 8.0) is what makes 30 km of
/// crust read as ~0.038R slab walls, independent of the ~3e-5 surface relief lens.
/// </para>
/// <para>
/// <b>Rim extraction + loop chaining.</b> The plate's RIM is the set of undirected edges that appear
/// in EXACTLY ONE top triangle (interior edges appear in two). Rim edges are chained into ordered
/// closed loop(s) deterministically by following each edge's successor at its head vertex, with the
/// starting edge chosen by the smallest (min,max) key so the output order is stable. Each rim edge
/// (directed as it appears in its single owning triangle, wound CCW for that triangle's outward
/// normal) emits a quad connecting the top edge to the corresponding bottom edge.
/// </para>
/// <para>
/// <b>Winding convention.</b> Top triangles keep the cap's winding (outward normal points away from
/// the globe centre). Bottom triangles reverse the winding (swap the two non-leading indices:
/// top <c>(a,b,c)</c> -> bottom <c>(N+a, N+c, N+b)</c>) so the bottom face's outward normal points
/// toward the globe centre. Side-wall quads are wound so the wall's outward normal points AWAY
/// from the plate: each rim edge <c>(u, v)</c> as it appears in its owning triangle emits the quad
/// <c>(u, v, N+v, N+u)</c> split into triangles <c>(u, v, N+v)</c> and <c>(u, N+v, N+u)</c>. The
/// rim edge direction is the top triangle's CCW direction, which makes the wall normal point
/// outward (away from the plate interior) for a CCW-wound top cap.
/// </para>
/// <para>
/// <b>Spherical area for the centroid.</b> The area-weighted centroid direction uses the
/// spherical-triangle area of each cell on the BASE unit sphere. GiantCroissant.UnifyGeometry does
/// not expose a public spherical-triangle-area primitive in the version available to this repo, so
/// the area is computed via Girard's theorem (spherical excess: <c>area = (A + B + C) - pi</c> for a
/// unit-sphere triangle, with the angles at each vertex derived from the great-circle arcs using
/// UnifyMaths <see cref="Vector3D"/> dot/cross/normalize). This keeps all vector math inside the
/// Unify* family — no hand-rolled vector primitive is introduced.
/// </para>
/// </remarks>
public static class PlateSolidBuilder
{
    /// <summary>
    /// Default explode offset in unit-radius fractions (M-B): plates translate by
    /// <c>centroidDir * factor * maxOffset</c>. 0.35 ≈ 35% of the base radius, enough to read each
    /// plate as a discrete slab without the gaps swallowing the globe.
    /// </summary>
    public const double DefaultMaxOffset = 0.35;

    // Degenerate-length guard, sourced from UnifyMaths.Numerics (matches GlobeSurfaceBuilder).
    private static readonly double Epsilon = Tolerance.Strict.Epsilon;

    /// <summary>
    /// Builds one closed watertight <see cref="PlateSolid"/> per plate from the relief-applied top
    /// caps and a per-cell crust THICKNESS field. This is the ASSEMBLED state (factor 0): positions
    /// are exactly the top-cap positions plus the radially-inset bottom positions, with no explode
    /// translation applied.
    /// </summary>
    /// <param name="caps">Per-plate relief-applied top caps (from <see cref="GlobePlateSurfaces.BuildSurfaces"/>).</param>
    /// <param name="crustThicknessByCellMetres">
    /// Per-cell crust THICKNESS in metres, indexed by cell id. Cells outside the array's range
    /// contribute 0.0 (no thickness — the bottom coincides with the top at that vertex).
    /// </param>
    /// <param name="exaggeration">
    /// Metres-to-unit-radius scale applied to the thickness depth (LINEAR: <c>depth = thk_m * exg</c>).
    /// This is the THICKNESS exaggeration — DISTINCT from the surface relief exaggeration that shaped
    /// <paramref name="caps"/>. Pass D3's <c>RadialSectionProfile.CrustThicknessExaggeration</c>
    /// (default 8.0), NOT the document's <c>VerticalExaggeration</c> (surface relief, ~3e-5).
    /// </param>
    /// <param name="baseRadius">The unit-sphere base radius (default 1.0, matching <see cref="GlobeSurfaceBuilder.DefaultRadius"/>).</param>
    /// <returns>One <see cref="PlateSolid"/> per input cap, in the SAME order as <paramref name="caps"/>.</returns>
    public static IReadOnlyList<PlateSolid> Build(
        IReadOnlyList<PlateCap> caps,
        IReadOnlyList<double> crustThicknessByCellMetres,
        double exaggeration,
        double baseRadius = GlobeSurfaceBuilder.DefaultRadius)
    {
        ArgumentNullException.ThrowIfNull(caps);
        ArgumentNullException.ThrowIfNull(crustThicknessByCellMetres);
        if (double.IsNaN(exaggeration) || double.IsInfinity(exaggeration))
            throw new ArgumentOutOfRangeException(nameof(exaggeration), "Exaggeration must be finite.");
        if (!IsPositiveFinite(baseRadius))
            throw new ArgumentOutOfRangeException(nameof(baseRadius), "Base radius must be positive and finite.");

        var result = new PlateSolid[caps.Count];
        for (int p = 0; p < caps.Count; p++)
            result[p] = BuildOneSolid(caps[p], crustThicknessByCellMetres, exaggeration, baseRadius);
        return result;
    }

    /// <summary>
    /// Computes the area-weighted unit centroid direction for each plate from the ORIGINAL snapshot's
    /// BASE unit-sphere cell corners (tick/relief-invariant), so the explode translation is stable.
    /// </summary>
    /// <param name="snapshot">The tessellation + per-cell plate ownership + corner positions.</param>
    /// <returns>
    /// One <see cref="PlateSolidCentroid"/> per non-empty plate (ascending <see cref="GlobeCell.PlateId"/>).
    /// </returns>
    public static IReadOnlyList<PlateSolidCentroid> ComputeCentroids(WorldGlobeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Group cells by plate in ascending plate-id order (deterministic, matches GlobePlateSurfaces).
        var byPlate = new SortedDictionary<int, List<GlobeCell>>();
        foreach (var cell in snapshot.Cells)
        {
            if (!byPlate.TryGetValue(cell.PlateId, out var list))
            {
                list = new List<GlobeCell>();
                byPlate[cell.PlateId] = list;
            }
            list.Add(cell);
        }

        var result = new List<PlateSolidCentroid>(byPlate.Count);
        foreach (var kvp in byPlate)
            result.Add(new PlateSolidCentroid(kvp.Key, ComputeOneCentroid(kvp.Value)));
        return result;
    }

    /// <summary>
    /// Pure radial-explode transform: each plate solid translates along its area-weighted centroid
    /// direction by <c>factor * maxOffset</c>. <paramref name="factor"/> is clamped to [0, 1].
    /// <c>factor = 0</c> yields positions BIT-EQUAL to the assembled solids (zero translation vector),
    /// so the assembled globe is exactly the factor-0 exploded globe.
    /// </summary>
    /// <param name="assembled">The assembled solids (from <see cref="Build"/>).</param>
    /// <param name="centroids">Per-plate centroid directions (from <see cref="ComputeCentroids"/>), indexed by <see cref="PlateSolid.PlateId"/>.</param>
    /// <param name="factor">Explode factor in [0, 1]. Clamped; NaN is rejected.</param>
    /// <param name="maxOffset">Maximum radial offset in unit-radius fractions (default <see cref="DefaultMaxOffset"/>).</param>
    /// <returns>
    /// New <see cref="PlateSolid"/>s with translated positions and IDENTICAL triangles (the explode
    /// is a pure translation — topology is unchanged). One per input solid, in the SAME order.
    /// </returns>
    public static IReadOnlyList<PlateSolid> ApplyExplodedFactor(
        IReadOnlyList<PlateSolid> assembled,
        IReadOnlyList<PlateSolidCentroid> centroids,
        double factor,
        double maxOffset = DefaultMaxOffset)
    {
        ArgumentNullException.ThrowIfNull(assembled);
        ArgumentNullException.ThrowIfNull(centroids);
        if (double.IsNaN(factor))
            throw new ArgumentOutOfRangeException(nameof(factor), "Factor must not be NaN.");
        if (double.IsNaN(maxOffset) || double.IsInfinity(maxOffset) || maxOffset < 0.0)
            throw new ArgumentOutOfRangeException(nameof(maxOffset), "Max offset must be non-negative and finite.");

        double clamped = Math.Clamp(factor, 0.0, 1.0);
        double offset = clamped * maxOffset;

        var byPlate = new Dictionary<int, PlateSolidCentroid>(centroids.Count);
        foreach (var c in centroids)
            byPlate[c.PlateId] = c;

        var result = new PlateSolid[assembled.Count];
        for (int i = 0; i < assembled.Count; i++)
        {
            var solid = assembled[i];
            if (!byPlate.TryGetValue(solid.PlateId, out var centroid))
                throw new InvalidOperationException(
                    $"No centroid direction registered for plate {solid.PlateId}.");
            result[i] = TranslateSolid(solid, centroid.CentroidDirection, offset);
        }
        return result;
    }

    // --- solid construction ---------------------------------------------------------------------

    private static PlateSolid BuildOneSolid(
        PlateCap cap,
        IReadOnlyList<double> crustThicknessByCellMetres,
        double exaggeration,
        double baseRadius)
    {
        var surface = cap.Surface;
        int n = surface.VertexCount;
        var topTriangles = surface.Triangles;
        int faceCount = surface.TriangleCount;

        // 1. Per-FACE thickness metres (face t came from cell cap.CellIds[t]), gathered to per-VERTEX
        //    arithmetic means via the SAME GlobeSurfaceBuilder.GatherVertexHeights the relief path uses.
        //    Shared corners get one thickness -> the bottom face is watertight.
        var faceThickness = new double[faceCount];
        for (int t = 0; t < faceCount; t++)
        {
            int cellId = cap.CellIds[t];
            faceThickness[t] = cellId >= 0 && cellId < crustThicknessByCellMetres.Count
                ? crustThicknessByCellMetres[cellId]
                : 0.0;
        }
        var vertexThickness = GlobeSurfaceBuilder.GatherVertexHeights(n, topTriangles, faceThickness);

        // 2. Positions: top (0..N-1) then bottom (N..2N-1). Bottom = top - normalize(top) * (thk * exg).
        //    LINEAR in thickness (no height exponent) -> 2x thickness gives 2x radial depth.
        var positions = new CartesianPoint3[n * 2];
        for (int v = 0; v < n; v++)
        {
            var p = surface.Positions[v];
            positions[v] = p;
            double len = Math.Sqrt((p.X * p.X) + (p.Y * p.Y) + (p.Z * p.Z));
            // Guard |p| ≈ 0 (never happens on a real globe; the cap builder displaces along the unit
            // direction so |p| ≈ baseRadius + displacement > 0). Defensive only against a degenerate.
            double invLen = len > Epsilon ? 1.0 / len : 0.0;
            double depth = vertexThickness[v] * exaggeration;
            positions[n + v] = new CartesianPoint3(
                p.X - (p.X * invLen * depth),
                p.Y - (p.Y * invLen * depth),
                p.Z - (p.Z * invLen * depth));
        }

        // 3. Triangles: top (winding unchanged) + bottom (winding reversed) + side walls.
        //    Bottom winding reversal: top (a,b,c) -> bottom (N+a, N+c, N+b) — swap the two non-leading
        //    indices so the bottom face's outward normal points toward the globe centre.
        var topTriCount = topTriangles.Length;
        var bottomTriangles = new int[topTriCount];
        for (int t = 0; t < faceCount; t++)
        {
            int a = topTriangles[(t * 3) + 0];
            int b = topTriangles[(t * 3) + 1];
            int c = topTriangles[(t * 3) + 2];
            bottomTriangles[(t * 3) + 0] = n + a;
            bottomTriangles[(t * 3) + 1] = n + c;
            bottomTriangles[(t * 3) + 2] = n + b;
        }

        // 4. Rim extraction: undirected edges appearing in EXACTLY ONE top triangle. Each rim edge
        //    remembers the directed edge (u, v) as it appeared in its single owning triangle (CCW for
        //    that triangle's outward normal) so the wall winding is deterministic and outward-facing.
        var rimEdges = ExtractRimEdges(topTriangles, n);

        // 5. Chain rim edges into ordered closed loop(s) deterministically, then emit wall quads.
        //    Each rim edge (u, v) emits the quad (u, v, N+v, N+u): triangles (u, v, N+v) and
        //    (u, N+v, N+u). The rim edge direction is the top triangle's CCW direction, which makes
        //    the wall normal point outward (away from the plate interior).
        var wallTriangles = BuildWallTriangles(rimEdges, n);

        // 6. Concatenate: top + bottom + walls.
        var triangles = new int[topTriCount + bottomTriangles.Length + wallTriangles.Length];
        Array.Copy(topTriangles, 0, triangles, 0, topTriCount);
        Array.Copy(bottomTriangles, 0, triangles, topTriCount, bottomTriangles.Length);
        Array.Copy(wallTriangles, 0, triangles, topTriCount + bottomTriangles.Length, wallTriangles.Length);

        return new PlateSolid(cap.PlateId, positions, triangles);
    }

    // Extract the plate's RIM: undirected edges that appear in EXACTLY ONE top triangle. Each rim
    // edge carries the DIRECTED edge (u, v) as it appeared in its single owning triangle, so the
    // wall winding is deterministic (CCW for the top face's outward normal -> wall normal points
    // outward). Deterministic: edge keys are (min, max); the owning triangle's directed form is
    // recorded on first encounter and removed if a second triangle shares the edge.
    private static List<DirectedRimEdge> ExtractRimEdges(int[] triangles, int vertexCount)
    {
        // First pass: count directed-edge occurrences per undirected key. We track the directed
        // form from the FIRST triangle that emits the edge; if a second triangle emits the same
        // undirected key (in either direction), the edge is interior and is dropped.
        var directedByKey = new Dictionary<long, (int U, int V)>();
        var interiorKeys = new HashSet<long>();

        for (int t = 0; t < triangles.Length / 3; t++)
        {
            int a = triangles[(t * 3) + 0];
            int b = triangles[(t * 3) + 1];
            int c = triangles[(t * 3) + 2];
            ConsiderEdge(a, b, directedByKey, interiorKeys);
            ConsiderEdge(b, c, directedByKey, interiorKeys);
            ConsiderEdge(c, a, directedByKey, interiorKeys);
        }

        // Rim = directed edges whose undirected key is NOT interior. Sort deterministically by
        // (U, V) so the output is stable regardless of dictionary enumeration order.
        var rim = new List<DirectedRimEdge>();
        foreach (var kvp in directedByKey)
        {
            if (interiorKeys.Contains(kvp.Key))
                continue;
            rim.Add(new DirectedRimEdge(kvp.Value.U, kvp.Value.V));
        }
        rim.Sort(CompareRimEdge);
        return rim;
    }

    private static void ConsiderEdge(int u, int v, Dictionary<long, (int U, int V)> directedByKey, HashSet<long> interior)
    {
        long key = UndirectedKey(u, v);
        if (interior.Contains(key))
            return; // already known interior; no need to track further
        if (directedByKey.TryGetValue(key, out _))
        {
            // Second triangle shares this undirected edge -> interior. Drop it.
            directedByKey.Remove(key);
            interior.Add(key);
        }
        else
        {
            directedByKey[key] = (u, v);
        }
    }

    private static long UndirectedKey(int a, int b)
    {
        int lo = a < b ? a : b;
        int hi = a < b ? b : a;
        // Pack two ints into one long deterministically. Vertex counts fit in 31 bits for any
        // realistic tessellation, so this packing is collision-free.
        return (((long)lo) << 32) | (uint)hi;
    }

    private static int CompareRimEdge(DirectedRimEdge x, DirectedRimEdge y)
    {
        int c = x.U.CompareTo(y.U);
        return c != 0 ? c : x.V.CompareTo(y.V);
    }

    // Chain rim edges into ordered closed loop(s) and emit two triangles per quad. Each rim edge
    // (u, v) emits the quad (u, v, N+v, N+u): triangles (u, v, N+v) and (u, N+v, N+u). The loop chain
    // is only needed for deterministic OUTPUT ORDER (the wall set is the same regardless of order),
    // but a stable order makes the triangles array deterministic across runs.
    private static int[] BuildWallTriangles(List<DirectedRimEdge> rimEdges, int n)
    {
        if (rimEdges.Count == 0)
            return Array.Empty<int>();

        // Index rim edges by their head vertex (V) so we can chain: an edge (u, v) is followed by
        // the edge whose U == v. Each vertex has at most two rim edges (one in, one out) for a
        // manifold cap boundary, so the chain is unambiguous.
        var byHead = new Dictionary<int, List<DirectedRimEdge>>(rimEdges.Count);
        foreach (var e in rimEdges)
        {
            if (!byHead.TryGetValue(e.V, out var list))
            {
                list = new List<DirectedRimEdge>();
                byHead[e.V] = list;
            }
            list.Add(e);
        }

        var remaining = new HashSet<DirectedRimEdge>(rimEdges);
        var ordered = new List<DirectedRimEdge>(rimEdges.Count);

        // Deterministic start: begin each loop at the rim edge with the smallest (U, V) that has
        // not yet been consumed. This makes the loop order — and hence the triangle emit order —
        // independent of dictionary enumeration.
        while (remaining.Count > 0)
        {
            // Smallest remaining edge (by U, then V) is the loop seed.
            DirectedRimEdge seed = default;
            bool haveSeed = false;
            foreach (var e in remaining)
            {
                if (!haveSeed || CompareRimEdge(e, seed) < 0)
                {
                    seed = e;
                    haveSeed = true;
                }
            }
            if (!haveSeed)
                break;

            // Walk the loop: edge (u, v) -> next edge whose U == v.
            var current = seed;
            while (remaining.Remove(current))
            {
                ordered.Add(current);
                if (!byHead.TryGetValue(current.V, out var candidates) || candidates.Count == 0)
                    break; // open boundary (shouldn't happen on a closed manifold cap)
                DirectedRimEdge next = default;
                bool haveNext = false;
                foreach (var c in candidates)
                {
                    if (remaining.Contains(c) && (!haveNext || CompareRimEdge(c, next) < 0))
                    {
                        next = c;
                        haveNext = true;
                    }
                }
                if (!haveNext)
                    break;
                current = next;
            }
        }

        // Emit two triangles per quad. Quad (u, v, N+v, N+u) -> (u, v, N+v) and (u, N+v, N+u).
        // The rim edge direction (u -> v) is the top triangle's CCW direction; the wall normal
        // (cross of edge_v - edge_u and edge_Nu - edge_u) points outward (away from the plate).
        var triangles = new int[ordered.Count * 6];
        for (int i = 0; i < ordered.Count; i++)
        {
            int u = ordered[i].U;
            int v = ordered[i].V;
            int b = (i * 6);
            triangles[b + 0] = u;
            triangles[b + 1] = v;
            triangles[b + 2] = n + v;
            triangles[b + 3] = u;
            triangles[b + 4] = n + v;
            triangles[b + 5] = n + u;
        }
        return triangles;
    }

    private readonly record struct DirectedRimEdge(int U, int V);

    // --- centroid ------------------------------------------------------------------------------

    private static CartesianPoint3 ComputeOneCentroid(List<GlobeCell> cells)
    {
        // Area-weighted unit centroid direction: sum(area * cellCentroidDir) / sum(area).
        // Cell centroid direction = normalize(C0 + C1 + C2) from the BASE unit corners (tick/relief-
        // invariant). Spherical-triangle area via Girard's theorem (unit sphere):
        //   area = (A + B + C) - pi
        // where A, B, C are the interior angles at the three vertices, computed from the great-
        // circle arcs using UnifyMaths Vector3D (dot/cross/normalize). All vector math stays inside
        // the Unify* family.
        double sumArea = 0.0;
        double sx = 0.0, sy = 0.0, sz = 0.0;
        foreach (var cell in cells)
        {
            var a = ToUnitVector3D(cell.C0);
            var b = ToUnitVector3D(cell.C1);
            var c = ToUnitVector3D(cell.C2);
            double area = SphericalTriangleArea(a, b, c);
            var centroidDir = (a + b + c).NormalizeOrZero();
            sumArea += area;
            sx += area * centroidDir.X;
            sy += area * centroidDir.Y;
            sz += area * centroidDir.Z;
        }

        if (sumArea <= 0.0)
        {
            // Degenerate plate (all zero-area cells): fall back to the unweighted mean direction.
            double mx = 0.0, my = 0.0, mz = 0.0;
            foreach (var cell in cells)
            {
                mx += cell.C0.X + cell.C1.X + cell.C2.X;
                my += cell.C0.Y + cell.C1.Y + cell.C2.Y;
                mz += cell.C0.Z + cell.C1.Z + cell.C2.Z;
            }
            return NormalizeOrZero(new CartesianPoint3(mx, my, mz));
        }

        return NormalizeOrZero(new CartesianPoint3(sx / sumArea, sy / sumArea, sz / sumArea));
    }

    // Girard's theorem: unit-sphere spherical-triangle area = (A + B + C) - pi, where A, B, C are
    // the interior angles at the three vertices. Each angle is the dihedral angle between the two
    // great-circle arcs meeting at that vertex, computed from the arc tangent vectors via the
    // atan2(|cross(t1, t2)|, dot(t1, t2)) form (robust for small angles). All math via UnifyMaths
    // Vector3D — no hand-rolled vector primitive.
    private static double SphericalTriangleArea(Vector3D a, Vector3D b, Vector3D c)
    {
        // Tangent directions at each vertex along the two arcs to the other two vertices.
        // Tangent along the great circle from p to q at p = normalize(q - (p.q) p) (projection of
        // q onto the plane tangent to the sphere at p).
        var tAB = TangentAlongArc(a, b);
        var tAC = TangentAlongArc(a, c);
        var tBA = TangentAlongArc(b, a);
        var tBC = TangentAlongArc(b, c);
        var tCA = TangentAlongArc(c, a);
        var tCB = TangentAlongArc(c, b);

        double angleA = AngleBetween(tAB, tAC);
        double angleB = AngleBetween(tBA, tBC);
        double angleC = AngleBetween(tCA, tCB);

        double excess = (angleA + angleB + angleC) - Math.PI;
        // On a unit sphere the spherical excess IS the area. Clamp tiny negative jitter from
        // degenerate/colinear cells (shouldn't happen on a real tessellation) to zero.
        return Math.Max(0.0, excess);
    }

    // Unit tangent at p along the great circle from p to q. Project q onto the plane tangent to the
    // sphere at p, then normalize. Returns the zero vector if p and q are (anti)parallel.
    private static Vector3D TangentAlongArc(Vector3D p, Vector3D q)
    {
        double dot = Vector3D.Dot(p, q);
        var projected = q - (p * dot);
        return projected.NormalizeOrZero();
    }

    // Robust angle between two unit vectors via atan2(|cross|, dot). Returns 0 for (near-)parallel.
    private static double AngleBetween(Vector3D u, Vector3D v)
    {
        double dot = Vector3D.Dot(u, v);
        var cross = Vector3D.Cross(u, v);
        double crossLen = cross.Length();
        return Math.Atan2(crossLen, dot);
    }

    // --- explode transform ---------------------------------------------------------------------

    private static PlateSolid TranslateSolid(PlateSolid solid, CartesianPoint3 direction, double offset)
    {
        if (offset == 0.0)
            return solid; // factor 0 -> bit-equal identity (no allocation, same arrays)

        double dx = direction.X * offset;
        double dy = direction.Y * offset;
        double dz = direction.Z * offset;

        var src = solid.Positions;
        var translated = new CartesianPoint3[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var p = src[i];
            translated[i] = new CartesianPoint3(p.X + dx, p.Y + dy, p.Z + dz);
        }
        // Triangles are unchanged — the explode is a pure translation of positions.
        return new PlateSolid(solid.PlateId, translated, solid.Triangles);
    }

    // --- helpers -------------------------------------------------------------------------------

    private static Vector3D ToUnitVector3D(GlobeVec3 v)
    {
        var d = new Vector3D(v.X, v.Y, v.Z);
        double len = d.Length();
        return len > Epsilon ? d / len : d;
    }

    private static CartesianPoint3 NormalizeOrZero(CartesianPoint3 p)
    {
        double len = Math.Sqrt((p.X * p.X) + (p.Y * p.Y) + (p.Z * p.Z));
        return len > Epsilon
            ? new CartesianPoint3(p.X / len, p.Y / len, p.Z / len)
            : new CartesianPoint3(0.0, 0.0, 0.0);
    }

    private static bool IsPositiveFinite(double value)
        => value > 0.0 && !double.IsNaN(value) && !double.IsInfinity(value);
}

/// <summary>
/// Extension methods on UnifyMaths <see cref="Vector3D"/> used by the solid builder. Kept here (not
/// added to the UnifyMaths package) because they are thin convenience wrappers over the existing
/// public <see cref="Vector3D"/> API (Normalize/Length/Cross/Dot) — no new vector primitive is
/// introduced, satisfying the "no hand-rolled vector primitive that Unify already provides" lock.
/// </summary>
internal static class Vector3DExtensions
{
    /// <summary>
    /// Returns the unit-length vector in the direction of <paramref name="v"/>, or the zero vector
    /// if <paramref name="v"/> is degenerate (zero-length). Matches the cartography
    /// <c>VectorMath.NormalizeOr</c> convention but on UnifyMaths <see cref="Vector3D"/>.
    /// </summary>
    public static Vector3D NormalizeOrZero(this Vector3D v)
    {
        double len = v.Length();
        const double eps = 1e-15;
        return len <= eps ? new Vector3D(0.0, 0.0, 0.0) : v / len;
    }
}