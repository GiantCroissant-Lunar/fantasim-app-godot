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
/// Assembled-world slice 2 (vault/specs/2026-07-16-assembled-world-northstar.md, clause 3):
/// "so we can see how mountain, trench is formed. How plate A is under plate b and moved." The
/// convergent / divergent / transform mechanics are expressed as slab-edge GEOMETRY on the per-plate
/// solid slabs slice 1 ships — pure contracts-tier, Godot-free.
///
/// <para>These tests pin the six required proofs:</para>
/// <list type="number">
/// <item><b>Convergent dip</b> — the subducting slab's edge-band vertices sit at strictly lower
/// radius than the matching overriding edge-band vertices in the overlap zone (NON-COAXIAL fixture:
/// the joint arc is on a generic great circle, not +Z and not the equator).</item>
/// <item><b>No interpenetration</b> — in the overlap zone the subducting TOP surface stays strictly
/// below the overriding BOTTOM surface.</item>
/// <item><b>Divergent gap wider</b> — the divergent joint's gap exceeds the default (profile) gap,
/// which is itself positive.</item>
/// <item><b>Transform identity</b> — a transform joint shapes the slabs bit-identically to slice 1
/// (no dip, no raise, no widen).</item>
/// <item><b>Purity</b> — identical inputs produce bit-identical outputs.</item>
/// <item><b>Plate count + watertightness preserved</b> — one solid per plate, every undirected edge
/// still shared by exactly two triangles after shaping.</item>
/// </list>
///
/// <para>The seam between <b>joint classification</b> (which pair / kind / side subducts) and
/// <b>edge shaping</b> (geometry) is the <see cref="SlabJointClassification"/> record: tests build
/// classifications directly, bypassing the adapter, so the geometry proofs are independent of how
/// classifications are produced. <see cref="SlabJointClassifier"/> (the adapter) is exercised
/// separately.</para>
/// </summary>
public sealed class WorldSlabJointMechanicsTests
{
    private static readonly NoiseParams NoNoise = new(Amplitude: 0.0);

    // ─── The declared parameter family (defaults pinned) ───────────────────────────────────────

    [Fact]
    public void Default_joint_mechanics_profile_is_declared_with_eye_tuned_positive_parameters()
    {
        var profile = SlabJointMechanicsProfile.Default;

        // Every declared magnitude is positive and finite — the joint mechanics must produce a
        // VISIBLE subduction underride, a visible mountain onset, and a wider divergent gap. Each is
        // eye-tuned (not physical); pinning the defaults makes any retune a conscious decision.
        Assert.True(profile.SubductionDipUnitRadius > 0.0, "subduction dip must be positive");
        Assert.True(profile.OverridingMarginRaiseUnitRadius > 0.0, "overriding raise must be positive");
        Assert.True(profile.EdgeBandHalfWidthRad > 0.0, "edge band half-width must be positive");
        Assert.True(profile.DivergentGapMultiplier > 1.0, "divergent gap multiplier must widen (>1)");
        Assert.True(profile.MinClearanceUnitRadius > 0.0, "min clearance must be positive");

        Assert.Equal(SlabJointMechanicsProfile.DefaultSubductionDipUnitRadius, profile.SubductionDipUnitRadius);
        Assert.Equal(SlabJointMechanicsProfile.DefaultOverridingMarginRaiseUnitRadius, profile.OverridingMarginRaiseUnitRadius);
        Assert.Equal(SlabJointMechanicsProfile.DefaultEdgeBandHalfWidthRad, profile.EdgeBandHalfWidthRad);
        Assert.Equal(SlabJointMechanicsProfile.DefaultDivergentGapMultiplier, profile.DivergentGapMultiplier);
    }

    // ─── Proof (a): convergent subduction dips the subducting edge band below the overriding ────

