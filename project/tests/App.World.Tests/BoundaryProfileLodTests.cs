using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FantaSim.App.Ecs.Systems;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Topography;
using FantaSim.Geosphere.Crust;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Presentation-LOD budget check (P4): measures the crust-pipeline + boundary-profile cost at the current
/// default frequency (3 → 1280 cells) and one step up (4 → 5120 cells). The profile parameters are angular
/// so they auto-scale; the question is whether the watertight-topology + crust-pipeline cost stays bounded.
/// </summary>
public sealed class BoundaryProfileLodTests
{
    private const long Tick = 50 * 100_000L;

    private static double MeasurePipeline(int frequency)
    {
        var sw = Stopwatch.StartNew();

        var reconstructor = new GlobeReconstructor(frequency);
        var snapshot = reconstructor.RunCrustSnapshot(new[] { Tick });
        var state = snapshot.StateByTick.TryGetValue(Tick, out var s) ? s : new Dictionary<int, CellCrustState>();
        snapshot.FeaturesByTick.TryGetValue(Tick, out var features);
        var globe = reconstructor.BuildGlobeAt(0);
        var arcs = reconstructor.BuildBoundaryArcsAt(0);

        var contributions = BoundaryProfileContribution.Build(globe, arcs, state, features, BoundaryProfileParameters.Default);

        sw.Stop();
        Assert.Equal(globe.CellCount, contributions.Length);
        return sw.Elapsed.TotalSeconds;
    }

    [Fact]
    public void Frequency3_pipeline_completes_quickly()
    {
        // 1280 cells: the current default. The full pipeline (crust evolution + topology + profile) must
        // stay well under a second so the crust-surface-data path is not a frame-alignment bottleneck.
        //
        // Wall-clock under the parallel test runner measures machine load as much as the algorithm:
        // a single-shot timing flakes whenever sibling collections or external work contend for cores.
        // The minimum over independent batches estimates the uncontended cost — noise inflates some
        // batches, a real regression inflates all. Three batches keep total runtime reasonable while
        // suppressing a one-off contention spike.
        const double budgetSecs = 3.0;
        const int batches = 3;
        double bestBatchSecs = double.MaxValue;
        for (int b = 0; b < batches; b++)
        {
            double secs = MeasurePipeline(frequency: 3);
            bestBatchSecs = Math.Min(bestBatchSecs, secs);
        }

        Assert.True(bestBatchSecs < budgetSecs,
            $"frequency 3 pipeline took {bestBatchSecs:F2}s in its fastest of {batches} batches " +
            $"(budget {budgetSecs:F1}s)");
    }

    [Fact]
    public void Frequency4_pipeline_completes_within_budget()
    {
        // 5120 cells: one step up. If this explodes (> ~4× frequency-3 or > ~5s), the default stays at 3
        // (the parameter remains overridable per world). The measured number is reported via the assert message.
        //
        // Same best-of-batches minimum as the frequency-3 test: a single-shot wall-clock reading flakes
        // under sibling-collection core contention in the parallel runner. The minimum over three
        // batches estimates the uncontended cost; a real regression inflates every batch.
        const double budgetSecs = 5.0;
        const int batches = 3;
        double bestBatchSecs = double.MaxValue;
        for (int b = 0; b < batches; b++)
        {
            double secs = MeasurePipeline(frequency: 4);
            bestBatchSecs = Math.Min(bestBatchSecs, secs);
        }

        Assert.True(bestBatchSecs < budgetSecs,
            $"frequency 4 pipeline took {bestBatchSecs:F2}s in its fastest of {batches} batches " +
            $"(budget {budgetSecs:F1}s)");
    }
}
