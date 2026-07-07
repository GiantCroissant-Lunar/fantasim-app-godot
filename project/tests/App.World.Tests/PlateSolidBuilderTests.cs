using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Globe.Core;
using FantaSim.Cartography.Shared;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// M-B (exploded solid crust) T3 proof for <see cref="PlateSolidBuilder"/>: from each plate's
/// relief-applied <see cref="PlateCap"/> top surface and a per-cell crust THICKNESS field, builds a
/// closed WATERTIGHT <see cref="PlateSolid"/> per plate, and a pure radial-explode transform whose
/// factor-0 is bit-equal to the assembled state. These tests are Godot-free and exercise the real
/// cartography cap path end-to-end.
/// </summary>
public sealed class PlateSolidBuilderTests
{
    // Exact-depth assertions need the seeded "peaks" relief OFF so top positions are pure envelope
    // (radius = baseRadius + mean elevation * exaggeration). With zero elevation the top radius is
    // EXACTLY baseRadius, so |topPos| - |bottomPos| = thickness * exaggeration with no relief term.
    private static readonly NoiseParams NoNoise = new(Amplitude: 0.0);

    // Reuse the exact two-plate fixture shape from GlobePlateSurfacesTests so the cap topology is
    // already proven watertight and the test focuses on the SOLID extrusion + explode.
    private static WorldGlobeSnapshot TwoPlateSnapshot()
    {
        // Plate 0, face A (cell 0): v0, v1, v2.  Plate 0, face B (cell 1): v0, v2, v3 (shares edge v0-v2).
        var v0 = new GlobeVec3(0f, 0f, 1f);
        var v1 = new GlobeVec3(1f, 0f, 1f);
        var v2 = new GlobeVec3(0f, 1f, 1f);
        var v3 = new GlobeVec3(-1f, 1f, 1f);
        // Plate 1, face (cell 2): disjoint corners on the far side.
        var w0 = new GlobeVec3(0f, 0f, -1f);
        var w1 = new GlobeVec3(1f, 0f, -1f);
        var w2 = new GlobeVec3(0f, 1f, -1f);

        var cells = new List<GlobeCell>
        {
            new(0, 0, v0, v1, v2),
            new(1, 0, v0, v2, v3),
            new(2, 1, w0, w1, w2),
        };
        var plates = new List<GlobePlate>
        {
            new(0, new GlobeVec3(0, 0, 1), 0.0),
            new(1, new GlobeVec3(0, 1, 0), 0.0),
        };
        return new WorldGlobeSnapshot(0, 3, 2, 100_000, cells, plates);
    }

    private static double Radius(CartesianPoint3 p)
        => Math.Sqrt((p.X * p.X) + (p.Y * p.Y) + (p.Z * p.Z));

