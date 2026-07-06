using FantaSim.App.World.Composition;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Globe;
using Xunit;
using Xunit.Abstractions;

namespace FantaSim.App.World.Tests;

// TEMPORARY DIAGNOSTIC PROBE (2026-07-06 motion-death trace): measures whether the exact
// engine path Service.BuildPlanetPresentationRuntime uses produces a different cell->plate
// map at onset vs onset+20M ticks, and prints the actual runtime pole rates.
public sealed class MotionProbeTests
{
    private readonly ITestOutputHelper _output;

    public MotionProbeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Probe_reassignment_between_onset_and_window_end()
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var options = WorldGenerationRenderOptions.Default; // Seed 7, freq 4 — same as the app default
        var roster = OnsetRoster.Build(options.Seed, onsetTick, options.TessellationFrequency);
        var reconstructor = GlobeReconstructor.FromOnsetRoster(
            roster, onsetTick, SphereRegimeScheduleDefaults.GeosphereDefault, options.TessellationFrequency);

        var seeds = roster.SeedPlatesAt(onsetTick);
        _output.WriteLine($"plates={seeds.Count} onsetTick={onsetTick}");
        foreach (var p in seeds)
            _output.WriteLine(
                $"plate {p.PlateId}: rate={p.Pole.AngularRate:E3} rad/tick " +
                $"({p.Pole.AngularRate * 100_000:E3} rad/Ma), axis=({p.Pole.Axis.X:F3},{p.Pole.Axis.Y:F3},{p.Pole.Axis.Z:F3})");

        var a = reconstructor.BuildGlobeAt(onsetTick);
        var b = reconstructor.BuildGlobeAt(onsetTick + 20_000_000L);

        int changed = 0;
        for (int i = 0; i < a.Cells.Count; i++)
            if (a.Cells[i].PlateId != b.Cells[i].PlateId) changed++;

        double pct = 100.0 * changed / a.Cells.Count;
        _output.WriteLine($"cells changed plate between t=onset and t=onset+20M: {changed}/{a.Cells.Count} ({pct:F1}%)");

        // Deliberately loose: the probe's job is the printout above; assert only that the build ran.
        Assert.Equal(a.Cells.Count, b.Cells.Count);
    }
}
