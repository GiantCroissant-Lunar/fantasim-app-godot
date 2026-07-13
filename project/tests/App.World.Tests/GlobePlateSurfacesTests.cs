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
/// T3 proof for <see cref="GlobePlateSurfaces"/> (the un-shattering step): from a
/// <see cref="WorldGlobeSnapshot"/> it partitions cells by plate and, per plate, assembles ONE
/// WATERTIGHT cap — neighbouring cells share corner vertices, each corner sits at the MEAN height of
/// the cells touching it (via the cartography <c>GlobeSurfaceBuilder</c>). These tests are Godot-free
/// and exercise the real cartography part end-to-end.
/// </summary>
public sealed class GlobePlateSurfacesTests
{
    // Exact-height assertions need the seeded "peaks" relief OFF so positions are pure envelope.
    // Amplitude 0 makes NoiseRelief a no-op (base height unchanged).
    private static readonly NoiseParams NoNoise = new(Amplitude: 0.0);
    private static readonly NoiseParams StrongCrustFabric = new(
        Seed: 1337,
        BaseFrequency: 9.0,
        Octaves: 5,
        Lacunarity: 2.0,
        Gain: 0.5,
        Amplitude: 14_000.0,
        Ridged: false);

    // A tiny hand-built snapshot: two plates. Plate 0 is two triangles sharing an edge (a "diamond");
    // plate 1 is a single triangle. Corner positions are deliberately authored so the shared edge of
    // plate 0's two faces is bit-for-bit identical (dedupe must collapse them) and so plate 1's lone
    // face is disjoint. Positions need only be non-zero (they get normalized onto the unit sphere).
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

    private static WorldGlobeSnapshot TwoPlatesSharingBoundaryEdgeWithEndpointRelief()
    {
        var s0 = new GlobeVec3(0f, 0f, 1f);
        var s1 = new GlobeVec3(0f, 1f, 1f);
        var a0 = new GlobeVec3(1f, 0f, 1f);
        var a1 = new GlobeVec3(1f, -1f, 1f);
        var b0 = new GlobeVec3(-1f, 0f, 1f);
        var b1 = new GlobeVec3(-1f, 1f, 1f);

        var cells = new List<GlobeCell>
        {
            new(0, 0, s0, s1, a0),
            new(1, 0, s0, a0, a1),
            new(2, 1, s1, s0, b0),
            new(3, 1, s1, b1, b0),
        };
        var plates = new List<GlobePlate>
        {
            new(0, new GlobeVec3(1, 0, 1), 0.0),
            new(1, new GlobeVec3(-1, 0, 1), 0.0),
        };
        return new WorldGlobeSnapshot(0, 4, 2, 100_000, cells, plates);
    }

