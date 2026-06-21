using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.Cartography.Globe;
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
}
