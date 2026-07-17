using System;
using System.Collections.Generic;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Shared;
using UnifyMaths;

namespace FantaSim.App.World.Globe;

/// <summary>
/// Declared mesh-fidelity options for <see cref="BoundaryConcentratedSubdivider"/>. These are
/// presentation-layer look numbers (S1 discipline), NOT geological authority (§9 guard).
/// </summary>
/// <param name="MaxDepth">
/// Number of 1-to-4 subdivision passes applied to boundary-near triangles. Each pass multiplies
/// boundary density by 4 and extends the green-split transition ring by one cell. 2 gives 16x
/// boundary density — enough that smooth-shaded normals round off the cell grid at boundary
/// arcs without growing the whole-globe triangle count.
/// </param>
/// <param name="BoundaryHalfWidthRad">
/// Angular half-width (radians) of the boundary band: a cell qualifies when its unit-sphere
/// centroid lies within this distance of any <see cref="CrustVolumeState.BoundaryArcs"/> point.
/// At geodesic frequency 3/4 cells are ~0.3 rad apart, so 0.15 rad captures the boundary cell plus
/// its immediate neighbourhood. The ring is what gets denser; quiet interiors stay coarse.
/// </param>
public sealed record BoundaryConcentrationOptions(
    int MaxDepth = 2,
    double BoundaryHalfWidthRad = 0.15)
{
    public static BoundaryConcentrationOptions Default { get; } = new();
}

/// <summary>
/// One plate's subdivision bookkeeping after a pass: the subdivided <see cref="PlateCap"/> plus the
/// number of SOURCE triangles that received at least one new midpoint. The count feeds the V1 boot
/// log marker so a lead session can confirm the concentration path ran and bound a non-zero band.
/// Mesh-fidelity accounting only — no geological authority.
/// </summary>
public sealed record BoundarySubdivisionResult(
    PlateCap Cap,
    int SubdividedSourceTriangles,
    int TotalTriangles);

/// <summary>
/// Pure, Godot-free mesh helper that subdivides the triangles of a volume-derived
/// <see cref="PlateCap"/> near plate-boundary arcs (V1 "closed skin",
/// <c>vault/specs/2026-07-18-visual-fidelity-slices-decision.md</c>).
///
/// <para>
/// New vertices sample their position through <see cref="CrustVolumeState.MapMaterialPoint"/> at the
/// outer surface (<c>depthFraction = 0</c>), so this changes surface FIDELITY only — never the shape
/// authority (no renderer-authored displacement, no noise, no gap-filling strips). Quiet interiors
/// keep base resolution; the boundary ring gets denser geometry so smooth-shaded normals have enough
/// vertex density to round off the visible cell grid at boundary arcs.
/// </para>
///
/// <para>
/// <b>Crack-free by construction (red-green).</b> A pass marks every undirected edge touched by a
/// boundary-near face as needing a midpoint, then emits each face with 0/1/2/3 midpoint edges as
/// 1/2/3/4 triangles respectively (the standard red-green split). Adjacent faces agree on every
/// shared edge's midpoint, so no T-junction opens at the transition between subdivided boundary
/// and coarse interior. Multiple passes compose: each pass runs the same logic on the previous
/// output, and the green-split ring extends one cell further into the interior per pass.
/// </para>
///
/// <para>
/// <b>Watertight across cells.</b> Midpoints are keyed by undirected surface-vertex edge, so two
/// faces of the same plate sharing an edge share the same midpoint vertex. The volume state welds
/// outer controls across cells (<see cref="GlobePlateSurfaces.BuildVolumeSurfaces"/> enforces it),
/// so <c>MapMaterialPoint(cellA, w, 0)</c> and <c>MapMaterialPoint(cellB, w, 0)</c> return bit-equal
/// positions for a shared edge — the subdivided cap stays watertight without any new state query.
/// </para>
///
/// <para>
/// Pure, deterministic, no wall-clock. Two calls with equal inputs produce equal outputs. A
/// <c>null</c> or zero-depth options record returns the input cap unchanged.
/// </para>
/// </summary>
public static class BoundaryConcentratedSubdivider
{
    /// <summary>
    /// Subdivides boundary-near triangles of <paramref name="cap"/> using the volume's outer-surface
    /// mapping. Returns the input cap unchanged when <paramref name="options"/> is null, has
    /// <c>MaxDepth &lt;= 0</c>, or the volume declares no active boundary arcs.
    /// </summary>
    public static BoundarySubdivisionResult Subdivide(
        PlateCap cap,
        CrustVolumeState volume,
        BoundaryConcentrationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cap);
        ArgumentNullException.ThrowIfNull(volume);

