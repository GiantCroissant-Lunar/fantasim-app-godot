namespace FantaSim.App.World.Rendering;

/// <summary>
/// World-view terrain ramp (W1, §5c): maps per-cell elevation (metres) to a bare-rock PRODUCT ramp
/// distinct from the crust-diagnostic <see cref="HypsometricTint"/>. The world view is what a
/// waterless planet reads as from space — dark basalt lowlands -> rust/ochre mid plains -> pale
/// rock highlands — warmer and more oxidized than the greyer crust-diagnostic palette.
///
/// <para>Reuses the same percentile-clamp normalization (<see cref="HypsometricRampOptions"/>) so an
/// early low-relief world still renders the full terrain vocabulary instead of collapsing to one
/// band. Every stop is a warm rock tone with R >= G >= B (no sphere-costume rendering: no blue, so
/// no cell can read as ocean — water belongs to the future hydrosphere lane). Luminance (Rec. 709)
/// is strictly ascending so darker-is-lower reads correctly under the half-Lambert light.</para>
/// </summary>
public static class WorldTerrainRamp
{
    // (normalized-position, color). Positions strictly ascending in [0,1]. Luma strictly ascending.
    // Every stop is warm (R >= G >= B). Distinct from HypsometricTint: rust/ochre mid tones instead
    // of basalt-brown/grey, giving the product view a warmer, more oxidized-Mars-like feel.
    private static readonly (double Pos, RampColor Color)[] RampStops =
    {
        (0.00, new RampColor(0.06, 0.05, 0.045)),   // dark basalt lowlands — near-black warm
        (0.22, new RampColor(0.18, 0.13, 0.10)),    // basalt — dark warm grey-brown
        (0.45, new RampColor(0.42, 0.26, 0.16)),    // rust lowland plains — oxidized iron
        (0.65, new RampColor(0.56, 0.38, 0.22)),    // ochre mid plains — warm desert rock
        (0.82, new RampColor(0.68, 0.56, 0.42)),    // pale rock highlands — sun-bleached
        (1.00, new RampColor(0.82, 0.78, 0.70)),    // pale highland summits — near-white warm
    };

    private const double DegenerateNormalized = 0.5;

    /// <summary>
    /// One world-ramp color per cell, indexed identically to <paramref name="elevationsByCell"/>.
    /// Normalized via percentile clamp over the input distribution (same options as
    /// <see cref="HypsometricTint"/>).
    /// </summary>
    public static IReadOnlyList<RampColor> ComputeColors(
        IReadOnlyList<double> elevationsByCell,
        HypsometricRampOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(elevationsByCell);

        int n = elevationsByCell.Count;
        if (n == 0)
            return Array.Empty<RampColor>();

        var opts = options ?? new HypsometricRampOptions();
        var (lo, hi) = PercentileBounds(elevationsByCell, opts);

        var colors = new RampColor[n];
        double range = hi - lo;
        bool degenerate = range < 1e-9;

        for (int i = 0; i < n; i++)
        {
            double t = degenerate
                ? DegenerateNormalized
                : Clamp((elevationsByCell[i] - lo) / range, 0.0, 1.0);
            colors[i] = SampleRamp(t);
        }
        return colors;
    }

    private static (double Lo, double Hi) PercentileBounds(
        IReadOnlyList<double> values,
        HypsometricRampOptions opts)
    {
        int n = values.Count;
        if (n == 1)
            return (values[0], values[0]);

        var sorted = new double[n];
        for (int i = 0; i < n; i++) sorted[i] = values[i];
        Array.Sort(sorted);

        double lo = Percentile(sorted, opts.LowerPercentile);
        double hi = Percentile(sorted, opts.UpperPercentile);
        if (hi < lo) (lo, hi) = (hi, lo);
        return (lo, hi);
    }

    private static double Percentile(double[] sorted, double p)
    {
        int n = sorted.Length;
        double rank = p * (n - 1);
        int lower = (int)Math.Floor(rank);
        int upper = Math.Min(lower + 1, n - 1);
        double frac = rank - lower;
        return sorted[lower] * (1.0 - frac) + sorted[upper] * frac;
    }

    private static RampColor SampleRamp(double t)
    {
        t = Clamp(t, 0.0, 1.0);
        for (int i = 1; i < RampStops.Length; i++)
        {
            if (t <= RampStops[i].Pos)
            {
                var (p0, c0) = RampStops[i - 1];
                var (p1, c1) = RampStops[i];
                double frac = p1 - p0 < 1e-9 ? 0.0 : (t - p0) / (p1 - p0);
                return new RampColor(
                    c0.R + (c1.R - c0.R) * frac,
                    c0.G + (c1.G - c0.G) * frac,
                    c0.B + (c1.B - c0.B) * frac);
            }
        }
        return RampStops[^1].Color;
    }

    private static double Clamp(double v, double min, double max)
        => v < min ? min : v > max ? max : v;
}