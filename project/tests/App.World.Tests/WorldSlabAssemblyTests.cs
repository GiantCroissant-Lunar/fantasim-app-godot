using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Shared;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Assembled-world slice 1 (vault/specs/2026-07-16-assembled-world-northstar.md): the DEFAULT
/// World view presents the per-plate SOLID SLAB assembly — "the normal complete sphere could not
/// see how convergent, divergent, transform being presented. But the split part with thickness
/// can." Split slabs are not a mode; THE world.
///
/// <para>These tests are Godot-free and exercise the pure seam the binder consumes:</para>
/// <list type="bullet">
/// <item><see cref="WorldSurfacePresentationProfile"/> — the DECLARED parameter family: which
/// presentation the World view renders (slab assembly by DEFAULT, the old watertight sphere as an
/// explicit fallback) and the declared JOINT GAP that keeps slab joints readable from orbit.</item>
/// <item><see cref="WorldSurfacePresentationPolicy"/> — the pure gate: only the World view under
/// the SlabAssembly presentation mounts the assembly; every layer-focused view keeps its own
/// composition, and the WatertightSphere fallback keeps the old single-surface path.</item>
/// <item><see cref="WorldSlabAssemblyComposer"/> — the pure composition: one closed solid per
/// plate (the existing <see cref="PlateSolidBuilder"/> machinery), translated by the joint gap
/// along the SAME centroid-direction separation math the exploded view uses — assembled state of
/// the sketchfab exploded-plates family, far smaller than the exploded translation.</item>
/// </list>
/// </summary>
public sealed class WorldSlabAssemblyTests
{
    private static readonly NoiseParams NoNoise = new(Amplitude: 0.0);

    // ─── The declared parameter family (defaults pinned) ───────────────────────────────────────

    [Fact]
    public void Default_profile_is_the_slab_assembly_with_a_positive_joint_gap()
    {
        var profile = WorldSurfacePresentationProfile.Default;

        // The north-star verdict: the split slabs ARE the default World presentation.
        Assert.Equal(WorldSurfacePresentation.SlabAssembly, profile.Presentation);

        // The joint gap is a DECLARED parameter: positive (a joint must exist), pinned so any
        // retune is a conscious decision, and in the 0.004–0.008 unit-radius band the slice locks.
        Assert.Equal(WorldSurfacePresentationProfile.DefaultSlabJointGapUnitRadius, profile.SlabJointGapUnitRadius);
        Assert.True(profile.SlabJointGapUnitRadius > 0.0, "the joint gap must be positive — slabs must not touch");
        // Eye-tuned band, widened 2026-07-17 (user eye-fail: 0.006R read as hairline cracks,
        // not separated slabs). The ceiling keeps the gap far below the exploded translation.
        Assert.InRange(profile.SlabJointGapUnitRadius, 0.004, 0.08);

        // A JOINT, not an explosion: far smaller than the exploded view's radial translation.
        // Factor 0.05 -> 0.10 -> 0.15 across the 2026-07-17 eye-tunes (gap 0.02 -> 0.035R per the
        // acceptance image 2026-07-17-user-reference-assembled-final.png: seams wide enough to
        // glow). Still well below the exploded translation.
        Assert.True(
            profile.SlabJointGapUnitRadius < PlateSolidBuilder.DefaultMaxOffset * 0.15,
            $"the joint gap ({profile.SlabJointGapUnitRadius}R) must be far smaller than the exploded translation "
            + $"({PlateSolidBuilder.DefaultMaxOffset}R) or the assembled world reads as exploded");
    }

    // ─── The policy gate ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void World_view_with_the_default_profile_resolves_to_the_slab_assembly()
    {
        Assert.True(WorldSurfacePresentationPolicy.ShowsSlabAssembly(
            WorldSurfacePresentationProfile.Default, GlobeViewMode.World));
    }

    [Fact]
    public void Watertight_sphere_fallback_keeps_the_single_surface_world_path()
    {
        // The old watertight-sphere World path stays available behind the SAME declared parameter
        // family — the fallback keeps the policy gate closed, so the binder keeps the existing
        // single-surface plate root (whose machinery the GlobePlateSurfaces suite still covers).
        var fallback = WorldSurfacePresentationProfile.Default with
        {
            Presentation = WorldSurfacePresentation.WatertightSphere,
        };

        Assert.False(WorldSurfacePresentationPolicy.ShowsSlabAssembly(fallback, GlobeViewMode.World));
    }

