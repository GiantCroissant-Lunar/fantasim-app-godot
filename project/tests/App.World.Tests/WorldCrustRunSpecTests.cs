using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using FantaSim.App.Ecs.Systems;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Crust;
using FantaSim.App.World.GenerationGraph;
using FantaSim.World.Contracts.Units;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class WorldCrustRunSpecTests
{
    [Fact]
    public void FromExecutionPayload_preserves_current_default_provider_contract()
    {
        var spec = WorldCrustRunSpec.FromExecutionPayload(new JsonObject());

        Assert.Equal(WorldGenerationRenderOptions.Default.TessellationFrequency, spec.TessellationFrequency);
        Assert.Equal(UnitConverter.MegaAnnumToTickDelta(8.0), spec.EndTick);
        Assert.Equal(0L, spec.StartTick);
        Assert.Equal(0L, spec.RotationReferenceTick);
        Assert.Equal(new[] { spec.EndTick }, spec.SnapshotTicks);
        AssertSetEqual(new[] { 0, 1 }, spec.Recipe.ContinentalPlateIds);
        Assert.Equal(1.0 / UnitConverter.TicksPerMegaAnnum, spec.Rates.OrogenicPerTick, 12);
        Assert.Equal(0.6 / UnitConverter.TicksPerMegaAnnum, spec.Rates.ArcVolcanismPerTick, 12);
        Assert.Equal(CellElevationHydrosphereMode.Absent, spec.HydrosphereMode);
    }

    [Fact]
    public void FromExecutionPayload_default_plates_match_presentation_onset_roster()
    {
        long referenceTick = UnitConverter.MegaAnnumToTickDelta(8.0);
        var spec = WorldCrustRunSpec.FromExecutionPayload(new JsonObject());
        var presentationSpec = WorldCrustRunSpec.ForPresentation(
            WorldGenerationRenderOptions.Default,
            SphereRegimeScheduleDefaults.PlateOnsetTick,
            referenceTick);

        Assert.Equal(presentationSpec.TessellationFrequency, spec.TessellationFrequency);
        Assert.Equal(presentationSpec.Plates.Count, spec.Plates.Count);
        Assert.Equal(presentationSpec.Plates, spec.Plates);
    }

    [Fact]
    public void FromExecutionPayload_merges_nested_options_and_payload_overrides()
    {
        var payload = new JsonObject
        {
            ["options"] = new JsonObject
            {
                ["frequency"] = 2,
                ["durationMegaAnnum"] = 3.0,
                ["rotationReferenceTick"] = 11L,
                ["continentalPlates"] = new JsonArray(2, 4),
            },
            ["canonicalTick"] = 123_456L,
            ["snapshotTicks"] = new JsonArray(100L, 200L),
            ["rotationReferenceTick"] = 99L,
        };

        var spec = WorldCrustRunSpec.FromExecutionPayload(payload);

        Assert.Equal(2, spec.TessellationFrequency);
        Assert.Equal(200L, spec.EndTick);
        Assert.Equal(new[] { 100L, 200L }, spec.SnapshotTicks);
        Assert.Equal(99L, spec.RotationReferenceTick);
        AssertSetEqual(new[] { 2, 4 }, spec.Recipe.ContinentalPlateIds);
    }

    [Fact]
    public void ForPresentation_uses_render_options_onset_roster_and_onset_rotation_reference()
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var options = WorldGenerationRenderOptions.Default;

        var spec = WorldCrustRunSpec.ForPresentation(options, onsetTick, onsetTick);
        var expected = OnsetRoster.Build(options.Seed, onsetTick, options.TessellationFrequency)
            .SeedPlatesAt(onsetTick);

        Assert.Equal(options.Seed, spec.Seed);
        Assert.Equal(options.TessellationFrequency, spec.TessellationFrequency);
        Assert.Equal(0L, spec.StartTick);
        Assert.Equal(onsetTick, spec.EndTick);
        Assert.Equal(onsetTick, spec.RotationReferenceTick);
        Assert.Equal(expected, spec.Plates);
        Assert.Equal(new[] { onsetTick }, spec.SnapshotTicks);
        Assert.Equal(options.BoundaryProfiles, spec.BoundaryProfiles);
        Assert.Equal(options.VerticalExaggeration, spec.VerticalExaggeration);
        Assert.Equal(options.HydrosphereMode, spec.HydrosphereMode);
    }

    [Fact]
    public void ForReconstructor_uses_supplied_plates_ticks_and_default_crust_config()
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var snapshots = new[] { onsetTick + 100_000L, onsetTick + 800_000L };
        var plates = OnsetRoster.Build(worldSeed: 2024, onsetTick: onsetTick, tessellationFrequency: 3)
            .SeedPlatesAt(onsetTick);

        var spec = WorldCrustRunSpec.ForReconstructor(
            tessellationFrequency: 3,
            rotationReferenceTick: onsetTick,
            snapshotTicks: snapshots,
            plates: plates);

        Assert.Equal(3, spec.TessellationFrequency);
        Assert.Equal(0L, spec.StartTick);
        Assert.Equal(snapshots[^1], spec.EndTick);
        Assert.Equal(onsetTick, spec.RotationReferenceTick);
        Assert.Equal(snapshots, spec.SnapshotTicks);
        Assert.Equal(plates, spec.Plates);
        AssertSetEqual(new[] { 0, 1 }, spec.Recipe.ContinentalPlateIds);
        Assert.Equal(1.0 / UnitConverter.TicksPerMegaAnnum, spec.Rates.OrogenicPerTick, 12);
        Assert.Equal(0.6 / UnitConverter.TicksPerMegaAnnum, spec.Rates.ArcVolcanismPerTick, 12);
        Assert.Equal(0.4 / UnitConverter.TicksPerMegaAnnum, spec.Rates.IslandArcVolcanismPerTick, 12);
        Assert.Equal(0.5 / UnitConverter.TicksPerMegaAnnum, spec.Rates.RidgeVolcanismPerTick, 12);
        Assert.Equal(WorldGenerationRenderOptions.DefaultVerticalExaggeration, spec.VerticalExaggeration);
        Assert.Equal(CellElevationHydrosphereMode.Absent, spec.HydrosphereMode);
    }

    private static void AssertSetEqual(IEnumerable<int> expected, IReadOnlySet<int> actual)
        => Assert.True(expected.ToHashSet().SetEquals(actual));
}