        var opts = options ?? BoundaryConcentrationOptions.Default;
        if (opts.MaxDepth <= 0 || opts.BoundaryHalfWidthRad <= 0.0)
            return new BoundarySubdivisionResult(cap, 0, cap.Surface.TriangleCount);

        var arcPoints = CollectActiveArcUnitPoints(volume);
        if (arcPoints.Count == 0)
            return new BoundarySubdivisionResult(cap, 0, cap.Surface.TriangleCount);

        var boundaryCells = BuildBoundaryCellSet(volume, arcPoints, opts.BoundaryHalfWidthRad);
        if (boundaryCells.Count == 0)
            return new BoundarySubdivisionResult(cap, 0, cap.Surface.TriangleCount);

        var surface = cap.Surface;
        // Source triangles whose cell is boundary-near. These (and only these) drive edge midpoints;
        // the green-split transition is a consequence of shared edges, tracked separately per pass.
        int subdividedSourceTriangles = CountSourceTrianglesInBoundaryCells(cap, boundaryCells);

        var currentPositions = surface.Positions;
        var currentTriangles = surface.Triangles;
        var currentCellIds = cap.CellIds;

        for (int pass = 0; pass < opts.MaxDepth; pass++)
        {
            var r = SubdivideOnce(
                currentPositions,
                currentTriangles,
                currentCellIds,
                volume,
                boundaryCells);
            currentPositions = r.Positions;
            currentTriangles = r.Triangles;
            currentCellIds = r.CellIds;
        }

        var (flatNormals, smoothNormals) = ComputeNormals(currentPositions, currentTriangles);

        var subdividedSurface = new GlobeSurface(
            currentPositions,
            currentTriangles,
            smoothNormals,
            flatNormals);