    // Build the assembled solids for the two-plate snapshot under a given per-cell thickness field
    // and (optional) per-cell elevation. Zero elevation + NoNoise makes top radius == baseRadius so
    // the radial depth measurement is exact (|top| - |bottom| = thickness * exaggeration).
    private static (IReadOnlyList<PlateCap> Caps, IReadOnlyList<PlateSolid> Solids) BuildAssembled(
        double[] thicknessByCell,
        double[]? elevationsByCell = null,
        double exaggeration = 0.0001,
        double baseRadius = 1.0)
    {
        elevationsByCell ??= new double[thicknessByCell.Length];
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);
        var caps = surfaces.BuildSurfaces(elevationsByCell, exaggeration: exaggeration, baseRadius: baseRadius);
        var solids = PlateSolidBuilder.Build(caps, thicknessByCell, exaggeration, baseRadius);
        return (caps, solids);
    }

    [Fact]
    public void Each_plate_solid_is_watertight_every_edge_shared_by_exactly_two_triangles()
    {
        // Two-plate snapshot with non-zero thickness so the bottom is genuinely inset (not coincident
        // with the top). Every undirected edge across (top + bottom + walls) must appear in EXACTLY
        // 2 triangles — the closed-solid invariant. Thickness kept < baseRadius/exaggeration so the
        // bottom stays inside the globe (depth 0.4 and 0.8, both < 1.0).
        var thickness = new double[] { 4_000.0, 4_000.0, 8_000.0 };
        var (_, solids) = BuildAssembled(thickness);

        foreach (var solid in solids)
        {
            var edgeCounts = CountUndirectedEdges(solid);
            Assert.NotEmpty(edgeCounts);
            foreach (var kvp in edgeCounts)
            {
                Assert.Equal(2, kvp.Value);
            }
        }
    }

    [Fact]
    public void Exploded_factor_zero_is_bit_equal_identity()
    {
        // factor = 0 -> translation vector is zero -> the exploded positions are BIT-EQUAL to the
        // assembled positions (same array references / values). Triangles are always identical.
        var thickness = new double[] { 4_000.0, 4_000.0, 8_000.0 };
        var (_, assembled) = BuildAssembled(thickness);

        var snapshot = TwoPlateSnapshot();
        var centroids = PlateSolidBuilder.ComputeCentroids(snapshot);

        var exploded0 = PlateSolidBuilder.ApplyExplodedFactor(assembled, centroids, factor: 0.0);

        Assert.Equal(assembled.Count, exploded0.Count);
        for (int p = 0; p < assembled.Count; p++)
        {
            Assert.Equal(assembled[p].PlateId, exploded0[p].PlateId);
            Assert.Equal(assembled[p].Positions, exploded0[p].Positions);
            Assert.Equal(assembled[p].Triangles, exploded0[p].Triangles);
        }
    }

    [Fact]
    public void Thickness_is_data_true_double_thickness_gives_double_radial_depth()
    {
        // Two cells with IDENTICAL geometry (the two-plate snapshot's plate 1 has a single cell, so
        // there is no sharing to confound the depth). We build TWO snapshots-worth of solids with
        // thickness T and 2T at the SAME cell, both with zero elevation so |topPos| == baseRadius
        // exactly, and assert the radial depth (|topPos| - |bottomPos|) at the thicker cell's vertices
        // is 2x the thinner's. Thickness kept < baseRadius/exaggeration so depth stays < 1.0.
        const double t = 3_000.0;
        const double exaggeration = 0.0001;
        const double baseRadius = 1.0;

        var thinSolids = PlateSolidBuilder.Build(
            new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise)
                .BuildSurfaces(new double[] { 0, 0, 0 }, exaggeration: exaggeration, baseRadius: baseRadius),
            new double[] { 0, 0, t },
            exaggeration,
            baseRadius);

        var thickSolids = PlateSolidBuilder.Build(
            new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise)
                .BuildSurfaces(new double[] { 0, 0, 0 }, exaggeration: exaggeration, baseRadius: baseRadius),
            new double[] { 0, 0, 2 * t },
            exaggeration,
            baseRadius);

        // Plate 1 is the single-cell plate (cell 2). Its solid has 3 top + 3 bottom vertices.
        var thinPlate1 = thinSolids.Single(s => s.PlateId == 1);
        var thickPlate1 = thickSolids.Single(s => s.PlateId == 1);

        int n = thinPlate1.VertexCount / 2; // top count == bottom count
        for (int v = 0; v < n; v++)
        {
            double thinTopR = Radius(thinPlate1.Positions[v]);
            double thinBotR = Radius(thinPlate1.Positions[n + v]);
            double thickTopR = Radius(thickPlate1.Positions[v]);
            double thickBotR = Radius(thickPlate1.Positions[n + v]);

            double thinDepth = thinTopR - thinBotR;
            double thickDepth = thickTopR - thickBotR;

            // Top radius is exactly baseRadius (zero elevation + NoNoise).
            Assert.Equal(baseRadius, thinTopR, 9);
            Assert.Equal(baseRadius, thickTopR, 9);

            // Data-trueness: 2x thickness -> 2x radial depth.
            Assert.True(thinDepth > 0.0, $"thin depth must be positive: {thinDepth}");
            Assert.Equal(2.0, thickDepth / thinDepth, 6);
        }
    }

    [Fact]
    public void Build_is_deterministic()
    {
        // Two back-to-back builds from identical inputs produce identical Positions and Triangles.
        var thickness = new double[] { 4_000.0, 4_000.0, 8_000.0 };

        var surfaces1 = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);
        var surfaces2 = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);

        var caps1 = surfaces1.BuildSurfaces(new double[] { 0, 0, 0 }, exaggeration: 0.0001);
        var caps2 = surfaces2.BuildSurfaces(new double[] { 0, 0, 0 }, exaggeration: 0.0001);

        var solids1 = PlateSolidBuilder.Build(caps1, thickness, 0.0001);
        var solids2 = PlateSolidBuilder.Build(caps2, thickness, 0.0001);

        Assert.Equal(solids1.Count, solids2.Count);
        for (int p = 0; p < solids1.Count; p++)
        {
            Assert.Equal(solids1[p].PlateId, solids2[p].PlateId);
            Assert.Equal(solids1[p].Positions, solids2[p].Positions);
            Assert.Equal(solids1[p].Triangles, solids2[p].Triangles);
        }
    }

    [Fact]
    public void Exploded_factor_one_translates_along_centroid_direction()
    {
        // Sanity: factor = 1 with maxOffset = R translates every position of a plate by exactly
        // R * centroidDir. The plate 1 single-cell centroid direction is normalize(C0+C1+C2); every
        // position moves by maxOffset along that direction. Top and bottom move by the SAME vector,
        // so the relative depth (|top|-|bottom|) is unchanged.
        var thickness = new double[] { 0, 0, 4_000.0 };
        var (_, assembled) = BuildAssembled(thickness, exaggeration: 0.0001, baseRadius: 1.0);
        var centroids = PlateSolidBuilder.ComputeCentroids(TwoPlateSnapshot());
        const double maxOffset = 0.35;

        var exploded = PlateSolidBuilder.ApplyExplodedFactor(assembled, centroids, factor: 1.0, maxOffset: maxOffset);

        var plate1 = exploded.Single(s => s.PlateId == 1);
        var centroid1 = centroids.Single(c => c.PlateId == 1).CentroidDirection;
        var assembledPlate1 = assembled.Single(s => s.PlateId == 1);

        for (int v = 0; v < plate1.VertexCount; v++)
        {
            var ap = assembledPlate1.Positions[v];
            var ep = plate1.Positions[v];
            Assert.Equal(ap.X + centroid1.X * maxOffset, ep.X, 9);
            Assert.Equal(ap.Y + centroid1.Y * maxOffset, ep.Y, 9);
            Assert.Equal(ap.Z + centroid1.Z * maxOffset, ep.Z, 9);
        }

        // Triangles unchanged.
        Assert.Equal(assembledPlate1.Triangles, plate1.Triangles);

        // The radial depth is preserved under pure translation ONLY when measured along the
        // original radial direction (the top vertex's unit direction). |top|-|bottom| changes
        // because the translation vector is not aligned with every vertex's radial direction, but
        // the projection of (top - bottom) onto the top unit direction is invariant. Both top and
        // bottom move by the same vector, so (top - bottom) is unchanged.
        int n = plate1.VertexCount / 2;
        for (int v = 0; v < n; v++)
        {
            var beforeTop = assembledPlate1.Positions[v];
            var beforeBot = assembledPlate1.Positions[n + v];
            var afterTop = plate1.Positions[v];
            var afterBot = plate1.Positions[n + v];
            Assert.Equal(beforeTop.X - beforeBot.X, afterTop.X - afterBot.X, 9);
            Assert.Equal(beforeTop.Y - beforeBot.Y, afterTop.Y - afterBot.Y, 9);
            Assert.Equal(beforeTop.Z - beforeBot.Z, afterTop.Z - afterBot.Z, 9);
        }
    }

    [Fact]
    public void ComputeCentroids_is_deterministic()
    {
        // Two centroid computations from the same snapshot produce identical directions.
        var snapshot = TwoPlateSnapshot();
        var c1 = PlateSolidBuilder.ComputeCentroids(snapshot);
        var c2 = PlateSolidBuilder.ComputeCentroids(snapshot);

        Assert.Equal(c1.Count, c2.Count);
        for (int i = 0; i < c1.Count; i++)
        {
            Assert.Equal(c1[i].PlateId, c2[i].PlateId);
            Assert.Equal(c1[i].CentroidDirection, c2[i].CentroidDirection);
        }
    }

    [Fact]
    public void Real_seed_globe_every_plate_solid_is_watertight()
    {
        // The REAL four-plate seed at frequency 3 (1280 cells) through the real cartography part.
        // Every plate's SOLID (top + bottom + walls) must be watertight: every undirected edge is
        // shared by exactly two triangles.
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);
        var elevations = new double[snapshot.CellCount];
        var thickness = Enumerable.Repeat(3_000.0, snapshot.CellCount).ToArray();

        var caps = surfaces.BuildSurfaces(elevations, exaggeration: 0.0001);
        var solids = PlateSolidBuilder.Build(caps, thickness, 0.0001);

        foreach (var solid in solids)
        {
            var edgeCounts = CountUndirectedEdges(solid);
            Assert.NotEmpty(edgeCounts);
            foreach (var kvp in edgeCounts)
            {
                Assert.Equal(2, kvp.Value);
            }
        }
    }

    // D3: the default RadialSectionProfile's crust-thickness exaggeration (8.0) must make a
    // default-thickness (30 km) solid read as visible slab walls — the bottom depth ≥ 0.03R against
    // the old ~0.0009R invisibility of the surface-relief-coupled exaggeration. This is the
    // presentation contract the mantle-interior LAYER view (D1) depends on for "separated slabs".
    [Fact]
    public void Default_profile_thickness_exaggeration_yields_visible_slab_walls()
    {
        var profile = RadialSectionProfile.Default;
        const double baseRadius = 1.0;

        // Zero elevation so top radius == baseRadius exactly (radial depth is exactly thk * scale).
        var thickness = new double[] { 0, 0, profile.DefaultCrustThicknessMetres };
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise)
            .BuildSurfaces(new double[] { 0, 0, 0 }, exaggeration: 0.0001, baseRadius: baseRadius);
        // The profile exposes the metres-to-unit-radius scale the builder expects (8.0 / 6,371,000).
        var solids = PlateSolidBuilder.Build(surfaces, thickness, profile.ThicknessDepthScale(), baseRadius);

        // Plate 1 is the single-cell plate (cell 2). Its solid has 3 top + 3 bottom vertices.
        var plate1 = solids.Single(s => s.PlateId == 1);
        int n = plate1.VertexCount / 2;

        double expectedDepth = profile.DisplayedCrustFraction();
        for (int v = 0; v < n; v++)
        {
            double topR = Radius(plate1.Positions[v]);
            double botR = Radius(plate1.Positions[n + v]);
            double depth = topR - botR;

            Assert.Equal(baseRadius, topR, 9);
            // The depth equals the profile's displayed crust fraction to float precision.
            Assert.Equal(expectedDepth, depth, 6);
            // The visibility contract: ≥ 0.03R (the D1 separated-slabs gate).
            Assert.True(depth >= 0.03, $"default-profile slab wall depth must read as visible (>=0.03R): {depth}");
        }
    }

    // Count occurrences of each UNDIRECTED edge (min(a,b), max(a,b)) across all triangles. A
    // watertight closed solid has every edge shared by EXACTLY 2 triangles.
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