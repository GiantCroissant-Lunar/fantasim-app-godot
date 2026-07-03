using System;
using System.Linq;
using FantaSim.App.World.Rendering;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// World-view terrain ramp proof (W1, §5c): per-cell elevation maps to a bare-rock product ramp
/// (dark basalt lowlands -> rust/ochre mid plains -> pale rock highlands). This is the PRODUCT face,
/// distinct from the crust-diagnostic hypsometric palette: warmer rust/ochre mid tones, no blue
/// anywhere (waterless world doctrine), luminance strictly ascending so darker-is-lower reads under
/// the half-Lambert light.
/// </summary>
public sealed class WorldTerrainRampTests
{
    private static readonly RampColor DarkBasalt = new(0.06, 0.05, 0.045);
    private static readonly RampColor PaleHighland = new(0.82, 0.78, 0.70);

    [Fact]
    public void ComputeColors_returns_one_color_per_cell()
    {
        var elevations = new double[] { -1000, -500, 0, 500, 1000 };

        var colors = WorldTerrainRamp.ComputeColors(elevations);

        Assert.Equal(elevations.Length, colors.Count);
    }

    [Fact]
    public void Empty_input_returns_empty()
    {
        var colors = WorldTerrainRamp.ComputeColors(Array.Empty<double>());
        Assert.Empty(colors);
    }

    [Fact]
    public void Lowest_elevation_maps_near_dark_basalt_and_highest_near_pale_highland()
    {
        var elevations = new double[] { -2000, 0, 2000 };

        var colors = WorldTerrainRamp.ComputeColors(elevations);
        var ordered = colors.ToArray();

        AssertLuma(ordered[0], DarkBasalt, 0.08);
        AssertLuma(ordered[2], PaleHighland, 0.08);
    }

    [Fact]
    public void Ordering_is_monotonic_low_to_high_luminance()
    {
        var elevations = new double[] { -1500, -800, -100, 400, 1200, 2500 };

        var colors = WorldTerrainRamp.ComputeColors(elevations);

        var luma = colors.Select(Luma).ToArray();
        for (int i = 1; i < luma.Length; i++)
            Assert.True(luma[i] >= luma[i - 1] - 1e-6,
                $"luma not monotonic at {i}: {luma[i]} < {luma[i - 1]}");
    }

    [Fact]
    public void World_ramp_never_lets_blue_dominate()
    {
        var elevations = Enumerable.Range(0, 64).Select(i => (double)i).ToArray();

        var colors = WorldTerrainRamp.ComputeColors(elevations);

        Assert.All(colors, c =>
        {
            Assert.True(c.B <= c.G + 1e-9,
                $"blue dominates (B {c.B:F3} > G {c.G:F3}) — stop reads as water, not bare rock");
            Assert.True(c.G <= c.R + 1e-9,
                $"green dominates R (G {c.G:F3} > R {c.R:F3}) — stop is not a warm rock tone");
        });
    }

    [Fact]
    public void All_equal_elevations_does_not_throw_and_returns_uniform()
    {
        var elevations = new double[] { 42.0, 42.0, 42.0, 42.0 };

        var colors = WorldTerrainRamp.ComputeColors(elevations);

        Assert.Equal(4, colors.Count);
        var first = colors[0];
        Assert.All(colors, c => Assert.Equal(first, c));
    }

    [Fact]
    public void Single_extreme_outlier_does_not_compress_the_rest()
    {
        var bulk = Enumerable.Range(0, 100).Select(i => i * 0.1).ToArray();
        var elevations = bulk.Append(50_000.0).ToArray();

        var colors = WorldTerrainRamp.ComputeColors(elevations);

        var bulkMaxLuma = colors.Take(100).Select(Luma).Max();
        var darkBasaltLuma = Luma(DarkBasalt);
        Assert.True(bulkMaxLuma > darkBasaltLuma + 0.10,
            $"outlier compressed the bulk: max bulk-luma {bulkMaxLuma:F3} not clearly above dark basalt {darkBasaltLuma:F3}");
    }

    [Fact]
    public void Low_relief_world_uses_full_ramp_not_a_single_band()
    {
        var elevations = new double[] { -10, -7, -3, 0, 2, 5, 8, 10 };

        var colors = WorldTerrainRamp.ComputeColors(elevations);

        var luma = colors.Select(Luma).ToArray();
        Assert.True(luma[^1] - luma[0] > 0.20,
            $"low-relief world did not spread the ramp: luma range [{luma[0]:F3}, {luma[^1]:F3}] too narrow");
        var midRampLuma = Luma(new RampColor(0.40, 0.28, 0.18));
        Assert.True(luma[0] < midRampLuma - 0.05,
            $"lowest low-relief cell landed in the mid band, luma {luma[0]:F3} — expected dark basalt");
    }

    [Fact]
    public void World_ramp_is_distinct_from_hypsometric_crust_ramp()
    {
        // The world ramp must read as a DIFFERENT palette from the crust-diagnostic hypsometric ramp:
        // the mid-plains should carry warmer rust/ochre (more red, less grey) than the crust ramp's
        // basalt-brown. A mid-elevation cell must produce visibly different colors under the two ramps.
        var elevations = new double[] { -500, 0, 500, 1500 };

        var worldColors = WorldTerrainRamp.ComputeColors(elevations);
        var crustColors = HypsometricTint.ComputeColors(elevations);

        // At least one cell must differ by a visible margin (not a rounding-level difference).
        double maxDelta = 0.0;
        for (int i = 0; i < worldColors.Count; i++)
        {
            var w = worldColors[i];
            var c = crustColors[i];
            double d = Math.Abs(w.R - c.R) + Math.Abs(w.G - c.G) + Math.Abs(w.B - c.B);
            if (d > maxDelta) maxDelta = d;
        }
        Assert.True(maxDelta > 0.10,
            $"world ramp and hypsometric ramp are too similar: max channel-delta {maxDelta:F3}");
    }

    [Fact]
    public void Skewed_low_heavy_distribution_still_shows_mid_tone_plains()
    {
        // The failure mode seen in the windowed world view (2026-07-03): most cells sit in a thin
        // low-elevation band with a small high tail. Value-linear normalization parks the low bulk
        // at the near-black ramp bottom and the rust/ochre mid plains never appear. Rank
        // equalization must spread the bulk across the vocabulary instead.
        var elevations = Enumerable.Range(0, 80).Select(i => (double)i)              // 80 cells: 0..79 m
            .Concat(Enumerable.Range(0, 20).Select(i => 4000.0 + i * 300.0))         // 20 cells: high tail
            .ToArray();

        var colors = WorldTerrainRamp.ComputeColors(elevations);

        var rustLuma = Luma(new RampColor(0.42, 0.26, 0.16));
        int atOrAboveRust = colors.Count(c => Luma(c) >= rustLuma - 0.02);
        Assert.True(atOrAboveRust >= 40,
            $"low-heavy world collapsed to the ramp bottom: only {atOrAboveRust}/100 cells reached the rust mid-tones");
    }

    [Fact]
    public void Equal_elevations_get_equal_colors_regardless_of_position()
    {
        var elevations = new double[] { 100, 500, 100, 900, 500, 100 };

        var colors = WorldTerrainRamp.ComputeColors(elevations);

        Assert.Equal(colors[0], colors[2]);
        Assert.Equal(colors[0], colors[5]);
        Assert.Equal(colors[1], colors[4]);
    }

    private static double Luma(RampColor c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    private static void AssertLuma(RampColor actual, RampColor expected, double tolerance)
    {
        Assert.InRange(Luma(actual), Luma(expected) - tolerance, Luma(expected) + tolerance);
    }
}