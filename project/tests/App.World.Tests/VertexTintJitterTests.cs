using System;
using FantaSim.App.World.Rendering;
using FantaSim.Cartography.Globe.Core;
using FantaSim.Cartography.Shared;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Per-vertex tint jitter proof (W1, §5c "sub-cell detail"): deterministic from vertex position +
/// world seed, warm (never lets blue dominate), small magnitude so it breaks color banding without
/// rewriting the ramp. Reuses the cartography <see cref="NoiseRelief"/> machinery — no new noise
/// system.
/// </summary>
public sealed class VertexTintJitterTests
{
    private static readonly CartesianPoint3[] SampleVertices =
    {
        new(1.0, 0.0, 0.0),
        new(0.0, 1.0, 0.0),
        new(0.0, 0.0, 1.0),
        new(-0.5, 0.5, 0.7071),
        new(0.8165, -0.4082, 0.4082),
    };

    [Fact]
    public void Deterministic_same_position_and_seed_yields_same_jitter()
    {
        var p = new CartesianPoint3(0.6, 0.8, 0.0);
        var baseColor = new RampColor(0.42, 0.28, 0.18);
        var jp = new VertexTintJitter(seed: 1337, amplitude: 0.05);

        var a = jp.Apply(p, baseColor);
        var b = jp.Apply(p, baseColor);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_seeds_yield_different_jitter()
    {
        var p = new CartesianPoint3(0.6, 0.8, 0.0);
        var baseColor = new RampColor(0.42, 0.28, 0.18);
        var jpA = new VertexTintJitter(seed: 1337, amplitude: 0.10);
        var jpB = new VertexTintJitter(seed: 9999, amplitude: 0.10);

        var a = jpA.Apply(p, baseColor);
        var b = jpB.Apply(p, baseColor);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Jitter_never_lets_blue_dominate()
    {
        var baseColor = new RampColor(0.42, 0.28, 0.18);
        var jp = new VertexTintJitter(seed: 1337, amplitude: 0.12);

        foreach (var v in SampleVertices)
        {
            var c = jp.Apply(v, baseColor);
            Assert.True(c.B <= c.G + 1e-9,
                $"jitter made blue dominate: B {c.B:F3} > G {c.G:F3} at {v}");
            Assert.True(c.G <= c.R + 1e-9,
                $"jitter made green dominate R: G {c.G:F3} > R {c.R:F3} at {v}");
        }
    }

    [Fact]
    public void Jitter_stays_within_small_delta_of_base()
    {
        var baseColor = new RampColor(0.42, 0.28, 0.18);
        const double amplitude = 0.06;
        var jp = new VertexTintJitter(seed: 1337, amplitude: amplitude);

        foreach (var v in SampleVertices)
        {
            var c = jp.Apply(v, baseColor);
            double delta = Math.Abs(c.R - baseColor.R) + Math.Abs(c.G - baseColor.G) + Math.Abs(c.B - baseColor.B);
            Assert.True(delta <= amplitude * 3.0 + 1e-9,
                $"jitter delta {delta:F4} exceeds amplitude*3 {amplitude * 3.0:F4} at {v}");
        }
    }

    [Fact]
    public void Zero_amplitude_returns_base_unchanged()
    {
        var baseColor = new RampColor(0.42, 0.28, 0.18);
        var jp = new VertexTintJitter(seed: 1337, amplitude: 0.0);

        foreach (var v in SampleVertices)
        {
            var c = jp.Apply(v, baseColor);
            Assert.Equal(baseColor, c);
        }
    }

    [Fact]
    public void Jitter_channels_clamp_to_0_1()
    {
        var darkBase = new RampColor(0.01, 0.01, 0.01);
        var brightBase = new RampColor(0.99, 0.99, 0.99);
        var jp = new VertexTintJitter(seed: 1337, amplitude: 0.50);

        foreach (var v in SampleVertices)
        {
            var dark = jp.Apply(v, darkBase);
            var bright = jp.Apply(v, brightBase);
            Assert.True(dark.R >= 0.0 && dark.G >= 0.0 && dark.B >= 0.0,
                $"jitter produced negative channel at {v}: {dark}");
            Assert.True(bright.R <= 1.0 && bright.G <= 1.0 && bright.B <= 1.0,
                $"jitter produced channel > 1 at {v}: {bright}");
        }
    }
}