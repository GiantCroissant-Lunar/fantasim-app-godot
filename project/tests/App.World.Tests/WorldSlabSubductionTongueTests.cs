using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Globe.Core;
using FantaSim.Cartography.Shared;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Assembled-world slice 3 — the SUBDUCTION TONGUE
/// (vault/specs/2026-07-16-assembled-world-northstar.md clause 3, read against the v4/v5 binding
/// reference vault/reference/2026-07-17-assembled-world-image-prompt.md: "the edge of one plate
/// extends inward and slides UNDERNEATH the raised edge of its neighbor, leaving a thin gap between
/// the lower plate's top surface and the upper plate's underside").
///
/// <para>For every CONVERGENT, NON-COLLISION joint, the SUBDUCTING plate's solid grows a real,
/// watertight TONGUE — a thick strip reaching laterally past the joint path under the overriding
/// side and descending radially. These tests pin the seven required proofs on a NON-COAXIAL
/// fixture (the joint arc is on a generic great circle, never +Z and never the equator):</para>
/// <list type="number">
/// <item><b>(a) Tongue only on the subducting side</b> — convergent non-collision grows the
/// subducting solid's vertex/triangle count; transform / divergent / collision joints and the
/// overriding plate are bit-identical to the pre-tongue (slice-2) output.</item>
/// <item><b>(b) Watertight</b> — every undirected edge of the tongued solid is shared by exactly
/// two triangles.</item>
/// <item><b>(c) Assembled no-interpenetration</b> — the tongue top stays strictly below the
/// overriding bottom by at least MinClearanceUnitRadius in the overlap zone.</item>
/// <item><b>(d) Tongue reaches past the path</b> — at least one tongue vertex lies on the
/// OVERRIDING side of the joint-path plane.</item>
/// <item><b>(e) Exploded translates the tongue with the plate</b> — every tongue vertex receives
/// the bit-identical translation delta as the plate's other vertices under
/// <see cref="PlateSolidBuilder.ApplyExplodedFactor"/>.</item>
/// <item><b>(f) Purity</b> — identical inputs yield bit-identical outputs.</item>
/// <item><b>(g) Declared parameters</b> — positive/finite (Segments >= 1), rejected when not.</item>
/// </list>
/// </summary>
public sealed class WorldSlabSubductionTongueTests
{
    private static readonly NoiseParams NoNoise = new(Amplitude: 0.0);

    // ─── Declared parameters (defaults pinned) ────────────────────────────────────────────────

    [Fact]
    public void Default_profile_declares_eye_tuned_positive_tongue_parameters()
    {
        var profile = SlabJointMechanicsProfile.Default;

        Assert.True(profile.TongueReachUnitRadius > 0.0, "tongue reach must be positive");
        Assert.True(profile.TongueDropUnitRadius > 0.0, "tongue drop must be positive");
        Assert.True(profile.TongueSegments >= 1, "tongue segments must be >= 1");

        Assert.Equal(SlabJointMechanicsProfile.DefaultTongueReachUnitRadius, profile.TongueReachUnitRadius);
        Assert.Equal(SlabJointMechanicsProfile.DefaultTongueDropUnitRadius, profile.TongueDropUnitRadius);
        Assert.Equal(SlabJointMechanicsProfile.DefaultTongueSegments, profile.TongueSegments);
    }

    // ─── Proof (a): tongue ONLY on the subducting side of convergent non-collision joints ─────

