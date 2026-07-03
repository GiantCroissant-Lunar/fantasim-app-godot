using System;
using System.Linq;
using FantaSim.App.World.Rendering;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Hypsometric tint proof (sub-project A2): per-cell elevation must map to a terrain ramp whose
/// normalized range is robust to the snapshot's actual relief. The ramp reads as BARE CRUST
/// (doctrine: no sphere-costume rendering): deep basalt (near-black warm grey) → basalt brown →
/// rock tan → light rock. No blue anywhere — water belongs to the future hydrosphere lane.
/// </summary>
public sealed class HypsometricTintTests
{
    // Ramp stop expectations (must match HypsometricRamp definition). Checked loosely — the exact
    // RGB is a look-and-feel constant, but the BAND ORDERING and band-head colors are contracts the
    // normalization + ramp must honour so the globe reads as bare crust, not ocean.
    private static readonly RampColor DeepBasalt = new(0.07, 0.06, 0.055);
    private static readonly RampColor LightRock = new(0.84, 0.82, 0.78);

    [Fact]
    public void ComputeColors_returns_one_color_per_cell()
    {
        var elevations = new double[] { -1000, -500, 0, 500, 1000 };

        var colors = HypsometricTint.ComputeColors(elevations);

        Assert.Equal(elevations.Length, colors.Count);
    }

    [Fact]
    public void Empty_input_returns_empty()
    {
        var colors = HypsometricTint.ComputeColors(Array.Empty<double>());
        Assert.Empty(colors);
    }

    [Fact]
    public void Lowest_elevation_maps_near_deep_basalt_and_highest_near_light_rock()
    {
        var elevations = new double[] { -2000, 0, 2000 };

        var colors = HypsometricTint.ComputeColors(elevations);
        var ordered = colors.ToArray();

        // Min is at the deep-basalt end of the ramp; max at the light-rock end.
        AssertLuma(ordered[0], DeepBasalt, 0.08);
        AssertLuma(ordered[2], LightRock, 0.08);
    }

    [Fact]
    public void Ordering_is_monotonic_low_to_high_luminance()
    {
        // A spread that crosses the full ramp: low basalt → high light rock.
        var elevations = new double[] { -1500, -800, -100, 400, 1200, 2500 };

        var colors = HypsometricTint.ComputeColors(elevations);

        // Luminance (Rec. 709) must be non-decreasing with elevation: dark basalt → bright rock.
        var luma = colors.Select(Luma).ToArray();
        for (int i = 1; i < luma.Length; i++)
            Assert.True(luma[i] >= luma[i - 1] - 1e-6,
                $"luma not monotonic at {i}: {luma[i]} < {luma[i - 1]}");
    }

    [Fact]
    public void Bare_crust_ramp_never_lets_blue_dominate()
    {
        // Doctrine (no sphere-costume rendering): the crust view must not read as water. Assert the
        // blue channel never dominates any color across the full normalized ramp — for every output
        // B <= G (a warm rock tone), so no cell can read as ocean.
        var elevations = Enumerable.Range(0, 64).Select(i => (double)i).ToArray();

        var colors = HypsometricTint.ComputeColors(elevations);

        Assert.All(colors, c =>
        {
            Assert.True(c.B <= c.G + 1e-9,
                $"blue dominates (B {c.B:F3} > G {c.G:F3}) — stop reads as water, not bare crust");
            Assert.True(c.G <= c.R + 1e-9,
                $"green dominates R (G {c.G:F3} > R {c.R:F3}) — stop is not a warm rock tone");
        });
    }

    [Fact]
    public void All_equal_elevations_does_not_throw_and_returns_uniform()
    {
        // Degenerate: every cell at the same height. Must not divide by zero; every cell gets the
        // SAME color (whatever ramp point the degenerate normalisation picks).
        var elevations = new double[] { 42.0, 42.0, 42.0, 42.0 };

        var colors = HypsometricTint.ComputeColors(elevations);

        Assert.Equal(4, colors.Count);
        var first = colors[0];
        Assert.All(colors, c => Assert.Equal(first, c));
    }

