using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Globe.Core;
using FantaSim.Cartography.Shared;

namespace FantaSim.App.World.Rendering;

/// <summary>
/// Per-vertex tint jitter for the world view (W1, §5c "sub-cell detail"): breaks color banding on
/// the 5120-cell faceted surface by nudging each vertex's ramp color by a small, deterministic
/// amount derived from the vertex's base position + a world seed. Reuses the cartography
/// <see cref="NoiseRelief"/> fBm machinery — no new noise system — so the jitter is a pure function
/// of (position, seed) and stays watertight-safe (coincident boundary vertices get the same nudge).
///
/// <para>The jitter is WARM: it perturbs R and G by the noise term and B by a smaller fraction, so
/// it can never let the blue channel dominate (R >= G >= B preserved). Amplitude is in color units
/// (0..1), typically 0.04-0.08: enough to break banding, not enough to rewrite the ramp.</para>
/// </summary>
public sealed class VertexTintJitter
{
    private readonly NoiseParams _noise;
    private readonly double _amplitude;

    /// <param name="seed">World seed; deterministic for a given seed.</param>
    /// <param name="amplitude">Peak per-channel nudge in [0,1] color units (e.g. 0.06).</param>
    public VertexTintJitter(int seed, double amplitude)
    {
        _amplitude = amplitude;
        // High frequency so the jitter varies across adjacent vertices (breaks banding), 3 octaves
        // for a touch of finer grain. Color jitter does not need the relief octaves' low-freq swell.
        _noise = new NoiseParams(
            Seed: seed,
            BaseFrequency: 48.0,
            Octaves: 3,
            Lacunarity: 2.0,
            Gain: 0.5,
            Amplitude: 1.0,
            Ridged: false);
    }

    /// <summary>
    /// Applies the deterministic warm jitter to <paramref name="baseColor"/> at vertex position
    /// <paramref name="unitPos"/>. The same (position, seed) always yields the same result.
    /// </summary>
    public RampColor Apply(CartesianPoint3 unitPos, RampColor baseColor)
    {
        if (_amplitude <= 0.0)
            return baseColor;

        double n = NoiseRelief.Sample(unitPos, _noise); // [-1, 1]
        // Warm jitter: R gets the full swing, G gets 0.7x, B gets 0.4x — so a positive n warms
        // (raises red) and a negative n cools toward dark without ever letting B exceed G or R.
        double dr = n * _amplitude;
        double dg = n * _amplitude * 0.7;
        double db = n * _amplitude * 0.4;

        double r = Clamp01(baseColor.R + dr);
        double g = Clamp01(baseColor.G + dg);
        double b = Clamp01(baseColor.B + db);

        // Enforce R >= G >= B after jitter so no vertex can read as water (no sphere-costume).
        if (b > g) { b = g; }
        if (g > r) { g = r; }

        return new RampColor(r, g, b);
    }

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
}