    [Fact]
    public void Tongue_grows_only_the_subducting_solid_of_a_convergent_non_collision_joint()
    {
        var fixture = NonCoaxialTongueFixture();
        var shaped = fixture.ShapedSolids;

        var tongued = WorldSlabAssemblyComposer.ShapeSubductionTongues(
            shaped, new[] { fixture.ConvergentJoint }, SlabJointMechanicsProfile.Default);

        Assert.Equal(shaped.Count, tongued.Count);

        var subBefore = SingleSolid(shaped, fixture.SubductingPlateId);
        var subAfter = SingleSolid(tongued, fixture.SubductingPlateId);

        // The subducting solid GREW: a real watertight extension was appended (more vertices AND
        // more triangles). Both must increase — a position-only change would not.
        Assert.True(subAfter.VertexCount > subBefore.VertexCount,
            $"subducting solid must gain tongue vertices ({subAfter.VertexCount} > {subBefore.VertexCount})");
        Assert.True(subAfter.TriangleCount > subBefore.TriangleCount,
            $"subducting solid must gain tongue triangles ({subAfter.TriangleCount} > {subBefore.TriangleCount})");

        // The overriding plate is bit-identical to the pre-tongue output (same reference, positions, triangles).
        var overBefore = SingleSolid(shaped, fixture.OverridingPlateId);
        var overAfter = SingleSolid(tongued, fixture.OverridingPlateId);
        Assert.Same(overBefore, overAfter);
    }

    [Fact]
    public void Transform_divergent_and_collision_joints_produce_no_tongue_bit_identical_to_slice2()
    {
        var fixture = NonCoaxialTongueFixture();
        var shaped = fixture.ShapedSolids;

        // Three joint kinds that must NOT grow a tongue.
        var transform = fixture.ConvergentJoint with { Kind = SlabJointKind.Transform, SubductingPlateId = null };
        var divergent = fixture.ConvergentJoint with { Kind = SlabJointKind.Divergent, SubductingPlateId = null };
        var collision = fixture.ConvergentJoint with { SubductingPlateId = null, IsCollision = true };

        foreach (var joint in new[] { transform, divergent, collision })
        {
            var tongued = WorldSlabAssemblyComposer.ShapeSubductionTongues(
                shaped, new[] { joint }, SlabJointMechanicsProfile.Default);

            Assert.Equal(shaped.Count, tongued.Count);
            for (int p = 0; p < shaped.Count; p++)
            {
                Assert.Same(shaped[p], tongued[p]); // bit-identical references — no allocation, no change
            }
        }
    }

    [Fact]
    public void Empty_joint_list_is_bit_identical_to_the_shaped_solids()
    {
        var fixture = NonCoaxialTongueFixture();
        var shaped = fixture.ShapedSolids;

        var tongued = WorldSlabAssemblyComposer.ShapeSubductionTongues(
            shaped, Array.Empty<SlabJointClassification>(), SlabJointMechanicsProfile.Default);

        Assert.Equal(shaped.Count, tongued.Count);
        for (int p = 0; p < shaped.Count; p++)
            Assert.Same(shaped[p], tongued[p]);
    }

    // ─── Proof (b): the tongued solid stays watertight ────────────────────────────────────────

    [Fact]
    public void Tongued_subducting_solid_is_watertight_every_edge_shared_by_exactly_two_triangles()
    {
        var fixture = NonCoaxialTongueFixture();

        var tongued = WorldSlabAssemblyComposer.ShapeSubductionTongues(
            fixture.ShapedSolids, new[] { fixture.ConvergentJoint }, SlabJointMechanicsProfile.Default);

        foreach (var solid in tongued)
        {
            var edgeCounts = CountUndirectedEdges(solid);
            Assert.NotEmpty(edgeCounts);
            foreach (var kvp in edgeCounts)
                Assert.Equal(2, kvp.Value);
        }
    }

    // ─── Proof (c): assembled no-interpenetration in the overlap zone ─────────────────────────