    [Fact]
    public void Partition_covers_all_cells_exactly_once_and_lands_them_in_the_right_plate()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot());

        var caps = surfaces.BuildSurfaces(new double[] { 0, 0, 0 }, exaggeration: 1.0);

        // Exactly the two non-empty plates appear.
        Assert.Equal(new[] { 0, 1 }, caps.Select(c => c.PlateId).OrderBy(p => p).ToArray());

        var plate0 = caps.Single(c => c.PlateId == 0);
        var plate1 = caps.Single(c => c.PlateId == 1);

        // Each cell appears exactly once, in the correct plate's cap.
        Assert.Equal(new[] { 0, 1 }, plate0.CellIds.OrderBy(c => c).ToArray());
        Assert.Equal(new[] { 2 }, plate1.CellIds);

        // Union over all caps == every cell id exactly once.
        var allCells = caps.SelectMany(c => c.CellIds).OrderBy(c => c).ToArray();
        Assert.Equal(new[] { 0, 1, 2 }, allCells);
    }

    [Fact]
    public void Plate0_cap_is_watertight_shared_vertices_fewer_than_faces_times_three()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot());

        var plate0 = surfaces.BuildSurfaces(new double[] { 0, 0, 0 }, exaggeration: 1.0)
            .Single(c => c.PlateId == 0);

        // Two faces -> 6 triangle indices.
        Assert.Equal(2, plate0.Surface.TriangleCount);
        Assert.Equal(6, plate0.Surface.Triangles.Length);

        // Watertight: the two faces share edge v0-v2, so the unique-vertex count is 4 (v0,v1,v2,v3),
        // strictly fewer than faces*3 == 6. Positions length == that unique-vertex count.
        Assert.Equal(4, plate0.Surface.VertexCount);
        Assert.Equal(plate0.Surface.VertexCount, plate0.Surface.Positions.Length);
        Assert.True(plate0.Surface.VertexCount < plate0.Surface.TriangleCount * 3,
            "a watertight cap must reuse shared corners (vertexCount < faces*3)");
    }

    [Fact]
    public void Single_face_plate_cap_has_exactly_three_vertices()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot());

        var plate1 = surfaces.BuildSurfaces(new double[] { 0, 0, 0 }, exaggeration: 1.0)
            .Single(c => c.PlateId == 1);

        Assert.Equal(1, plate1.Surface.TriangleCount);
        Assert.Equal(3, plate1.Surface.VertexCount);
    }

    [Fact]
    public void Shared_vertex_gets_mean_height_of_incident_cells()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);

        // Plate 0: face A (cell 0) elevation a, face B (cell 1) elevation b. Exaggeration 1.0 so the
        // height equals the elevation. The shared corners v0 & v2 touch BOTH faces -> mean (a+b)/2;
        // v1 touches only face A -> a; v3 touches only face B -> b. The watertight position of a
        // shared corner therefore sits at radius (1 + mean) along its unit direction.
        const double a = 0.20;
        const double b = 0.60;
        var caps = surfaces.BuildSurfaces(new double[] { a, b, 0 }, exaggeration: 1.0);
        var plate0 = caps.Single(c => c.PlateId == 0);

        // Recover each vertex's height back out of its position radius (positions = unit(dir)*(1+h)).
        double HeightOf(int localVertex)
        {
            var p = plate0.Surface.Positions[localVertex];
            return Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z) - 1.0;
        }

        // Identify the shared corners: a corner referenced by both triangles. Build incidence from the
        // index buffer (face 0 = indices 0..2, face 1 = indices 3..5).
        var tris = plate0.Surface.Triangles;
        var face0 = new HashSet<int> { tris[0], tris[1], tris[2] };
        var face1 = new HashSet<int> { tris[3], tris[4], tris[5] };
        var shared = face0.Where(face1.Contains).ToArray();
        Assert.Equal(2, shared.Length); // exactly the two endpoints of the shared edge

        foreach (var s in shared)
            Assert.Equal((a + b) / 2.0, HeightOf(s), 6); // mean height at every shared corner

        // The two non-shared corners carry their single face's height.
        var onlyFace0 = face0.Single(i => !face1.Contains(i));
        var onlyFace1 = face1.Single(i => !face0.Contains(i));
        Assert.Equal(a, HeightOf(onlyFace0), 6);
        Assert.Equal(b, HeightOf(onlyFace1), 6);
    }

    [Fact]
    public void BuildSurfaces_UsesDeclaredBaseRadius()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);

        var cap = surfaces.BuildSurfaces(
                new double[] { 0.0, 0.0, 0.0 },
                exaggeration: 1.0,
                baseRadius: 1.04)
            .Single(c => c.PlateId == 0);

        foreach (var position in cap.Surface.Positions)
            Assert.Equal(1.04, RadiusOf(position), 6);
    }

    [Fact]
    public void BuildAdaptiveSurfaces_UsesDeclaredBaseRadius()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);

        var cap = surfaces.BuildAdaptiveSurfaces(
                new double[] { 0.0, 0.0, 0.0 },
                exaggeration: 1.0,
                options: new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 100.0),
                baseRadius: 1.04)
            .Single(c => c.PlateId == 0);

        foreach (var position in cap.Surface.Positions)
            Assert.Equal(1.04, RadiusOf(position), 6);
    }

    private static double RadiusOf(CartesianPoint3 point)
        => Math.Sqrt((point.X * point.X) + (point.Y * point.Y) + (point.Z * point.Z));

    [Fact]
    public void Displacement_uses_the_exaggeration_factor()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);

        // Plate 1 is a single face (cell 2). With a uniform elevation E and exaggeration X every corner
        // height is E*X, so each position radius is 1 + E*X. Doubling X doubles the displacement.
        const double e = 1000.0;
        const double x = 0.00012;
        var plate1 = surfaces.BuildSurfaces(new double[] { 0, 0, e }, exaggeration: x)
            .Single(c => c.PlateId == 1);

        double RadiusOf(int v)
        {
            var p = plate1.Surface.Positions[v];
            return Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
        }

        for (int v = 0; v < plate1.Surface.VertexCount; v++)
            Assert.Equal(1.0 + e * x, RadiusOf(v), 6);
    }

    [Fact]
    public void Topology_is_cached_across_ticks_only_heights_change()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot());

        var t0 = surfaces.BuildSurfaces(new double[] { 0.1, 0.2, 0.3 }, exaggeration: 1.0)
            .Single(c => c.PlateId == 0);
        var t1 = surfaces.BuildSurfaces(new double[] { 0.9, 0.8, 0.7 }, exaggeration: 1.0)
            .Single(c => c.PlateId == 0);

        // Topology (index buffer + cell ordering + vertex count) is tick-invariant: identical across ticks.
        Assert.Equal(t0.Surface.Triangles, t1.Surface.Triangles);
        Assert.Equal(t0.CellIds, t1.CellIds);
        Assert.Equal(t0.Surface.VertexCount, t1.Surface.VertexCount);

        // Heights (and therefore positions) differ between the two ticks.
        Assert.NotEqual(
            t0.Surface.Positions.Select(p => p.Z),
            t1.Surface.Positions.Select(p => p.Z));
    }

    [Fact]
    public void Real_seed_globe_every_cell_partitioned_once_and_caps_are_watertight()
    {
        // The REAL four-plate seed at frequency 3 (1280 cells) through the real cartography part.
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot);

        var elevations = new double[snapshot.CellCount]; // flat is fine; topology is what we assert
        var caps = surfaces.BuildSurfaces(elevations, exaggeration: 0.00012);

        // Partition covers all cells exactly once.
        var allCells = caps.SelectMany(c => c.CellIds).OrderBy(c => c).ToArray();
        Assert.Equal(Enumerable.Range(0, snapshot.CellCount).ToArray(), allCells);

        // Every plate's cap is watertight: unique vertices strictly fewer than faces*3 (corners shared
        // across the plate's interior), and the index buffer references only in-range local vertices.
        foreach (var cap in caps)
        {
            int faces = cap.Surface.TriangleCount;
            Assert.Equal(cap.CellIds.Length, faces);
            Assert.True(cap.Surface.VertexCount < faces * 3,
                $"plate {cap.PlateId}: cap is not watertight ({cap.Surface.VertexCount} verts, {faces} faces)");
            Assert.All(cap.Surface.Triangles, i => Assert.InRange(i, 0, cap.Surface.VertexCount - 1));
        }
    }

    [Fact]
    public void BuildAdaptiveSurfaces_ProducesMoreTrianglesWhenReliefCrossesSharedEdges()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);
        var elevations = new double[] { 0.0, 1000.0, 0.0 };

        var fixedCaps = surfaces.BuildSurfaces(elevations, exaggeration: 1.0);
        var adaptiveCaps = surfaces.BuildAdaptiveSurfaces(
            elevations,
            exaggeration: 1.0,
            new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 100.0));

        var fixedPlate0 = fixedCaps.Single(c => c.PlateId == 0);
        var adaptivePlate0 = adaptiveCaps.Single(c => c.PlateId == 0);
        Assert.True(adaptivePlate0.Surface.TriangleCount > fixedPlate0.Surface.TriangleCount);
    }

    [Fact]
    public void BuildAdaptiveSurfaces_MapsGeneratedSubfacesToParentCellIds()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);
        var elevations = new double[] { 0.0, 1000.0, 0.0 };

        var plate0 = surfaces.BuildAdaptiveSurfaces(
                elevations,
                exaggeration: 1.0,
                new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 100.0))
            .Single(c => c.PlateId == 0);

        Assert.Equal(plate0.Surface.TriangleCount, plate0.CellIds.Length);
        Assert.All(plate0.CellIds, id => Assert.Contains(id, new[] { 0, 1 }));
        Assert.True(plate0.CellIds.Count(id => id == 0) > 1);
        Assert.True(plate0.CellIds.Count(id => id == 1) > 1);
    }

    [Fact]
    public void BuildAdaptiveSurfaces_SplitsFlatTerrainWhenFeatureWeightChanges()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);
        var elevations = new double[] { 0.0, 0.0, 0.0 };
        var featureWeights = new double[] { 0.0, 1.0, 0.0 };

        var fixedCaps = surfaces.BuildSurfaces(elevations, exaggeration: 1.0);
        var adaptiveCaps = surfaces.BuildAdaptiveSurfaces(
            elevations,
            exaggeration: 1.0,
            new AdaptiveSubdivisionOptions(
                MaxDepth: 1,
                EdgeHeightDeltaThreshold: 10.0,
                FeatureWeightDeltaThreshold: 0.25),
            featureWeightsByCell: featureWeights);

        var fixedPlate0 = fixedCaps.Single(c => c.PlateId == 0);
        var adaptivePlate0 = adaptiveCaps.Single(c => c.PlateId == 0);
        Assert.True(adaptivePlate0.Surface.TriangleCount > fixedPlate0.Surface.TriangleCount);
        Assert.Equal(adaptivePlate0.Surface.TriangleCount, adaptivePlate0.CellIds.Length);
        Assert.All(adaptivePlate0.CellIds, id => Assert.Contains(id, new[] { 0, 1 }));
    }

    [Fact]
    public void BuildAdaptiveSurfaces_PreservesCrossPlateSharedMidpoints()
    {
        var snapshot = TwoPlatesSharingBoundaryEdgeWithEndpointRelief();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);
        var elevations = new double[] { 0.0, 1000.0, 0.0, -1000.0 };

        var fixedCrossGroups = CountCrossPlateDirectionGroups(
            surfaces.BuildSurfaces(elevations, exaggeration: 1.0).OrderBy(c => c.PlateId).ToArray());
        var adaptiveCaps = surfaces.BuildAdaptiveSurfaces(
                elevations,
                exaggeration: 1.0,
                new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 100.0))
            .OrderBy(c => c.PlateId)
            .ToArray();

        Assert.True(CountCrossPlateDirectionGroups(adaptiveCaps) > fixedCrossGroups);
        AssertEveryCrossPlateBoundaryVertexMatchesExactly(adaptiveCaps);
    }

    [Fact]
    public void Binder_regime_flat_zero_elevation_cross_plate_boundary_vertices_match_exactly()
    {
        // Under a uniform/zero envelope the per-plate envelope MEAN is identical everywhere, so the
        // only thing that could crack a shared boundary corner between two caps is the noise. Sampling
        // noise on the shared BASE position makes it identical in both caps -> the two caps' coincident
        // boundary vertex matches exactly. This is the property PlanetPresentationBinder now relies on.
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot);
        var elevations = new double[snapshot.CellCount];
        var caps = surfaces.BuildSurfaces(elevations, exaggeration: 0.00012)
            .OrderBy(c => c.PlateId).ToArray();

        AssertCrossPlateBoundaryVerticesMatchExactly(caps);
    }

    [Fact]
    public void Binder_regime_nonuniform_elevation_cross_plate_boundary_vertices_match_exactly()
    {
        // The watertight-at-zero proof (above) only exercises the noise layer. Under REAL per-cell
        // elevations the ENVELOPE mean at a shared boundary corner must also agree across plates. The
        // naive per-plate GatherVertexHeights means each side only sees its OWN cells -> the two copies
        // of the corner get different heights -> a thin dark sliver crack. This test pins the fix:
        // the elevation used for a shared corner is the mean over ALL incident cells GLOBALLY (across
        // every plate), so the corner lands at one radius regardless of which plate builds it.
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise); // isolate the envelope

        // Deterministic, varying, non-uniform field. Boundary-adjacent cells of different plates get
        // different elevations (cell ids interleave across plates), which is exactly the crack trigger.
        var elevations = new double[snapshot.CellCount];
        for (int i = 0; i < elevations.Length; i++)
            elevations[i] = (i % 11) * 100.0 - 500.0; // -500..+400, varies per cell

        var caps = surfaces.BuildSurfaces(elevations, exaggeration: 0.00012)
            .OrderBy(c => c.PlateId).ToArray();

        AssertEveryCrossPlateBoundaryVertexMatchesExactly(caps);
    }

    [Fact]
    public void Strong_render_fabric_keeps_nonuniform_crust_caps_watertight()
    {
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: StrongCrustFabric);

        var elevations = new double[snapshot.CellCount];
        for (int i = 0; i < elevations.Length; i++)
            elevations[i] = (i % 17) * 180.0 - 900.0;

        var caps = surfaces.BuildSurfaces(elevations, exaggeration: 0.00001)
            .OrderBy(c => c.PlateId)
            .ToArray();

        AssertEveryCrossPlateBoundaryVertexMatchesExactly(caps);
    }

    // Strict watertight check: group every cap's vertices by their BASE unit direction (quantized),
    // find groups that span more than one plate, and assert the displaced positions agree to 9
    // decimals across every plate in the group. This is the property the binder relies on — NO
    // cross-plate boundary corner may crack, not just "at least one happens to line up".
    private static void AssertEveryCrossPlateBoundaryVertexMatchesExactly(PlateCap[] caps)
    {
        const double DirEps = 1e-5;
        const double DirScale = 1.0 / DirEps;

        static (long, long, long) DirKey(CartesianPoint3 p)
        {
            double r = Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
            double inv = r > 0 ? 1.0 / r : 0.0;
            return (
                (long)Math.Round(p.X * inv * DirScale),
                (long)Math.Round(p.Y * inv * DirScale),
                (long)Math.Round(p.Z * inv * DirScale));
        }

        var groups = new Dictionary<(long, long, long), List<(int PlateId, int LocalVertex, CartesianPoint3 Pos)>>();
        foreach (var cap in caps)
        {
            for (int v = 0; v < cap.Surface.VertexCount; v++)
            {
                var p = cap.Surface.Positions[v];
                var key = DirKey(p);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<(int, int, CartesianPoint3)>();
                    groups[key] = list;
                }
                list.Add((cap.PlateId, v, p));
            }
        }

        int crossGroups = 0;
        foreach (var kv in groups)
        {
            var members = kv.Value;
            var plateIds = members.Select(m => m.PlateId).Distinct().ToArray();
            if (plateIds.Length < 2)
                continue; // interior vertex, incident to a single plate

            crossGroups++;
            var refPos = members[0].Pos;
            foreach (var m in members)
            {
                Assert.Equal(refPos.X, m.Pos.X, 9);
                Assert.Equal(refPos.Y, m.Pos.Y, 9);
                Assert.Equal(refPos.Z, m.Pos.Z, 9);
            }
        }

        Assert.True(crossGroups > 0, "expected at least one cross-plate boundary vertex group");
    }

    private static int CountCrossPlateDirectionGroups(PlateCap[] caps)
    {
        const double DirEps = 1e-5;
        const double DirScale = 1.0 / DirEps;

        static (long, long, long) DirKey(CartesianPoint3 p)
        {
            double r = Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
            double inv = r > 0 ? 1.0 / r : 0.0;
            return (
                (long)Math.Round(p.X * inv * DirScale),
                (long)Math.Round(p.Y * inv * DirScale),
                (long)Math.Round(p.Z * inv * DirScale));
        }

        var groups = new Dictionary<(long, long, long), HashSet<int>>();
        foreach (var cap in caps)
        {
            for (int v = 0; v < cap.Surface.VertexCount; v++)
            {
                var key = DirKey(cap.Surface.Positions[v]);
                if (!groups.TryGetValue(key, out var plateIds))
                {
                    plateIds = new HashSet<int>();
                    groups[key] = plateIds;
                }
                plateIds.Add(cap.PlateId);
            }
        }

        return groups.Values.Count(plateIds => plateIds.Count > 1);
    }

    // Shared assertion: every cross-plate coincident boundary vertex matches exactly (same quantized
    // key -> same position within tolerance), matching the flat-zero test's tolerance style.
    private static void AssertCrossPlateBoundaryVerticesMatchExactly(PlateCap[] caps)
    {

        // Gather every cap's vertex positions onto a canonical (quantized) key so corners that are
        // the same icosphere vertex (bar a few ulps) compare equal across caps.
        var seen = new Dictionary<(long, long, long), (int PlateId, int LocalVertex)>();
        var matches = 0;
        foreach (var cap in caps)
        {
            for (int v = 0; v < cap.Surface.VertexCount; v++)
            {
                var p = cap.Surface.Positions[v];
                var key = Quantize(p);
                if (seen.TryGetValue(key, out var prior))
                {
                    Assert.NotEqual(prior.PlateId, cap.PlateId);
                    var priorCap = caps.Single(c => c.PlateId == prior.PlateId);
                    var priorPos = priorCap.Surface.Positions[prior.LocalVertex];
                    Assert.Equal(priorPos.X, p.X, 9);
                    Assert.Equal(priorPos.Y, p.Y, 9);
                    Assert.Equal(priorPos.Z, p.Z, 9);
                    matches++;
                }
                else
                {
                    seen[key] = (cap.PlateId, v);
                }
            }
        }

        Assert.True(matches > 0, "expected at least one cross-plate boundary vertex match");
    }

    private const double QuantEps = 1e-5;
    private const double QuantScale = 1.0 / QuantEps;

    private static (long, long, long) Quantize(CartesianPoint3 p) => (
        (long)Math.Round(p.X * QuantScale),
        (long)Math.Round(p.Y * QuantScale),
        (long)Math.Round(p.Z * QuantScale));

    // === seeded "peaks" relief =====================================================================

    // A snapshot whose two plates SHARE a boundary corner position: plate 0's face and plate 1's face
    // both reference the exact same corner `s`. Because the renderer samples the noise on the cap's
    // BASE (tick-0) corner positions, that shared corner gets the SAME noise in both caps — so under a
    // uniform envelope the two caps' coincident boundary vertex lands at the SAME final height (no crack).
    private static (WorldGlobeSnapshot Snap, GlobeVec3 Shared) TwoPlatesSharingABoundaryCorner()
    {
        var s = new GlobeVec3(0f, 1f, 0f);   // the SHARED boundary corner (same position in both plates)
        // Plate 0 face (cell 0): s, a0, a1.
        var a0 = new GlobeVec3(1f, 1f, 0.2f);
        var a1 = new GlobeVec3(1f, 1f, -0.2f);
        // Plate 1 face (cell 1): s, b0, b1 (other side of the shared corner).
        var b0 = new GlobeVec3(-1f, 1f, 0.2f);
        var b1 = new GlobeVec3(-1f, 1f, -0.2f);

        var cells = new List<GlobeCell>
        {
            new(0, 0, s, a0, a1),
            new(1, 1, s, b0, b1),
        };
        var plates = new List<GlobePlate>
        {
            new(0, new GlobeVec3(1, 1, 0), 0.0),
            new(1, new GlobeVec3(-1, 1, 0), 0.0),
        };
        return (new WorldGlobeSnapshot(0, 2, 2, 100_000, cells, plates), s);
    }

    private static double Radius(CartesianPoint3 p) => Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);

    private static CartesianPoint3 CellCenter(GlobeCell cell)
    {
        var p = new CartesianPoint3(
            cell.C0.X + cell.C1.X + cell.C2.X,
            cell.C0.Y + cell.C1.Y + cell.C2.Y,
            cell.C0.Z + cell.C1.Z + cell.C2.Z);
        double len = Radius(p);
        return new CartesianPoint3(p.X / len, p.Y / len, p.Z / len);
    }

    [Fact]
    public void TectonicDetailSampler_UsesWeakerInteriorAndRidgedActiveFeatureProfiles()
    {
        var snapshot = TwoPlateSnapshot();
        var features = new[]
        {
            new CellCrustFeature(0, 0.0),
            new CellCrustFeature(1, 10.0),
            new CellCrustFeature(5, 10.0),
        };
        var sampler = new TectonicDetailSampler(snapshot, features, StrongCrustFabric);

        var interior = sampler.ResolveProfile(CellCenter(snapshot.Cells[0]));
        var mountain = sampler.ResolveProfile(CellCenter(snapshot.Cells[1]));
        var fault = sampler.ResolveProfile(CellCenter(snapshot.Cells[2]));

        Assert.True(interior.Noise.Amplitude < StrongCrustFabric.Amplitude);
        Assert.True(mountain.Noise.Amplitude > interior.Noise.Amplitude);
        Assert.True(mountain.Noise.Ridged);
        Assert.False(interior.Noise.Ridged);
        Assert.True(fault.Noise.Amplitude > interior.Noise.Amplitude);
        Assert.True(fault.Noise.Amplitude < mountain.Noise.Amplitude);
    }

    [Fact]
    public void TectonicDetailSampler_IsDeterministicForIdenticalPositions()
    {
        var snapshot = TwoPlateSnapshot();
        var features = new[]
        {
            new CellCrustFeature(0, 0.0),
            new CellCrustFeature(4, 2.0),
            new CellCrustFeature(0, 0.0),
        };
        var sampler = new TectonicDetailSampler(snapshot, features, StrongCrustFabric);
        var position = CellCenter(snapshot.Cells[1]);

        Assert.Equal(sampler.ResolveProfile(position), sampler.ResolveProfile(position));
        Assert.Equal(sampler.Sample(position), sampler.Sample(position), 12);
    }

    [Fact]
    public void TectonicDetailSampler_CapsResolvedInteriorAndActiveAmplitudes_WhenMultipliersExceedOne()
    {
        var snapshot = TwoPlateSnapshot();
        var noise = StrongCrustFabric with { Amplitude = TectonicDetailSampler.MaxResidualAmplitudeMetres };
        var interior = new TectonicDetailSampler(
            snapshot,
            new CellCrustFeature[snapshot.CellCount],
            noise,
            interiorAmplitudeMultiplier: 8.0,
            activeAmplitudeMultiplier: 8.0)
            .ResolveProfile(CellCenter(snapshot.Cells[0]));
        var active = new TectonicDetailSampler(
            snapshot,
            Enumerable.Repeat(
                new CellCrustFeature(TectonicFeatureKind.Mountain.ToWireByte(), 10_000.0),
                snapshot.CellCount).ToArray(),
            noise,
            interiorAmplitudeMultiplier: 8.0,
            activeAmplitudeMultiplier: 8.0)
            .ResolveProfile(CellCenter(snapshot.Cells[0]));

        Assert.InRange(interior.Noise.Amplitude, 0.0, TectonicDetailSampler.MaxResidualAmplitudeMetres);
        Assert.InRange(active.Noise.Amplitude, 0.0, TectonicDetailSampler.MaxResidualAmplitudeMetres);
        Assert.True(double.IsFinite(interior.Noise.Amplitude));
        Assert.True(double.IsFinite(active.Noise.Amplitude));
    }

    [Fact]
    public void TectonicDetailSampler_RejectsNegativeOrNonFiniteAmplitudeInputs()
    {
        var snapshot = TwoPlateSnapshot();
        foreach (double invalid in new[] { -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TectonicDetailSampler(snapshot, null, StrongCrustFabric, interiorAmplitudeMultiplier: invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TectonicDetailSampler(snapshot, null, StrongCrustFabric, activeAmplitudeMultiplier: invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TectonicDetailSampler(snapshot, null, StrongCrustFabric with { Amplitude = invalid }));
        }
    }

    // Find the local vertex index in a cap whose unit direction matches `dir` (the shared corner).
    private static int VertexClosestTo(PlateCap cap, GlobeVec3 dir)
    {
        double dl = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y + dir.Z * dir.Z);
        double dx = dir.X / dl, dy = dir.Y / dl, dz = dir.Z / dl;
        int best = -1; double bestDot = double.NegativeInfinity;
        for (int v = 0; v < cap.Surface.VertexCount; v++)
        {
            var p = cap.Surface.Positions[v];
            double pl = Radius(p);
            double d = (p.X / pl * dx) + (p.Y / pl * dy) + (p.Z / pl * dz);
            if (d > bestDot) { bestDot = d; best = v; }
        }
        return best;
    }

    [Fact]
    public void Noise_raises_per_vertex_height_variance_versus_envelope_only()
    {
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();

        // Same elevations through two instances: one with the seeded peaks, one with noise disabled.
        // A flat envelope (all-zero elevation) is the cleanest probe — envelope-only positions all sit at
        // radius 1.0 (zero variance), so any height spread is purely the peaks layer.
        var elevations = new double[snapshot.CellCount];

        var withNoise = new GlobePlateSurfaces(snapshot); // production NoiseParams (peaks ON)
        var noNoise = new GlobePlateSurfaces(snapshot, noise: NoNoise);

        var capN = withNoise.BuildSurfaces(elevations, exaggeration: 0.00012);
        var capE = noNoise.BuildSurfaces(elevations, exaggeration: 0.00012);

        double VarOfRadii(IReadOnlyList<PlateCap> caps)
        {
            var radii = caps.SelectMany(c => c.Surface.Positions.Select(Radius)).ToArray();
            double mean = radii.Average();
            return radii.Sum(r => (r - mean) * (r - mean)) / radii.Length;
        }

        double varEnvelope = VarOfRadii(capE);
        double varWithNoise = VarOfRadii(capN);

        Assert.Equal(0.0, varEnvelope, 12);                  // flat envelope -> no spread
        Assert.True(varWithNoise > varEnvelope,              // peaks add per-vertex height spread
            $"peaks must raise height variance: withNoise={varWithNoise:E3} !> envelope={varEnvelope:E3}");
        Assert.True(varWithNoise > 0.0);
    }

    [Fact]
    public void Watertight_coincident_boundary_vertices_across_two_caps_get_equal_final_height()
    {
        var (snap, shared) = TwoPlatesSharingABoundaryCorner();

        // Uniform elevation: the per-plate envelope MEAN is identical everywhere, so the only thing that
        // could crack the shared corner between the two caps is the noise. Sampling noise on the shared
        // BASE position makes it identical in both caps -> the coincident boundary vertex matches exactly.
        const double uniformElevation = 1234.0;
        var surfaces = new GlobePlateSurfaces(snap); // peaks ON (production params)
        var elevations = new[] { uniformElevation, uniformElevation };

        var caps = surfaces.BuildSurfaces(elevations, exaggeration: 0.00012);
        var cap0 = caps.Single(c => c.PlateId == 0);
        var cap1 = caps.Single(c => c.PlateId == 1);

        int v0 = VertexClosestTo(cap0, shared);
        int v1 = VertexClosestTo(cap1, shared);

        // Same direction (it is the same corner) AND same radius (same envelope + same noise) -> the two
        // caps meet exactly at the shared corner: watertight despite the high-frequency peaks.
        Assert.Equal(Radius(cap0.Surface.Positions[v0]), Radius(cap1.Surface.Positions[v1]), 12);
        var p0 = cap0.Surface.Positions[v0];
        var p1 = cap1.Surface.Positions[v1];
        Assert.Equal(p0.X, p1.X, 12);
        Assert.Equal(p0.Y, p1.Y, 12);
        Assert.Equal(p0.Z, p1.Z, 12);
    }

    [Fact]
    public void Noise_component_is_tick_invariant_only_the_envelope_changes()
    {
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot); // peaks ON

        // Two ticks with DIFFERENT uniform elevations. With a uniform elevation E the per-vertex envelope
        // height is exactly E*exaggeration everywhere, so the noise contribution at vertex v is
        //   noise[v] = radius[v] - 1 - E*exaggeration.
        // Tick-invariance of the (cached) noise means this recovered value matches across the two ticks.
        const double x = 0.00012;
        const double e0 = 0.0;
        const double e1 = 5000.0;

        var caps0 = surfaces.BuildSurfaces(Enumerable.Repeat(e0, snapshot.CellCount).ToArray(), x);
        var caps1 = surfaces.BuildSurfaces(Enumerable.Repeat(e1, snapshot.CellCount).ToArray(), x);

        foreach (var cap0 in caps0)
        {
            var cap1 = caps1.Single(c => c.PlateId == cap0.PlateId);
            Assert.Equal(cap0.Surface.VertexCount, cap1.Surface.VertexCount);
            for (int v = 0; v < cap0.Surface.VertexCount; v++)
            {
                double noise0 = Radius(cap0.Surface.Positions[v]) - 1.0 - (e0 * x);
                double noise1 = Radius(cap1.Surface.Positions[v]) - 1.0 - (e1 * x);
                Assert.Equal(noise0, noise1, 12); // noise rides the plate, unchanged by the tick
            }
        }
    }

    [Fact]
    public void Noise_amplitude_zero_matches_pure_envelope()
    {
        // Sanity: the noise-off instance reproduces exactly the pre-peaks behaviour (pure envelope),
        // so disabling the layer is a true no-op on the geometry.
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var elevations = Enumerable.Range(0, snapshot.CellCount).Select(i => (i % 11) * 100.0 - 500.0).ToArray();

        var noNoise = new GlobePlateSurfaces(snapshot, noise: NoNoise);
        var caps = noNoise.BuildSurfaces(elevations, exaggeration: 0.00012);

        // Every position radius equals 1 + (mean incident elevation)*exaggeration — i.e. no peaks added.
        // We just assert the field is NOT all-equal (envelope varies) yet contains no peak high-frequency
        // term beyond the per-cell means: compare against a second no-noise build (determinism / purity).
        var caps2 = new GlobePlateSurfaces(snapshot, noise: NoNoise).BuildSurfaces(elevations, 0.00012);
        foreach (var c in caps)
        {
            var c2 = caps2.Single(x => x.PlateId == c.PlateId);
            for (int v = 0; v < c.Surface.VertexCount; v++)
                Assert.Equal(Radius(c.Surface.Positions[v]), Radius(c2.Surface.Positions[v]), 12);
        }
    }

    [Fact]
    public void Height_exponent_compresses_the_extreme_to_typical_relief_ratio()
    {
        // The non-linear height profile (look-dev 2026-07-03): displacement = sign(h)*|h|^p * scale.
        // With interiors at ~100 m and orogenic peaks at ~10,000 m, the linear lens renders a 100:1
        // ratio (interior relief invisible at any scale that keeps peaks sane). p = 0.5 must compress
        // that to 10:1 so the limb reads knobbly everywhere while peaks stay proportionate.
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);

        // Plate 0's two cells both at 100 m (its cap sits at a uniform interior height);
        // plate 1's lone cell at 10,000 m (the orogenic extreme).
        var elevations = new double[] { 100.0, 100.0, 10_000.0 };

        var sqrtCaps = surfaces.BuildSurfaces(elevations, exaggeration: 0.0001, heightExponent: 0.5);
        double sqrtInterior = Radius(sqrtCaps.Single(c => c.PlateId == 0).Surface.Positions[0]) - 1.0;
        double sqrtPeak = Radius(sqrtCaps.Single(c => c.PlateId == 1).Surface.Positions[0]) - 1.0;
        Assert.Equal(Math.Sqrt(100.0) * 0.0001, sqrtInterior, 10);
        Assert.Equal(Math.Sqrt(10_000.0) * 0.0001, sqrtPeak, 10);
        Assert.Equal(10.0, sqrtPeak / sqrtInterior, 6);
    }

    [Fact]
    public void Height_exponent_default_is_the_linear_lens()
    {
        // Omitting the exponent must reproduce the historical linear displacement exactly.
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);
        var elevations = new double[] { 250.0, 250.0, -4_000.0 };

        var implicitLinear = surfaces.BuildSurfaces(elevations, exaggeration: 0.00001);
        var explicitLinear = surfaces.BuildSurfaces(elevations, exaggeration: 0.00001, heightExponent: 1.0);

        foreach (var cap in implicitLinear)
        {
            var other = explicitLinear.Single(c => c.PlateId == cap.PlateId);
            for (int v = 0; v < cap.Surface.VertexCount; v++)
                Assert.Equal(Radius(cap.Surface.Positions[v]), Radius(other.Surface.Positions[v]), 12);
        }
    }

    [Fact]
    public void Height_exponent_preserves_sign_for_basins()
    {
        // Basins (negative elevations) must displace INWARD under the profile: sign(h)*|h|^p.
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);
        var elevations = new double[] { -900.0, -900.0, 900.0 };

        var caps = surfaces.BuildSurfaces(elevations, exaggeration: 0.0001, heightExponent: 0.5);
        double basin = Radius(caps.Single(c => c.PlateId == 0).Surface.Positions[0]) - 1.0;
        double peak = Radius(caps.Single(c => c.PlateId == 1).Surface.Positions[0]) - 1.0;

        Assert.True(basin < 0.0, $"basin displaced outward: {basin}");
        Assert.Equal(-peak, basin, 10);
    }

    // === Adaptive midpoint detail resample (Slice 2) ==============================================
    //
    // BuildAdaptiveSurfaces now passes PRE-LENS metres plus a HeightFinalizer (the lens) and a
    // DetailSampler (NoiseRelief.Sample). The adaptive builder resamples the high-frequency noise at
    // the midpoint's base position in pre-lens metres, then applies the lens. The tests below pin
    // that contract: an adaptive midpoint's final displacement must equal the analytic
    // lens(envelope_interp + NoiseRelief.Sample(midPos, peaks)), the real-snapshot cross-plate seam
    // stays exact under boundary-edge splits, and disabling the noise reproduces the pre-change
    // behaviour byte-for-byte.

    [Fact]
    public void BuildAdaptiveSurfaces_MidpointFinalDisplacementMatchesAnalyticLensOfResampledDetail()
    {
        // Two-plate snapshot, peaks ON. Plate 0's two faces share edge (v0,v2); with a threshold that
        // forces that edge to split, the midpoint lands at the normalized (v0+v2) direction. Its
        // FINAL radius must equal 1 + lens(envelope_mid + NoiseRelief.Sample(midPos, _peaks)), where
        // envelope_mid is the mean of the endpoint envelope metres (both endpoints are incident to
        // BOTH faces, so each endpoint envelope = (elev[0] + elev[1]) / 2 = 500), and midPos is the
        // BASE midpoint direction (normalize(v0 + v2)) — the exact position the builder samples at.
        var snap = TwoPlateSnapshot();
        var surfaces = new GlobePlateSurfaces(snap); // peaks ON (DefaultPeaks)
        var elevations = new double[] { 0.0, 1000.0, 0.0 };
        const double exaggeration = 0.00012;

        var caps = surfaces.BuildAdaptiveSurfaces(
            elevations,
            exaggeration,
            new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 1e-5));
        var plate0 = caps.Single(c => c.PlateId == 0);

        // Find a midpoint vertex (an appended one, past the base vertex count).
        var fixedCaps = surfaces.BuildSurfaces(elevations, exaggeration);
        int baseVertCount = fixedCaps.Single(c => c.PlateId == 0).Surface.VertexCount;
        var midIndices = Enumerable.Range(baseVertCount, plate0.Surface.VertexCount - baseVertCount).ToArray();
        Assert.NotEmpty(midIndices);

        // The shared edge is (v0, v2) from TwoPlateSnapshot (LocalTriangles 0,1,2 / 0,2,3). The base
        // midpoint direction is normalize(v0 + v2) — exactly what the builder passes to DetailSampler.
        var v0 = new CartesianPoint3(0, 0, 1);
        var v2 = new CartesianPoint3(0, 1, 1);
        var sumPos = new CartesianPoint3(v0.X + v2.X, v0.Y + v2.Y, v0.Z + v2.Z);
        double sumLen = Math.Sqrt(sumPos.X * sumPos.X + sumPos.Y * sumPos.Y + sumPos.Z * sumPos.Z);
        var midUnit = new CartesianPoint3(sumPos.X / sumLen, sumPos.Y / sumLen, sumPos.Z / sumLen);
        double envelopeMid = (elevations[0] + elevations[1]) * 0.5; // both endpoints see both cells
        double detailMid = NoiseRelief.Sample(midUnit, GlobePlateSurfaces.DefaultPeaks);
        double metres = envelopeMid + detailMid;
        double expectedRadius = 1.0 + (metres * exaggeration); // linear lens (default heightExponent)

        // Target the midpoint of the SHARED edge (0,2) via provenance — only its endpoints are both
        // incident to both faces, so only it has envelope_mid = (elev[0]+elev[1])/2. Other midpoints
        // on the same cap have endpoints incident to a single face and different envelopes.
        var sharedMidV = Enumerable.Range(0, plate0.VertexProvenance!.Length)
            .Single(i => plate0.VertexProvenance[i] is VertexProvenance.Midpoint mp
                         && ((mp.EndpointA == 0 && mp.EndpointB == 2)
                             || (mp.EndpointA == 2 && mp.EndpointB == 0)));
        var pShared = plate0.Surface.Positions[sharedMidV];
        double rShared = Math.Sqrt(pShared.X * pShared.X + pShared.Y * pShared.Y + pShared.Z * pShared.Z);
        Assert.Equal(expectedRadius, rShared, 9);
    }

    [Fact]
    public void BuildAdaptiveSurfaces_CustomDetailSamplerResamplesMidpointDetail()
    {
        var snap = TwoPlateSnapshot();
        static double Detail(CartesianPoint3 pos) => (pos.X * 100.0) + (pos.Y * 250.0) + (pos.Z * 500.0);
        var surfaces = new GlobePlateSurfaces(snap, noise: NoNoise, detailSampler: Detail);
        var elevations = new double[] { 0.0, 1000.0, 0.0 };
        const double exaggeration = 0.00012;

        var caps = surfaces.BuildAdaptiveSurfaces(
            elevations,
            exaggeration,
            new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 1e-5));
        var plate0 = caps.Single(c => c.PlateId == 0);

        var v0 = new CartesianPoint3(0, 0, 1);
        var v2 = new CartesianPoint3(0, 1, 1);
        var sumPos = new CartesianPoint3(v0.X + v2.X, v0.Y + v2.Y, v0.Z + v2.Z);
        double sumLen = Radius(sumPos);
        var midUnit = new CartesianPoint3(sumPos.X / sumLen, sumPos.Y / sumLen, sumPos.Z / sumLen);
        double envelopeMid = (elevations[0] + elevations[1]) * 0.5;
        double expectedRadius = 1.0 + ((envelopeMid + Detail(midUnit)) * exaggeration);

        var sharedMidV = Enumerable.Range(0, plate0.VertexProvenance!.Length)
            .Single(i => plate0.VertexProvenance[i] is VertexProvenance.Midpoint mp
                         && ((mp.EndpointA == 0 && mp.EndpointB == 2)
                             || (mp.EndpointA == 2 && mp.EndpointB == 0)));
        var pShared = plate0.Surface.Positions[sharedMidV];
        Assert.Equal(expectedRadius, Radius(pShared), 9);
    }

    [Fact]
    public void BuildAdaptiveSurfaces_RealSnapshotCrossPlateBoundaryVerticesMatchExactlyUnderBoundarySplits()
    {
        // Mirror Binder_regime_nonuniform_elevation... but through the ADAPTIVE path with a threshold
        // that forces boundary-adjacent edges to split. Every coincident cross-plate boundary vertex
        // (originals AND midpoints) must still match exactly across caps: the DetailSampler is a pure
        // function of the shared base position, so two caps sampling at the same boundary position
        // get the same detail, and the HeightFinalizer is a pure function of the raw metres, so the
        // finalized height agrees across caps.
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot); // peaks ON

        var elevations = new double[snapshot.CellCount];
        for (int i = 0; i < elevations.Length; i++)
            elevations[i] = (i % 11) * 100.0 - 500.0; // -500..+400, varies per cell

        var caps = surfaces.BuildAdaptiveSurfaces(
                elevations,
                exaggeration: 0.00012,
                new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 0.0005))
            .OrderBy(c => c.PlateId)
            .ToArray();

        AssertEveryCrossPlateBoundaryVertexMatchesExactly(caps);
    }

    [Fact]
    public void BuildAdaptiveSurfaces_NoiseAmplitudeZeroIsByteIdenticalToPreChangeBehavior()
    {
        // With NoiseRelief amplitude 0, DetailSampler returns 0 everywhere, so the midpoint raw height
        // reduces to the plain arithmetic mean — exactly the pre-change behaviour. The adaptive output
        // must therefore be byte-identical to a reference build that passes POST-LENS heights with NO
        // delegates (the old code path). We construct the reference by calling the cartography
        // adaptive builder directly with post-lens heights and null delegates.
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);

        var elevations = new double[snapshot.CellCount];
        for (int i = 0; i < elevations.Length; i++)
            elevations[i] = (i % 11) * 100.0 - 500.0;
        const double exaggeration = 0.00012;

        var adaptiveCaps = surfaces.BuildAdaptiveSurfaces(
            elevations,
            exaggeration,
            new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 0.0005))
            .OrderBy(c => c.PlateId)
            .ToArray();

        // Reference: the OLD code path — post-lens heights, no delegates. Reproduce by calling the
        // cartography AdaptiveGlobeSurfaceBuilder directly with the same post-lens per-vertex heights
        // the fixed-path BuildSurfaces would compute, and null delegates.
        var referenceBuilder = new AdaptiveGlobeSurfaceBuilder();
        var plateVertexHeights = typeof(GlobePlateSurfaces)
            .GetMethod("BuildPlateVertexHeights", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(surfaces, new object[] { elevations, exaggeration, 1.0, double.PositiveInfinity }) as double[][];
        Assert.NotNull(plateVertexHeights);

        var referenceCaps = new PlateCap[surfaces.PlateIds.Count];
        for (int p = 0; p < surfaces.PlateIds.Count; p++)
        {
            // Access the cached plate topology via reflection so we can call the builder with the
            // exact inputs BuildAdaptiveSurfaces would have used pre-change.
            var plateField = typeof(GlobePlateSurfaces)
                .GetField("_plates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var plates = (System.Collections.Generic.IReadOnlyList<object>)plateField!.GetValue(surfaces)!;
            var plate = plates[p];
            var plateType = plate.GetType();
            var localVertices = (CartesianPoint3[])plateType.GetProperty("LocalVertices")!.GetValue(plate)!;
            var localTriangles = (int[])plateType.GetProperty("LocalTriangles")!.GetValue(plate)!;
            var cellIds = (int[])plateType.GetProperty("CellIds")!.GetValue(plate)!;
            var plateId = (int)plateType.GetProperty("PlateId")!.GetValue(plate)!;

            var adaptive = referenceBuilder.BuildAdaptive(
                localVertices,
                localTriangles,
                plateVertexHeights![p],
                new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 0.0005));
            var mappedCellIds = MapSourceTriangleIdsToCellIds(adaptive.SourceTriangleIds, cellIds);
            referenceCaps[p] = new PlateCap(plateId, mappedCellIds, adaptive.Surface, adaptive.VertexProvenance);
        }
        referenceCaps = referenceCaps.OrderBy(c => c.PlateId).ToArray();

        Assert.Equal(referenceCaps.Length, adaptiveCaps.Length);
        for (int p = 0; p < referenceCaps.Length; p++)
        {
            Assert.Equal(referenceCaps[p].PlateId, adaptiveCaps[p].PlateId);
            Assert.Equal(referenceCaps[p].Surface.VertexCount, adaptiveCaps[p].Surface.VertexCount);
            Assert.Equal(referenceCaps[p].Surface.TriangleCount, adaptiveCaps[p].Surface.TriangleCount);
            for (int v = 0; v < referenceCaps[p].Surface.VertexCount; v++)
            {
                Assert.Equal(referenceCaps[p].Surface.Positions[v].X, adaptiveCaps[p].Surface.Positions[v].X, 12);
                Assert.Equal(referenceCaps[p].Surface.Positions[v].Y, adaptiveCaps[p].Surface.Positions[v].Y, 12);
                Assert.Equal(referenceCaps[p].Surface.Positions[v].Z, adaptiveCaps[p].Surface.Positions[v].Z, 12);
            }
        }
    }

    private static int[] MapSourceTriangleIdsToCellIds(int[] sourceTriangleIds, int[] cellIds)
    {
        var mapped = new int[sourceTriangleIds.Length];
        for (int i = 0; i < sourceTriangleIds.Length; i++)
        {
            int source = sourceTriangleIds[i];
            mapped[i] = source >= 0 && source < cellIds.Length ? cellIds[source] : -1;
        }
        return mapped;
    }

    // === Silhouette budget (spec §1) ===============================================================
    //
    // The planet limb is a circle: total radial displacement (post-lens, post-amplification) is
    // clamped to a cap in unit-radius units. The clamp is a PURE function of the finalized height:
    //   displacement = sign(lens(m)) * min(|lens(m)|, cap)
    // applied identically in the fixed path AND inside the adaptive path's HeightFinalizer. Because
    // it is pure, shared corners see identical inputs -> identical clamped outputs -> seams stay
    // watertight. cap = +inf reproduces today's behaviour byte-identically.

    [Fact]
    public void BuildSurfaces_ClampsFinalDisplacementToCapForPeaks()
    {
        // Plate 1 is a single face (cell 2). With a uniform elevation E and exaggeration X, every
        // corner's finalized displacement is E*X. A cap smaller than E*X must clamp the radius to
        // 1 + sign(E)*cap.
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);
        const double e = 1000.0;
        const double x = 0.00012;
        const double unclamped = e * x;            // 0.12 — far above the 0.005 planet cap
        const double cap = 0.005;

        var plate1 = surfaces.BuildSurfaces(
                new double[] { 0, 0, e },
                exaggeration: x,
                maxDisplacementUnitRadius: cap)
            .Single(c => c.PlateId == 1);

        foreach (var p in plate1.Surface.Positions)
        {
            double r = Radius(p);
            Assert.Equal(1.0 + cap, r, 9);        // clamped exactly to +cap
        }

        Assert.True(cap < unclamped, "fixture must actually exercise the clamp");
    }

    [Fact]
    public void BuildSurfaces_ClampPreservesSignForBasins()
    {
        // Basins (negative elevations) displace INWARD; the clamp must preserve the sign so a deep
        // basin still reads as a depression, just capped: radius = 1 - cap (not 1 + cap).
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);
        const double e = -1000.0;
        const double x = 0.00012;
        const double cap = 0.005;

        var plate1 = surfaces.BuildSurfaces(
                new double[] { 0, 0, e },
                exaggeration: x,
                maxDisplacementUnitRadius: cap)
            .Single(c => c.PlateId == 1);

        foreach (var p in plate1.Surface.Positions)
        {
            double r = Radius(p);
            Assert.Equal(1.0 - cap, r, 9);        // sign preserved: inward clamp
        }
    }

    [Fact]
    public void BuildSurfaces_ClampPreservesSignForBasinsUnderNonLinearLens()
    {
        // Same sign-preservation check through the non-linear lens: sign(m)*|m|^p * x, clamped.
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);
        const double e = -2_500.0;
        const double x = 0.0001;
        const double p = 0.5;
        const double cap = 0.004;
        double unclamped = Math.Sign(e) * Math.Pow(Math.Abs(e), p) * x;  // -0.005, |.| > cap

        var plate1 = surfaces.BuildSurfaces(
                new double[] { 0, 0, e },
                exaggeration: x,
                heightExponent: p,
                maxDisplacementUnitRadius: cap)
            .Single(c => c.PlateId == 1);

        Assert.True(unclamped < -cap, "fixture must exceed the cap on the negative side");
        foreach (var pos in plate1.Surface.Positions)
        {
            double r = Radius(pos);
            Assert.Equal(1.0 - cap, r, 9);        // inward clamp under the non-linear lens
        }
    }

    [Fact]
    public void BuildSurfaces_FiniteCapKeepsCrossPlateSeamsWatertight()
    {
        // The clamp is a pure function of the finalized height, so two caps that share a boundary
        // corner (identical finalized height pre-clamp) clamp to identical radii — the seam stays
        // watertight under a finite cap. Use the real frequency-3 snapshot so cross-plate boundary
        // vertices actually exist.
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);

        var elevations = new double[snapshot.CellCount];
        for (int i = 0; i < elevations.Length; i++)
            elevations[i] = (i % 11) * 100.0 - 500.0;

        // A cap tight enough that some boundary vertices would exceed it.
        const double cap = 0.004;
        var caps = surfaces.BuildSurfaces(
                elevations,
                exaggeration: 0.00012,
                maxDisplacementUnitRadius: cap)
            .OrderBy(c => c.PlateId)
            .ToArray();

        AssertEveryCrossPlateBoundaryVertexMatchesExactly(caps);
    }

    [Fact]
    public void BuildAdaptiveSurfaces_FiniteCapKeepsCrossPlateSeamsWatertight()
    {
        // Same watertight property through the adaptive path: the cap lives inside HeightFinalizer,
        // which is pure, so midpoints shared across plates clamp identically.
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);

        var elevations = new double[snapshot.CellCount];
        for (int i = 0; i < elevations.Length; i++)
            elevations[i] = (i % 11) * 100.0 - 500.0;

        const double cap = 0.004;
        var caps = surfaces.BuildAdaptiveSurfaces(
                elevations,
                exaggeration: 0.00012,
                options: new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 0.0005),
                maxDisplacementUnitRadius: cap)
            .OrderBy(c => c.PlateId)
            .ToArray();

        AssertEveryCrossPlateBoundaryVertexMatchesExactly(caps);
    }

    [Fact]
    public void BuildSurfaces_InfiniteCapReproducesCurrentOutputsByteIdentically()
    {
        // cap = +inf must be a true no-op: every position matches the capless build byte-identically.
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);

        var elevations = new double[snapshot.CellCount];
        for (int i = 0; i < elevations.Length; i++)
            elevations[i] = (i % 11) * 100.0 - 500.0;

        var without = surfaces.BuildSurfaces(elevations, exaggeration: 0.00012)
            .OrderBy(c => c.PlateId).ToArray();
        var withInf = surfaces.BuildSurfaces(
                elevations,
                exaggeration: 0.00012,
                maxDisplacementUnitRadius: double.PositiveInfinity)
            .OrderBy(c => c.PlateId).ToArray();

        Assert.Equal(without.Length, withInf.Length);
        for (int p = 0; p < without.Length; p++)
        {
            Assert.Equal(without[p].Surface.VertexCount, withInf[p].Surface.VertexCount);
            for (int v = 0; v < without[p].Surface.VertexCount; v++)
            {
                Assert.Equal(without[p].Surface.Positions[v].X, withInf[p].Surface.Positions[v].X, 12);
                Assert.Equal(without[p].Surface.Positions[v].Y, withInf[p].Surface.Positions[v].Y, 12);
                Assert.Equal(without[p].Surface.Positions[v].Z, withInf[p].Surface.Positions[v].Z, 12);
            }
        }
    }

    [Fact]
    public void BuildAdaptiveSurfaces_InfiniteCapReproducesCurrentOutputsByteIdentically()
    {
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);

        var elevations = new double[snapshot.CellCount];
        for (int i = 0; i < elevations.Length; i++)
            elevations[i] = (i % 11) * 100.0 - 500.0;

        var without = surfaces.BuildAdaptiveSurfaces(
                elevations,
                exaggeration: 0.00012,
                options: new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 0.0005))
            .OrderBy(c => c.PlateId).ToArray();
        var withInf = surfaces.BuildAdaptiveSurfaces(
                elevations,
                exaggeration: 0.00012,
                options: new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 0.0005),
                maxDisplacementUnitRadius: double.PositiveInfinity)
            .OrderBy(c => c.PlateId).ToArray();

        Assert.Equal(without.Length, withInf.Length);
        for (int p = 0; p < without.Length; p++)
        {
            Assert.Equal(without[p].Surface.VertexCount, withInf[p].Surface.VertexCount);
            Assert.Equal(without[p].Surface.TriangleCount, withInf[p].Surface.TriangleCount);
            for (int v = 0; v < without[p].Surface.VertexCount; v++)
            {
                Assert.Equal(without[p].Surface.Positions[v].X, withInf[p].Surface.Positions[v].X, 12);
                Assert.Equal(without[p].Surface.Positions[v].Y, withInf[p].Surface.Positions[v].Y, 12);
                Assert.Equal(without[p].Surface.Positions[v].Z, withInf[p].Surface.Positions[v].Z, 12);
            }
        }
    }

    [Fact]
    public void BuildAdaptiveSurfaces_ClampsFinalDisplacementToCap()
    {
        // Adaptive path: the cap lives inside HeightFinalizer, so an adaptive midpoint whose
        // unclamped finalized displacement exceeds the cap lands exactly at the cap.
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: NoNoise);
        var elevations = new double[] { 0.0, 1000.0, 0.0 };
        const double exaggeration = 0.00012;
        const double cap = 0.005;
        double unclamped = 1000.0 * exaggeration;   // 0.12 — exceeds cap

        var caps = surfaces.BuildAdaptiveSurfaces(
            elevations,
            exaggeration,
            options: new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 1e-5),
            maxDisplacementUnitRadius: cap);

        Assert.True(unclamped > cap);
        foreach (var cap0 in caps)
        {
            foreach (var p in cap0.Surface.Positions)
            {
                double r = Radius(p);
                double disp = r - 1.0;
                Assert.True(disp <= cap + 1e-9, $"adaptive displacement {disp} exceeds cap {cap}");
                Assert.True(disp >= -cap - 1e-9, $"adaptive displacement {disp} below -cap {-cap}");
            }
        }
    }
}