        var subdividedCap = new PlateCap(
            cap.PlateId,
            currentCellIds,
            subdividedSurface,
            VertexProvenance: null);
        return new BoundarySubdivisionResult(
            subdividedCap,
            subdividedSourceTriangles,
            subdividedSurface.TriangleCount);
    }

    // --- boundary classification ---------------------------------------------------------------

    // Collects unit-sphere points from every non-Inactive arc, pre-normalized. The arc contract
    // promises unit-length points; the defensive normalize keeps the angular-distance math exact
    // even if a future carrier relaxes that. Read-only over the state's public accessor.
    private static List<Vector3D> CollectActiveArcUnitPoints(CrustVolumeState volume)
    {
        var arcs = volume.BoundaryArcs;
        var points = new List<Vector3D>(arcs.Count * 8);
        foreach (var arc in arcs)
        {
            if (arc.Kind == PlateBoundaryKind.Inactive) continue;
            foreach (var p in arc.Points)
            {
                var v = new Vector3D(p.X, p.Y, p.Z);
                double len = v.Length();
                if (len > 1e-12)
                    points.Add(v * (1.0 / len));
            }
        }
        return points;
    }

    // A cell is boundary-near when its unit-sphere centroid lies within the declared angular
    // half-width of any active arc point. Mirrors the CellBoundaryField.Build centroid-distance
    // idiom (acos(clamp(max dot))). Cells with no nearby arc stay at base resolution.
    private static HashSet<int> BuildBoundaryCellSet(
        CrustVolumeState volume,
        List<Vector3D> arcPoints,
        double halfWidthRad)
    {
        double minDot = Math.Cos(halfWidthRad);
        var cells = volume.Globe.Cells;
        var set = new HashSet<int>();
        foreach (var cell in cells)
        {
            var centroid = UnitMean(cell.C0, cell.C1, cell.C2);
            double bestDot = -2.0;
            for (int i = 0; i < arcPoints.Count; i++)
            {
                double dot = Vector3D.Dot(centroid, arcPoints[i]);
                if (dot > bestDot) bestDot = dot;
            }
            if (bestDot >= minDot)
                set.Add(cell.CellId);
        }
        return set;
    }

    private static Vector3D UnitMean(GlobeVec3 a, GlobeVec3 b, GlobeVec3 c)
    {
        var v = new Vector3D(a.X + b.X + c.X, a.Y + b.Y + c.Y, a.Z + b.Z + c.Z);
        double len = v.Length();
        return len > 1e-12 ? v * (1.0 / len) : v;
    }

    private static int CountSourceTrianglesInBoundaryCells(PlateCap cap, HashSet<int> boundaryCells)
    {
        int count = 0;
        foreach (var cellId in cap.CellIds)
            if (boundaryCells.Contains(cellId))
                count++;
        return count;
    }

    // --- one subdivision pass ------------------------------------------------------------------

    private readonly record struct EdgeKey(int Lo, int Hi);

    private sealed class PassAccumulator
    {
        public List<CartesianPoint3> Positions;
        public List<int> Triangles;
        public List<int> CellIds;
        public Dictionary<EdgeKey, int> EdgeMidpoint;
        public Dictionary<EdgeKey, (int Face, int CornerA, int CornerB)> EdgeFirstFace;

        public PassAccumulator(CartesianPoint3[] seedPositions)
        {
            Positions = new List<CartesianPoint3>(seedPositions.Length * 2);
            for (int i = 0; i < seedPositions.Length; i++)
                Positions.Add(seedPositions[i]);
            Triangles = new List<int>(seedPositions.Length);
            CellIds = new List<int>(seedPositions.Length);
            EdgeMidpoint = new Dictionary<EdgeKey, int>();
            EdgeFirstFace = new Dictionary<EdgeKey, (int, int, int)>();
        }
    }

    private static (CartesianPoint3[] Positions, int[] Triangles, int[] CellIds) SubdivideOnce(
        CartesianPoint3[] positions,
        int[] triangles,
        int[] cellIds,
        CrustVolumeState volume,
        HashSet<int> boundaryCells)
    {
        int faceCount = triangles.Length / 3;
        var acc = new PassAccumulator(positions);

        // Phase 1: record for every undirected edge the first face (and its corner pair) that owns
        // it AND has a boundary-near cell. That face/cell is the authoritative sampler for the
        // midpoint (interior neighbours agree by the state's cross-cell weld). This is also the set
        // of edges that will receive a midpoint in this pass.
        for (int f = 0; f < faceCount; f++)
        {
            int cellId = cellIds[f];
            if (!boundaryCells.Contains(cellId)) continue;
            ConsiderEdge(f, 0, 1);
            ConsiderEdge(f, 1, 2);
            ConsiderEdge(f, 2, 0);
        }

        void ConsiderEdge(int face, int cornerA, int cornerB)
        {
            int va = triangles[(face * 3) + cornerA];
            int vb = triangles[(face * 3) + cornerB];
            var key = MakeKey(va, vb);
            if (!acc.EdgeFirstFace.ContainsKey(key))
                acc.EdgeFirstFace[key] = (face, cornerA, cornerB);
        }

        // Phase 2: materialise midpoints for the recorded edges by sampling the volume's outer
        // surface mapping at depthFraction = 0. The barycentric weights are the two incident corner
        // weights (0.5 each); the third corner gets 0.
        Span<double> weights = stackalloc double[3];
        foreach (var kvp in acc.EdgeFirstFace)
        {
            var (face, cornerA, cornerB) = kvp.Value;
            int cellId = cellIds[face];
            weights[0] = 0.0;
            weights[1] = 0.0;
            weights[2] = 0.0;
            weights[cornerA] = 0.5;
            weights[cornerB] = 0.5;
            var mapped = volume.MapMaterialPoint(cellId, weights[0], weights[1], weights[2], depthFraction: 0.0);
            var point = new CartesianPoint3(mapped.X, mapped.Y, mapped.Z);
            acc.EdgeMidpoint[kvp.Key] = acc.Positions.Count;
            acc.Positions.Add(point);
        }

        // Phase 3: emit each face with 0..3 midpoint edges as 1..4 triangles (red-green). Child
        // triangles inherit the parent's cell id so downstream colour/feature lookups (and further
        // passes) keep working without a parallel array.
        for (int f = 0; f < faceCount; f++)
        {
            int cellId = cellIds[f];
            int a = triangles[(f * 3) + 0];
            int b = triangles[(f * 3) + 1];
            int c = triangles[(f * 3) + 2];
            var kAb = MakeKey(a, b);
            var kBc = MakeKey(b, c);
            var kCa = MakeKey(c, a);
            bool hasAb = acc.EdgeMidpoint.TryGetValue(kAb, out int mAb);
            bool hasBc = acc.EdgeMidpoint.TryGetValue(kBc, out int mBc);
            bool hasCa = acc.EdgeMidpoint.TryGetValue(kCa, out int mCa);

            if (hasAb && hasBc && hasCa)
            {
                Emit(acc, cellId, a, mAb, mCa);
                Emit(acc, cellId, mAb, b, mBc);
                Emit(acc, cellId, mCa, mBc, c);
                Emit(acc, cellId, mAb, mBc, mCa);
            }
            else if (hasAb && hasBc)
            {
                Emit(acc, cellId, a, mAb, c);
                Emit(acc, cellId, mAb, b, mBc);
                Emit(acc, cellId, mAb, mBc, c);
            }
            else if (hasBc && hasCa)
            {
                Emit(acc, cellId, a, b, mCa);
                Emit(acc, cellId, b, mBc, mCa);
                Emit(acc, cellId, mBc, c, mCa);
            }
            else if (hasAb && hasCa)
            {
                Emit(acc, cellId, a, mAb, mCa);
                Emit(acc, cellId, mAb, b, c);
                Emit(acc, cellId, mAb, c, mCa);
            }
            else if (hasAb)
            {
                Emit(acc, cellId, a, mAb, c);
                Emit(acc, cellId, mAb, b, c);
            }
            else if (hasBc)
            {
                Emit(acc, cellId, a, b, mBc);
                Emit(acc, cellId, a, mBc, c);
            }
            else if (hasCa)
            {
                Emit(acc, cellId, a, b, mCa);
                Emit(acc, cellId, a, mCa, c);
            }
            else
            {
                Emit(acc, cellId, a, b, c);
            }
        }

        return (acc.Positions.ToArray(), acc.Triangles.ToArray(), acc.CellIds.ToArray());
    }

    private static void Emit(PassAccumulator acc, int cellId, int a, int b, int c)
    {
        acc.Triangles.Add(a);
        acc.Triangles.Add(b);
        acc.Triangles.Add(c);
        acc.CellIds.Add(cellId);
    }

    private static EdgeKey MakeKey(int a, int b)
        => a <= b ? new EdgeKey(a, b) : new EdgeKey(b, a);

    // --- normal recomputation ------------------------------------------------------------------

    // Flat normals: one per emitted triangle (normalised cross product). Smooth normals: per
    // vertex, the area-weighted arithmetic mean of every incident triangle's flat normal, then
    // normalised. Area-weighting (rather than naive mean) keeps the shading continuous across
    // triangles of very different sizes — the standard Gouraud-with-areas rule. Mirrors what the
    // cartography GlobeSurfaceBuilder produces for the fixed caps.
    private static (CartesianPoint3[] Flat, CartesianPoint3[] Smooth) ComputeNormals(
        CartesianPoint3[] positions,
        int[] triangles)
    {
        int triCount = triangles.Length / 3;
        var flat = new CartesianPoint3[triCount];
        var smoothSums = new Vector3D[positions.Length];

        for (int t = 0; t < triCount; t++)
        {
            int i0 = triangles[(t * 3) + 0];
            int i1 = triangles[(t * 3) + 1];
            int i2 = triangles[(t * 3) + 2];
            var p0 = positions[i0];
            var p1 = positions[i1];
            var p2 = positions[i2];

            double ux = p1.X - p0.X, uy = p1.Y - p0.Y, uz = p1.Z - p0.Z;
            double vx = p2.X - p0.X, vy = p2.Y - p0.Y, vz = p2.Z - p0.Z;
            double nx = (uy * vz) - (uz * vy);
            double ny = (uz * vx) - (ux * vz);
            double nz = (ux * vy) - (uy * vx);
            double len = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
            double ax = nx, ay = ny, az = nz; // raw cross = area-weighted face normal
            if (len > 1e-12)
            {
                flat[t] = new CartesianPoint3(nx / len, ny / len, nz / len);
            }
            else
            {
                flat[t] = new CartesianPoint3(0.0, 0.0, 1.0);
                ax = 0.0; ay = 0.0; az = 1.0;
            }

            smoothSums[i0] += new Vector3D(ax, ay, az);
            smoothSums[i1] += new Vector3D(ax, ay, az);
            smoothSums[i2] += new Vector3D(ax, ay, az);
        }

        var smooth = new CartesianPoint3[positions.Length];
        for (int v = 0; v < smooth.Length; v++)
        {
            var s = smoothSums[v];
            double len = Math.Sqrt((s.X * s.X) + (s.Y * s.Y) + (s.Z * s.Z));
            smooth[v] = len > 1e-12
                ? new CartesianPoint3(s.X / len, s.Y / len, s.Z / len)
                : new CartesianPoint3(0.0, 0.0, 1.0);
        }
        return (flat, smooth);
    }
}