    [Fact]
    public void Assembled_tongue_top_stays_below_overriding_bottom_by_min_clearance_in_overlap()
    {
        var fixture = NonCoaxialTongueFixture();
        var shaped = fixture.ShapedSolids;
        double minClearance = SlabJointMechanicsProfile.Default.MinClearanceUnitRadius;

        var tongued = WorldSlabAssemblyComposer.ShapeSubductionTongues(
            shaped, new[] { fixture.ConvergentJoint }, SlabJointMechanicsProfile.Default);

        var subBefore = SingleSolid(shaped, fixture.SubductingPlateId);
        var subAfter = SingleSolid(tongued, fixture.SubductingPlateId);
        var overriding = SingleSolid(tongued, fixture.OverridingPlateId);

        // Tongue vertices are the APPENDED ones (indices >= the pre-tongue subducting vertex count).
        int firstTongueVertex = subBefore.VertexCount;

        // The overriding bottom radius in the path's edge band (the slice-2 definition of the
        // overlap zone): the minimum radius among overriding BOTTOM vertices whose top twin is
        // within EdgeBandHalfWidthRad of the joint arc.
        double minOverBotR = MinOverridingBottomRadiusNearPath(overriding, fixture.ArcUnit);

        // Every tongue vertex that has crossed onto the OVERRIDING side of the joint-path plane
        // (the overlap zone) must clear the overriding bottom by at least MinClearanceUnitRadius.
        var planeNormal = PathPlaneNormalTowardOverriding(fixture);
        foreach (var tongueIdx in TongueVertexIndices(subAfter, firstTongueVertex))
        {
            var p = subAfter.Positions[tongueIdx];
            double signedDistOver = SignedDistanceToPathPlane(p, planeNormal);
            if (signedDistOver <= 0.0) continue; // not yet past the path — outside the overlap zone

            double r = Radius(p);
            Assert.True(r <= minOverBotR - minClearance,
                $"NO INTERPENETRATION (overlap zone): tongue vertex r={r:G6} must clear the overriding "
                + $"bottom r={minOverBotR:G6} by >= MinClearance={minClearance:G6} "
                + $"(signed dist past path={signedDistOver:G4})");
        }
    }

    // ─── Proof (d): the tongue reaches LATERALLY past the joint path ──────────────────────────

    [Fact]
    public void Tongue_reaches_past_the_joint_path_onto_the_overriding_side()
    {
        var fixture = NonCoaxialTongueFixture();
        var shaped = fixture.ShapedSolids;

        var tongued = WorldSlabAssemblyComposer.ShapeSubductionTongues(
            shaped, new[] { fixture.ConvergentJoint }, SlabJointMechanicsProfile.Default);

        var subBefore = SingleSolid(shaped, fixture.SubductingPlateId);
        var subAfter = SingleSolid(tongued, fixture.SubductingPlateId);
        var planeNormal = PathPlaneNormalTowardOverriding(fixture);

        // At least one tongue vertex must lie STRICTLY on the overriding side of the joint-path
        // plane (signed distance > 0). This is the whole point of the tongue — it reaches UNDER
        // the neighbor, proving the overlap pair is legible.
        bool anyPast = false;
        foreach (var idx in TongueVertexIndices(subAfter, subBefore.VertexCount))
        {
            if (SignedDistanceToPathPlane(subAfter.Positions[idx], planeNormal) > 1e-6)
            {
                anyPast = true;
                break;
            }
        }
        Assert.True(anyPast, "at least one tongue vertex must lie on the OVERRIDING side of the joint-path plane");
    }

    // ─── Proof (e): exploded translates the tongue with the plate (bit-identical delta) ───────