    [Fact]
    public void Convergent_subduction_dips_subducting_edge_band_below_overriding_in_overlap_zone()
    {
        // NON-COAXIAL fixture: the shared edge (the joint arc through a-b) sits on a GENERIC great
        // circle, away from the +Z axis and away from the equator. Plate 0 subducts under plate 1.
        var fixture = NonCoaxialTwoPlateFixture(subductingPlateId: 0, overridingPlateId: 1);

        // Slice 1 assembly (the gap-translated slabs), then slice 2 joint shaping.
        var shaped = WorldSlabAssemblyComposer.ShapeSlabJoints(
            fixture.GappedSolids,
            new[] { fixture.ConvergentJoint },
            SlabJointMechanicsProfile.Default,
            fixture.Centroids,
            fixture.Gap);

        var subducting = SingleSolid(shaped, fixture.SubductingPlateId);
        var overriding = SingleSolid(shaped, fixture.OverridingPlateId);

        // The overlap zone is the joint arc itself: the two shared corners a and b. For each shared
        // corner, the subducting slab's edge-band vertex must end up at a STRICTLY LOWER radius than
        // the overriding slab's matching edge-band vertex — "plate A under plate B" — AND the
        // subducting vertex must have actually DIPPED (its radius dropped below its slice-1 radius),
        // so the test has teeth against a raise-only implementation.
        foreach (var sharedDir in fixture.SharedCornerDirections)
        {
            var subTopShaped = TopVertexNearestDirection(subducting, sharedDir);
            var overTop = TopVertexNearestDirection(overriding, sharedDir);
            var subTopUnshaped = TopVertexNearestDirection(fixture.GappedSolids[0], sharedDir);

            Assert.True(subTopShaped.Radius < subTopUnshaped.Radius,
                $"subducting edge-band vertex must DIP (shaped r={subTopShaped.Radius:G5} < slice-1 "
                + $"r={subTopUnshaped.Radius:G5}); shared corner {sharedDir}");
            Assert.True(subTopShaped.Radius < overTop.Radius,
                $"subducting edge-band vertex (r={subTopShaped.Radius:G5}) must sit below the matching "
                + $"overriding edge-band vertex (r={overTop.Radius:G5}) at the overlap zone; shared corner {sharedDir}");
        }
    }

    // ─── Proof (b): no interpenetration — subducting top strictly below overriding bottom ───────

    [Fact]
    public void Convergent_subduction_keeps_subducting_top_strictly_below_overriding_bottom()
    {
        var fixture = NonCoaxialTwoPlateFixture(subductingPlateId: 0, overridingPlateId: 1);

        var shaped = WorldSlabAssemblyComposer.ShapeSlabJoints(
            fixture.GappedSolids,
            new[] { fixture.ConvergentJoint },
            SlabJointMechanicsProfile.Default,
            fixture.Centroids,
            fixture.Gap);

        var subducting = SingleSolid(shaped, fixture.SubductingPlateId);
        var overriding = SingleSolid(shaped, fixture.OverridingPlateId);
        int overN = overriding.VertexCount / 2;

        foreach (var sharedDir in fixture.SharedCornerDirections)
        {
            var subTop = TopVertexNearestDirection(subducting, sharedDir);
            var overTop = TopVertexNearestDirection(overriding, sharedDir);
            // The overriding BOTTOM vertex is the top vertex's twin at index n + topIndex.
            var overBottom = overriding.Positions[overN + overTop.Index];
            double overBottomR = Radius(overBottom);

            Assert.True(subTop.Radius < overBottomR,
                $"NO INTERPENETRATION: subducting top (r={subTop.Radius:G5}) must stay strictly below the "
                + $"overriding bottom (r={overBottomR:G5}) in the overlap zone; shared corner {sharedDir}");
        }
    }

    // ─── Structural floor: the dip grows to keep the invariant under a thicker-than-declared slab ─

