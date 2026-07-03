using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Rendering;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Shared;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// T3 proof for the per-vertex color envelope: a shared vertex gets the component-wise AVERAGE of
/// the ramp colors of every cell incident to it, GLOBALLY across plate caps, so cross-plate seams
/// show no color step and the world view reads as smooth Gouraud-shaded terrain instead of chunky
/// per-cell triangles. Mirrors the elevation envelope (<see cref="GlobePlateSurfaces"/>) — same
/// topology, same gather, only the per-face scalar becomes a per-face <see cref="RampColor"/>.
/// </summary>
public sealed class VertexColorEnvelopeTests
{
    // Two triangles sharing an edge: vertex set {0,1,2,3} with faces (0,1,2) and (0,2,3) — so
    // vertices 0 and 2 are incident to BOTH faces, vertices 1 and 3 to exactly one face each.
    private static readonly int[] TwoFaceTris = { 0, 1, 2, 0, 2, 3 };

    private static void AssertEqual(RampColor expected, RampColor actual, int precision = 9)
    {
        Assert.Equal(expected.R, actual.R, precision);
        Assert.Equal(expected.G, actual.G, precision);
        Assert.Equal(expected.B, actual.B, precision);
    }

    [Fact]
    public void GatherVertexColors_shared_vertex_gets_componentwise_average_of_incident_faces()
    {
        // Face 0 = pure red, face 1 = pure green. Shared vertices 0 & 2 see both -> (0.5, 0.5, 0).
        // Vertex 1 sees only face 0 -> red; vertex 3 sees only face 1 -> green.
        var perFace = new[]
        {
            new RampColor(1.0, 0.0, 0.0),
            new RampColor(0.0, 1.0, 0.0),
        };

        var colors = VertexColorEnvelope.GatherVertexColors(vertexCount: 4, TwoFaceTris, perFace);

        AssertEqual(new RampColor(0.5, 0.5, 0.0), colors[0]);
        Assert.Equal(new RampColor(1.0, 0.0, 0.0), colors[1]);
        AssertEqual(new RampColor(0.5, 0.5, 0.0), colors[2]);
        Assert.Equal(new RampColor(0.0, 1.0, 0.0), colors[3]);
    }

    [Fact]
    public void GatherVertexColors_interior_vertex_keeps_its_single_incident_cell_color()
    {
        // Both faces the same color -> every vertex, shared or not, is that color (interior case).
        var perFace = new[]
        {
            new RampColor(0.2, 0.4, 0.6),
            new RampColor(0.2, 0.4, 0.6),
        };

        var colors = VertexColorEnvelope.GatherVertexColors(vertexCount: 4, TwoFaceTris, perFace);

        Assert.All(colors, c => Assert.Equal(new RampColor(0.2, 0.4, 0.6), c));
    }

    [Fact]
    public void GatherVertexColors_three_incident_faces_average_componentwise()
    {
        // Fan around vertex 0: three faces all share vertex 0. (0,1,2),(0,2,3),(0,3,4) -> vertex 0
        // is incident to all three; vertices 1 and 4 to one; vertices 2 and 3 to two each.
        var tris = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4 };
        var perFace = new[]
        {
            new RampColor(0.30, 0.0, 0.0),
            new RampColor(0.0, 0.30, 0.0),
            new RampColor(0.0, 0.0, 0.30),
        };

        var colors = VertexColorEnvelope.GatherVertexColors(vertexCount: 5, tris, perFace);

