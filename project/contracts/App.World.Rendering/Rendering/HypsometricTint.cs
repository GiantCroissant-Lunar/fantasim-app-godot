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
/// terrain vocabulary (dark basalt grey → weathered rock grey → light rock) instead of collapsing
/// to a nearly black band.
///
/// <para>BARE-CRUST PALETTE (doctrine: no sphere-costume rendering — terminology-strata-scale-
/// resolution §1 rule 3). This view renders bare geosphere crust; water belongs to the future
/// hydrosphere lane, so the ramp contains NO blue. Every stop is neutral-to-warm grey rock with
/// R ≥ G ≥ B, so the blue channel never dominates. Luminance (Rec. 709) is strictly ascending so
/// darker-is-lower reads correctly while still leaving exposed crust readable. Color stops:
/// dark basalt grey → basalt grey → weathered rock grey → lighter rock → pale fractured rock.</para>
/// </summary>
public static class HypsometricTint
{
    // (normalized-position, color). Positions are strictly ascending in [0,1]. Luma is strictly
    // ascending. Every stop is neutral/warm grey (R ≥ G ≥ B) so no stop reads as water.
    private static readonly (double Pos, RampColor Color)[] RampStops =
    {
        (0.00, new RampColor(0.22, 0.22, 0.21)),   // dark basalt grey, still readable
        (0.18, new RampColor(0.34, 0.34, 0.32)),   // basalt grey
        (0.40, new RampColor(0.46, 0.45, 0.42)),   // weathered rock grey
        (0.62, new RampColor(0.56, 0.55, 0.52)),   // light fractured rock
        (0.82, new RampColor(0.64, 0.63, 0.60)),   // pale highland rock
        (1.00, new RampColor(0.70, 0.69, 0.66)),   // light grey summit rock
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