    [Fact]
    public void Convergent_subduction_structural_floor_keeps_invariant_under_a_thick_slab()
    {
        // When the slab is THICKER than the declared visual dip (here 60 km -> ~0.075R, above the
        // 0.06R declared dip), the shaper must grow the effective dip so the non-interpenetration
        // invariant still holds — the structural floor (MinClearanceUnitRadius) overrides the
        // eye-tuned declared dip rather than letting the subducting top punch through.
        var fixture = NonCoaxialTwoPlateFixture(subductingPlateId: 0, overridingPlateId: 1);

        // Rebuild with a doubled crust thickness so the slab wall exceeds the declared dip.
        var surfaces = new GlobePlateSurfaces(fixture.Snapshot, noise: NoNoise);
        var caps = surfaces.BuildSurfaces(new double[] { 0.0, 0.0 }, exaggeration: 0.0001);
        double thick = RadialSectionProfile.Default.DefaultCrustThicknessMetres * 2.0;
        double scale = RadialSectionProfile.Default.ThicknessDepthScale();
        var thickGapped = WorldSlabAssemblyComposer.BuildAssembly(
            caps, fixture.Centroids, new[] { thick, thick }, scale, WorldSurfacePresentationProfile.Default);

        var shaped = WorldSlabAssemblyComposer.ShapeSlabJoints(
            thickGapped,
            new[] { fixture.ConvergentJoint },
            SlabJointMechanicsProfile.Default,
            fixture.Centroids,
            fixture.Gap);

        var subducting = SingleSolid(shaped, fixture.SubductingPlateId);
        var overriding = SingleSolid(shaped, fixture.OverridingPlateId);
        int overN = overriding.VertexCount / 2;

        foreach (var sharedDir in fixture.SharedCornerDirections)
        {
            var subTop = TopVertexNearestDirection(subducting, sharedDir);
            var overTop = TopVertexNearestDirection(overriding, sharedDir);
            double overBottomR = Radius(overriding.Positions[overN + overTop.Index]);

            Assert.True(subTop.Radius < overBottomR,
                $"structural floor: subducting top (r={subTop.Radius:G5}) must stay below the overriding "
                + $"bottom (r={overBottomR:G5}) even when the slab is thicker than the declared dip");
        }
    }

    // ─── Proof (c): divergent gap wider than the default gap, which is itself positive ──────────

    [Fact]
    public void Divergent_joint_gap_is_wider_than_the_default_gap_which_is_positive()
    {
        var fixture = NonCoaxialTwoPlateFixture(subductingPlateId: 0, overridingPlateId: 1);

        // Default (slice-1) separation at the shared corners: the gap is positive and the formerly-
        // coincident corners open into a visible joint.
        double defaultSeparation = SeparationAtSharedCorner(
            fixture.GappedSolids[0], fixture.GappedSolids[1], fixture.SharedCornerDirections[0]);
        Assert.True(defaultSeparation > 0.0, $"default joint gap must be positive; was {defaultSeparation:G5}");

        // Divergent shaping widens the gap at this joint.
        var divergent = fixture.ConvergentJoint with { Kind = SlabJointKind.Divergent, SubductingPlateId = null };
        var shaped = WorldSlabAssemblyComposer.ShapeSlabJoints(
            fixture.GappedSolids,
            new[] { divergent },
            SlabJointMechanicsProfile.Default,
            fixture.Centroids,
            fixture.Gap);

        double divergentSeparation = SeparationAtSharedCorner(shaped[0], shaped[1], fixture.SharedCornerDirections[0]);
        Assert.True(divergentSeparation > defaultSeparation,
            $"divergent joint gap ({divergentSeparation:G5}) must be WIDER than the default gap "
            + $"({defaultSeparation:G5})");
    }

    // ─── Proof (d): transform joints shape bit-identically to slice 1 ───────────────────────────

