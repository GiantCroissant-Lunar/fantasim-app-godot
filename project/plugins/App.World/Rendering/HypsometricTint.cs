namespace FantaSim.App.World.Rendering;

/// <summary>Godot-free RGB color (each channel in [0,1]) for terrain tint computation.</summary>
public readonly record struct RampColor(double R, double G, double B);

/// <summary>
/// Percentile-clamp bounds for hypsometric normalization. The ramp is stretched over
/// [<see cref="LowerPercentile"/>, <see cref="UpperPercentile"/>] of the elevation distribution so a
/// handful of extreme cells cannot compress the rest into one band.
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
/// NORMALIZED over the snapshot's actual relief via percentile clamping, so an early low-relief
/// world (small elevation spread) still renders the full terrain vocabulary (ocean → shelf →
/// lowland → upland → mountain → snow) instead of collapsing to all-green.
///
/// <para>The ramp stops are luminance-monotonic (Rec. 709) so darker-is-lower reads correctly under
/// the half-Lambert light the host applies. Color stops: deep ocean (dark blue) → shallow shelf
/// (medium blue) → lowland (green) → upland (brown/tan) → highland (grey) → snow (near-white).</para>
/// </summary>
public static class HypsometricTint
{
    // (normalized-position, color). Positions are strictly ascending in [0,1]. Luma is ascending.
    private static readonly (double Pos, RampColor Color)[] RampStops =
    {
        (0.00, new RampColor(0.03, 0.10, 0.28)),  // deep ocean — dark blue
        (0.18, new RampColor(0.08, 0.24, 0.40)),  // shallow shelf — medium blue
        (0.36, new RampColor(0.16, 0.38, 0.18)),  // lowland — green
        (0.58, new RampColor(0.42, 0.36, 0.24)),  // upland — brown/tan
        (0.80, new RampColor(0.52, 0.50, 0.48)),  // highland — grey
        (1.00, new RampColor(0.88, 0.90, 0.93)),  // snow — near-white
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