    [Fact]
    public void Single_extreme_outlier_does_not_compress_the_rest()
    {
        // A realistic bulk (100 cells with small variation 0..10) + one extreme outlier at 50_000.
        // Without percentile clamping the outlier would pin the top of the range and every bulk cell
        // would collapse to the deep-basalt end. At 1280-cell scale (real terrain) the default 2/98
        // clamp excludes ~25 cells per tail; here 100 bulk cells lets the 98th percentile land inside
        // the bulk so the outlier is clamped and the bulk spreads across the ramp.
        var bulk = Enumerable.Range(0, 100).Select(i => i * 0.1).ToArray();
        var elevations = bulk.Append(50_000.0).ToArray();

        var colors = HypsometricTint.ComputeColors(elevations);

        // The bulk cells must NOT all sit at the deep-basalt end: the highest bulk cell (elevation
        // 9.9) must land clearly above the deep-basalt luma (it should reach at least the brown/tan
        // band).
        var bulkMaxLuma = colors.Take(100).Select(Luma).Max();
        var deepBasaltLuma = Luma(DeepBasalt);
        Assert.True(bulkMaxLuma > deepBasaltLuma + 0.10,
            $"outlier compressed the bulk: max bulk-luma {bulkMaxLuma:F3} not clearly above deep-basalt {deepBasaltLuma:F3}");
    }

    [Fact]
    public void Low_relief_world_uses_full_ramp_not_a_single_band()
    {
        // The spec's concern: an early low-relief world (±10 m) must NOT render as one flat band.
        // With percentile normalization the tiny range stretches across the full ramp so the lowest
        // cells read as deep basalt and the highest as light rock — not a uniform mid-toned field.
        var elevations = new double[] { -10, -7, -3, 0, 2, 5, 8, 10 };

        var colors = HypsometricTint.ComputeColors(elevations);

        var luma = colors.Select(Luma).ToArray();
        // Lowest cell reads clearly darker (deep-basalt band) than the highest (light-rock band).
        Assert.True(luma[^1] - luma[0] > 0.20,
            $"low-relief world did not spread the ramp: luma range [{luma[0]:F3}, {luma[^1]:F3}] too narrow");
        // And specifically: the lowest is NOT in the mid basalt-brown band (luma clearly below it).
        var midRampLuma = Luma(new RampColor(0.27, 0.22, 0.18));
        Assert.True(luma[0] < midRampLuma - 0.05,
            $"lowest low-relief cell landed in the mid band, luma {luma[0]:F3} — expected deep basalt");
    }

    [Fact]
    public void Custom_percentile_options_are_respected()
    {
        // A bulk with real variation (0..10) bracketed by two extreme outliers. A wider percentile
        // (default 2/98 over 12 pts) would let the outliers widen the range; a tight 10/90 clamps
        // them so the bulk's tiny [0,10] range stretches further across the ramp.
        var bulk = Enumerable.Range(0, 10).Select(i => (double)i).ToArray();
        var elevations = new[] { -100_000.0 }
            .Concat(bulk)
            .Concat(new[] { 100_000.0 })
            .ToArray();

        var tightColors = HypsometricTint.ComputeColors(elevations, new HypsometricRampOptions(0.10, 0.90));
        var defaultColors = HypsometricTint.ComputeColors(elevations);

        // The tight clamp stretches the bulk more: the mid-bulk cell must reach a higher ramp
        // position (brighter) under the tight clamp than under the default.
        var midIdx = 1 + 5; // the -100_000 outlier is index 0, bulk starts at 1
        Assert.True(Luma(tightColors[midIdx]) > Luma(defaultColors[midIdx]),
            "tight percentile should stretch the bulk more than the default");
    }

    private static double Luma(RampColor c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    private static void AssertLuma(RampColor actual, RampColor expected, double tolerance)
    {
        Assert.InRange(Luma(actual), Luma(expected) - tolerance, Luma(expected) + tolerance);
    }
}