    [Fact]
    public void Transform_joint_shaping_is_bit_identical_to_slice1_assembly()
    {
        var fixture = NonCoaxialTwoPlateFixture(subductingPlateId: 0, overridingPlateId: 1);
        var transform = fixture.ConvergentJoint with { Kind = SlabJointKind.Transform, SubductingPlateId = null };

        var shaped = WorldSlabAssemblyComposer.ShapeSlabJoints(
            fixture.GappedSolids,
            new[] { transform },
            SlabJointMechanicsProfile.Default,
            fixture.Centroids,
            fixture.Gap);

        Assert.Equal(fixture.GappedSolids.Count, shaped.Count);
        for (int p = 0; p < shaped.Count; p++)
        {
            Assert.Equal(fixture.GappedSolids[p].PlateId, shaped[p].PlateId);
            Assert.Equal(fixture.GappedSolids[p].Positions, shaped[p].Positions);
            Assert.Equal(fixture.GappedSolids[p].Triangles, shaped[p].Triangles);
        }
    }

    // ─── Proof (e): purity — identical inputs yield bit-identical outputs ──────────────────────

    [Fact]
    public void ShapeSlabJoints_is_pure_identical_inputs_yield_bit_identical_outputs()
    {
        var fixture = NonCoaxialTwoPlateFixture(subductingPlateId: 0, overridingPlateId: 1);

        var first = WorldSlabAssemblyComposer.ShapeSlabJoints(
            fixture.GappedSolids,
            new[] { fixture.ConvergentJoint },
            SlabJointMechanicsProfile.Default,
            fixture.Centroids,
            fixture.Gap);
        var second = WorldSlabAssemblyComposer.ShapeSlabJoints(
            fixture.GappedSolids,
            new[] { fixture.ConvergentJoint },
            SlabJointMechanicsProfile.Default,
            fixture.Centroids,
            fixture.Gap);

        Assert.Equal(first.Count, second.Count);
        for (int p = 0; p < first.Count; p++)
        {
            Assert.Equal(first[p].PlateId, second[p].PlateId);
            Assert.Equal(first[p].Positions, second[p].Positions);
            Assert.Equal(first[p].Triangles, second[p].Triangles);
        }
    }

    // ─── Proof (f): plate count + per-slab watertightness preserved under shaping ──────────────

    [Fact]
    public void ShapeSlabJoints_preserves_plate_count_and_per_slab_watertightness()
    {
        var fixture = NonCoaxialTwoPlateFixture(subductingPlateId: 0, overridingPlateId: 1);

        var shaped = WorldSlabAssemblyComposer.ShapeSlabJoints(
            fixture.GappedSolids,
            new[] { fixture.ConvergentJoint },
            SlabJointMechanicsProfile.Default,
            fixture.Centroids,
            fixture.Gap);

        Assert.Equal(fixture.GappedSolids.Count, shaped.Count);
        Assert.Equal(fixture.GappedSolids.Select(s => s.PlateId), shaped.Select(s => s.PlateId));

        foreach (var solid in shaped)
        {
            var edgeCounts = CountUndirectedEdges(solid);
            Assert.NotEmpty(edgeCounts);
            foreach (var kvp in edgeCounts)
                Assert.Equal(2, kvp.Value);
        }
    }

    // ─── Supporting proofs: empty joints, collision symmetry, real-globe watertightness ────────

    [Fact]
    public void Empty_joint_list_is_bit_identical_to_the_input_solids()
    {
        // No joints => no shaping => the gapped slabs come back unchanged. This is the transform-
        // identity generalised: the shaper is a no-op when there is nothing to express.
        var fixture = NonCoaxialTwoPlateFixture(subductingPlateId: 0, overridingPlateId: 1);

        var shaped = WorldSlabAssemblyComposer.ShapeSlabJoints(
            fixture.GappedSolids,
            Array.Empty<SlabJointClassification>(),
            SlabJointMechanicsProfile.Default,
            fixture.Centroids,
            fixture.Gap);

        Assert.Equal(fixture.GappedSolids.Count, shaped.Count);
        for (int p = 0; p < shaped.Count; p++)
        {
            Assert.Equal(fixture.GappedSolids[p].PlateId, shaped[p].PlateId);
            Assert.Equal(fixture.GappedSolids[p].Positions, shaped[p].Positions);
            Assert.Equal(fixture.GappedSolids[p].Triangles, shaped[p].Triangles);
        }
    }