    [Theory]
    [InlineData(GlobeViewMode.Inactive)]
    [InlineData(GlobeViewMode.PlateIdentity)]
    [InlineData(GlobeViewMode.Continents)]
    [InlineData(GlobeViewMode.HypsometricTerrain)]
    [InlineData(GlobeViewMode.MantleInterior)]
    public void Non_world_views_never_resolve_to_the_slab_assembly(GlobeViewMode viewMode)
    {
        // Layer-focused views own their compositions (Continents caps, crust diagnostic, the
        // mantle layer's own separated slabs); the slab-assembly default applies to the World
        // view ONLY.
        Assert.False(WorldSurfacePresentationPolicy.ShowsSlabAssembly(
            WorldSurfacePresentationProfile.Default, viewMode));
    }

    // ─── The composition: one solid per plate ───────────────────────────────────────────────────

    [Fact]
    public void BuildAssembly_produces_one_solid_per_plate_on_the_real_seed_globe()
    {
        // The REAL four-plate seed at frequency 3 (1280 cells) through the real cartography part:
        // the World slab assembly is one closed solid per plate — the plate count IS the part count.
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);
        var caps = surfaces.BuildSurfaces(new double[snapshot.CellCount], exaggeration: 0.0001);
        var centroids = PlateSolidBuilder.ComputeCentroids(snapshot);
        var thickness = Enumerable.Repeat(3_000.0, snapshot.CellCount).ToArray();

        var solids = WorldSlabAssemblyComposer.BuildAssembly(
            caps,
            centroids,
            thickness,
            RadialSectionProfile.Default.ThicknessDepthScale(),
            WorldSurfacePresentationProfile.Default);

