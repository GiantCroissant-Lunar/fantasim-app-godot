using System;
using System.Linq;
using FantaSim.App.World.Rendering;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Hypsometric tint proof (sub-project A2): per-cell elevation must map to a terrain ramp whose
/// normalized range is robust to the snapshot's actual relief. The ramp reads as BARE CRUST
/// (doctrine: no sphere-costume rendering): dark basalt grey → weathered rock grey → light rock.
/// No blue anywhere — water belongs to the future hydrosphere lane.
/// </summary>
public sealed class HypsometricTintTests
{
    // Ramp stop expectations (must match HypsometricRamp definition). Checked loosely — the exact
    // RGB is a look-and-feel constant, but the BAND ORDERING and band-head colors are contracts the
    // normalization + ramp must honour so the globe reads as bare crust, not ocean.
    private static readonly RampColor DarkBasaltGrey = new(0.22, 0.22, 0.21);
    private static readonly RampColor LightRock = new(0.70, 0.69, 0.66);

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
    public void Lowest_elevation_maps_to_readable_dark_grey_and_highest_near_light_rock()
    {
        var elevations = new double[] { -2000, 0, 2000 };

        var colors = HypsometricTint.ComputeColors(elevations);
        var ordered = colors.ToArray();

        // Min is at the readable dark-grey end of the ramp; max at the light-rock end.
        AssertLuma(ordered[0], DarkBasaltGrey, 0.08);
        AssertLuma(ordered[2], LightRock, 0.08);
    }

    [Fact]
    public void Highest_elevation_is_light_grey_rock_not_clipped_white()
    {
        var colors = HypsometricTint.ComputeColors(new double[] { -2000, 0, 2000 });
        var highest = colors[2];

        Assert.True(Luma(highest) <= 0.72,
            $"highest crust tint is too close to white for the diagnostic view: luma={Luma(highest):F3}");
        Assert.True(highest.R <= 0.73 && highest.G <= 0.72 && highest.B <= 0.69,
            $"highest crust tint channels wash out: ({highest.R:F3}, {highest.G:F3}, {highest.B:F3})");
    }

    [Fact]
    public void Lowest_elevation_remains_readable_grey_rock_not_near_black()
    {
        var colors = HypsometricTint.ComputeColors(new double[] { -2000, 0, 2000 });

        Assert.True(Luma(colors[0]) >= 0.18,
            $"lowest crust tint is too dark for a dry exposed crust diagnostic: luma={Luma(colors[0]):F3}");
    }

    [Fact]
    public void Mid_elevation_reads_as_warm_rock_not_neutral_grey()
    {
        // North-star spec §2: "The dry crust is NOT monochrome" — mid-elevation must carry warm
        // saturation (R > B), not neutral grey (R ≈ B).
        var colors = HypsometricTint.ComputeColors(new double[] { -2000, 0, 2000 });
        var mid = colors[1];

        Assert.True(mid.R - mid.B > 0.08,
            $"mid crust tint is neutral grey (R-B={mid.R - mid.B:F3}); spec §2 requires saturated warm bands");
    }

    // === Color-first dry crust (north-star spec §2) ==============================================

    [Fact]
    public void Ramp_is_saturated_not_monochrome_grey()
    {
        // Spec §2: "The dry crust is NOT monochrome: hypsometric ramp with distinct, saturated bands."
        var colors = HypsometricTint.ComputeColors(UniformElevations(101));

        var mid = colors[50];
        Assert.True(mid.R - mid.B > 0.08,
            $"mid-elevation is monochrome grey (R-B={mid.R - mid.B:F3}); spec §2 requires saturated bands");
    }

    [Fact]
    public void Ramp_has_bimodal_base_basin_tonally_separated_from_continent()
    {
        // Spec §2: "bimodal base — ocean-basin level tonally separated from continental level
        // (shelf ramp between) even with no water." Basin and continent must differ in warmth
        // (R-B), not just lightness — they are different tone families.
        var colors = HypsometricTint.ComputeColors(UniformElevations(101));

        double Warmth(RampColor c) => c.R - c.B;
        var basin = colors[15];
        var continent = colors[65];

        Assert.True(Math.Abs(Warmth(continent) - Warmth(basin)) > 0.04,
            $"basin and continent are the same tone family (warmth delta {Warmth(continent) - Warmth(basin):F3}); spec §2 requires bimodal tonal separation");
    }

    [Fact]
    public void Shelf_transition_is_steeper_than_basin_or_continent_interior()
    {
        // Spec §2: "shelf ramp between" the bimodal modes — warmth change per unit rank is steeper
        // in the shelf zone than in either mode's interior.
        var colors = HypsometricTint.ComputeColors(UniformElevations(101));

        double Warmth(int i) => colors[i].R - colors[i].B;

        double basinSlope = Math.Abs(Warmth(20) - Warmth(10));
        double shelfSlope = Math.Abs(Warmth(40) - Warmth(30));
        double continentSlope = Math.Abs(Warmth(70) - Warmth(60));

        Assert.True(shelfSlope > basinSlope,
            $"shelf ({shelfSlope:F3}) not steeper than basin interior ({basinSlope:F3})");
        Assert.True(shelfSlope > continentSlope,
            $"shelf ({shelfSlope:F3}) not steeper than continental interior ({continentSlope:F3})");
    }

