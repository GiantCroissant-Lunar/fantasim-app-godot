using System;
using System.Linq;
using FantaSim.App.World.Rendering;
using FantaSim.Cartography.Shared;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Continental-scale albedo province tint proof (W1, one octave below VertexTintJitter): a LOW-
/// frequency deterministic tint field sampled at each cell's unit-sphere center direction, so large
/// warm/cool-rock regions emerge on the otherwise uniform bare-rock disk. Same doctrine as the vertex
/// jitter — warm only (R >= G >= B, never blue/green), subtle swing so elevation still dominates —
/// but at continental frequency (a handful of cycles across the sphere) instead of per-vertex.
/// </summary>
public sealed class ProvinceTintTests
{
    private static readonly CartesianPoint3[] SampleDirections =
    {
        new(1.0, 0.0, 0.0),
        new(0.0, 1.0, 0.0),
        new(0.0, 0.0, 1.0),
        new(-0.5, 0.5, 0.7071),
        new(0.8165, -0.4082, 0.4082),
        new(-0.7071, -0.7071, 0.0),
    };

    // Representative ramp colors the world view actually emits (WorldTerrainRamp stops + interpolated),
    // produced the same way the host produces them so the invariant is proven over real inputs.
    private static readonly RampColor[] RampColors = WorldTerrainRamp
        .ComputeColors(Enumerable.Range(0, 64).Select(i => (double)(i - 32) * 80.0).ToArray())
        .ToArray();

