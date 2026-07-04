using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Rendering;
using FantaSim.Cartography.Globe;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class PlateCapMeshBuilderTests
{
    private static WorldGlobeSnapshot TwoPlateSnapshot()
    {
        var v0 = new GlobeVec3(0f, 0f, 1f);
        var v1 = new GlobeVec3(1f, 0f, 1f);
        var v2 = new GlobeVec3(0f, 1f, 1f);
        var v3 = new GlobeVec3(-1f, 1f, 1f);
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

    private static readonly RampColor MissingTerrainColor = new(0.3, 0.35, 0.28);

    [Fact]
    public void Terrain_mesh_is_flat_shaded_nonindexed_plate_cap_data()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot());
        var cap = surfaces.BuildSurfaces(new double[] { 0.0, 0.0, 0.0 }, exaggeration: 1.0)
            .Single(c => c.PlateId == 0);
        var surface = cap.Surface;
        var vertexColors = Enumerable.Range(0, surface.VertexCount)
            .Select(i => new RampColor(0.1 + i * 0.01, 0.2 + i * 0.01, 0.3 + i * 0.01))
            .ToArray();
        var perCellEmission = new[] { 0.25f, 0.75f, 0.0f };

        var mesh = PlateCapMeshBuilder.BuildTerrain(
            cap,
            new Dictionary<int, RampColor[]> { [cap.PlateId] = vertexColors },
            perCellEmission,
            jitter: null);

        Assert.Equal(PlateCapMeshNormalMode.Flat, mesh.NormalMode);
        Assert.Equal(surface.TriangleCount * 3, mesh.VertexCount);
        Assert.Equal(surface.TriangleCount, mesh.TriangleCount);
        Assert.Equal(mesh.VertexCount * 3, mesh.Positions.Length);
        Assert.Equal(mesh.VertexCount * 3, mesh.Normals.Length);
        Assert.Equal(mesh.VertexCount * 3, mesh.Colors.Length);
        Assert.Equal(mesh.VertexCount * 2, mesh.Uv2.Length);

        for (int t = 0; t < surface.TriangleCount; t++)
        {
            int vertexBase = t * 3;
            int normalBase = vertexBase * 3;
            for (int v = 0; v < 3; v++)
            {
                Assert.Equal((float)surface.FlatNormals[t].X, mesh.Normals[normalBase + (v * 3) + 0]);
                Assert.Equal((float)surface.FlatNormals[t].Y, mesh.Normals[normalBase + (v * 3) + 1]);
                Assert.Equal((float)surface.FlatNormals[t].Z, mesh.Normals[normalBase + (v * 3) + 2]);

                int surfaceVertex = surface.Triangles[(t * 3) + v];
                var expectedColor = vertexColors[surfaceVertex];
                int colorBase = (vertexBase + v) * 3;
                Assert.Equal((float)expectedColor.R, mesh.Colors[colorBase + 0]);
                Assert.Equal((float)expectedColor.G, mesh.Colors[colorBase + 1]);
                Assert.Equal((float)expectedColor.B, mesh.Colors[colorBase + 2]);

                int uvBase = (vertexBase + v) * 2;
                Assert.Equal(perCellEmission[cap.CellIds[t]], mesh.Uv2[uvBase + 0]);
                Assert.Equal(0.0f, mesh.Uv2[uvBase + 1]);
            }
        }
    }

    [Fact]
    public void BuildTerrain_SourceCellFacetColorMode_uses_source_cell_color_for_every_triangle_corner()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: new NoiseParams(Amplitude: 0.0));
        var elevations = new double[] { 0.0, 1000.0, 0.0 };
        var options = new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 100.0);

        var cap = surfaces.BuildAdaptiveSurfaces(elevations, exaggeration: 1.0, options)
            .Single(c => c.PlateId == 0);
        var surface = cap.Surface;
        var smoothedVertexColors = Enumerable.Range(0, surface.VertexCount)
            .Select(i => new RampColor(0.05 + i * 0.01, 0.15 + i * 0.01, 0.25 + i * 0.01))
            .ToArray();
        var perCellColors = new[]
        {
            new RampColor(0.88, 0.82, 0.74),
            new RampColor(0.28, 0.30, 0.32),
            new RampColor(0.10, 0.12, 0.14),
        };

        var mesh = PlateCapMeshBuilder.BuildTerrain(
            cap,
            new Dictionary<int, RampColor[]> { [cap.PlateId] = smoothedVertexColors },
            perCellEmission: new[] { 0f, 0f, 0f },
            jitter: null,
            colorMode: PlateCapMeshColorMode.SourceCellFacet,
            perCellColors: perCellColors);

        for (int t = 0; t < surface.TriangleCount; t++)
        {
            var expected = perCellColors[cap.CellIds[t]];
            for (int v = 0; v < 3; v++)
            {
                int colorBase = ((t * 3) + v) * 3;
                Assert.Equal((float)expected.R, mesh.Colors[colorBase + 0], 5);
                Assert.Equal((float)expected.G, mesh.Colors[colorBase + 1], 5);
                Assert.Equal((float)expected.B, mesh.Colors[colorBase + 2], 5);
            }
        }
    }

    [Fact]
    public void BuildTerrain_SourceCellFacetColorMode_applies_tint_once_per_triangle()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: new NoiseParams(Amplitude: 0.0));
        var cap = surfaces.BuildSurfaces(new double[] { 0.0, 0.0, 0.0 }, exaggeration: 1.0)
            .Single(c => c.PlateId == 0);
        var perCellColors = new[]
        {
            new RampColor(0.55, 0.52, 0.48),
            new RampColor(0.42, 0.40, 0.36),
            new RampColor(0.10, 0.12, 0.14),
        };

        var mesh = PlateCapMeshBuilder.BuildTerrain(
            cap,
            new Dictionary<int, RampColor[]> { [cap.PlateId] = Array.Empty<RampColor>() },
            perCellEmission: new[] { 0f, 0f, 0f },
            jitter: new VertexTintJitter(seed: 1777, amplitude: 0.15),
            colorMode: PlateCapMeshColorMode.SourceCellFacet,
            perCellColors: perCellColors);

        for (int t = 0; t < cap.Surface.TriangleCount; t++)
        {
            int first = t * 9;
            for (int v = 1; v < 3; v++)
            {
                int colorBase = ((t * 3) + v) * 3;
                Assert.Equal(mesh.Colors[first + 0], mesh.Colors[colorBase + 0]);
                Assert.Equal(mesh.Colors[first + 1], mesh.Colors[colorBase + 1]);
                Assert.Equal(mesh.Colors[first + 2], mesh.Colors[colorBase + 2]);
            }
        }
    }

    [Fact]
    public void Plate_identity_mesh_uses_smooth_normals_identity_color_and_zero_emission()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot());
        var cap = surfaces.BuildSurfaces(new double[] { 0.0, 0.0, 0.0 }, exaggeration: 1.0)
            .Single(c => c.PlateId == 1);
        var surface = cap.Surface;

        var mesh = PlateCapMeshBuilder.BuildPlateIdentity(cap);

        Assert.Equal(PlateCapMeshNormalMode.Smooth, mesh.NormalMode);
        Assert.Equal(surface.TriangleCount * 3, mesh.VertexCount);

        var expectedColor = PlateIdentityPalette.ColorFor(cap.PlateId);
        for (int t = 0; t < surface.TriangleCount; t++)
        {
            for (int v = 0; v < 3; v++)
            {
                int vertexIndex = (t * 3) + v;
                int surfaceVertex = surface.Triangles[vertexIndex];
                int normalBase = vertexIndex * 3;
                Assert.Equal((float)surface.SmoothNormals[surfaceVertex].X, mesh.Normals[normalBase + 0]);
                Assert.Equal((float)surface.SmoothNormals[surfaceVertex].Y, mesh.Normals[normalBase + 1]);
                Assert.Equal((float)surface.SmoothNormals[surfaceVertex].Z, mesh.Normals[normalBase + 2]);

                int colorBase = vertexIndex * 3;
                Assert.Equal((float)expectedColor.R, mesh.Colors[colorBase + 0]);
                Assert.Equal((float)expectedColor.G, mesh.Colors[colorBase + 1]);
                Assert.Equal((float)expectedColor.B, mesh.Colors[colorBase + 2]);

                int uvBase = vertexIndex * 2;
                Assert.Equal(0.0f, mesh.Uv2[uvBase + 0]);
                Assert.Equal(0.0f, mesh.Uv2[uvBase + 1]);
            }
        }
    }

    [Fact]
    public void BuildTerrain_smooth_normal_mode_uses_per_vertex_smooth_normals()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot());
        var cap = surfaces.BuildSurfaces(new double[] { 0.0, 0.0, 0.0 }, exaggeration: 1.0)
            .Single(c => c.PlateId == 0);
        var surface = cap.Surface;
        var vertexColors = Enumerable.Range(0, surface.VertexCount)
            .Select(i => new RampColor(0.1 + i * 0.01, 0.2 + i * 0.01, 0.3 + i * 0.01))
            .ToArray();

        var mesh = PlateCapMeshBuilder.BuildTerrain(
            cap,
            new Dictionary<int, RampColor[]> { [cap.PlateId] = vertexColors },
            perCellEmission: new[] { 0f, 0f, 0f },
            jitter: null,
            normalMode: PlateCapMeshNormalMode.Smooth);

        Assert.Equal(PlateCapMeshNormalMode.Smooth, mesh.NormalMode);
        for (int t = 0; t < surface.TriangleCount; t++)
        {
            for (int v = 0; v < 3; v++)
            {
                int meshVertex = (t * 3) + v;
                int surfaceVertex = surface.Triangles[meshVertex];
                int normalBase = meshVertex * 3;
                Assert.Equal((float)surface.SmoothNormals[surfaceVertex].X, mesh.Normals[normalBase + 0]);
                Assert.Equal((float)surface.SmoothNormals[surfaceVertex].Y, mesh.Normals[normalBase + 1]);
                Assert.Equal((float)surface.SmoothNormals[surfaceVertex].Z, mesh.Normals[normalBase + 2]);
            }
        }
    }

    [Fact]
    public void BuildTerrain_flat_normal_mode_uses_per_triangle_flat_normals()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot());
        var cap = surfaces.BuildSurfaces(new double[] { 0.0, 0.0, 0.0 }, exaggeration: 1.0)
            .Single(c => c.PlateId == 0);
        var surface = cap.Surface;
        var vertexColors = Enumerable.Range(0, surface.VertexCount)
            .Select(i => new RampColor(0.1 + i * 0.01, 0.2 + i * 0.01, 0.3 + i * 0.01))
            .ToArray();

        var mesh = PlateCapMeshBuilder.BuildTerrain(
            cap,
            new Dictionary<int, RampColor[]> { [cap.PlateId] = vertexColors },
            perCellEmission: new[] { 0f, 0f, 0f },
            jitter: null,
            normalMode: PlateCapMeshNormalMode.Flat);

        Assert.Equal(PlateCapMeshNormalMode.Flat, mesh.NormalMode);
        for (int t = 0; t < surface.TriangleCount; t++)
        {
            var flatNormal = surface.FlatNormals[t];
            for (int v = 0; v < 3; v++)
            {
                int normalBase = ((t * 3) + v) * 3;
                Assert.Equal((float)flatNormal.X, mesh.Normals[normalBase + 0]);
                Assert.Equal((float)flatNormal.Y, mesh.Normals[normalBase + 1]);
                Assert.Equal((float)flatNormal.Z, mesh.Normals[normalBase + 2]);
            }
        }
    }

    [Fact]
    public void AppWorldRendering_contract_stays_godot_free()
    {
        var referenced = typeof(PlateCapMeshBuilder).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(referenced, r => r.Name == "GodotSharp");
    }

    [Fact]
    public void BuildTerrain_AdaptiveMidpointVerticesGetInterpolatedColorsNotMissingFallback()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: new NoiseParams(Amplitude: 0.0));
        var elevations = new double[] { 0.0, 1000.0, 0.0 };
        var options = new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 100.0);

        var cap = surfaces.BuildAdaptiveSurfaces(elevations, exaggeration: 1.0, options)
            .Single(c => c.PlateId == 0);
        var provenance = cap.VertexProvenance
            ?? throw new InvalidOperationException("adaptive cap should expose vertex provenance");

        // Distinct base colours per base vertex so the midpoint interpolation is observable. Index by
        // base local vertex id (the same indexing BuildVertexColors / ResolveTerrainColor use).
        var baseVertexColors = new[]
        {
            new RampColor(0.10, 0.20, 0.30),
            new RampColor(0.40, 0.50, 0.60),
            new RampColor(0.70, 0.80, 0.90),
            new RampColor(0.05, 0.15, 0.25),
        };
        var perPlate = new Dictionary<int, RampColor[]> { [cap.PlateId] = baseVertexColors };
        var perCellEmission = new[] { 0f, 0f, 0f };

        var mesh = PlateCapMeshBuilder.BuildTerrain(cap, perPlate, perCellEmission, jitter: null);

        // Every generated surface vertex must resolve to a real colour, never the MissingTerrainColor
        // placeholder. Pin that first across the whole mesh, then pin the interpolation per midpoint.
        for (int t = 0; t < cap.Surface.TriangleCount; t++)
        {
            for (int v = 0; v < 3; v++)
            {
                int meshVertex = (t * 3) + v;
                int colorBase = meshVertex * 3;
                var r = mesh.Colors[colorBase + 0];
                var g = mesh.Colors[colorBase + 1];
                var b = mesh.Colors[colorBase + 2];
                Assert.False(
                    r == (float)MissingTerrainColor.R && g == (float)MissingTerrainColor.G && b == (float)MissingTerrainColor.B,
                    $"adaptive vertex {meshVertex} fell back to MissingTerrainColor");
            }
        }

        // For each midpoint vertex, assert the mesh colour equals the mean of the two endpoint base
        // colours (component-wise, matching VertexColorEnvelope / GatherVertexHeights convention).
        for (int sv = 0; sv < cap.Surface.VertexCount; sv++)
        {
            if (provenance[sv] is not VertexProvenance.Midpoint mp)
                continue;

            var expected = new RampColor(
                (baseVertexColors[mp.EndpointA].R + baseVertexColors[mp.EndpointB].R) * 0.5,
                (baseVertexColors[mp.EndpointA].G + baseVertexColors[mp.EndpointB].G) * 0.5,
                (baseVertexColors[mp.EndpointA].B + baseVertexColors[mp.EndpointB].B) * 0.5);

            // The non-indexed mesh duplicates each surface vertex once per incident triangle corner;
            // find any mesh vertex referencing this surface vertex and check it.
            int meshVertex = FindMeshVertexReferencingSurfaceVertex(cap.Surface, sv);
            int colorBase = meshVertex * 3;
            Assert.Equal((float)expected.R, mesh.Colors[colorBase + 0], 5);
            Assert.Equal((float)expected.G, mesh.Colors[colorBase + 1], 5);
            Assert.Equal((float)expected.B, mesh.Colors[colorBase + 2], 5);
        }
    }

    [Fact]
    public void BuildTerrain_AdaptiveOriginalVerticesKeepBaseVertexColor()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: new NoiseParams(Amplitude: 0.0));
        var elevations = new double[] { 0.0, 1000.0, 0.0 };
        var options = new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 100.0);

        var cap = surfaces.BuildAdaptiveSurfaces(elevations, exaggeration: 1.0, options)
            .Single(c => c.PlateId == 0);
        var provenance = cap.VertexProvenance
            ?? throw new InvalidOperationException("adaptive cap should expose vertex provenance");
        var baseVertexColors = new[]
        {
            new RampColor(0.10, 0.20, 0.30),
            new RampColor(0.40, 0.50, 0.60),
            new RampColor(0.70, 0.80, 0.90),
            new RampColor(0.05, 0.15, 0.25),
        };
        var perPlate = new Dictionary<int, RampColor[]> { [cap.PlateId] = baseVertexColors };
        var perCellEmission = new[] { 0f, 0f, 0f };

        var mesh = PlateCapMeshBuilder.BuildTerrain(cap, perPlate, perCellEmission, jitter: null);

        for (int sv = 0; sv < cap.Surface.VertexCount; sv++)
        {
            if (provenance[sv] is not VertexProvenance.Original orig)
                continue;

            int meshVertex = FindMeshVertexReferencingSurfaceVertex(cap.Surface, sv);
            int colorBase = meshVertex * 3;
            Assert.Equal((float)baseVertexColors[orig.SourceIndex].R, mesh.Colors[colorBase + 0], 5);
            Assert.Equal((float)baseVertexColors[orig.SourceIndex].G, mesh.Colors[colorBase + 1], 5);
            Assert.Equal((float)baseVertexColors[orig.SourceIndex].B, mesh.Colors[colorBase + 2], 5);
        }
    }

    [Fact]
    public void BuildTerrain_RecursiveAdaptiveMidpointVerticesResolveInterpolatedColors()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot(), noise: new NoiseParams(Amplitude: 0.0));
        var elevations = new double[] { 0.0, 1000.0, 0.0 };
        var options = new AdaptiveSubdivisionOptions(MaxDepth: 2, EdgeHeightDeltaThreshold: 100.0);

        var cap = surfaces.BuildAdaptiveSurfaces(elevations, exaggeration: 1.0, options)
            .Single(c => c.PlateId == 0);
        var provenance = cap.VertexProvenance
            ?? throw new InvalidOperationException("adaptive cap should expose vertex provenance");
        var baseVertexColors = new[]
        {
            new RampColor(0.10, 0.20, 0.30),
            new RampColor(0.40, 0.50, 0.60),
            new RampColor(0.70, 0.80, 0.90),
            new RampColor(0.05, 0.15, 0.25),
        };
        Assert.Contains(provenance, p =>
            p is VertexProvenance.Midpoint mp
            && (mp.EndpointA >= baseVertexColors.Length || mp.EndpointB >= baseVertexColors.Length));

        var perPlate = new Dictionary<int, RampColor[]> { [cap.PlateId] = baseVertexColors };
        var perCellEmission = new[] { 0f, 0f, 0f };

        var mesh = PlateCapMeshBuilder.BuildTerrain(cap, perPlate, perCellEmission, jitter: null);

        for (int sv = 0; sv < cap.Surface.VertexCount; sv++)
        {
            if (provenance[sv] is not VertexProvenance.Midpoint)
                continue;

            var expected = ResolveExpectedColor(sv, provenance, baseVertexColors);
            int meshVertex = FindMeshVertexReferencingSurfaceVertex(cap.Surface, sv);
            int colorBase = meshVertex * 3;
            Assert.Equal((float)expected.R, mesh.Colors[colorBase + 0], 5);
            Assert.Equal((float)expected.G, mesh.Colors[colorBase + 1], 5);
            Assert.Equal((float)expected.B, mesh.Colors[colorBase + 2], 5);
        }
    }

    [Fact]
    public void BuildTerrain_FixedCapWithoutProvenanceStillFallsBackToMissingColorForOutOfRange()
    {
        var surfaces = new GlobePlateSurfaces(TwoPlateSnapshot());
        var cap = surfaces.BuildSurfaces(new double[] { 0.0, 0.0, 0.0 }, exaggeration: 1.0)
            .Single(c => c.PlateId == 0);
        Assert.Null(cap.VertexProvenance);

        // Pass an empty colour array: every vertex is out of range, so the mesh must fall back to
        // MissingTerrainColor. This pins the fixed-cap path's existing behaviour and proves the
        // provenance path did not regress non-adaptive caps.
        var perPlate = new Dictionary<int, RampColor[]> { [cap.PlateId] = Array.Empty<RampColor>() };
        var perCellEmission = new[] { 0f, 0f, 0f };

        var mesh = PlateCapMeshBuilder.BuildTerrain(cap, perPlate, perCellEmission, jitter: null);

        for (int i = 0; i < mesh.VertexCount; i++)
        {
            int cb = i * 3;
            Assert.Equal((float)MissingTerrainColor.R, mesh.Colors[cb + 0]);
            Assert.Equal((float)MissingTerrainColor.G, mesh.Colors[cb + 1]);
            Assert.Equal((float)MissingTerrainColor.B, mesh.Colors[cb + 2]);
        }
    }

    private static int FindMeshVertexReferencingSurfaceVertex(GlobeSurface surface, int surfaceVertex)
    {
        for (int t = 0; t < surface.TriangleCount; t++)
        {
            for (int v = 0; v < 3; v++)
            {
                int meshVertex = (t * 3) + v;
                if (surface.Triangles[meshVertex] == surfaceVertex)
                    return meshVertex;
            }
        }
        throw new InvalidOperationException($"no mesh vertex references surface vertex {surfaceVertex}");
    }

    private static RampColor ResolveExpectedColor(
        int surfaceVertex,
        VertexProvenance[] provenance,
        RampColor[] baseVertexColors)
    {
        var resolved = TryResolveExpectedColor(surfaceVertex, provenance, baseVertexColors, new HashSet<int>());
        return resolved.HasColor ? resolved.Color : MissingTerrainColor;
    }

    private static (bool HasColor, RampColor Color) TryResolveExpectedColor(
        int surfaceVertex,
        VertexProvenance[] provenance,
        RampColor[] baseVertexColors,
        HashSet<int> visiting)
    {
        if (surfaceVertex < 0 || surfaceVertex >= provenance.Length || !visiting.Add(surfaceVertex))
            return (false, MissingTerrainColor);

        var result = provenance[surfaceVertex] switch
        {
            VertexProvenance.Original orig when orig.SourceIndex >= 0 && orig.SourceIndex < baseVertexColors.Length
                => (true, baseVertexColors[orig.SourceIndex]),
            VertexProvenance.Midpoint mp => Average(
                TryResolveExpectedColor(mp.EndpointA, provenance, baseVertexColors, visiting),
                TryResolveExpectedColor(mp.EndpointB, provenance, baseVertexColors, visiting)),
            _ => (false, MissingTerrainColor),
        };
        visiting.Remove(surfaceVertex);
        return result;
    }

    private static (bool HasColor, RampColor Color) Average(
        (bool HasColor, RampColor Color) a,
        (bool HasColor, RampColor Color) b)
        => (a.HasColor, b.HasColor) switch
        {
            (true, true) => (true, new RampColor(
                (a.Color.R + b.Color.R) * 0.5,
                (a.Color.G + b.Color.G) * 0.5,
                (a.Color.B + b.Color.B) * 0.5)),
            (true, false) => a,
            (false, true) => b,
            _ => (false, MissingTerrainColor),
        };
}