    [Fact]
    public void Convergent_collision_raises_both_sides_symmetrically()
    {
        // Continent-continent collision (IsCollision) is symmetric uplift: BOTH plates' edge bands
        // rise — no subduction underride, no trench. This is the mountain-piling onset on both sides.
        var fixture = NonCoaxialTwoPlateFixture(subductingPlateId: 0, overridingPlateId: 1);
        var collision = fixture.ConvergentJoint with { SubductingPlateId = null, IsCollision = true };

        var shaped = WorldSlabAssemblyComposer.ShapeSlabJoints(
            fixture.GappedSolids,
            new[] { collision },
            SlabJointMechanicsProfile.Default,
            fixture.Centroids,
            fixture.Gap);

        var plateA = SingleSolid(shaped, 0);
        var plateB = SingleSolid(shaped, 1);

        // Both plates' shared-corner tops rise above their slice-1 (unshaped) radius.
        foreach (var sharedDir in fixture.SharedCornerDirections)
        {
            double beforeA = TopVertexNearestDirection(fixture.GappedSolids[0], sharedDir).Radius;
            double beforeB = TopVertexNearestDirection(fixture.GappedSolids[1], sharedDir).Radius;
            double afterA = TopVertexNearestDirection(plateA, sharedDir).Radius;
            double afterB = TopVertexNearestDirection(plateB, sharedDir).Radius;

            Assert.True(afterA > beforeA, $"collision must raise plate A's margin ({afterA:G5} > {beforeA:G5})");
            Assert.True(afterB > beforeB, $"collision must raise plate B's margin ({afterB:G5} > {beforeB:G5})");
        }
    }

    [Fact]
    public void Real_seed_globe_shaping_preserves_per_slab_watertightness()
    {
        // The REAL four-plate seed at frequency 3 through the real cartography part. Applying joint
        // shaping (here with synthetic transform joints over every adjacent pair, a strict no-op)
        // must leave every plate solid watertight and bit-identical to slice 1 — the shaping never
        // edits topology, only positions, and only where a non-transform joint demands it.
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);
        var caps = surfaces.BuildSurfaces(new double[snapshot.CellCount], exaggeration: 0.0001);
        var centroids = PlateSolidBuilder.ComputeCentroids(snapshot);
        var thickness = Enumerable.Repeat(3_000.0, snapshot.CellCount).ToArray();

        var gapped = WorldSlabAssemblyComposer.BuildAssembly(
            caps, centroids, thickness, RadialSectionProfile.Default.ThicknessDepthScale(),
            WorldSurfacePresentationProfile.Default);

        var shaped = WorldSlabAssemblyComposer.ShapeSlabJoints(
            gapped,
            Array.Empty<SlabJointClassification>(),
            SlabJointMechanicsProfile.Default,
            centroids,
            WorldSurfacePresentationProfile.Default.SlabJointGapUnitRadius);