        // vertex 0 = mean of all three = (0.1, 0.1, 0.1)
        AssertEqual(new RampColor(0.1, 0.1, 0.1), colors[0]);
        // vertex 2 = mean of face 0 and face 1 = (0.15, 0.15, 0)
        AssertEqual(new RampColor(0.15, 0.15, 0.0), colors[2]);
        // vertex 1 = face 0 only
        Assert.Equal(new RampColor(0.30, 0.0, 0.0), colors[1]);
    }

    [Fact]
    public void GatherVertexColors_vertex_referenced_by_no_face_is_black()
    {
        // Vertex 4 is never referenced -> defaults to (0,0,0), matching GatherVertexHeights' 0.0
        // convention for unreferenced vertices.
        var perFace = new[] { new RampColor(1.0, 1.0, 1.0) };
        var tris = new[] { 0, 1, 2 };

        var colors = VertexColorEnvelope.GatherVertexColors(vertexCount: 5, tris, perFace);

        Assert.Equal(new RampColor(0.0, 0.0, 0.0), colors[3]);
        Assert.Equal(new RampColor(0.0, 0.0, 0.0), colors[4]);
        Assert.Equal(new RampColor(1.0, 1.0, 1.0), colors[0]);
    }

    [Fact]
    public void GatherVertexColors_same_inputs_produce_same_outputs_determinism()
    {
        var perFace = new[]
        {
            new RampColor(0.1, 0.2, 0.3),
            new RampColor(0.4, 0.5, 0.6),
        };

        var a = VertexColorEnvelope.GatherVertexColors(4, TwoFaceTris, perFace);
        var b = VertexColorEnvelope.GatherVertexColors(4, TwoFaceTris, perFace);

        Assert.Equal(a, b);
    }

    [Fact]
    public void GatherVertexColors_rejects_mismatched_face_count()
    {
        var perFace = new[] { new RampColor(1, 1, 1) }; // one face but TwoFaceTris encodes two
        Assert.Throws<ArgumentException>(
            () => VertexColorEnvelope.GatherVertexColors(4, TwoFaceTris, perFace));
    }

    [Fact]
    public void GatherVertexColors_rejects_non_multiple_of_three_triangles()
    {
        var perFace = new[] { new RampColor(1, 1, 1) };
        var badTris = new[] { 0, 1 };
        Assert.Throws<ArgumentException>(
            () => VertexColorEnvelope.GatherVertexColors(3, badTris, perFace));
    }

    // === end-to-end through GlobePlateSurfaces.BuildVertexColors ============================

    // Two plates sharing a boundary corner: plate 0's face and plate 1's face both reference the
    // same corner `s`. Reuses the TwoPlatesSharingABoundaryCorner shape from GlobePlateSurfacesTests
    // so the topology is known to dedupe `s` into one global vertex.
    private static (WorldGlobeSnapshot Snap, GlobeVec3 Shared) TwoPlatesSharingABoundaryCorner()
    {
        var s = new GlobeVec3(0f, 1f, 0f);
        var a0 = new GlobeVec3(1f, 1f, 0.2f);
        var a1 = new GlobeVec3(1f, 1f, -0.2f);
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

    private static int VertexClosestTo(PlateCap cap, GlobeVec3 dir)
    {
        double dl = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y + dir.Z * dir.Z);
        double dx = dir.X / dl, dy = dir.Y / dl, dz = dir.Z / dl;
        int best = -1; double bestDot = double.NegativeInfinity;
        for (int v = 0; v < cap.Surface.VertexCount; v++)
        {
            var p = cap.Surface.Positions[v];
            double pl = Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
            double d = (p.X / pl * dx) + (p.Y / pl * dy) + (p.Z / pl * dz);
            if (d > bestDot) { bestDot = d; best = v; }
        }
        return best;
    }

    [Fact]
    public void BuildVertexColors_returns_one_per_local_vertex_array_per_plate()
    {
        var (snap, _) = TwoPlatesSharingABoundaryCorner();
        var surfaces = new GlobePlateSurfaces(snap);

        var perCell = new[]
        {
            new RampColor(1.0, 0.0, 0.0),
            new RampColor(0.0, 1.0, 0.0),
        };

        var perPlate = surfaces.BuildVertexColors(perCell);

        // Two non-empty plates -> two entries, ordered by plate id ascending.
        Assert.Equal(new[] { 0, 1 }, perPlate.Select(p => p.PlateId).OrderBy(x => x).ToArray());

        // Each per-plate array is parallel to that cap's local vertex array (which the surface also
        // indexes). We can't read the local vertex count directly, so check through BuildSurfaces.
        var caps = surfaces.BuildSurfaces(new double[] { 0, 0 }, exaggeration: 1.0)
            .OrderBy(c => c.PlateId).ToArray();
        Assert.Equal(perPlate[0].Colors.Length, caps[0].Surface.VertexCount);
        Assert.Equal(perPlate[1].Colors.Length, caps[1].Surface.VertexCount);
    }

    [Fact]
    public void BuildVertexColors_interior_vertex_keeps_its_cell_color()
    {
        var (snap, _) = TwoPlatesSharingABoundaryCorner();
        var surfaces = new GlobePlateSurfaces(snap);

        // Each plate is a single face; the three corners of each face are incident to that one cell
        // only (the shared corner `s` is incident to BOTH plates' cells -> it is NOT interior). The
        // two NON-shared corners of each face are interior to that plate's single cell.
        var perCell = new[]
        {
            new RampColor(0.8, 0.1, 0.1), // plate 0 / cell 0
            new RampColor(0.1, 0.8, 0.1), // plate 1 / cell 1
        };

        var perPlate = surfaces.BuildVertexColors(perCell)
            .OrderBy(p => p.PlateId).ToArray();
        var caps = surfaces.BuildSurfaces(new double[] { 0, 0 }, exaggeration: 1.0)
            .OrderBy(c => c.PlateId).ToArray();

        // Every local vertex of plate 0 that is NOT the shared corner must be cell 0's color.
        var cap0 = caps[0];
        var shared0 = VertexClosestTo(cap0, new GlobeVec3(0f, 1f, 0f));
        for (int v = 0; v < cap0.Surface.VertexCount; v++)
        {
            if (v == shared0) continue;
            Assert.Equal(perCell[0], perPlate[0].Colors[v]);
        }
    }

    [Fact]
    public void BuildVertexColors_shared_boundary_corner_agrees_across_both_plates()
    {
        // The cross-plate seam test: the shared corner `s` is ONE global vertex, so its color is the
        // mean of BOTH plates' incident cell colors, regardless of which cap reads it. Both caps must
        // therefore report the SAME color at the shared corner — no color step across the seam.
        var (snap, shared) = TwoPlatesSharingABoundaryCorner();
        var surfaces = new GlobePlateSurfaces(snap);

        var perCell = new[]
        {
            new RampColor(1.0, 0.0, 0.0),
            new RampColor(0.0, 0.0, 1.0),
        };

        var perPlate = surfaces.BuildVertexColors(perCell)
            .OrderBy(p => p.PlateId).ToArray();
        var caps = surfaces.BuildSurfaces(new double[] { 0, 0 }, exaggeration: 1.0)
            .OrderBy(c => c.PlateId).ToArray();

        int v0 = VertexClosestTo(caps[0], shared);
        int v1 = VertexClosestTo(caps[1], shared);

        // Both plates see the mean of cell 0 and cell 1 = (0.5, 0, 0.5) at the shared corner.
        AssertEqual(new RampColor(0.5, 0.0, 0.5), perPlate[0].Colors[v0]);
        AssertEqual(new RampColor(0.5, 0.0, 0.5), perPlate[1].Colors[v1]);
        AssertEqual(perPlate[0].Colors[v0], perPlate[1].Colors[v1]);
    }

    [Fact]
    public void BuildVertexColors_is_deterministic_across_calls()
    {
        var (snap, _) = TwoPlatesSharingABoundaryCorner();
        var surfaces = new GlobePlateSurfaces(snap);

        var perCell = new[]
        {
            new RampColor(0.2, 0.3, 0.4),
            new RampColor(0.5, 0.6, 0.7),
        };

        var a = surfaces.BuildVertexColors(perCell).OrderBy(p => p.PlateId).ToArray();
        var b = surfaces.BuildVertexColors(perCell).OrderBy(p => p.PlateId).ToArray();

        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].PlateId, b[i].PlateId);
            Assert.Equal(a[i].Colors, b[i].Colors);
        }
    }

    [Fact]
    public void BuildVertexColors_real_seed_globe_cross_plate_boundary_colors_agree()
    {
        // The real four-plate seed at frequency 3 (1280 cells): every cross-plate coincident
        // boundary vertex must report the SAME color in both caps (no color step across seams),
        // mirroring the elevation watertight proof in GlobePlateSurfacesTests.
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot);

        // Distinct, deterministic per-cell colors so adjacent cells of different plates differ.
        var perCell = new RampColor[snapshot.CellCount];
        for (int c = 0; c < perCell.Length; c++)
            perCell[c] = new RampColor(
                ((c * 37) % 255) / 255.0,
                ((c * 91) % 255) / 255.0,
                ((c * 53) % 255) / 255.0);

        var perPlate = surfaces.BuildVertexColors(perCell)
            .OrderBy(p => p.PlateId).ToArray();
        var caps = surfaces.BuildSurfaces(new double[snapshot.CellCount], exaggeration: 0.00012)
            .OrderBy(c => c.PlateId).ToArray();

        AssertEveryCrossPlateBoundaryColorAgrees(caps, perPlate);
    }

    private static void AssertEveryCrossPlateBoundaryColorAgrees(PlateCap[] caps, PlateVertexColors[] perPlate)
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

        // Group cap-local vertices by their base unit direction, then assert all members of any
        // group spanning more than one plate share the exact same envelope color.
        var groups = new Dictionary<(long, long, long), List<(int PlateId, int LocalVertex, RampColor Color)>>();
        for (int pi = 0; pi < caps.Length; pi++)
        {
            var cap = caps[pi];
            var colors = perPlate[pi].Colors;
            for (int v = 0; v < cap.Surface.VertexCount; v++)
            {
                var key = DirKey(cap.Surface.Positions[v]);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<(int, int, RampColor)>();
                    groups[key] = list;
                }
                list.Add((cap.PlateId, v, colors[v]));
            }
        }

        int crossGroups = 0;
        foreach (var kv in groups)
        {
            var members = kv.Value;
            var plateIds = members.Select(m => m.PlateId).Distinct().ToArray();
            if (plateIds.Length < 2) continue; // interior vertex

            crossGroups++;
            var refColor = members[0].Color;
            foreach (var m in members)
            {
                Assert.Equal(refColor.R, m.Color.R, 9);
                Assert.Equal(refColor.G, m.Color.G, 9);
                Assert.Equal(refColor.B, m.Color.B, 9);
            }
        }

        Assert.True(crossGroups > 0, "expected at least one cross-plate boundary vertex group");
    }
}