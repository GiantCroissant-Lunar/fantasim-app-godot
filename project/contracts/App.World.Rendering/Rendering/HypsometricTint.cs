namespace FantaSim.App.World.Rendering;

/// <summary>Godot-free RGB color (each channel in [0,1]) for terrain tint computation.</summary>
public readonly record struct RampColor(double R, double G, double B);

/// <summary>
/// Percentile-clamp bounds for hypsometric normalization. Values are clamped to
/// [<see cref="LowerPercentile"/>, <see cref="UpperPercentile"/>] before rank normalization so a
/// handful of extreme cells cannot pin the diagnostic ramp.
/// </summary>
public sealed record HypsometricRampOptions(
    double LowerPercentile = 0.02,
    double UpperPercentile = 0.98)
{
    private const double Epsilon = 1e-9;

    public bool IsValid =>
        LowerPercentile >= 0.0 && LowerPercentile <= 1.0
        && UpperPercentile >= 0.0 && UpperPercentile <= 1.0
        && UpperPercentile - LowerPercentile > Epsilon;
}

/// <summary>
/// Maps per-cell elevation (metres) to a hypsometric terrain ramp (sub-project A2). The ramp is
/// NORMALIZED over the snapshot's actual relief via percentile-clamped rank, so an early low-relief
/// world (small elevation spread) or a world with broad lowland plateaus still renders the full
/// terrain vocabulary instead of collapsing to a single band.
///
/// <para>BIMODAL SATURATED PALETTE (north-star spec §2 — color-first dry crust): the dry crust is
/// NOT monochrome. The ramp has distinct saturated bands with a bimodal base — ocean-basin level
/// tonally separated from continental level by a shelf ramp between them, even with no water.
/// Basin mode (low ranks) is cool-warm grey rock; the shelf break transitions steeply in warmth
/// (R-B delta); continental mode (high ranks) is warmer saturated rock. Belt accents
/// (trench/ridge/arc) stay visible on top. Every stop is warm (R >= G >= B) so no stop reads as
/// water (doctrine: no sphere-costume rendering — water belongs to the future hydrosphere lane).
/// Luminance (Rec. 709) is strictly ascending so darker-is-lower reads correctly.</para>
/// </summary>
public static class HypsometricTint
{
    // (normalized-position, color). Positions strictly ascending in [0,1]. Luma strictly ascending.
    // Every stop is warm (R >= G >= B). Bimodal: basin mode (0.00-0.28, low warmth), shelf transition
    // (0.28-0.58, steep warmth ramp), continent mode (0.58-1.00, high warmth).
    private static readonly (double Pos, RampColor Color)[] RampStops =
    {
        (0.00, new RampColor(0.21, 0.20, 0.18)),   // basin floor — dark warm grey
        (0.28, new RampColor(0.32, 0.29, 0.24)),   // basin plains — settled basin tone family
        (0.48, new RampColor(0.52, 0.42, 0.30)),   // shelf break — warmth transition begins
        (0.58, new RampColor(0.60, 0.46, 0.32)),   // continental plains — warm saturated rock
        (0.78, new RampColor(0.70, 0.56, 0.40)),   // continental highlands — warm rock
        (1.00, new RampColor(0.72, 0.66, 0.55)),   // peaks — light warm rock
    };

    private const double DegenerateNormalized = 0.5; // mid-ramp when all elevations are equal

    /// <summary>
    /// One terrain-ramp color per cell, indexed identically to <paramref name="elevationsByCell"/>.
    /// The ramp is normalized via percentile clamp over the input distribution.
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
        var colors = new RampColor[n];
        if (n == 1)
        {
            colors[0] = SampleRamp(DegenerateNormalized);
            return colors;
        }

        var (lo, hi) = PercentileBounds(elevationsByCell, opts);
        var ranked = new double[n];
        for (int i = 0; i < n; i++)
            ranked[i] = Clamp(elevationsByCell[i], lo, hi);
        Array.Sort(ranked);

        for (int i = 0; i < n; i++)
        {
            double v = Clamp(elevationsByCell[i], lo, hi);
            int first = LowerBound(ranked, v);
            int last = UpperBound(ranked, v) - 1;
            double t = (first + last) * 0.5 / (n - 1);
            colors[i] = SampleRamp(t);
        }
        return colors;
    }

    // Linear-interpolated percentile: rank = p*(n-1); interpolate between the two bracketing sorted
    // values. Robust for small samples (test fixtures) and correct for large ones (1280 cells).
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

    private static int LowerBound(double[] sorted, double v)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (sorted[mid] < v) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    private static int UpperBound(double[] sorted, double v)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (sorted[mid] <= v) lo = mid + 1; else hi = mid;
        }
        return lo;
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