    [Fact]
    public void Deterministic_same_direction_and_seed_yields_same_tint()
    {
        var dir = new CartesianPoint3(0.6, 0.8, 0.0);
        var baseColor = new RampColor(0.42, 0.28, 0.18);
        var pt = new ProvinceTint(seed: 1337, amplitude: 0.07);

        var a = pt.Apply(dir, baseColor);
        var b = pt.Apply(dir, baseColor);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_seeds_yield_different_tints()
    {
        var dir = new CartesianPoint3(0.6, 0.8, 0.0);
        var baseColor = new RampColor(0.42, 0.28, 0.18);
        var ptA = new ProvinceTint(seed: 1337, amplitude: 0.07);
        var ptB = new ProvinceTint(seed: 9999, amplitude: 0.07);

        var a = ptA.Apply(dir, baseColor);
        var b = ptB.Apply(dir, baseColor);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Warm_tone_invariant_holds_under_modulation_for_all_ramp_colors()
    {
        // Stress the clamp + R>=G>=B enforcement paths with a large amplitude; if the invariant holds
        // here it holds at the subtle production amplitude. Every ramp stop is warm (R>=G>=B); the
        // province tint must keep every result warm and in [0,1] so no cell can read as water/veg.
        var pt = new ProvinceTint(seed: 1337, amplitude: 0.30);

        foreach (var dir in SampleDirections)
        {
            foreach (var baseColor in RampColors)
            {
                var c = pt.Apply(dir, baseColor);
                Assert.True(c.R >= 0.0 && c.G >= 0.0 && c.B >= 0.0,
                    $"province tint produced a negative channel at {dir}: {c}");
                Assert.True(c.R <= 1.0 && c.G <= 1.0 && c.B <= 1.0,
                    $"province tint produced a channel > 1 at {dir}: {c}");
                Assert.True(c.B <= c.G + 1e-9,
                    $"province tint made blue dominate: B {c.B:F4} > G {c.G:F4} at {dir}");
                Assert.True(c.G <= c.R + 1e-9,
                    $"province tint made green dominate red: G {c.G:F4} > R {c.R:F4} at {dir}");
            }
        }
    }

    [Fact]
    public void Warm_Tone_invariant_holds_for_near_grey_bases()
    {
        // Edge case: a near-grey warm base (R==G==B) stresses the enforcement hardest because the
        // warm channel weighting can momentarily invert the ordering before it is repaired.
        var pt = new ProvinceTint(seed: 1337, amplitude: 0.30);
        var greyBases = new[]
        {
            new RampColor(0.5, 0.5, 0.5),
            new RampColor(0.3, 0.3, 0.3),
            new RampColor(0.9, 0.9, 0.9),
            new RampColor(0.06, 0.06, 0.06),
        };

        foreach (var dir in SampleDirections)
        {
            foreach (var baseColor in greyBases)
            {
                var c = pt.Apply(dir, baseColor);
                Assert.True(c.B <= c.G + 1e-9 && c.G <= c.R + 1e-9,
                    $"province tint broke R>=G>=B for near-grey {baseColor} at {dir}: {c}");
            }
        }
    }

    [Fact]
    public void Low_Frequency_nearby_directions_get_near_identical_tints()
    {
        // Continental scale: two directions a tiny angular distance apart sample the SAME low-frequency
        // lattice region, so their province tints are nearly identical. (A high-frequency field like
        // VertexTintJitter would differ measurably at this offset.)
        var pt = new ProvinceTint(seed: 1337, amplitude: 0.07);
        var baseColor = new RampColor(0.42, 0.28, 0.18);

        foreach (var d in SampleDirections)
        {
            var nearby = new CartesianPoint3(d.X + 1e-3, d.Y + 1e-3, d.Z);
            var a = pt.Apply(d, baseColor);
            var b = pt.Apply(nearby, baseColor);

            double diff = ChannelDiff(a, b);
            Assert.True(diff < 0.01,
                $"nearby directions differed by {diff:F4} — field is not low-frequency at {d}");
        }
    }

    [Fact]
    public void Low_Frequency_antipodal_directions_can_differ()
    {
        // Continental scale: antipodes sit in decorrelated noise regions, so provinces on opposite
        // sides of the globe CAN read as different albedo regions (the whole point — large warm/cool
        // provinces the eye can anchor on).
        var pt = new ProvinceTint(seed: 1337, amplitude: 0.07);
        var baseColor = new RampColor(0.42, 0.28, 0.18);

        double maxAntipodalDiff = 0.0;
        foreach (var d in SampleDirections)
        {
            var anti = new CartesianPoint3(-d.X, -d.Y, -d.Z);
            var a = pt.Apply(d, baseColor);
            var b = pt.Apply(anti, baseColor);
            maxAntipodalDiff = Math.Max(maxAntipodalDiff, ChannelDiff(a, b));
        }

        Assert.True(maxAntipodalDiff > 0.02,
            $"antipodal provinces never differed (max diff {maxAntipodalDiff:F4}) — no continental contrast");
    }

    [Fact]
    public void Modulation_never_exceeds_configured_amplitude_for_ramp_colors()
    {
        // Doctrine: subtle swing (±0.05..0.08) so elevation still dominates the read. Verified over
        // the real ramp vocabulary at the production amplitude: no channel moves more than amplitude.
        const double amplitude = 0.07;
        var pt = new ProvinceTint(seed: 1337, amplitude: amplitude);

        foreach (var dir in SampleDirections)
        {
            foreach (var baseColor in RampColors)
            {
                var c = pt.Apply(dir, baseColor);
                Assert.True(Math.Abs(c.R - baseColor.R) <= amplitude + 1e-9,
                    $"R moved {Math.Abs(c.R - baseColor.R):F4} > amp {amplitude} at {dir}");
                Assert.True(Math.Abs(c.G - baseColor.G) <= amplitude + 1e-9,
                    $"G moved {Math.Abs(c.G - baseColor.G):F4} > amp {amplitude} at {dir}");
                Assert.True(Math.Abs(c.B - baseColor.B) <= amplitude + 1e-9,
                    $"B moved {Math.Abs(c.B - baseColor.B):F4} > amp {amplitude} at {dir}");
            }
        }
    }

    [Fact]
    public void Zero_amplitude_returns_base_unchanged()
    {
        var baseColor = new RampColor(0.42, 0.28, 0.18);
        var pt = new ProvinceTint(seed: 1337, amplitude: 0.0);

        foreach (var dir in SampleDirections)
        {
            var c = pt.Apply(dir, baseColor);
            Assert.Equal(baseColor, c);
        }
    }

    private static double ChannelDiff(RampColor a, RampColor b)
        => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
}
