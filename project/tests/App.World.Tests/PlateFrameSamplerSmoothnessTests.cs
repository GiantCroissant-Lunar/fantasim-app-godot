using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FantaSim.App.Ecs.Systems;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Crust;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Globe;
using FantaSim.Geosphere.Crust;
using FantaSim.World.Contracts.Units;
using UnifyCell;
using Xunit;
using TopoPlate = FantaSim.Geosphere.Plate.Topology.Plate;

namespace FantaSim.App.World.Tests;

/// <summary>
/// P3 per-tick fraction smoothness: the <see cref="PlateFrameSampler"/> driven by an
/// <see cref="ImportedRotationProvider"/> must produce per-cell continental fractions that drift
/// GRADUALLY across seek ticks — not stay frozen on a multi-mega-tick plateau the way the
/// 5 M-tick crust-snapshot-only updates did. The state-key remap (snapshot onset material keyed
/// at the query tick) is what lets the sampler transport material to arbitrary ticks.
/// </summary>
public sealed class PlateFrameSamplerSmoothnessTests
{
    private const string FourPlateRotText = @"
1 0.0 90.0 0.0 0.0 0
1 4.0 90.0 0.0 6.0 0
1 8.0 90.0 0.0 12.0 0
2 0.0 0.0 90.0 0.0 0
2 4.0 0.0 90.0 10.0 0
2 8.0 0.0 90.0 20.0 0
3 0.0 -45.0 180.0 0.0 0
3 4.0 -45.0 180.0 -8.0 0
3 8.0 -45.0 180.0 -16.0 0
4 0.0 45.0 90.0 0.0 0
4 4.0 45.0 90.0 14.0 0
4 8.0 45.0 90.0 28.0 0
";

    private const int Frequency = 3;
    private const long TickStep = UnitConverter.TicksPerMegaAnnum / 2; // 50k ticks = 0.5 Ma

    [Fact]
    public async Task Imported_rotation_yields_per_tick_fraction_change_no_plateau()
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var tessellation = new GeodesicSphereTessellation(Frequency);
        var roster = OnsetRoster.Build(WorldGenerationRenderOptions.Default.Seed, onsetTick, Frequency);
        var plates = roster.SeedPlatesAt(onsetTick);

        var spec = WorldCrustRunSpec.ForPresentation(
            WorldGenerationRenderOptions.Default, onsetTick, onsetTick + UnitConverter.MegaAnnumToTickDelta(8.0));
        var materialization = await WorldCrustMaterializer.MaterializeAsync(spec);
        var snapshotState = materialization.Result.StateByTick[spec.SnapshotTicks[0]];

        var provider = new ImportedRotationProvider("four-plate", FourPlateRotText, onsetTick);
        var sampler = new PlateFrameSampler(
            tessellation, plates, materialization.Topology, onsetTick, provider);

        var fractionsByTick = new Dictionary<long, IReadOnlyDictionary<int, double>>();
        long duration = UnitConverter.MegaAnnumToTickDelta(4.0);
        for (long offset = 0; offset <= duration; offset += TickStep)
        {
            long tick = onsetTick + offset;
            var stateAtTick = new Dictionary<long, IReadOnlyDictionary<int, CellCrustState>>
            {
                [tick] = snapshotState,
            };
            var sampled = sampler.SampleAt(tick, stateAtTick);
            fractionsByTick[tick] = ContinentalFractionsFromState(sampled);
        }

        var ticks = fractionsByTick.Keys.OrderBy(t => t).ToList();
        Assert.True(ticks.Count >= 5, "Expected at least 5 sample points across the window.");

        int consecutiveChanges = 0;
        for (int i = 1; i < ticks.Count; i++)
        {
            if (FractionsDiffer(fractionsByTick[ticks[i - 1]], fractionsByTick[ticks[i]]))
                consecutiveChanges++;
        }

        Assert.True(consecutiveChanges >= ticks.Count - 2,
            $"Expected fractions to change at nearly every {TickStep}-tick step (per-tick smoothness), " +
            $"but only {consecutiveChanges}/{ticks.Count - 1} consecutive pairs differed. " +
            "This indicates a multi-mega-tick plateau (the snapshot-only defect P3 fixes).");
    }

    [Fact]
    public async Task Imported_rotation_fractions_at_t_and_t_plus_100k_differ()
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var tessellation = new GeodesicSphereTessellation(Frequency);
        var roster = OnsetRoster.Build(WorldGenerationRenderOptions.Default.Seed, onsetTick, Frequency);
        var plates = roster.SeedPlatesAt(onsetTick);

        var spec = WorldCrustRunSpec.ForPresentation(
            WorldGenerationRenderOptions.Default, onsetTick, onsetTick + UnitConverter.MegaAnnumToTickDelta(8.0));
        var materialization = await WorldCrustMaterializer.MaterializeAsync(spec);
        var snapshotState = materialization.Result.StateByTick[spec.SnapshotTicks[0]];

        var provider = new ImportedRotationProvider("four-plate", FourPlateRotText, onsetTick);
        var sampler = new PlateFrameSampler(
            tessellation, plates, materialization.Topology, onsetTick, provider);

        long t1 = onsetTick + UnitConverter.MegaAnnumToTickDelta(2.0);
        long t2 = t1 + UnitConverter.MegaAnnumToTickDelta(1.0);

        var f1 = SampleFractions(sampler, snapshotState, t1);
        var f2 = SampleFractions(sampler, snapshotState, t2);

        Assert.True(FractionsDiffer(f1, f2),
            "Expected the continental fraction field to differ between ticks 1 Ma apart under imported rotation.");
    }

    private static IReadOnlyDictionary<int, double> SampleFractions(
        PlateFrameSampler sampler, IReadOnlyDictionary<int, CellCrustState> snapshotState, long tick)
    {
        var stateAtTick = new Dictionary<long, IReadOnlyDictionary<int, CellCrustState>>
        {
            [tick] = snapshotState,
        };
        return ContinentalFractionsFromState(sampler.SampleAt(tick, stateAtTick));
    }

    private static IReadOnlyDictionary<int, double> ContinentalFractionsFromState(
        IReadOnlyDictionary<int, CellCrustState> state)
    {
        var result = new Dictionary<int, double>(state.Count);
        foreach (var (cellId, s) in state)
            result[cellId] = s.ContinentalFraction;
        return result;
    }

    private static bool FractionsDiffer(
        IReadOnlyDictionary<int, double> a, IReadOnlyDictionary<int, double> b)
    {
        if (a.Count == 0 && b.Count == 0)
            return false;

        var keys = a.Keys.Union(b.Keys);
        foreach (var key in keys)
        {
            double fa = a.TryGetValue(key, out var va) ? va : 0.0;
            double fb = b.TryGetValue(key, out var vb) ? vb : 0.0;
            if (Math.Abs(fa - fb) > 1e-9)
                return true;
        }
        return false;
    }
}
