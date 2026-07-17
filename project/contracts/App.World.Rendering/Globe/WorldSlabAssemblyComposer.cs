using System;
using System.Collections.Generic;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using FantaSim.Cartography.Globe.Core;
using FantaSim.Cartography.Shared;
using UnifyMaths;
using UnifyMaths.Numerics;

namespace FantaSim.App.World.Globe;

/// <summary>
/// Pure, Godot-free composer for the DEFAULT World view's slab assembly (assembled-world slice 1):
/// one closed watertight <see cref="PlateSolid"/> per plate from the relief-applied top caps
/// (the SAME <see cref="PlateSolidBuilder"/> machinery the mantle-layer and exploded views use),
/// translated by the declared JOINT GAP along each plate's area-weighted centroid direction — the
/// EXISTING separation math (<see cref="PlateSolidBuilder.ApplyExplodedFactor"/>) at joint scale
/// instead of explode scale.
/// </summary>
/// <remarks>
/// <para>The gap is a pure per-plate translation: topology unchanged, the slab undeformed, and
/// adjacent slabs' formerly-coincident boundary vertices open by <c>gap × |dirA − dirB|</c>. Two
/// assemblies from identical inputs are bit-identical (everything downstream of the deterministic
/// builder + transform).</para>
/// <para>The joint gap must be positive and finite — the spec requires a VISIBLE joint. A seamless
/// sphere is the <see cref="WorldSurfacePresentation.WatertightSphere"/> presentation (the old
/// single-surface path), never a zero gap smuggled through the slab path.</para>
/// </remarks>
public static class WorldSlabAssemblyComposer
{
    /// <summary>
    /// Builds the assembled World slabs: <see cref="PlateSolidBuilder.Build"/> over the caps +
    /// thickness field, then the joint-gap translation via
    /// <see cref="PlateSolidBuilder.ApplyExplodedFactor"/> with <c>factor = 1</c> and
    /// <c>maxOffset = SlabJointGapUnitRadius</c>.
    /// </summary>
    /// <param name="caps">Per-plate relief-applied top caps (e.g. from <see cref="SlabTopReliefComposer.BuildCaps"/> — slab-declared relief, no World silhouette clamp).</param>
    /// <param name="centroids">Per-plate centroid directions (from <see cref="PlateSolidBuilder.ComputeCentroids"/>), indexed by plate id.</param>
    /// <param name="crustThicknessByCellMetres">Per-cell crust THICKNESS in metres, indexed by cell id.</param>
    /// <param name="thicknessDepthScale">Metres-to-unit-radius thickness depth scale (D3's <c>RadialSectionProfile.ThicknessDepthScale()</c>).</param>
    /// <param name="profile">The declared World-surface presentation profile (owns the joint gap).</param>
    /// <param name="baseRadius">The unit-sphere base radius (default 1.0, matching <see cref="GlobeSurfaceBuilder.DefaultRadius"/>).</param>
    /// <returns>One gapped <see cref="PlateSolid"/> per input cap, in the SAME order as <paramref name="caps"/>.</returns>
    public static IReadOnlyList<PlateSolid> BuildAssembly(
        IReadOnlyList<PlateCap> caps,
        IReadOnlyList<PlateSolidCentroid> centroids,
        IReadOnlyList<double> crustThicknessByCellMetres,
        double thicknessDepthScale,
        WorldSurfacePresentationProfile profile,
        double baseRadius = GlobeSurfaceBuilder.DefaultRadius)
    {
        ArgumentNullException.ThrowIfNull(caps);
        ArgumentNullException.ThrowIfNull(centroids);
        ArgumentNullException.ThrowIfNull(crustThicknessByCellMetres);
        ArgumentNullException.ThrowIfNull(profile);

        double gap = profile.SlabJointGapUnitRadius;
        if (double.IsNaN(gap) || double.IsInfinity(gap) || gap <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(profile),
                gap,
                "The slab joint gap must be positive and finite — the assembled world requires a visible joint. "
                + "Use the WatertightSphere presentation for a seamless sphere.");
        }

