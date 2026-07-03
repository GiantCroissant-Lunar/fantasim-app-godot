namespace FantaSim.App.World.Rendering;

/// <summary>
/// World-view terrain ramp (W1, §5c): maps per-cell elevation (metres) to a bare-rock PRODUCT ramp
/// distinct from the crust-diagnostic <see cref="HypsometricTint"/>. The world view is what a
/// waterless planet reads as from space — dark basalt lowlands -> rust/ochre mid plains -> pale
/// rock highlands — warmer and more oxidized than the greyer crust-diagnostic palette.
///
/// <para>Normalizes by RANK (histogram equalization), not by elevation value: each cell's ramp
/// position is its percentile rank in the world's own elevation distribution, with ties sharing the
/// average rank so equal elevations always get equal colors. Value-linear normalization (what the
/// crust-diagnostic <see cref="HypsometricTint"/> uses) parks a low-heavy distribution at the
/// near-black bottom stop and the rust/ochre mid plains never appear — the 2026-07-03 world-view
/// failure. The product view instead guarantees the full terrain vocabulary on every world,
/// whatever its relief histogram. Every stop is a warm rock tone with R >= G >= B (no
/// sphere-costume rendering: no blue, so no cell can read as ocean — water belongs to the future
/// hydrosphere lane). Luminance (Rec. 709) is strictly ascending so darker-is-lower reads correctly
/// under the half-Lambert light.</para>
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
    /// Normalized by percentile RANK over the input distribution (histogram equalization); ties get
    /// the average rank of their run, so equal elevations map to equal colors.
    /// </summary>
    public static IReadOnlyList<RampColor> ComputeColors(IReadOnlyList<double> elevationsByCell)
    {
        ArgumentNullException.ThrowIfNull(elevationsByCell);

        int n = elevationsByCell.Count;
        if (n == 0)
            return Array.Empty<RampColor>();

        var colors = new RampColor[n];
        if (n == 1)
        {
            colors[0] = SampleRamp(DegenerateNormalized);
            return colors;
        }

        var sorted = new double[n];
        for (int i = 0; i < n; i++) sorted[i] = elevationsByCell[i];
        Array.Sort(sorted);

        for (int i = 0; i < n; i++)
        {
            double v = elevationsByCell[i];
            int first = LowerBound(sorted, v);
            int last = UpperBound(sorted, v) - 1;
            double rank = (first + last) * 0.5;
            colors[i] = SampleRamp(rank / (n - 1));
        }
        return colors;
    }

    /// <summary>Index of the first element >= <paramref name="v"/>.</summary>
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

    /// <summary>Index one past the last element <= <paramref name="v"/>.</summary>
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