using System;
using FantaSim.App.Common;
using FantaSim.App.World.Cells;
using FantaSim.App.World.Composition;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Globe;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.World;

public static class CellElevationComposition
{
    public static (CellElevationModel?, WorldGenerationRenderOptions) ComposeCellElevation(HostCompositionContext ctx)
    {
        var log = ctx.LoggerFactory.CreateLogger("HostComposition.CellElevation");
        try
        {
            long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
            var renderOptions = ResolveWorldRenderOptions();
            var roster = OnsetRoster.Build(
                renderOptions.Seed,
                onsetTick,
                renderOptions.TessellationFrequency,
                renderOptions.SpinRateRadiansPerMegaAnnum);
            var schedule = SphereRegimeScheduleDefaults.GeosphereDefault;
            var reconstructor = GlobeReconstructor.FromOnsetRoster(roster, onsetTick, schedule, renderOptions.TessellationFrequency);

            // Build snapshot at onset (the full N-plate mesh) to get TicksPerAnchor for the cadence.
            var snapshot = reconstructor.BuildGlobeAt(onsetTick);

            // Same anchor cadence the world view uses (120 anchors, every 5), so the cell model and
            // the feature scrubber sample the same crust run. Pre-onset ticks get empty states (gated
            // by ShowsPlateFeatures — RunCrustEvolution short-circuits them).
            var snapshotTicks = new System.Collections.Generic.List<long>();
            for (long anchor = 0; anchor <= 120; anchor += 5) snapshotTicks.Add(anchor * snapshot.TicksPerAnchor);

            // Build populates one ECS entity per cell and registers CellElevationSystem into the model's
            // ArchSystemRunner (mirrors how ReduceFieldsSystem registers into EcsWorldActor's runner).
            var cellElevation = CellElevationModel.Build(reconstructor, snapshotTicks, renderOptions.HydrosphereMode);

            // Populate/derive for the onset tick (first active tick — pre-onset would be empty)
            // and report the relief extent C will upload.
            cellElevation.UpdateForTick(onsetTick);
            var elevations = cellElevation.GetElevations();
            double min = double.MaxValue, max = double.MinValue;
            foreach (var e in elevations) { if (e < min) min = e; if (e > max) max = e; }
            log.LogInformation("[Host] Cell elevation: {CellCount} cells derived (onset tick={OnsetTick:N0}), range [{Min:F1}, {Max:F1}]", elevations.Length, onsetTick, min, max);
            return (cellElevation, renderOptions);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[Host] Cell elevation model failed: {Message}", ex.Message);
            return (null, ResolveWorldRenderOptions());
        }
    }

    private static WorldGenerationRenderOptions ResolveWorldRenderOptions()
    {
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            WorldGenerationGraphDefaults.BuildFamily(),
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            SphereRegimeScheduleDefaults.PlateOnsetTick,
            WorldGenerationGraphDefaults.GeosphereSphereId);

        return WorldGenerationRenderOptions.Resolve(source.Graph);
    }
}
