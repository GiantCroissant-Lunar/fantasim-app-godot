using System;
using System.Collections.Generic;
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
    /// <param name="joints">Per-joint classifications (from <see cref="SlabJointClassifier"/> or built
    /// directly). Inactive joints are ignored.</param>
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
        IReadOnlyList<SlabJointClassification> joints,
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
        var shapedJoints = new List<(SlabJointClassification Joint, Vector3D[] Arc, double EffectiveDip)>(joints.Count);
        foreach (var joint in joints)
        {
            if (joint.Kind == PlateBoundaryKind.Inactive) continue;
            if (joint.ArcPoints.Count < 2) continue;
            var arc = new Vector3D[joint.ArcPoints.Count];
            for (int i = 0; i < arc.Length; i++)
            {
                var p = joint.ArcPoints[i];
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
    /// Convenience overload: slice-1 assembly + slice-2 joint shaping in one call (extend-alongside
    /// the slice-1 <see cref="BuildAssembly"/> — the existing 5-arg overload is unchanged).
    /// </summary>
    public static IReadOnlyList<PlateSolid> BuildAssembly(
        IReadOnlyList<PlateCap> caps,
        IReadOnlyList<PlateSolidCentroid> centroids,
        IReadOnlyList<double> crustThicknessByCellMetres,
        double thicknessDepthScale,
        WorldSurfacePresentationProfile profile,
        IReadOnlyList<SlabJointClassification> joints,
        SlabJointMechanicsProfile jointProfile,
        double baseRadius = GlobeSurfaceBuilder.DefaultRadius)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var gapped = BuildAssembly(caps, centroids, crustThicknessByCellMetres, thicknessDepthScale, profile, baseRadius);
        return ShapeSlabJoints(gapped, joints, jointProfile, centroids, profile.SlabJointGapUnitRadius, baseRadius);
    }

    // --- joint shaping internals ----------------------------------------------------------------

    private static readonly double Epsilon = Tolerance.Strict.Epsilon;

    private static bool HasShapingJoint(IReadOnlyList<SlabJointClassification> joints)
    {
        foreach (var j in joints)
        {
            if (j.Kind != PlateBoundaryKind.Inactive && j.ArcPoints.Count >= 2)
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

    // The effective dip is the declared visual dip OR the structural clearance the slab thickness
    // demands, whichever is larger — so the subducting top always clears the overriding bottom.
    private static double ResolveEffectiveDip(
        SlabJointClassification joint,
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
        SlabJointClassification joint,
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