        Assert.Equal(gapped.Count, shaped.Count);
        foreach (var solid in shaped)
        {
            var edgeCounts = CountUndirectedEdges(solid);
            Assert.NotEmpty(edgeCounts);
            foreach (var kvp in edgeCounts)
                Assert.Equal(2, kvp.Value);
        }
    }

    // ─── The classifier adapter: arcs + sections -> per-joint classifications ──────────────────

    [Fact]
    public void SlabJointClassifier_builds_per_joint_classifications_from_arcs_and_sections()
    {
        // The adapter is the narrow seam between the existing boundary data (PlateBoundaryArc for the
        // kind/pair/geometry + BoundarySectionDocument for the resolved subduction polarity) and the
        // edge shaper. It emits one classification per ACTIVE plate pair, carrying the polarity.
        var a = Unit(1, 2, 3);
        var b = Unit(3, 1, 2);

        var arcs = new[]
        {
            new PlateBoundaryArc(0, 1, PlateBoundaryKind.Convergent, new[] { a, b }),
            new PlateBoundaryArc(0, 1, PlateBoundaryKind.Convergent, new[] { b, a }), // same pair, 2nd segment
            new PlateBoundaryArc(1, 2, PlateBoundaryKind.Transform, new[] { a, b }),
        };
        var sections = new[]
        {
            new BoundarySectionDocument(
                PlateA: 0, PlateB: 1, Kind: PlateBoundaryKind.Convergent,
                Origin: a, NormalAxis: b,
                Samples: Array.Empty<BoundarySectionSample>(),
                InteriorBands: Array.Empty<BoundarySectionBand>(),
                Exaggeration: 1.0, PlanetRadiusMetres: 6_371_000.0, LabelOverride: null,
                SubductingPlateId: 0, IsCollision: false),
        };

        var joints = SlabJointClassifier.Classify(arcs, sections);

        // One classification per active pair (the two convergent segments collapse into one pair).
        Assert.Equal(2, joints.Count);

        var convergent = joints.Single(j => j.Kind == SlabJointKind.Convergent);
        Assert.Equal(0, convergent.PlateA);
        Assert.Equal(1, convergent.PlateB);
        Assert.Equal(0, convergent.SubductingPlateId);
        Assert.False(convergent.IsCollision);
        Assert.True(convergent.Path.Count >= 2);

        var transform = joints.Single(j => j.Kind == SlabJointKind.Transform);
        Assert.Null(transform.SubductingPlateId);
        Assert.False(transform.IsCollision);
    }

    [Fact]
    public void SlabJointClassifier_handles_missing_polarity_as_collision_free_unknown_subduction()
    {
        // A convergent pair with NO matching section document (polarity not yet resolved by the
        // pipeline): the adapter must not crash. It emits the joint with no resolved subducting side
        // (treated as collision-free unknown) so the shaper skips the asymmetric underride gracefully.
        var arcs = new[]
        {
            new PlateBoundaryArc(2, 3, PlateBoundaryKind.Convergent, new[] { Unit(1, 0, 0), Unit(0, 1, 0) }),
        };

        var joints = SlabJointClassifier.Classify(arcs, sections: null);

        var joint = Assert.Single(joints);
        Assert.Equal(SlabJointKind.Convergent, joint.Kind);
        Assert.Null(joint.SubductingPlateId);
        Assert.False(joint.IsCollision);
    }

    // === fixtures ===============================================================================

    // Two plates sharing the edge a-b on a GENERIC great circle (NOT +Z, NOT the equator). Each
    // plate is one triangular cell so the shared corners a, b are exactly the convergent edge band.
    private sealed class NonCoaxialFixture
    {
        public WorldGlobeSnapshot Snapshot { get; init; } = null!;
        public IReadOnlyList<PlateSolidCentroid> Centroids { get; init; } = null!;
        public IReadOnlyList<PlateSolid> GappedSolids { get; init; } = null!;
        public SlabJointClassification ConvergentJoint { get; init; } = null!;
        public int SubductingPlateId { get; init; }
        public int OverridingPlateId { get; init; }
        public double Gap { get; init; }
        public IReadOnlyList<GlobeVec3> SharedCornerDirections { get; init; } = null!;
    }

    private static NonCoaxialFixture NonCoaxialTwoPlateFixture(int subductingPlateId, int overridingPlateId)
    {
        // Generic unit directions — none on a coordinate axis or the equator.
        var a = Unit(1, 2, 3);
        var b = Unit(3, 1, 2);
        var c = Unit(2, 3, 1);   // plate 0 third corner (subducting side)
        var d = Unit(-1, 3, 2);  // plate 1 third corner (overriding side)

        var lo = Math.Min(subductingPlateId, overridingPlateId);
        var hi = Math.Max(subductingPlateId, overridingPlateId);
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
        // Default crust thickness (30 km -> ~0.0377R): a realistic slab, not a 3 km shim that would
        // trivialise the dip-vs-thickness interpenetration invariant.
        var thickness = new double[]
        {
            RadialSectionProfile.Default.DefaultCrustThicknessMetres,
            RadialSectionProfile.Default.DefaultCrustThicknessMetres,
        };
        double scale = RadialSectionProfile.Default.ThicknessDepthScale();
        double gap = WorldSurfacePresentationProfile.Default.SlabJointGapUnitRadius;

        var gapped = WorldSlabAssemblyComposer.BuildAssembly(
            caps, centroids, thickness, scale, WorldSurfacePresentationProfile.Default);

        var joint = new SlabJointClassification(
            PlateA: lo,
            PlateB: hi,
            Kind: SlabJointKind.Convergent,
            SubductingPlateId: subductingPlateId,
            IsCollision: false,
            Path: new[] { a, b });

        return new NonCoaxialFixture
        {
            Snapshot = snapshot,
            Centroids = centroids,
            GappedSolids = gapped,
            ConvergentJoint = joint,
            SubductingPlateId = subductingPlateId,
            OverridingPlateId = overridingPlateId,
            Gap = gap,
            SharedCornerDirections = new[] { a, b },
        };
    }

    // === helpers ================================================================================

    private static PlateSolid SingleSolid(IReadOnlyList<PlateSolid> solids, int plateId)
        => solids.Single(s => s.PlateId == plateId);

    private static double Radius(CartesianPoint3 p)
        => Math.Sqrt((p.X * p.X) + (p.Y * p.Y) + (p.Z * p.Z));

    private static GlobeVec3 Unit(double x, double y, double z)
    {
        double len = Math.Sqrt((x * x) + (y * y) + (z * z));
        return new GlobeVec3((float)(x / len), (float)(y / len), (float)(z / len));
    }

    // The top vertex (index in [0, n)) whose unit direction is nearest the given direction, with its
    // current radius. Top vertices occupy indices [0, VertexCount/2).
    private static (int Index, double Radius) TopVertexNearestDirection(PlateSolid solid, GlobeVec3 dir)
    {
        int n = solid.VertexCount / 2;
        int best = 0;
        double bestDot = double.NegativeInfinity;
        for (int v = 0; v < n; v++)
        {
            var p = solid.Positions[v];
            double len = Math.Sqrt((p.X * p.X) + (p.Y * p.Y) + (p.Z * p.Z));
            if (len <= 0.0) continue;
            double dot = ((p.X * dir.X) + (p.Y * dir.Y) + (p.Z * dir.Z)) / len;
            if (dot > bestDot)
            {
                bestDot = dot;
                best = v;
            }
        }
        return (best, Radius(solid.Positions[best]));
    }

    // Euclidean distance between the two solids' nearest top vertices to a shared corner direction —
    // the visible joint separation at that corner.
    private static double SeparationAtSharedCorner(PlateSolid a, PlateSolid b, GlobeVec3 dir)
    {
        var ai = TopVertexNearestDirection(a, dir).Index;
        var bi = TopVertexNearestDirection(b, dir).Index;
        var pa = a.Positions[ai];
        var pb = b.Positions[bi];
        double dx = pa.X - pb.X;
        double dy = pa.Y - pb.Y;
        double dz = pa.Z - pb.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static Dictionary<(int, int), int> CountUndirectedEdges(PlateSolid solid)
    {
        var counts = new Dictionary<(int, int), int>();
        var tris = solid.Triangles;
        for (int t = 0; t < tris.Length; t += 3)
        {
            int a = tris[t + 0];
            int b = tris[t + 1];
            int c = tris[t + 2];
            Consider(a, b, counts);
            Consider(b, c, counts);
            Consider(c, a, counts);
        }
        return counts;
    }

    private static void Consider(int a, int b, Dictionary<(int, int), int> counts)
    {
        var key = a < b ? (a, b) : (b, a);
        counts.TryGetValue(key, out var current);
        counts[key] = current + 1;
    }
}