    [Fact]
    public void Exploded_translates_every_tongue_vertex_by_the_same_delta_as_the_plate()
    {
        var fixture = NonCoaxialTongueFixture();
        var shaped = fixture.ShapedSolids;

        var tongued = WorldSlabAssemblyComposer.ShapeSubductionTongues(
            shaped, new[] { fixture.ConvergentJoint }, SlabJointMechanicsProfile.Default);

        // Apply a non-trivial explode (factor > 0) — the SAME machinery the assembled/exploded view
        // uses. The tongue vertices are part of the plate's Positions, so they must translate by
        // the plate's centroid direction * offset — bit-identical to every other plate vertex.
        double factor = 0.5;
        double maxOffset = 0.2;
        var exploded = PlateSolidBuilder.ApplyExplodedFactor(tongued, fixture.Centroids, factor, maxOffset);

        var subTongued = SingleSolid(tongued, fixture.SubductingPlateId);
        var subExploded = SingleSolid(exploded, fixture.SubductingPlateId);

        // Replicate the EXACT arithmetic ApplyExplodedFactor applies (Clamp then multiply), so the
        // translation vector v is recovered bit-identically. Asserting after == before + v (NOT
        // after - before == v) avoids subtraction-rounding noise at radius~1 vs delta~0.05.
        var subCentroid = SingleCentroid(fixture.Centroids, fixture.SubductingPlateId);
        double offset = Math.Clamp(factor, 0.0, 1.0) * maxOffset;
        double vx = subCentroid.CentroidDirection.X * offset;
        double vy = subCentroid.CentroidDirection.Y * offset;
        double vz = subCentroid.CentroidDirection.Z * offset;

        int firstTongueVertex = SingleSolid(shaped, fixture.SubductingPlateId).VertexCount;

        // EVERY vertex of the subducting solid — tongue and non-tongue alike — must equal its
        // pre-explode position plus the SAME vector v. This is the bit-identical translation proof:
        // no vertex is special-cased, tongue vertices ride with the plate.
        for (int i = 0; i < subTongued.VertexCount; i++)
        {
            var before = subTongued.Positions[i];
            var after = subExploded.Positions[i];
            Assert.Equal(before.X + vx, after.X);
            Assert.Equal(before.Y + vy, after.Y);
            Assert.Equal(before.Z + vz, after.Z);
        }

        // And specifically confirm the tongue vertices were actually translated (they exist and got +v).
        var tongueIndices = TongueVertexIndices(subTongued, firstTongueVertex).ToArray();
        Assert.NotEmpty(tongueIndices);
        foreach (var idx in tongueIndices)
        {
            var before = subTongued.Positions[idx];
            var after = subExploded.Positions[idx];
            Assert.Equal(before.X + vx, after.X);
            Assert.Equal(before.Y + vy, after.Y);
            Assert.Equal(before.Z + vz, after.Z);
        }
    }

    // ─── Proof (f): purity — identical inputs yield bit-identical outputs ─────────────────────

    [Fact]
    public void ShapeSubductionTongues_is_pure_identical_inputs_yield_bit_identical_outputs()
    {
        var fixture = NonCoaxialTongueFixture();

        var first = WorldSlabAssemblyComposer.ShapeSubductionTongues(
            fixture.ShapedSolids, new[] { fixture.ConvergentJoint }, SlabJointMechanicsProfile.Default);
        var second = WorldSlabAssemblyComposer.ShapeSubductionTongues(
            fixture.ShapedSolids, new[] { fixture.ConvergentJoint }, SlabJointMechanicsProfile.Default);

        Assert.Equal(first.Count, second.Count);
        for (int p = 0; p < first.Count; p++)
        {
            Assert.Equal(first[p].PlateId, second[p].PlateId);
            Assert.Equal(first[p].Positions, second[p].Positions);
            Assert.Equal(first[p].Triangles, second[p].Triangles);
        }
    }

    // ─── Proof (g): declared parameters rejected when non-positive / non-finite ──────────────

