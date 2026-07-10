using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FantaSim.App.Ecs.Systems;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Crust;
using FantaSim.App.World.Dto;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Globe;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class WorldCrustMaterializerTests
{
    [Fact]
    public async Task MaterializeAsync_runs_pipeline_from_spec_once_for_requested_snapshots()
    {
        var spec = WorldCrustRunSpec.FromExecutionPayload(new JsonObject
        {
            ["canonicalTick"] = 20L,
            ["snapshotTicks"] = new JsonArray(10L, 20L),
        });

        var materialization = await WorldCrustMaterializer.MaterializeAsync(spec);

        Assert.Same(spec, materialization.Spec);
        Assert.Equal(spec.TessellationFrequency, materialization.Tessellation.Frequency);
        Assert.Equal(spec.Plates, materialization.Result.Plates);
        Assert.Equal(spec.Plates.Count, materialization.Topology.Assignment.Values.Distinct().Count());
        Assert.True(materialization.Topology.Boundaries.Count > 0);
        Assert.True(materialization.Result.StateByTick.ContainsKey(10L));
        Assert.True(materialization.Result.StateByTick.ContainsKey(20L));
        Assert.True(materialization.Result.FeaturesByTick.ContainsKey(20L));
    }

    [Fact]
    public async Task MaterializeAsync_forwards_authored_patch_recipe_to_crust_init()
    {
        // Zero patches ⇒ patch-based init seeds every cell fully oceanic. Under the
        // recipe-based default (Continental(0,1)) the plate-0/1 cells would start at 1.0,
        // so any continental cell here means the authored recipe was NOT forwarded.
        var payload = ExplicitPlatesPayload();
        payload["options"] = new JsonObject
        {
            ["continentalPatches"] = new JsonObject { ["count"] = 0 },
        };

        var materialization = await WorldCrustMaterializer.MaterializeAsync(
            WorldCrustRunSpec.FromExecutionPayload(payload));

        var state = materialization.Result.StateByTick[10L];
        Assert.All(state.Values, cell => Assert.True(cell.ContinentalFraction < 0.5));
    }

    [Fact]
    public async Task MaterializeAsync_without_authored_patches_keeps_recipe_based_init()
    {
        var materialization = await WorldCrustMaterializer.MaterializeAsync(
            WorldCrustRunSpec.FromExecutionPayload(ExplicitPlatesPayload()));

        var state = materialization.Result.StateByTick[10L];
        Assert.Contains(state.Values, cell => cell.ContinentalFraction > 0.5);
    }

    private static JsonObject ExplicitPlatesPayload() => new()
    {
        ["canonicalTick"] = 10L,
        ["frequency"] = 2,
        ["plates"] = new JsonArray(
            new JsonObject { ["id"] = 0, ["lat"] = 0.0, ["lon"] = 0.0 },
            new JsonObject { ["id"] = 1, ["lat"] = 0.0, ["lon"] = 120.0 },
            new JsonObject { ["id"] = 2, ["lat"] = 0.0, ["lon"] = -120.0 }),
    };

    [Fact]
    public async Task BuildSurfaceData_returns_globe_sized_elevations_and_features_at_reference_tick()
    {
        var options = WorldGenerationRenderOptions.Default;
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        long referenceTick = onsetTick + 2_000_000L;

        var spec = WorldCrustRunSpec.ForPresentation(options, onsetTick, referenceTick);
        var materialization = await WorldCrustMaterializer.MaterializeAsync(spec);

        var (globeAtOnset, arcsAtOnset) = BuildOnsetGlobeAndArcs(options, onsetTick);

        var (elevations, features) = materialization.BuildSurfaceData(globeAtOnset, arcsAtOnset, referenceTick, NullLogger.Instance);

        Assert.NotNull(elevations);
        Assert.NotNull(features);
        Assert.Equal(globeAtOnset.CellCount, elevations!.Length);
        Assert.Equal(globeAtOnset.CellCount, features!.Length);
    }

    [Fact]
    public async Task BuildSurfaceData_populates_non_default_features_when_features_are_present()
    {
        var options = WorldGenerationRenderOptions.Default;
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        long referenceTick = onsetTick + 2_000_000L;

        var spec = WorldCrustRunSpec.ForPresentation(options, onsetTick, referenceTick);
        var materialization = await WorldCrustMaterializer.MaterializeAsync(spec);
        Assert.True(materialization.Result.FeaturesByTick.ContainsKey(referenceTick),
            "Expected the pipeline to emit features at the reference tick; if this assumption changes, update the assertion to verify the fallback path.");

        var (globeAtOnset, arcsAtOnset) = BuildOnsetGlobeAndArcs(options, onsetTick);

        var (elevations, features) = materialization.BuildSurfaceData(globeAtOnset, arcsAtOnset, referenceTick, NullLogger.Instance);

        Assert.NotNull(features);
        Assert.Contains(features!, f => f.Kind != default || f.Magnitude != 0.0);
    }

    [Fact]
    public async Task BuildSurfaceData_uses_spec_hydrosphere_mode_for_elevations()
    {
        var options = WorldGenerationRenderOptions.Default;
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        long referenceTick = onsetTick + 2_000_000L;

        var drySpec = WorldCrustRunSpec.ForPresentation(options, onsetTick, referenceTick);
        var dryMaterialization = await WorldCrustMaterializer.MaterializeAsync(drySpec);
        var oceanicMaterialization = dryMaterialization with
        {
            Spec = drySpec with { HydrosphereMode = CellElevationHydrosphereMode.Present },
        };

        var (globeAtOnset, arcsAtOnset) = BuildOnsetGlobeAndArcs(options, onsetTick);

        var (dryElevations, _) = dryMaterialization.BuildSurfaceData(globeAtOnset, arcsAtOnset, referenceTick, NullLogger.Instance);
        var (oceanicElevations, _) = oceanicMaterialization.BuildSurfaceData(globeAtOnset, arcsAtOnset, referenceTick, NullLogger.Instance);

        Assert.NotNull(dryElevations);
        Assert.NotNull(oceanicElevations);
        Assert.Equal(oceanicElevations!.Length, dryElevations!.Length);
        Assert.True(dryElevations.Zip(oceanicElevations, (dry, oceanic) => dry - oceanic).Min() > 499.0,
            "dry mode should remove the legacy sea-level offset and age-deepening penalty from every crust cell");
    }

    [Fact]
    public async Task BuildBoundarySections_returns_representative_sections_for_available_boundary_kinds()
    {
        var options = WorldGenerationRenderOptions.Default;
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        // 10M ticks = 100 anchor units (ka) past onset. With the 2026-07-07 calibrated drift rate
        // (~5.7x slower than the old 0.02 default; tools/rates report) the full kind vocabulary
        // needs a longer window — at the default seed/frequency this is the first probed tick where
        // Convergent, Divergent, AND Transform arcs all exist (measured: C7/D2/T15), so the three
        // per-kind assertions below stay exact. Arcs are taken at the SAME tick as the state so the
        // section kinds and the sampled crust are coherent.
        long referenceTick = onsetTick + 10_000_000L;

        var spec = WorldCrustRunSpec.ForPresentation(options, onsetTick, referenceTick);
        var materialization = await WorldCrustMaterializer.MaterializeAsync(spec);
        var globeAtOnset = BuildGlobeAtOnset(options, onsetTick);
        var arcsAtReference = BuildReconstructor(options, onsetTick).BuildBoundaryArcsAt(referenceTick);

        var sections = materialization.BuildBoundarySections(globeAtOnset, arcsAtReference, referenceTick, NullLogger.Instance);

        Assert.Contains(sections, section => section.Kind == PlateBoundaryKind.Convergent);
        Assert.Contains(sections, section => section.Kind == PlateBoundaryKind.Divergent);
        Assert.Contains(sections, section => section.Kind == PlateBoundaryKind.Transform);
        Assert.All(sections, section =>
        {
            Assert.NotEmpty(section.Samples);
            Assert.Contains(section.InteriorBands, band => band.Label == "crust");
        });
    }

    [Fact]
    public async Task BuildSurfaceData_returns_null_arrays_for_pre_onset_tick()
    {
        var options = WorldGenerationRenderOptions.Default;
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        long referenceTick = onsetTick + 2_000_000L;

        var spec = WorldCrustRunSpec.ForPresentation(options, onsetTick, referenceTick);
        var materialization = await WorldCrustMaterializer.MaterializeAsync(spec);

        var (globeAtOnset, arcsAtOnset) = BuildOnsetGlobeAndArcs(options, onsetTick);

        var (elevations, features) = materialization.BuildSurfaceData(globeAtOnset, arcsAtOnset, onsetTick - 1L, NullLogger.Instance);

        Assert.Null(elevations);
        Assert.Null(features);
    }

    [Fact]
    public async Task BuildCrustThickness_returns_globe_sized_finite_non_negative_values()
    {
        var options = WorldGenerationRenderOptions.Default;
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        long referenceTick = onsetTick + 2_000_000L;

        var spec = WorldCrustRunSpec.ForPresentation(options, onsetTick, referenceTick);
        var materialization = await WorldCrustMaterializer.MaterializeAsync(spec);

        var globeAtOnset = BuildGlobeAtOnset(options, onsetTick);

        var thickness = materialization.BuildCrustThickness(globeAtOnset, referenceTick, NullLogger.Instance);

        Assert.NotNull(thickness);
        Assert.Equal(globeAtOnset.CellCount, thickness!.Length);
        Assert.All(thickness, v =>
        {
            Assert.True(double.IsFinite(v), "Thickness must be finite.");
            Assert.True(v >= 0.0, "Thickness must be non-negative.");
        });
    }

    [Fact]
    public async Task BuildCrustThickness_returns_null_for_pre_onset_tick()
    {
        var options = WorldGenerationRenderOptions.Default;
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        long referenceTick = onsetTick + 2_000_000L;

        var spec = WorldCrustRunSpec.ForPresentation(options, onsetTick, referenceTick);
        var materialization = await WorldCrustMaterializer.MaterializeAsync(spec);

        var globeAtOnset = BuildGlobeAtOnset(options, onsetTick);

        var thickness = materialization.BuildCrustThickness(globeAtOnset, onsetTick - 1L, NullLogger.Instance);

        Assert.Null(thickness);
    }

    private static (WorldGlobeSnapshot Globe, IReadOnlyList<PlateBoundaryArc> Arcs) BuildOnsetGlobeAndArcs(
        WorldGenerationRenderOptions options, long onsetTick)
    {
        var reconstructor = BuildReconstructor(options, onsetTick);
        return (reconstructor.BuildGlobeAt(onsetTick), reconstructor.BuildBoundaryArcsAt(onsetTick));
    }

    private static WorldGlobeSnapshot BuildGlobeAtOnset(WorldGenerationRenderOptions options, long onsetTick)
    {
        var reconstructor = BuildReconstructor(options, onsetTick);
        return reconstructor.BuildGlobeAt(onsetTick);
    }

    private static GlobeReconstructor BuildReconstructor(WorldGenerationRenderOptions options, long onsetTick)
    {
        var roster = OnsetRoster.Build(options.Seed, onsetTick, options.TessellationFrequency);
        var geosphere = SphereRegimeScheduleDefaults.GeosphereDefault;
        return GlobeReconstructor.FromOnsetRoster(roster, onsetTick, geosphere, options.TessellationFrequency);
    }
}
