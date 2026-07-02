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
        double secs = MeasurePipeline(frequency: 3);
        // 1280 cells: the current default. The full pipeline (crust evolution + topology + profile) must
        // stay well under a second so the crust-surface-data path is not a frame-alignment bottleneck.
        Assert.True(secs < 3.0, $"frequency 3 pipeline took {secs:F2}s (budget 3.0s)");
    }

    [Fact]
    public void Frequency4_pipeline_completes_within_budget()
    {
        double secs = MeasurePipeline(frequency: 4);
        // 5120 cells: one step up. If this explodes (> ~4× frequency-3 or > ~5s), the default stays at 3
        // (the parameter remains overridable per world). The measured number is reported via the assert message.
        Assert.True(secs < 5.0, $"frequency 4 pipeline took {secs:F2}s (budget 5.0s)");
    }
}