        Assert.Equal(snapshot.PlateCount, solids.Count);
        Assert.Equal(caps.Select(c => c.PlateId), solids.Select(s => s.PlateId));
    }

    // ─── The joint gap ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Joint_gap_separates_adjacent_slabs_that_touched_when_assembled()
    {
        // Two plates sharing an edge: the un-gapped solids' shared boundary corners are coincident
        // (the watertight composed sphere). Under the default profile the formerly-coincident
        // vertices must open into a visible joint — adjacent slabs no longer touch.
        var snapshot = TwoTouchingPlatesSnapshot();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);
        var caps = surfaces.BuildSurfaces(new double[] { 0.0, 0.0 }, exaggeration: 0.0001);
        var centroids = PlateSolidBuilder.ComputeCentroids(snapshot);
        var thickness = new double[] { 3_000.0, 3_000.0 };
        double scale = RadialSectionProfile.Default.ThicknessDepthScale();

        var touching = PlateSolidBuilder.Build(caps, thickness, scale);
        var coincident = CoincidentTopVertexPairs(touching[0], touching[1]);
        Assert.NotEmpty(coincident);

        var gapped = WorldSlabAssemblyComposer.BuildAssembly(
            caps, centroids, thickness, scale, WorldSurfacePresentationProfile.Default);

        foreach (var (a, b) in coincident)
        {
            double separation = Distance(gapped[0].Positions[a], gapped[1].Positions[b]);
            Assert.True(
                separation > 1e-6,
                $"formerly-coincident boundary vertices must open into a joint; separation was {separation:G5}");
        }
    }

    [Fact]
    public void BuildAssembly_translates_each_slab_by_exactly_the_joint_gap_along_its_centroid_direction()
    {
        // The joint gap reuses the EXISTING separation math (PlateSolidBuilder.ApplyExplodedFactor):
        // a pure translation of the whole slab by gap × centroidDirection — topology unchanged, the
        // slab itself undeformed. This pins the gap semantics to the declared parameter.
        var snapshot = TwoTouchingPlatesSnapshot();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);
        var caps = surfaces.BuildSurfaces(new double[] { 0.0, 0.0 }, exaggeration: 0.0001);
        var centroids = PlateSolidBuilder.ComputeCentroids(snapshot);
        var thickness = new double[] { 3_000.0, 3_000.0 };
        double scale = RadialSectionProfile.Default.ThicknessDepthScale();
        double gap = WorldSurfacePresentationProfile.Default.SlabJointGapUnitRadius;

        var touching = PlateSolidBuilder.Build(caps, thickness, scale);
        var gapped = WorldSlabAssemblyComposer.BuildAssembly(
            caps, centroids, thickness, scale, WorldSurfacePresentationProfile.Default);

        Assert.Equal(touching.Count, gapped.Count);
        for (int p = 0; p < touching.Count; p++)
        {
            var dir = centroids.Single(c => c.PlateId == touching[p].PlateId).CentroidDirection;
            for (int v = 0; v < touching[p].VertexCount; v++)
            {
                Assert.Equal(touching[p].Positions[v].X + (dir.X * gap), gapped[p].Positions[v].X, 12);
                Assert.Equal(touching[p].Positions[v].Y + (dir.Y * gap), gapped[p].Positions[v].Y, 12);
                Assert.Equal(touching[p].Positions[v].Z + (dir.Z * gap), gapped[p].Positions[v].Z, 12);
            }
            Assert.Equal(touching[p].Triangles, gapped[p].Triangles);
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.006)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void BuildAssembly_rejects_a_non_positive_or_non_finite_joint_gap(double gap)
    {
        // The spec requires a visible joint: gap must be > 0 and finite. A seamless sphere is the
        // WatertightSphere presentation, never a zero gap smuggled through the slab path.
        var snapshot = TwoTouchingPlatesSnapshot();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);
        var caps = surfaces.BuildSurfaces(new double[] { 0.0, 0.0 }, exaggeration: 0.0001);
        var centroids = PlateSolidBuilder.ComputeCentroids(snapshot);
        var thickness = new double[] { 3_000.0, 3_000.0 };
        var profile = WorldSurfacePresentationProfile.Default with { SlabJointGapUnitRadius = gap };

        Assert.Throws<ArgumentOutOfRangeException>(() => WorldSlabAssemblyComposer.BuildAssembly(
            caps, centroids, thickness, RadialSectionProfile.Default.ThicknessDepthScale(), profile));
    }

    // === fixtures ===============================================================================

    // Two plates sharing the edge v0–v2 (same corner-sharing shape as the PlateSolidBuilderTests
    // fixture, but the two faces sit on DIFFERENT plates so the shared corners are the cross-plate
    // boundary the joint gap must open).
    private static WorldGlobeSnapshot TwoTouchingPlatesSnapshot()
    {
        var v0 = new GlobeVec3(0f, 0f, 1f);
        var v1 = new GlobeVec3(1f, 0f, 1f);
        var v2 = new GlobeVec3(0f, 1f, 1f);
        var v3 = new GlobeVec3(-1f, 1f, 1f);

        var cells = new List<GlobeCell>
        {
            new(0, 0, v0, v1, v2),
            new(1, 1, v0, v2, v3),
        };
        var plates = new List<GlobePlate>
        {
            new(0, new GlobeVec3(0, 0, 1), 0.0),
            new(1, new GlobeVec3(0, 1, 0), 0.0),
        };
        return new WorldGlobeSnapshot(0, 2, 2, 100_000, cells, plates);
    }

    private static double Distance(CartesianPoint3 a, CartesianPoint3 b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    // Pairs of TOP vertex indices (a-solid, b-solid) whose positions coincide — the shared
    // boundary corners of two touching plates. Top vertices occupy indices [0, VertexCount/2).
    private static IReadOnlyList<(int A, int B)> CoincidentTopVertexPairs(PlateSolid a, PlateSolid b)
    {
        var pairs = new List<(int, int)>();
        int na = a.VertexCount / 2;
        int nb = b.VertexCount / 2;
        for (int i = 0; i < na; i++)
        {
            for (int j = 0; j < nb; j++)
            {
                if (Distance(a.Positions[i], b.Positions[j]) < 1e-9)
                    pairs.Add((i, j));
            }
        }
        return pairs;
    }
}