    [Fact]
    public void ShapeSubductionTongues_rejects_non_positive_tongue_reach()
    {
        var fixture = NonCoaxialTongueFixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldSlabAssemblyComposer.ShapeSubductionTongues(
            fixture.ShapedSolids, new[] { fixture.ConvergentJoint },
            SlabJointMechanicsProfile.Default with { TongueReachUnitRadius = 0.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldSlabAssemblyComposer.ShapeSubductionTongues(
            fixture.ShapedSolids, new[] { fixture.ConvergentJoint },
            SlabJointMechanicsProfile.Default with { TongueReachUnitRadius = -0.05 }));
    }

    [Fact]
    public void ShapeSubductionTongues_rejects_non_finite_tongue_reach()
    {
        var fixture = NonCoaxialTongueFixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldSlabAssemblyComposer.ShapeSubductionTongues(
            fixture.ShapedSolids, new[] { fixture.ConvergentJoint },
            SlabJointMechanicsProfile.Default with { TongueReachUnitRadius = double.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldSlabAssemblyComposer.ShapeSubductionTongues(
            fixture.ShapedSolids, new[] { fixture.ConvergentJoint },
            SlabJointMechanicsProfile.Default with { TongueReachUnitRadius = double.PositiveInfinity }));
    }

    [Fact]
    public void ShapeSubductionTongues_rejects_non_positive_tongue_drop()
    {
        var fixture = NonCoaxialTongueFixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldSlabAssemblyComposer.ShapeSubductionTongues(
            fixture.ShapedSolids, new[] { fixture.ConvergentJoint },
            SlabJointMechanicsProfile.Default with { TongueDropUnitRadius = 0.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldSlabAssemblyComposer.ShapeSubductionTongues(
            fixture.ShapedSolids, new[] { fixture.ConvergentJoint },
            SlabJointMechanicsProfile.Default with { TongueDropUnitRadius = double.NaN }));
    }

    [Fact]
    public void ShapeSubductionTongues_rejects_tongue_segments_below_one()
    {
        var fixture = NonCoaxialTongueFixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldSlabAssemblyComposer.ShapeSubductionTongues(
            fixture.ShapedSolids, new[] { fixture.ConvergentJoint },
            SlabJointMechanicsProfile.Default with { TongueSegments = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldSlabAssemblyComposer.ShapeSubductionTongues(
            fixture.ShapedSolids, new[] { fixture.ConvergentJoint },
            SlabJointMechanicsProfile.Default with { TongueSegments = -1 }));
    }

    [Fact]
    public void ShapeSubductionTongues_accepts_custom_positive_tongue_parameters()
    {
        var fixture = NonCoaxialTongueFixture();
        var custom = SlabJointMechanicsProfile.Default with
        {
            TongueReachUnitRadius = 0.07,
            TongueDropUnitRadius = 0.08,
            TongueSegments = 3,
        };
        var tongued = WorldSlabAssemblyComposer.ShapeSubductionTongues(
            fixture.ShapedSolids, new[] { fixture.ConvergentJoint }, custom);

        var subBefore = SingleSolid(fixture.ShapedSolids, fixture.SubductingPlateId);
        var subAfter = SingleSolid(tongued, fixture.SubductingPlateId);
        Assert.True(subAfter.VertexCount > subBefore.VertexCount, "custom tongue params must still grow the solid");
    }

    // === fixtures ===============================================================================

    private sealed class TongueFixture
    {
        public WorldGlobeSnapshot Snapshot { get; init; } = null!;
        public IReadOnlyList<PlateSolidCentroid> Centroids { get; init; } = null!;
        public IReadOnlyList<PlateSolid> ShapedSolids { get; init; } = null!; // slice-1 + slice-2 output
        public SlabJointClassification ConvergentJoint { get; init; } = null!;
        public int SubductingPlateId { get; init; }
        public int OverridingPlateId { get; init; }
        public IReadOnlyList<GlobeVec3> SharedCornerDirections { get; init; } = null!;
        // Unit double-precision directions of the joint path points (for plane / angular math).
        public double[][] ArcUnit { get; init; } = null!;
    }

    // Two plates sharing the edge a-b on a GENERIC great circle (NOT +Z, NOT the equator). Plate
    // 0 (subducting) is the single cell (a, c, b); plate 1 (overriding) is (a, b, d). The shared
    // corners a, b are exactly the convergent edge band, so the tongue attaches to the rim edge a-b.
    private static TongueFixture NonCoaxialTongueFixture(
        int subductingPlateId = 0, int overridingPlateId = 1)
    {
        var a = Unit(1, 2, 3);
        var b = Unit(3, 1, 2);
        var c = Unit(2, 3, 1);   // subducting plate third corner
        var d = Unit(-1, 3, 2);  // overriding plate third corner

        int lo = Math.Min(subductingPlateId, overridingPlateId);
        int hi = Math.Max(subductingPlateId, overridingPlateId);
        var cells = new List<GlobeCell>
        {
            new(0, lo, a, c, b),
            new(1, hi, a, b, d),
        };
        var plates = new List<GlobePlate>
        {
            new(lo, a, 0.0),
            new(hi, d, 0.0),
        };
        var snapshot = new WorldGlobeSnapshot(0, 2, 2, 100_000, cells, plates);

        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);
        var caps = surfaces.BuildSurfaces(new double[] { 0.0, 0.0 }, exaggeration: 0.0001);
        var centroids = PlateSolidBuilder.ComputeCentroids(snapshot);
        var thickness = new double[]
        {
            RadialSectionProfile.Default.DefaultCrustThicknessMetres,
            RadialSectionProfile.Default.DefaultCrustThicknessMetres,
        };
        double scale = RadialSectionProfile.Default.ThicknessDepthScale();

        // Slice 1 (gap) + slice 2 (convergent dip/raise): the pre-tongue input ShapeSubductionTongues owns.
        var gapped = WorldSlabAssemblyComposer.BuildAssembly(
            caps, centroids, thickness, scale, WorldSurfacePresentationProfile.Default);
        var shaped = WorldSlabAssemblyComposer.ShapeSlabJoints(
            gapped,
            new[] { new SlabJointClassification(lo, hi, SlabJointKind.Convergent, subductingPlateId, false, new[] { a, b }) },
            SlabJointMechanicsProfile.Default,
            centroids,
            WorldSurfacePresentationProfile.Default.SlabJointGapUnitRadius);

        return new TongueFixture
        {
            Snapshot = snapshot,
            Centroids = centroids,
            ShapedSolids = shaped,
            ConvergentJoint = new SlabJointClassification(
                lo, hi, SlabJointKind.Convergent, subductingPlateId, false, new[] { a, b }),
            SubductingPlateId = subductingPlateId,
            OverridingPlateId = overridingPlateId,
            SharedCornerDirections = new[] { a, b },
            ArcUnit = new[] { ToUnitDouble(a), ToUnitDouble(b) },
        };
    }

    // === helpers ================================================================================

    private static PlateSolid SingleSolid(IReadOnlyList<PlateSolid> solids, int plateId)
        => solids.Single(s => s.PlateId == plateId);

    private static PlateSolidCentroid SingleCentroid(IReadOnlyList<PlateSolidCentroid> centroids, int plateId)
        => centroids.Single(c => c.PlateId == plateId);

    private static double Radius(CartesianPoint3 p)
        => Math.Sqrt((p.X * p.X) + (p.Y * p.Y) + (p.Z * p.Z));

    private static GlobeVec3 Unit(double x, double y, double z)
    {
        double len = Math.Sqrt((x * x) + (y * y) + (z * z));
        return new GlobeVec3((float)(x / len), (float)(y / len), (float)(z / len));
    }

    private static double[] ToUnitDouble(GlobeVec3 v)
    {
        double len = Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
        return len > 0.0 ? new[] { v.X / len, v.Y / len, v.Z / len } : new[] { (double)v.X, v.Y, v.Z };
    }

    // The tongue vertices are the ones APPENDED after the pre-tongue solid (indices >= firstTongueVertex).
    private static IEnumerable<int> TongueVertexIndices(PlateSolid solid, int firstTongueVertex)
    {
        for (int i = firstTongueVertex; i < solid.VertexCount; i++)
            yield return i;
    }

    private static Dictionary<(int, int), int> CountUndirectedEdges(PlateSolid solid)
    {
        var counts = new Dictionary<(int, int), int>();
        var tris = solid.Triangles;
        for (int t = 0; t < tris.Length; t += 3)
        {
            Consider(tris[t], tris[t + 1], counts);
            Consider(tris[t + 1], tris[t + 2], counts);
            Consider(tris[t + 2], tris[t], counts);
        }
        return counts;
    }

    private static void Consider(int a, int b, Dictionary<(int, int), int> counts)
    {
        var key = a < b ? (a, b) : (b, a);
        counts.TryGetValue(key, out var current);
        counts[key] = current + 1;
    }

    // Min radius among the overriding BOTTOM vertices whose top twin is within EdgeBandHalfWidthRad
    // of the joint arc — the slice-2 definition of the overlap zone.
    private static double MinOverridingBottomRadiusNearPath(PlateSolid overriding, double[][] arc)
    {
        int n = overriding.VertexCount / 2;
        double halfWidth = SlabJointMechanicsProfile.Default.EdgeBandHalfWidthRad;
        double min = double.PositiveInfinity;
        for (int v = 0; v < n; v++)
        {
            var top = overriding.Positions[v];
            double tlen = Math.Sqrt((top.X * top.X) + (top.Y * top.Y) + (top.Z * top.Z));
            if (tlen <= 0.0) continue;
            double tuX = top.X / tlen, tuY = top.Y / tlen, tuZ = top.Z / tlen;
            double bestDot = double.NegativeInfinity;
            for (int i = 0; i < arc.Length; i++)
            {
                double dot = (tuX * arc[i][0]) + (tuY * arc[i][1]) + (tuZ * arc[i][2]);
                if (dot > bestDot) bestDot = dot;
            }
            double ang = Math.Acos(Math.Clamp(bestDot, -1.0, 1.0));
            if (ang > halfWidth) continue;

            var bottom = overriding.Positions[n + v];
            double br = Math.Sqrt((bottom.X * bottom.X) + (bottom.Y * bottom.Y) + (bottom.Z * bottom.Z));
            if (br < min) min = br;
        }
        return min;
    }

    private static double SignedDistanceToPathPlane(CartesianPoint3 p, double[] planeNormalTowardOverriding)
        => (p.X * planeNormalTowardOverriding[0])
           + (p.Y * planeNormalTowardOverriding[1])
           + (p.Z * planeNormalTowardOverriding[2]);

    private static double[] PathPlaneNormalTowardOverriding(TongueFixture fixture)
    {
        // The joint-path plane is the great-circle plane through the two path points and the origin.
        // Its normal is cross(a, b); orient it toward the overriding plate's interior so that a
        // point on the overriding side has POSITIVE signed distance.
        var a = fixture.ArcUnit[0];
        var b = fixture.ArcUnit[1];
        double nx = (a[1] * b[2]) - (a[2] * b[1]);
        double ny = (a[2] * b[0]) - (a[0] * b[2]);
        double nz = (a[0] * b[1]) - (a[1] * b[0]);
        double len = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        if (len <= 0.0) return new[] { 0.0, 0.0, 0.0 };
        nx /= len; ny /= len; nz /= len;

        // Orient toward the overriding plate: mean of the overriding top vertices.
        var overriding = SingleSolid(fixture.ShapedSolids, fixture.OverridingPlateId);
        int n = overriding.VertexCount / 2;
        double ox = 0.0, oy = 0.0, oz = 0.0;
        for (int v = 0; v < n; v++)
        {
            ox += overriding.Positions[v].X;
            oy += overriding.Positions[v].Y;
            oz += overriding.Positions[v].Z;
        }
        if (((ox * nx) + (oy * ny) + (oz * nz)) < 0.0)
        {
            nx = -nx; ny = -ny; nz = -nz;
        }
        return new[] { nx, ny, nz };
    }
}