    [Fact]
    public void Belt_accents_stay_visible_on_top_of_ramp()
    {
        // Spec §2: "Belt accents (trench/ridge/arc) stay visible on top of the ramp." Trench
        // darkening and ridge brightening must produce a visible face-on delta against actual
        // ramp colors, not just hardcoded bases.
        var colors = HypsometricTint.ComputeColors(UniformElevations(101));
        var baseColor = colors[50];
        double baseLuma = Luma(baseColor);

        var trench = CrustAccentMapper.Apply(baseColor, CrustAccentMapper.Map(3, 1.0));
        var ridge = CrustAccentMapper.Apply(baseColor, CrustAccentMapper.Map(4, 1.0));

        Assert.True(baseLuma - Luma(trench) >= 0.025,
            $"trench not visible on ramp: delta {baseLuma - Luma(trench):F3}");
        Assert.True(Luma(ridge) - baseLuma >= 0.025,
            $"ridge not visible on ramp: delta {Luma(ridge) - baseLuma:F3}");
    }

    [Fact]
    public void Dominant_lowland_plateau_still_reads_as_mid_grey_crust_when_high_features_are_sparse()
    {
        var elevations = Enumerable.Repeat(0.0, 90)
            .Concat(Enumerable.Range(1, 10).Select(i => i * 1000.0))
            .ToArray();

        var colors = HypsometricTint.ComputeColors(elevations);

        Assert.True(Luma(colors[0]) >= 0.36,
            $"dominant lowland plateau collapsed to too-dark crust: luma={Luma(colors[0]):F3}");
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
                $"green dominates R (G {c.G:F3} > R {c.R:F3}) — stop is not neutral/warm rock");
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
        // would collapse to the dark-grey end. At 1280-cell scale (real terrain) the default 2/98
        // clamp excludes ~25 cells per tail; here 100 bulk cells lets the 98th percentile land inside
        // the bulk so the outlier is clamped and the bulk spreads across the ramp.
        var bulk = Enumerable.Range(0, 100).Select(i => i * 0.1).ToArray();
        var elevations = bulk.Append(50_000.0).ToArray();

        var colors = HypsometricTint.ComputeColors(elevations);

        // The bulk cells must NOT all sit at the dark-grey end: the highest bulk cell (elevation
        // 9.9) must land clearly above the lowland luma.
        var bulkMaxLuma = colors.Take(100).Select(Luma).Max();
        var darkBasaltLuma = Luma(DarkBasaltGrey);
        Assert.True(bulkMaxLuma > darkBasaltLuma + 0.10,
            $"outlier compressed the bulk: max bulk-luma {bulkMaxLuma:F3} not clearly above dark basalt grey {darkBasaltLuma:F3}");
    }

    [Fact]
    public void Low_relief_world_uses_full_ramp_not_a_single_band()
    {
        // The spec's concern: an early low-relief world (±10 m) must NOT render as one flat band.
        // With rank normalization the tiny range stretches across the full ramp so the lowest
        // cells read as dark grey crust and the highest as light rock — not a uniform field.
        var elevations = new double[] { -10, -7, -3, 0, 2, 5, 8, 10 };

        var colors = HypsometricTint.ComputeColors(elevations);

        var luma = colors.Select(Luma).ToArray();
        // Lowest cell reads clearly darker than the highest, while staying readable.
        Assert.True(luma[^1] - luma[0] > 0.35,
            $"low-relief world did not spread the ramp: luma range [{luma[0]:F3}, {luma[^1]:F3}] too narrow");
        Assert.True(luma[0] >= Luma(DarkBasaltGrey) - 0.02,
            $"lowest low-relief cell became unreadably dark, luma {luma[0]:F3}");
    }

    [Fact]
    public void Custom_percentile_options_are_respected()
    {
        // A bulk with real variation (0..10) bracketed by two extreme outliers. A tight 10/90 clamp
        // should pull the low outlier into the clamped low tail instead of leaving it as a unique
        // rank-zero sample.
        var bulk = Enumerable.Range(0, 10).Select(i => (double)i).ToArray();
        var elevations = new[] { -100_000.0 }
            .Concat(bulk)
            .Concat(new[] { 100_000.0 })
            .ToArray();

        var tightColors = HypsometricTint.ComputeColors(elevations, new HypsometricRampOptions(0.10, 0.90));
        var defaultColors = HypsometricTint.ComputeColors(elevations);

        Assert.True(Luma(tightColors[0]) > Luma(defaultColors[0]),
            "tight percentile should clamp the low outlier into a shared low tail rank");
    }

    private static double Luma(RampColor c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    private static void AssertLuma(RampColor actual, RampColor expected, double tolerance)
    {
        Assert.InRange(Luma(actual), Luma(expected) - tolerance, Luma(expected) + tolerance);
    }

    private static double[] UniformElevations(int count)
        => Enumerable.Range(0, count).Select(i => (double)i).ToArray();
}
