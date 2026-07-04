using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FantaSim.App.Ecs.Systems;
using FantaSim.App.NodeGraph;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Topography;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class WorldGenerationRenderOptionsTests
{
    [Fact]
    public void Resolve_UsesEffectiveWorldOptionsFromSelectedGraph()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily() with
        {
            GraphOverrides = new[]
            {
                new WorldGenerationGraphScopedOverride(
                    "render_options",
                    WorldGenerationGraphDefaults.GeosphereGraphId,
                    "Render options",
                    new WorldGenerationTickRange(10, 20),
                    0,
                    new[]
                    {
                        new WorldGenerationGraphEdit(
                            "set-param",
                            NodeId: "options",
                            ParamKey: "seed",
                            ParamValue: "1234"),
                        new WorldGenerationGraphEdit(
                            "set-param",
                            NodeId: "options",
                            ParamKey: "frequency",
                            ParamValue: "4"),
                    }),
            },
        };
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 12,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        var options = WorldGenerationRenderOptions.Resolve(source.Graph);

        Assert.Equal(1234, options.Seed);
        Assert.Equal(4, options.TessellationFrequency);
    }

    [Fact]
    public void Default_uses_dry_no_hydrosphere_elevation_mode()
    {
        Assert.Equal(CellElevationHydrosphereMode.Absent, WorldGenerationRenderOptions.Default.HydrosphereMode);
    }

    [Fact]
    public void Default_uses_adaptive_surface_subdivision()
    {
        Assert.Equal(SurfaceSubdivisionMode.Adaptive, WorldGenerationRenderOptions.Default.SurfaceSubdivision);
    }

    [Fact]
    public void Resolve_uses_adaptive_surface_subdivision_from_default_graph()
    {
        var options = WorldGenerationRenderOptions.Resolve(WorldGenerationGraphDefaults.BuildCrustGraph());

        Assert.Equal(SurfaceSubdivisionMode.Adaptive, options.SurfaceSubdivision);
    }

    [Fact]
    public async Task Resolve_reads_hydrosphere_mode_override()
    {
        var source = new WorldGenerationGraphSource(
            "world-generation",
            WorldGenerationGraphDefaults.BuildCrustGraph());
        await source.ApplyEditAsync(new GraphEdit.SetParam("options", "hydrosphereMode", JsonValue.Create("present")));

        var options = WorldGenerationRenderOptions.Resolve(source.Graph);

        Assert.Equal(CellElevationHydrosphereMode.Present, options.HydrosphereMode);
    }

    [Fact]
    public async Task Resolve_reads_adaptive_surface_subdivision_overrides()
    {
        var source = new WorldGenerationGraphSource(
            "world-generation",
            WorldGenerationGraphDefaults.BuildCrustGraph());
        await source.ApplyEditAsync(new GraphEdit.SetParam("options", "surfaceSubdivision", JsonValue.Create("adaptive")));
        await source.ApplyEditAsync(new GraphEdit.SetParam("options", "adaptiveSubdivisionMaxDepth", JsonValue.Create(2)));
        await source.ApplyEditAsync(new GraphEdit.SetParam("options", "adaptiveSubdivisionEdgeHeightDelta", JsonValue.Create(0.02)));
        await source.ApplyEditAsync(new GraphEdit.SetParam("options", "adaptiveSubdivisionFeatureWeightDelta", JsonValue.Create(0.25)));

        var options = WorldGenerationRenderOptions.Resolve(source.Graph);

        Assert.Equal(SurfaceSubdivisionMode.Adaptive, options.SurfaceSubdivision);
        Assert.Equal(2, options.AdaptiveSubdivisionMaxDepth);
        Assert.Equal(0.02, options.AdaptiveSubdivisionEdgeHeightDelta);
        Assert.Equal(0.25, options.AdaptiveSubdivisionFeatureWeightDelta);
    }

    [Fact]
    public void Resolve_FallsBack_WhenGraphHasNoWorldOptionsNode()
    {
        var graph = WorldGenerationGraphDefaults.BuildLayerScopeGraph(
            "geosphere.test-layer",
            "Test Layer",
            "mobile-plate",
            "geosphere.test",
            "field-layer");

        var options = WorldGenerationRenderOptions.Resolve(
            graph,
            new WorldGenerationRenderOptions(Seed: 2024, TessellationFrequency: 3));

        Assert.Equal(2024, options.Seed);
        Assert.Equal(3, options.TessellationFrequency);
    }

    [Fact]
    public async Task Resolve_RejectsNonPositiveTessellationFrequency()
    {
        var source = new WorldGenerationGraphSource(
            "world-generation",
            WorldGenerationGraphDefaults.BuildCrustGraph());
        await source.ApplyEditAsync(new GraphEdit.SetParam("options", "frequency", JsonValue.Create(0)));

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorldGenerationRenderOptions.Resolve(source.Graph));

        Assert.Equal("TessellationFrequency", ex.ParamName);
    }

    [Fact]
    public void Resolve_reads_boundary_profile_overrides()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily() with
        {
            GraphOverrides = new[]
            {
                new WorldGenerationGraphScopedOverride(
                    "render_options",
                    WorldGenerationGraphDefaults.GeosphereGraphId,
                    "Render options",
                    new WorldGenerationTickRange(10, 20),
                    0,
                    new[]
                    {
                        new WorldGenerationGraphEdit("set-param", NodeId: "options", ParamKey: "convergentTrenchDepth", ParamValue: "-1234.5"),
                        new WorldGenerationGraphEdit("set-param", NodeId: "options", ParamKey: "divergentSwellHeight", ParamValue: "999"),
                        new WorldGenerationGraphEdit("set-param", NodeId: "options", ParamKey: "transformScarpAmplitude", ParamValue: "42.25"),
                    }),
            },
        };
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 12,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        var options = WorldGenerationRenderOptions.Resolve(source.Graph);

        Assert.Equal(-1234.5, options.BoundaryProfiles.ConvergentTrenchDepth);
        Assert.Equal(999.0, options.BoundaryProfiles.DivergentSwellHeight);
        Assert.Equal(42.25, options.BoundaryProfiles.TransformScarpAmplitude);
        Assert.Equal(BoundaryProfileParameters.Default.ConvergentArcHeight, options.BoundaryProfiles.ConvergentArcHeight);
    }

    [Fact]
    public void Resolve_defaults_boundary_profiles_to_Default_when_absent()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily() with
        {
            GraphOverrides = new[]
            {
                new WorldGenerationGraphScopedOverride(
                    "render_options",
                    WorldGenerationGraphDefaults.GeosphereGraphId,
                    "Render options",
                    new WorldGenerationTickRange(10, 20),
                    0,
                    new[]
                    {
                        new WorldGenerationGraphEdit("set-param", NodeId: "options", ParamKey: "seed", ParamValue: "2024"),
                    }),
            },
        };
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 12,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        var options = WorldGenerationRenderOptions.Resolve(source.Graph);

        Assert.Equal(2024, options.Seed);
        Assert.Equal(BoundaryProfileParameters.Default, options.BoundaryProfiles);
    }
}
