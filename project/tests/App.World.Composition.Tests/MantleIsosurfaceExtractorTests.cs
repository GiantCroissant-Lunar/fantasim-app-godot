using System;
using System.Linq;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using Xunit;

namespace App.World.Composition.Tests;

// Mantle x-ray view (M-A): end-to-end extractor tests (arcs -> volumetric field -> shell grid ->
// four marching-cubes isosurfaces with gradient normals). Small grid keeps the tests fast.
public class MantleIsosurfaceExtractorTests
{
    private const long TicksPerMa = 100_000;

    private static GlobeVec3 Unit(double x, double y, double z)
    {
        var len = Math.Sqrt(x * x + y * y + z * z);
        return new GlobeVec3((float)(x / len), (float)(y / len), (float)(z / len));
    }

    private static PlateBoundaryArc[] TrenchAndRidgeArcs() => new[]
    {
        new PlateBoundaryArc(PlateA: 0, PlateB: 1, Kind: PlateBoundaryKind.Convergent,
            Points: new[] { Unit(1, 0, 0), Unit(0.7, 0.7, 0), Unit(0, 1, 0) }),
        new PlateBoundaryArc(PlateA: 1, PlateB: 2, Kind: PlateBoundaryKind.Divergent,
            Points: new[] { Unit(-1, 0, 0), Unit(-0.7, -0.7, 0), Unit(0, -1, 0) }),
    };

    private static MantleViewConfig SmallGrid => new() { GridResolution = 32 };

    // Playhead 30 Ma after onset — a mature slab with real radial extent.
    private static long Tick => 30 * TicksPerMa;

    [Fact]
    public void Extract_ProducesFourDistinctSurfaces()
    {
        var set = MantleIsosurfaceExtractor.Extract(TrenchAndRidgeArcs(), Tick, plateOnsetTick: 0, SmallGrid);

        Assert.Equal(Tick, set.Tick);
        Assert.False(set.ColdOuter.IsEmpty, "cold outer surface should exist for a mature slab");
        Assert.False(set.ColdInner.IsEmpty, "cold inner surface should exist for a mature slab");
        Assert.False(set.WarmOuter.IsEmpty, "warm outer surface should exist (blanket/plumes/ridge)");
        Assert.False(set.WarmInner.IsEmpty, "warm inner surface should exist (blanket/plume cores)");

        // The four are genuinely distinct extractions, not one mesh copied around.
        Assert.False(set.ColdOuter.Vertices.SequenceEqual(set.ColdInner.Vertices));
        Assert.False(set.WarmOuter.Vertices.SequenceEqual(set.WarmInner.Vertices));
        Assert.False(set.ColdOuter.Vertices.SequenceEqual(set.WarmOuter.Vertices));

        // A lower threshold encloses the higher one: the outer surface has at least as many verts.
        Assert.True(set.ColdOuter.Vertices.Length >= set.ColdInner.Vertices.Length,
            "cold outer (lower threshold) should be at least as large as cold inner");
        Assert.True(set.WarmOuter.Vertices.Length >= set.WarmInner.Vertices.Length,
            "warm outer (lower threshold) should be at least as large as warm inner");
    }

    [Fact]
    public void Extract_VerticesStayInsideTheShell_AndArraysAreWellFormed()
    {
        var cfg = SmallGrid;
        var set = MantleIsosurfaceExtractor.Extract(TrenchAndRidgeArcs(), Tick, 0, cfg);

        foreach (var mesh in new[] { set.ColdOuter, set.ColdInner, set.WarmOuter, set.WarmInner })
        {
            Assert.Equal(0, mesh.Vertices.Length % 3);
            Assert.Equal(mesh.Vertices.Length, mesh.Normals.Length);
            Assert.Equal(0, mesh.Triangles.Length % 3);

            int vertexCount = mesh.Vertices.Length / 3;
            foreach (var index in mesh.Triangles)
                Assert.InRange(index, 0, vertexCount - 1);

            // Marching-cubes vertices lie on lattice edges, so the hard geometric guarantee is
            // "within one cell of the shell" (the sampler's fade keeps them inside in practice).
            double cell = 2.0 * cfg.OuterRadius / (cfg.GridResolution - 1);
            for (int i = 0; i < vertexCount; i++)
            {
                double x = mesh.Vertices[3 * i], y = mesh.Vertices[3 * i + 1], z = mesh.Vertices[3 * i + 2];
                double r = Math.Sqrt(x * x + y * y + z * z);
                Assert.InRange(r, cfg.InnerRadius - cell, cfg.OuterRadius + cell);
            }
        }
    }

    [Fact]
    public void Extract_NormalsAreUnitLength_AndFinite()
    {
        var set = MantleIsosurfaceExtractor.Extract(TrenchAndRidgeArcs(), Tick, 0, SmallGrid);

        foreach (var mesh in new[] { set.ColdOuter, set.ColdInner, set.WarmOuter, set.WarmInner })
        {
            int count = mesh.Normals.Length / 3;
            for (int i = 0; i < count; i++)
            {
                double x = mesh.Normals[3 * i], y = mesh.Normals[3 * i + 1], z = mesh.Normals[3 * i + 2];
                double len = Math.Sqrt(x * x + y * y + z * z);
                Assert.False(double.IsNaN(len) || double.IsInfinity(len));
                Assert.InRange(len, 0.999, 1.001);
            }
        }
    }

    [Fact]
    public void Extract_IsDeterministic()
    {
        var a = MantleIsosurfaceExtractor.Extract(TrenchAndRidgeArcs(), Tick, 0, SmallGrid);
        var b = MantleIsosurfaceExtractor.Extract(TrenchAndRidgeArcs(), Tick, 0, SmallGrid);

        AssertMeshEqual(a.ColdOuter, b.ColdOuter);
        AssertMeshEqual(a.ColdInner, b.ColdInner);
        AssertMeshEqual(a.WarmOuter, b.WarmOuter);
        AssertMeshEqual(a.WarmInner, b.WarmInner);
    }

    [Fact]
    public void Extract_EmptyArcs_YieldsWarmOnlyPreOnsetMantle()
    {
        // No boundaries: the engine field has no slabs (nothing cold) but still renders the basal
        // blanket + deterministic plumes (warm) — the pre-onset mantle is a valid world.
        var set = MantleIsosurfaceExtractor.Extract(null, tick: 0, plateOnsetTick: 0, SmallGrid);

        Assert.True(set.ColdOuter.IsEmpty, "no slabs -> no cold surfaces");
        Assert.True(set.ColdInner.IsEmpty, "no slabs -> no cold surfaces");
        Assert.False(set.WarmOuter.IsEmpty, "blanket/plumes should still render pre-onset");
    }

    private static void AssertMeshEqual(MantleIsosurfaceMesh a, MantleIsosurfaceMesh b)
    {
        Assert.True(a.Vertices.SequenceEqual(b.Vertices), "vertices must be bit-identical");
        Assert.True(a.Normals.SequenceEqual(b.Normals), "normals must be bit-identical");
        Assert.True(a.Triangles.SequenceEqual(b.Triangles), "triangles must be identical");
    }
}