        var assembled = PlateSolidBuilder.Build(caps, crustThicknessByCellMetres, thicknessDepthScale, baseRadius);
        return PlateSolidBuilder.ApplyExplodedFactor(assembled, centroids, factor: 1.0, maxOffset: gap);
    }

    /// <summary>
    /// Assembled-world slice 2: shapes the slab EDGES to express the convergent / divergent / transform
    /// mechanics at each joint (vault/specs/2026-07-16-assembled-world-northstar.md clause 3: "how
    /// mountain, trench is formed. How plate A is under plate b and moved"). Applied to the gap-
    /// translated slabs from slice 1; topology is NEVER edited, only positions, so each slab stays
    /// watertight and the plate count is unchanged.
    /// </summary>
    /// <remarks>
    /// <para><b>Convergent subduction</b> (polarity known): the SUBDUCTING slab's edge band dips
    /// radially inward along the joint — a smooth ramp over the band — so its top passes visibly BELOW
    /// the OVERRIDING slab's edge band, whose margin raises radially outward (the mountain-piling
    /// onset). The dive line reads as a trench-like depression. The effective dip is grown past the
    /// declared <see cref="SlabJointMechanicsProfile.SubductionDipUnitRadius"/> when a slab is thicker
    /// than the visual dip, so the subducting top always clears the overriding bottom by at least
    /// <see cref="SlabJointMechanicsProfile.MinClearanceUnitRadius"/> (no interpenetration).</para>
    /// <para><b>Convergent collision</b> (or unresolved polarity): BOTH sides raise symmetrically.</para>
    /// <para><b>Divergent</b>: the joint gap widens locally by the declared multiplier, reusing the
    /// SAME centroid-direction separation the base gap uses.</para>
    /// <para><b>Transform / inactive</b>: no change — a transform-only shaping is bit-identical to
    /// slice 1.</para>
    /// <para>Pure, Godot-free, deterministic. Same inputs always yield bit-identical outputs. Returns
    /// the input solid references unchanged when a joint demands no displacement (the no-op fast path
    /// keeps transform/empty-joint cases bit-identical to slice 1).</para>
    /// </remarks>
    /// <param name="gappedSolids">The slice-1 gap-translated slabs (from <see cref="BuildAssembly"/>).</param>
    /// <param name="joints">Canonical boundary segments. Inactive segments are ignored.</param>
    /// <param name="jointProfile">The declared joint-mechanics magnitudes (eye-tuned).</param>
    /// <param name="centroids">Per-plate centroid directions (from <see cref="PlateSolidBuilder.ComputeCentroids"/>),
    /// indexed by plate id. Drives the divergent widening direction.</param>
    /// <param name="jointGapUnitRadius">The slice-1 joint gap (from
    /// <see cref="WorldSurfacePresentationProfile.SlabJointGapUnitRadius"/>). The divergent multiplier
    /// scales THIS gap.</param>
    /// <param name="baseRadius">The unit-sphere base radius (default 1.0).</param>
    /// <returns>One shaped <see cref="PlateSolid"/> per input, SAME order, SAME triangles (positions only).</returns>
    public static IReadOnlyList<PlateSolid> ShapeSlabJoints(
        IReadOnlyList<PlateSolid> gappedSolids,
        IReadOnlyList<PlateBoundaryArc> joints,
        SlabJointMechanicsProfile jointProfile,
        IReadOnlyList<PlateSolidCentroid> centroids,
        double jointGapUnitRadius,
        double baseRadius = GlobeSurfaceBuilder.DefaultRadius)
    {
        ArgumentNullException.ThrowIfNull(gappedSolids);
        ArgumentNullException.ThrowIfNull(joints);
        ArgumentNullException.ThrowIfNull(jointProfile);
        ArgumentNullException.ThrowIfNull(centroids);
        if (double.IsNaN(jointGapUnitRadius) || double.IsInfinity(jointGapUnitRadius) || jointGapUnitRadius < 0.0)
            throw new ArgumentOutOfRangeException(nameof(jointGapUnitRadius), jointGapUnitRadius, "Joint gap must be non-negative and finite.");
        if (!IsPositiveFinite(baseRadius))
            throw new ArgumentOutOfRangeException(nameof(baseRadius), "Base radius must be positive and finite.");

        // No active joints => pure no-op: hand back the input solids unchanged (bit-identical to slice 1).
        if (joints.Count == 0 || !HasShapingJoint(joints))
            return gappedSolids;

        ValidateJointProfile(jointProfile);

        var centroidByPlate = new Dictionary<int, Vector3D>(centroids.Count);
        foreach (var c in centroids)
            centroidByPlate[c.PlateId] = new Vector3D(c.CentroidDirection.X, c.CentroidDirection.Y, c.CentroidDirection.Z);

        // Precompute each joint's arc as unit Vector3D points (normalized; the classifier emits unit
        // points but a defensive normalize keeps the angular-distance math exact).
        var shapedJoints = new List<(PlateBoundaryArc Joint, Vector3D[] Arc, double EffectiveDip)>(joints.Count);
        foreach (var joint in joints)
        {
            if (joint.Kind == PlateBoundaryKind.Inactive) continue;
            if (joint.Points.Count < 2) continue;
            var arc = new Vector3D[joint.Points.Count];
            for (int i = 0; i < arc.Length; i++)
            {
                var p = joint.Points[i];
                var v = new Vector3D(p.X, p.Y, p.Z);
                double len = v.Length();
                arc[i] = len > Epsilon ? v * (1.0 / len) : v;
            }
            double effectiveDip = ResolveEffectiveDip(
                joint, jointProfile, gappedSolids, centroidByPlate, arc);
            shapedJoints.Add((joint, arc, effectiveDip));
        }

        // If every joint was inactive / degenerate, the shaping is a no-op.
        if (shapedJoints.Count == 0)
            return gappedSolids;

        // Accumulate a displacement vector per vertex per solid. Vertices outside every edge band keep
        // a zero displacement. The accumulation is additive and joint-order-deterministic.
        var displacements = new Vector3D[gappedSolids.Count][];
        var anyDisplacement = new bool[gappedSolids.Count];
        for (int s = 0; s < gappedSolids.Count; s++)
            displacements[s] = new Vector3D[gappedSolids[s].VertexCount];

        foreach (var (joint, arc, effectiveDip) in shapedJoints)
        {
            for (int s = 0; s < gappedSolids.Count; s++)
            {
                var solid = gappedSolids[s];
                int plateId = solid.PlateId;
                if (plateId != joint.PlateA && plateId != joint.PlateB) continue;

                var disp = displacements[s];
                var positions = solid.Positions;
                for (int v = 0; v < positions.Length; v++)
                {
                    var p = positions[v];
                    var u = new Vector3D(p.X, p.Y, p.Z);
                    double len = u.Length();
                    if (len <= Epsilon) continue;
                    u = u * (1.0 / len);
                    double angularDist = MinAngularDistance(u, arc);
                    double w = EdgeBandWeight(angularDist, jointProfile.EdgeBandHalfWidthRad);
                    if (w <= 0.0) continue;

                    var contribution = JointContribution(
                        joint, plateId, u, w, effectiveDip, jointProfile,
                        centroidByPlate, jointGapUnitRadius);
                    if (Vector3D.Dot(contribution, contribution) > 0.0)
                    {
                        disp[v] = disp[v] + contribution;
                        anyDisplacement[s] = true;
                    }
                }
            }
        }

        // Apply: positions = old + displacement. Solids with no displacement keep their input
        // reference (bit-identical to slice 1 — the transform/empty no-op guarantee).
        var result = new PlateSolid[gappedSolids.Count];
        for (int s = 0; s < gappedSolids.Count; s++)
        {
            var solid = gappedSolids[s];
            if (!anyDisplacement[s])
            {
                result[s] = solid;
                continue;
            }
            var src = solid.Positions;
            var disp = displacements[s];
            var shaped = new CartesianPoint3[src.Length];
            for (int v = 0; v < src.Length; v++)
            {
                var d = disp[v];
                shaped[v] = new CartesianPoint3(src[v].X + d.X, src[v].Y + d.Y, src[v].Z + d.Z);
            }
            result[s] = new PlateSolid(solid.PlateId, shaped, solid.Triangles);
        }
        return result;
    }

    /// <summary>
    /// Convenience overload: slice-1 assembly plus slice-2 joint shaping in one call. Buried
    /// subduction geometry is intentionally omitted from this assembled scaffold; the canonical
    /// crust-volume extractor owns that geometry.
    /// </summary>
    public static IReadOnlyList<PlateSolid> BuildAssembly(
        IReadOnlyList<PlateCap> caps,
        IReadOnlyList<PlateSolidCentroid> centroids,
        IReadOnlyList<double> crustThicknessByCellMetres,
        double thicknessDepthScale,
        WorldSurfacePresentationProfile profile,
        IReadOnlyList<PlateBoundaryArc> joints,
        SlabJointMechanicsProfile jointProfile,
        double baseRadius = GlobeSurfaceBuilder.DefaultRadius)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var gapped = BuildAssembly(caps, centroids, crustThicknessByCellMetres, thicknessDepthScale, profile, baseRadius);
        return ShapeSlabJoints(gapped, joints, jointProfile, centroids, profile.SlabJointGapUnitRadius, baseRadius);
    }

    /// <summary>
    /// Assembled-world slice 3: for every CONVERGENT, NON-COLLISION joint, the SUBDUCTING plate's
    /// solid grows a TONGUE — a real, watertight thick-strip geometric extension that reaches
    /// laterally past the joint path toward / under the overriding side and descends radially (the
    /// "diving tongue beneath" of
    /// <c>vault/reference/2026-07-17-assembled-world-image-prompt.md</c> v4/v5). Applied to the
    /// slice-2 shaped slabs; the topology is EXTENDED (new vertices + triangles) following how
    /// <see cref="PlateSolidBuilder"/> builds walls, so the tongued solid stays watertight.
    /// </summary>
    /// <remarks>
    /// <para><b>Watertight by construction.</b> The tongue is a thick strip whose near edge reuses
    /// the subducting rim vertices along the joint path; the rim WALL QUADS along the path are
    /// dropped (rebuilt), so the path rim edge becomes INTERIOR (shared by the top cap and the
    /// tongue top surface — exactly two triangles). The tongue carries its own top surface,
    /// underside, far-end wall, and two side walls, every edge shared by exactly two triangles.</para>
    /// <para><b>Assembled no-interpenetration.</b> The tongue descends from the (slice-2-dipped)
    /// rim; its far-edge radial drop is grown structurally — same pattern
    /// <see cref="ShapeSlabJoints"/> uses for the dip — so the tongue top clears the overriding
    /// bottom by at least <see cref="SlabJointMechanicsProfile.MinClearanceUnitRadius"/>.</para>
    /// <para><b>Exploded.</b> Tongues are part of their plate's <see cref="PlateSolid.Positions"/>,
    /// so <see cref="PlateSolidBuilder.ApplyExplodedFactor"/> translates them with the plate — no
    /// special-casing, no exploded-math change.</para>
    /// <para>Pure, Godot-free, deterministic. Returns the input solid references unchanged when a
    /// joint demands no tongue (transform/divergent/collision/non-convergent, or no qualifying
    /// joint) — bit-identical to slice 2.</para>
    /// </remarks>
    /// <param name="shapedSolids">The slice-2 shape-translated slabs (from <see cref="ShapeSlabJoints"/>).</param>
    /// <param name="joints">Per-joint classifications. Only convergent non-collision joints with a
    /// resolved <see cref="PlateBoundaryArc.SubductingPlateId"/> grow a tongue.</param>
    /// <param name="jointProfile">The declared joint-mechanics magnitudes (owns the tongue params).</param>
    /// <param name="baseRadius">The unit-sphere base radius (default 1.0).</param>
    /// <returns>One <see cref="PlateSolid"/> per input, SAME order. The subducting plate of each
    /// qualifying joint carries a tongue; every other solid is the input reference unchanged.</returns>
    public static IReadOnlyList<PlateSolid> ShapeSubductionTongues(
        IReadOnlyList<PlateSolid> shapedSolids,
        IReadOnlyList<PlateBoundaryArc> joints,
        SlabJointMechanicsProfile jointProfile,
        double baseRadius = GlobeSurfaceBuilder.DefaultRadius)
    {
        ArgumentNullException.ThrowIfNull(shapedSolids);
        ArgumentNullException.ThrowIfNull(joints);
        ArgumentNullException.ThrowIfNull(jointProfile);
        if (!IsPositiveFinite(baseRadius))
            throw new ArgumentOutOfRangeException(nameof(baseRadius), "Base radius must be positive and finite.");
        ValidateTongueProfile(jointProfile);

        // No qualifying tongue joint => pure no-op: hand back the input solids unchanged
        // (bit-identical to slice 2).
        if (joints.Count == 0 || !HasTongueJoint(joints))
            return shapedSolids;

        // Seed the result with the input references; only a subducting plate of a qualifying joint
        // is rebuilt (every other solid keeps its input reference — bit-identical to slice 2).
        var result = new PlateSolid[shapedSolids.Count];
        var indexByPlate = new Dictionary<int, int>(shapedSolids.Count);
        for (int i = 0; i < shapedSolids.Count; i++)
        {
            indexByPlate[shapedSolids[i].PlateId] = i;
            result[i] = shapedSolids[i];
        }

        foreach (var joint in joints)
        {
            if (joint.Kind != PlateBoundaryKind.Convergent) continue;
            if (joint.IsCollision) continue;
            if (joint.SubductingPlateId is not int subductingId) continue;
            if (joint.Points.Count < 2) continue;
            if (!indexByPlate.TryGetValue(subductingId, out int subIdx)) continue;
            int overridingId = joint.PlateA == subductingId ? joint.PlateB : joint.PlateA;
            if (!indexByPlate.TryGetValue(overridingId, out int overIdx)) continue;

            var arc = NormalizeArc(joint.Points);
            // Operate on the current (possibly already-tongued) subducting solid so tongues
            // accumulate when one plate subducts at several joints. The overriding solid is only
            // read (for the structural floor), never rebuilt here.
            var tongued = BuildSubductionTongue(result[subIdx], result[overIdx], arc, subductingId, jointProfile);
            if (tongued is not null)
                result[subIdx] = tongued;
        }
        return result;
    }

    // --- subduction tongue internals ------------------------------------------------------------

    // Angular tolerance for matching a rim vertex to a joint-path point, as a fraction of the
    // edge-band half-width. The shared corners carry ALMOST the same unit direction as the path
    // points, but slice 1's joint-gap translation (centroid * gap) shifts every vertex a little off
    // its original unit direction (drift ~= gap, ~0.02 rad at the default gap). Half the band
    // (default ~0.06 rad) comfortably covers that drift while staying far below the inter-vertex
    // spacing (~0.3 rad at frequency 3), so it pinpoints exactly the rim edges that lie ON the path.
    private const double TonguePathMatchBandFraction = 0.5;

    private static PlateSolid? BuildSubductionTongue(
        PlateSolid subSolid,
        PlateSolid overSolid,
        Vector3D[] arc,
        int subductingId,
        SlabJointMechanicsProfile profile)
    {
        int n = subSolid.VertexCount / 2;
        var positions = subSolid.Positions;
        var triangles = subSolid.Triangles;

        // The top triangles are the leading run whose indices are all < n (the bottom + walls come
        // after). Slice 2 never edits topology, so the rim extracted here matches the original cap.
        int topTriInts = CountTopTriangleInts(triangles, n);
        var rimEdges = ExtractRimEdges(triangles, topTriInts);

        // The path rim edges are those whose BOTH endpoints lie on the joint path.
        var pathRimEdges = new List<(int U, int V)>();
        double pathTol = profile.EdgeBandHalfWidthRad * TonguePathMatchBandFraction;
        foreach (var e in rimEdges)
            if (IsPathVertex(e.U, positions, arc, pathTol) && IsPathVertex(e.V, positions, arc, pathTol))
                pathRimEdges.Add(e);
        if (pathRimEdges.Count == 0)
            return null; // graceful: no rim edge along the path -> no tongue.

        var pathEdgeKeys = new HashSet<long>(pathRimEdges.Count);
        foreach (var e in pathRimEdges)
            pathEdgeKeys.Add(UndirectedKey(e.U, e.V));

        var chains = ChainPathRimEdges(pathRimEdges);
        var overDir = MeanTopDirection(overSolid);

        // Structural drop floor (same pattern ResolveEffectiveDip uses for the rim dip): the
        // tongue's far edge must clear the overriding bottom near the path by >= MinClearance.
        double maxPathTopR = double.NegativeInfinity;
        var seenVerts = new HashSet<int>();
        foreach (var e in pathRimEdges)
        {
            if (seenVerts.Add(e.U)) maxPathTopR = Math.Max(maxPathTopR, Radius(positions[e.U]));
            if (seenVerts.Add(e.V)) maxPathTopR = Math.Max(maxPathTopR, Radius(positions[e.V]));
        }
        double minOverBotR = MinOverBottomRadiusNearArc(overSolid, arc, profile.EdgeBandHalfWidthRad);
        double requiredDrop = maxPathTopR - minOverBotR + profile.MinClearanceUnitRadius;
        double effectiveDrop = Math.Max(profile.TongueDropUnitRadius, requiredDrop);

        var newPositions = new List<CartesianPoint3>(positions);
        var newTriangles = new List<int>(triangles.Length);

        // Keep the top and bottom caps verbatim.
        for (int i = 0; i < 2 * topTriInts; i++)
            newTriangles.Add(triangles[i]);
        // Rebuild the rim walls, DROPPING the path rim edges (the tongue replaces those walls, so
        // the path rim edge becomes interior — shared by the top cap and the tongue top surface).
        foreach (var e in rimEdges)
        {
            if (pathEdgeKeys.Contains(UndirectedKey(e.U, e.V))) continue;
            int u = e.U, v = e.V;
            newTriangles.Add(u); newTriangles.Add(v); newTriangles.Add(n + v);
            newTriangles.Add(u); newTriangles.Add(n + v); newTriangles.Add(n + u);
        }

        foreach (var chain in chains)
            AppendTongueStrip(newPositions, newTriangles, chain, positions, n, overDir, profile, effectiveDrop);

        return new PlateSolid(subductingId, newPositions.ToArray(), newTriangles.ToArray());
    }

    // Append one watertight thick-strip tongue along an ordered chain of rim vertices (the path).
    // The strip is a (segments+1) x (k+1) grid of vertices (reach-step x path-corner), with top and
    // bottom twins. The near edge (s=0) REUSES the rim vertices; s>=1 vertices are new. Five faces
    // are emitted: top surface, underside, far-end wall, and the two side walls (skipped for a
    // closed loop). Every undirected edge ends up shared by exactly two triangles (watertight).
    private static void AppendTongueStrip(
        List<CartesianPoint3> newPositions,
        List<int> newTriangles,
        List<int> chain,
        CartesianPoint3[] origPositions,
        int n,
        Vector3D overDir,
        SlabJointMechanicsProfile profile,
        double effectiveDrop)
    {
        int k = chain.Count - 1;       // quad-strips along the path (k+1 corners)
        if (k < 1) return;
        int segments = profile.TongueSegments;

        var topPos = new CartesianPoint3[segments + 1, k + 1];
        var botPos = new CartesianPoint3[segments + 1, k + 1];
        var topIdx = new int[segments + 1, k + 1];
        var botIdx = new int[segments + 1, k + 1];

        for (int j = 0; j <= k; j++)
        {
            int vj = chain[j];
            var topV = origPositions[vj];
            var botV = origPositions[n + vj];
            double rTop = Radius(topV);
            double thk = rTop - Radius(botV);
            Vector3D p = UnitVector(topV);
            // Lateral direction toward the overriding side: project the overriding centroid onto
            // the tangent plane at p. The tongue reaches across the path in this direction.
            Vector3D lateral = (overDir - p * Vector3D.Dot(overDir, p)).NormalizeOrZero();

            topPos[0, j] = topV;  botPos[0, j] = botV;   // near edge reuses the rim vertices
            topIdx[0, j] = vj;   botIdx[0, j] = n + vj;

            for (int s = 1; s <= segments; s++)
            {
                double ramp = Smoothstep((double)s / segments);
                double reach = profile.TongueReachUnitRadius * ramp;
                double drop = effectiveDrop * ramp;
                Vector3D farDir = SphericalOffset(p, lateral, reach);
                double topR = rTop - drop;
                double botR = topR - thk;
                topPos[s, j] = new CartesianPoint3(farDir.X * topR, farDir.Y * topR, farDir.Z * topR);
                botPos[s, j] = new CartesianPoint3(farDir.X * botR, farDir.Y * botR, farDir.Z * botR);
            }
        }

        // New top vertices first (indices < bottom), then bottom twins — mirrors PlateSolid's
        // top-then-bottom layout so a future split can tell them apart the same way.
        for (int s = 1; s <= segments; s++)
            for (int j = 0; j <= k; j++)
            {
                topIdx[s, j] = newPositions.Count;
                newPositions.Add(topPos[s, j]);
            }
        for (int s = 1; s <= segments; s++)
            for (int j = 0; j <= k; j++)
            {
                botIdx[s, j] = newPositions.Count;
                newPositions.Add(botPos[s, j]);
            }

        bool closed = chain[0] == chain[k];

        // Top surface (outward = up): near edge traversed opposite the cap, so the path rim edge
        // becomes a continuous interior edge with both faces outward.
        for (int j = 0; j < k; j++)
            for (int s = 0; s < segments; s++)
                EmitQuad(newTriangles, topIdx[s, j + 1], topIdx[s, j], topIdx[s + 1, j], topIdx[s + 1, j + 1]);

        // Underside (outward = down): reversed winding.
        for (int j = 0; j < k; j++)
            for (int s = 0; s < segments; s++)
                EmitQuad(newTriangles, botIdx[s, j], botIdx[s + 1, j], botIdx[s + 1, j + 1], botIdx[s, j + 1]);

        // Far-end wall (s = segments).
        for (int j = 0; j < k; j++)
            EmitQuad(newTriangles, topIdx[segments, j + 1], topIdx[segments, j], botIdx[segments, j], botIdx[segments, j + 1]);

        if (!closed)
        {
            // Side wall at j = 0 (capped against the non-path rim wall ending at chain[0]).
            for (int s = 0; s < segments; s++)
                EmitQuad(newTriangles, topIdx[s, 0], topIdx[s + 1, 0], botIdx[s + 1, 0], botIdx[s, 0]);
            // Side wall at j = k (capped against the non-path rim wall ending at chain[k]).
            for (int s = 0; s < segments; s++)
                EmitQuad(newTriangles, topIdx[s + 1, k], topIdx[s, k], botIdx[s, k], botIdx[s + 1, k]);
        }
    }

    // Two triangles for a quad (A, B, C, D). Wound to match the PlateSolid convention (whose closed
    // solids carry a NEGATIVE signed volume — the cap is CW-from-outside): each tri is the reverse
    // of the naive CCW split, so the tongue's outward normals agree with the cap and walls.
    private static void EmitQuad(List<int> tris, int a, int b, int c, int d)
    {
        tris.Add(a); tris.Add(c); tris.Add(b);
        tris.Add(a); tris.Add(d); tris.Add(c);
    }

    private static Vector3D[] NormalizeArc(IReadOnlyList<GlobeVec3> path)
    {
        var arc = new Vector3D[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            var gp = path[i];
            arc[i] = UnitVector(new CartesianPoint3(gp.X, gp.Y, gp.Z));
        }
        return arc;
    }

    private static int CountTopTriangleInts(int[] triangles, int n)
    {
        int t = 0;
        while (t + 3 <= triangles.Length
               && triangles[t] < n && triangles[t + 1] < n && triangles[t + 2] < n)
            t += 3;
        return t;
    }

    private static bool IsPathVertex(int vertexIndex, CartesianPoint3[] positions, Vector3D[] arc, double tol)
        => MinAngularDistance(UnitVector(positions[vertexIndex]), arc) < tol;

    private static Vector3D MeanTopDirection(PlateSolid solid)
    {
        int n = solid.VertexCount / 2;
        double sx = 0.0, sy = 0.0, sz = 0.0;
        for (int v = 0; v < n; v++)
        {
            sx += solid.Positions[v].X;
            sy += solid.Positions[v].Y;
            sz += solid.Positions[v].Z;
        }
        return new Vector3D(sx, sy, sz).NormalizeOrZero();
    }

    private static double MinOverBottomRadiusNearArc(PlateSolid solid, Vector3D[] arc, double halfWidth)
    {
        int n = solid.VertexCount / 2;
        double min = double.PositiveInfinity;
        for (int v = 0; v < n; v++)
        {
            if (MinAngularDistance(UnitVector(solid.Positions[v]), arc) > halfWidth) continue;
            double br = Radius(solid.Positions[n + v]);
            if (br < min) min = br;
        }
        return min;
    }

    // Chain directed path rim edges into ordered vertex lists (one or more chains). Each directed
    // edge (U, V) follows its owning triangle's CCW order; a simple open joint path produces one
    // chain from an open endpoint. Deterministic: open-endpoint search is by ascending vertex id.
    private static List<List<int>> ChainPathRimEdges(List<(int U, int V)> edges)
    {
        var chains = new List<List<int>>();
        if (edges.Count == 0) return chains;

        var next = new Dictionary<int, int>();
        var isHead = new HashSet<int>();
        foreach (var e in edges)
        {
            next[e.U] = e.V;   // simple chain assumption (a path's rim edges share endpoints linearly)
            isHead.Add(e.V);
        }

        while (next.Count > 0)
        {
            int start = next.Keys.OrderBy(u => u).FirstOrDefault(u => !isHead.Contains(u));
            if (!next.ContainsKey(start))
                start = next.Keys.OrderBy(u => u).First();   // closed loop: start anywhere deterministic

            var chain = new List<int>();
            int cur = start;
            int guard = 0;
            while (next.TryGetValue(cur, out int v) && guard++ <= edges.Count + 1)
            {
                chain.Add(cur);
                isHead.Remove(v);
                next.Remove(cur);
                cur = v;
            }
            if (chain.Count == 0) break;
            if (chain[0] != cur) chain.Add(cur);   // close an open chain with its last vertex
            chains.Add(chain);
        }
        return chains;
    }

    // --- rim extraction (mirrors PlateSolidBuilder: edges in exactly one top triangle) ----------

    private static List<(int U, int V)> ExtractRimEdges(int[] triangles, int topTriInts)
    {
        var directedByKey = new Dictionary<long, (int U, int V)>();
        var interior = new HashSet<long>();
        for (int t = 0; t < topTriInts; t += 3)
        {
            ConsiderRimEdge(triangles[t], triangles[t + 1], directedByKey, interior);
            ConsiderRimEdge(triangles[t + 1], triangles[t + 2], directedByKey, interior);
            ConsiderRimEdge(triangles[t + 2], triangles[t], directedByKey, interior);
        }
        var rim = new List<(int U, int V)>(directedByKey.Count);
        foreach (var kvp in directedByKey)
            if (!interior.Contains(kvp.Key))
                rim.Add(kvp.Value);
        rim.Sort((x, y) =>
        {
            int c = x.U.CompareTo(y.U);
            return c != 0 ? c : x.V.CompareTo(y.V);
        });
        return rim;
    }

    private static void ConsiderRimEdge(int u, int v, Dictionary<long, (int U, int V)> directedByKey, HashSet<long> interior)
    {
        long key = UndirectedKey(u, v);
        if (interior.Contains(key)) return;
        if (directedByKey.TryGetValue(key, out _))
        {
            directedByKey.Remove(key);   // shared by two top triangles -> interior
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
        return (((long)lo) << 32) | (uint)hi;
    }

    // --- vector helpers -------------------------------------------------------------------------

    private static Vector3D UnitVector(CartesianPoint3 p)
    {
        var v = new Vector3D(p.X, p.Y, p.Z);
        double len = v.Length();
        return len > Epsilon ? v / len : v;
    }

    // Rotate the unit direction p toward the unit tangent `lateral` by angle `reach` (great-circle
    // rotation in the plane spanned by p and lateral). Stays on the unit sphere.
    private static Vector3D SphericalOffset(Vector3D p, Vector3D lateral, double reach)
        => (p * Math.Cos(reach) + lateral * Math.Sin(reach)).NormalizeOrZero();

    private static double Smoothstep(double t)
    {
        double c = Math.Clamp(t, 0.0, 1.0);
        return c * c * (3.0 - 2.0 * c);
    }

    private static double Radius(CartesianPoint3 p)
        => Math.Sqrt((p.X * p.X) + (p.Y * p.Y) + (p.Z * p.Z));

    // --- joint shaping internals ----------------------------------------------------------------

    private static readonly double Epsilon = Tolerance.Strict.Epsilon;

    private static bool HasShapingJoint(IReadOnlyList<PlateBoundaryArc> joints)
    {
        foreach (var j in joints)
        {
            if (j.Kind != PlateBoundaryKind.Inactive && j.Points.Count >= 2)
                return true;
        }
        return false;
    }

    private static void ValidateJointProfile(SlabJointMechanicsProfile profile)
    {
        if (!IsPositiveFinite(profile.SubductionDipUnitRadius))
            throw new ArgumentOutOfRangeException(nameof(profile), "SubductionDipUnitRadius must be positive and finite.");
        if (!IsPositiveFinite(profile.OverridingMarginRaiseUnitRadius))
            throw new ArgumentOutOfRangeException(nameof(profile), "OverridingMarginRaiseUnitRadius must be positive and finite.");
        if (!IsPositiveFinite(profile.EdgeBandHalfWidthRad))
            throw new ArgumentOutOfRangeException(nameof(profile), "EdgeBandHalfWidthRad must be positive and finite.");
        if (double.IsNaN(profile.DivergentGapMultiplier) || profile.DivergentGapMultiplier <= 1.0)
            throw new ArgumentOutOfRangeException(nameof(profile), "DivergentGapMultiplier must be > 1 (it widens the gap).");
        if (!IsPositiveFinite(profile.MinClearanceUnitRadius))
            throw new ArgumentOutOfRangeException(nameof(profile), "MinClearanceUnitRadius must be positive and finite.");
    }

    private static void ValidateTongueProfile(SlabJointMechanicsProfile profile)
    {
        if (!IsPositiveFinite(profile.TongueReachUnitRadius))
            throw new ArgumentOutOfRangeException(nameof(profile), "TongueReachUnitRadius must be positive and finite.");
        if (!IsPositiveFinite(profile.TongueDropUnitRadius))
            throw new ArgumentOutOfRangeException(nameof(profile), "TongueDropUnitRadius must be positive and finite.");
        if (profile.TongueSegments < 1)
            throw new ArgumentOutOfRangeException(nameof(profile), "TongueSegments must be >= 1.");
    }

    // A joint demands a tongue only when it is CONVERGENT, NON-COLLISION, with a resolved
    // subducting plate id and a usable path. Transform / divergent / collision / unresolved joints
    // are bit-identical to slice 2 (no tongue).
    private static bool HasTongueJoint(IReadOnlyList<PlateBoundaryArc> joints)
    {
        foreach (var j in joints)
        {
            if (j.Kind != PlateBoundaryKind.Convergent) continue;
            if (j.IsCollision) continue;
            if (j.SubductingPlateId is not int) continue;
            if (j.Points.Count >= 2) return true;
        }
        return false;
    }

    // The effective dip is the declared visual dip OR the structural clearance the slab thickness
    // demands, whichever is larger — so the subducting top always clears the overriding bottom.
    private static double ResolveEffectiveDip(
        PlateBoundaryArc joint,
        SlabJointMechanicsProfile profile,
        IReadOnlyList<PlateSolid> solids,
        Dictionary<int, Vector3D> centroidByPlate,
        Vector3D[] arc)
    {
        if (joint.Kind != PlateBoundaryKind.Convergent) return profile.SubductionDipUnitRadius;
        if (joint.SubductingPlateId is not int subductingId) return profile.SubductionDipUnitRadius;
        int overridingId = joint.PlateA == subductingId ? joint.PlateB : joint.PlateA;

        double maxSubTopR = double.NegativeInfinity;
        double minOverBotR = double.PositiveInfinity;

        foreach (var solid in solids)
        {
            if (solid.PlateId != subductingId && solid.PlateId != overridingId) continue;
            int n = solid.VertexCount / 2;
            var positions = solid.Positions;
            for (int v = 0; v < n; v++)
            {
                var top = positions[v];
                var u = new Vector3D(top.X, top.Y, top.Z);
                double len = u.Length();
                if (len <= Epsilon) continue;
                u = u * (1.0 / len);
                double w = EdgeBandWeight(MinAngularDistance(u, arc), profile.EdgeBandHalfWidthRad);
                if (w <= 0.0) continue;

                if (solid.PlateId == subductingId)
                {
                    double r = len; // top radius
                    if (r > maxSubTopR) maxSubTopR = r;
                }
                else // overriding: its bottom vertex twin at n + v
                {
                    var bottom = positions[n + v];
                    double br = Math.Sqrt((bottom.X * bottom.X) + (bottom.Y * bottom.Y) + (bottom.Z * bottom.Z));
                    if (br < minOverBotR) minOverBotR = br;
                }
            }
        }

        if (double.IsNegativeInfinity(maxSubTopR) || double.IsPositiveInfinity(minOverBotR))
            return profile.SubductionDipUnitRadius;

        double required = maxSubTopR - minOverBotR + profile.MinClearanceUnitRadius;
        return Math.Max(profile.SubductionDipUnitRadius, required);
    }

    // The per-vertex displacement for one (joint, plate) pair at ramp weight w. Radial along u.
    private static Vector3D JointContribution(
        PlateBoundaryArc joint,
        int plateId,
        Vector3D u,
        double w,
        double effectiveDip,
        SlabJointMechanicsProfile profile,
        Dictionary<int, Vector3D> centroidByPlate,
        double jointGapUnitRadius)
    {
        if (joint.Kind == PlateBoundaryKind.Convergent)
        {
            if (joint.SubductingPlateId == plateId)
            {
                // Subducting edge band dips radially inward.
                return u * (-(effectiveDip * w));
            }
            // Overriding margin raises (also both sides on collision / unresolved polarity).
            return u * (profile.OverridingMarginRaiseUnitRadius * w);
        }

        if (joint.Kind == PlateBoundaryKind.Divergent)
        {
            // Widen the gap: extra translation along this plate's centroid direction (the SAME
            // separation direction the base joint gap uses), scaled by (multiplier - 1) * gap.
            if (centroidByPlate.TryGetValue(plateId, out var dir))
                return dir * ((profile.DivergentGapMultiplier - 1.0) * jointGapUnitRadius * w);
            return new Vector3D(0.0, 0.0, 0.0);
        }

        // Transform / Inactive: no contribution.
        return new Vector3D(0.0, 0.0, 0.0);
    }

    // Smallest great-circle angular distance from u to any arc point (radians). acos(clamp(dot))
    // matches the CellBoundaryField idiom; the arc is edge-local so the nearest point suffices.
    private static double MinAngularDistance(Vector3D u, Vector3D[] arc)
    {
        double bestDot = -2.0;
        for (int i = 0; i < arc.Length; i++)
        {
            double dot = Vector3D.Dot(u, arc[i]);
            if (dot > bestDot) bestDot = dot;
        }
        return Math.Acos(Math.Clamp(bestDot, -1.0, 1.0));
    }

    // Smoothstep ramp: 1 at the arc (dist 0), 0 at the band edge (dist == halfWidth). A C1-smooth
    // falloff keeps the slab watertight (pure position deformation, topology untouched).
    private static double EdgeBandWeight(double angularDist, double halfWidth)
    {
        if (halfWidth <= 0.0) return angularDist <= 0.0 ? 1.0 : 0.0;
        if (angularDist <= 0.0) return 1.0;
        if (angularDist >= halfWidth) return 0.0;
        double t = angularDist / halfWidth;
        return (2.0 * t * t * t) - (3.0 * t * t) + 1.0;
    }

    private static bool IsPositiveFinite(double value)
        => value > 0.0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
