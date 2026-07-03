using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Globe.Core;
using FantaSim.Cartography.Shared;

namespace FantaSim.App.World.Rendering;

/// <summary>
/// Continental-scale albedo province tint for the world view (W1, one octave BELOW
/// <see cref="VertexTintJitter"/> in frequency): a LOW-frequency deterministic tint field sampled at
/// each cell's unit-sphere CENTER direction, so large warm/cool-rock regions (provinces) emerge on
/// the otherwise uniform bare-rock disk — the Tharsis-vs-Syrtis-vs-plains read real Mars gets from
/// continent shapes. This is the same "deterministic seed-derived stylization layered on truth"
/// pattern as the per-vertex jitter, just at continental frequency: where <see cref="VertexTintJitter"/>
/// breaks color banding across adjacent vertices (BaseFrequency 48), this modulates whole provinces
/// a few times across the sphere.
///
/// <para>HARD DOCTRINE: every resulting color stays a WARM rock tone (R &gt;= G &gt;= B, never blue/
/// green — no ocean/vegetation costume; water belongs to the future hydrosphere lane), and the
/// modulation is subtle (amplitude ~0.05-0.08) so elevation (whose ramp spans ~0.05..0.75 luma)
/// still dominates the read. Reuses the cartography <see cref="NoiseRelief"/> fBm machinery — no new
/// noise system — so the tint is a pure function of (direction, seed).</para>
/// </summary>
public sealed class ProvinceTint
{
    private readonly NoiseParams _noise;
    private readonly double _amplitude;

    /// <param name="seed">World seed; deterministic for a given seed.</param>
    /// <param name="amplitude">Peak per-channel nudge in [0,1] color units (e.g. 0.07).</param>
    public ProvinceTint(int seed, double amplitude)
    {
        _amplitude = amplitude;
        // LOW frequency: ~2.5 cycles across the unit sphere → a handful of continental provinces, not
        // per-cell jitter (VertexTintJitter's job at BaseFrequency 48). 3 octaves add a touch of
        // province-edge variation without breaking up the large warm/cool regions. Amplitude stays 1.0
        // so NoiseRelief returns [-1,1]; the per-channel scaling below applies the visual swing.
        _noise = new NoiseParams(
            Seed: seed,
            BaseFrequency: 2.5,
            Octaves: 3,
            Lacunarity: 2.0,
            Gain: 0.5,
            Amplitude: 1.0,
            Ridged: false);
    }

    /// <summary>
    /// Applies the deterministic continental tint to <paramref name="baseColor"/> in the direction of
    /// <paramref name="unitDirection"/> (the cell's unit-sphere center). The same (direction, seed)
    /// always yields the same result. Adjacent cells get near-identical tints (low frequency);
    /// antipodal provinces can differ, giving the disk large-scale albedo regions to anchor on.
    /// </summary>
    public RampColor Apply(CartesianPoint3 unitDirection, RampColor baseColor)
    {
        if (_amplitude <= 0.0)
            return baseColor;

        double n = NoiseRelief.Sample(unitDirection, _noise); // [-1, 1]
        // Warm continental swing (identical channel weighting to VertexTintJitter): R gets the full
        // swing, G 0.7x, B 0.4x — so a positive n warms (lifts red) and a negative n cools toward dark
        // without ever letting B dominate. The luminance swing (Rec. 709) peaks at ~0.74 * amplitude.
        double dr = n * _amplitude;
        double dg = n * _amplitude * 0.7;
        double db = n * _amplitude * 0.4;

        double r = Clamp01(baseColor.R + dr);
        double g = Clamp01(baseColor.G + dg);
        double b = Clamp01(baseColor.B + db);

        // Enforce R >= G >= B after modulation so no province can read as water (no sphere-costume).
        // Order matters: cap G to R FIRST, then B to the (possibly reduced) G. Capping B to G before
        // lowering G can re-break B <= G — the warm-channel weighting can momentarily invert a near-
        // equal base, and a second downward clamp is needed to restore the ordering.
        if (g > r) { g = r; }
        if (b > g) { b = g; }

        return new RampColor(r, g, b);
    }

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
}
