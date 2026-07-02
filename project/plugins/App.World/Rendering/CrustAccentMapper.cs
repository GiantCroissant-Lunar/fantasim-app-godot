using System;

namespace FantaSim.App.World.Rendering;

/// <summary>
/// How a typed crust-feature cell modifies the hypsometric base tint. These are surface-level
/// accents that COMPLEMENT the typed boundary polylines (which already color by boundary type),
/// not duplicate them. Packed per-vertex by the host so a spatial shader can apply emission
/// (volcanic vents) and albedo modulation (trench darkening, ridge brightening).
/// </summary>
/// <param name="AlbedoScale">Multiplier on the hypsometric albedo: 1.0 = unchanged, 0.4 = darken.</param>
/// <param name="AlbedoBrighten">Additive lerp toward white: 0.0 = unchanged, 0.12 = subtle seam.</param>
/// <param name="VolcanicEmission">Emission intensity in [0,1]: 0 = no glow, 1 = full vent glow.</param>
public readonly record struct CrustAccent(double AlbedoScale, double AlbedoBrighten, double VolcanicEmission)
{
    public static readonly CrustAccent Neutral = new(1.0, 0.0, 0.0);
}

/// <summary>
/// Maps a cell's crust feature (kind byte mirroring <c>CrustFeatureKind</c>, + magnitude) to a
/// surface accent (sub-project A2). Only VolcanicArc / Trench / Ridge produce an accent;
/// Mountain is already the ramp's highest band, Fault is carried by the boundary polylines, and
/// None is a no-op. Unknown kinds are neutral (forward-compatible).
/// </summary>
public static class CrustAccentMapper
{
    // Mirrors FantaSim.Geosphere.Crust.CrustFeatureKind byte values (None=0..Fault=5).
    public const byte KindVolcanicArc = 2;
    public const byte KindTrench = 3;
    public const byte KindRidge = 4;

    // Volcanic magnitude is the accumulated volcanic-activity value; the arc threshold is 3.0 (see
    // CrustFeatureThresholds.ArcActivity). We normalize above-threshold magnitudes so a typical arc
    // (magnitude ~10-30) maps to a clear glow, saturating well before extreme values.
    private const double ArcThreshold = 3.0;
    private const double ArcReferenceMagnitude = 25.0;

    private const double TrenchAlbedoScale = 0.40;
    private const double RidgeBrighten = 0.12;

    public static CrustAccent Map(byte featureKind, double magnitude) => featureKind switch
    {
        KindVolcanicArc => new CrustAccent(
            AlbedoScale: 1.0,
            AlbedoBrighten: 0.0,
            VolcanicEmission: VolcanicIntensity(magnitude)),

        KindTrench => new CrustAccent(
            AlbedoScale: TrenchAlbedoScale,
            AlbedoBrighten: 0.0,
            VolcanicEmission: 0.0),

        KindRidge => new CrustAccent(
            AlbedoScale: 1.0,
            AlbedoBrighten: RidgeBrighten,
            VolcanicEmission: 0.0),

        _ => CrustAccent.Neutral,
    };

    // Above-threshold magnitude → [0,1] intensity, saturating at ArcReferenceMagnitude. A sqrt curve
    // gives low-activity arcs a visible but modest glow and high-activity arcs a strong one.
    private static double VolcanicIntensity(double magnitude)
    {
        if (magnitude <= ArcThreshold) return 0.0;
        double normalized = (magnitude - ArcThreshold) / (ArcReferenceMagnitude - ArcThreshold);
        normalized = Math.Min(normalized, 1.0);
        return Math.Sqrt(normalized);
    }

    /// <summary>
    /// Applies an accent's albedo modulation (scale then brighten-toward-white) to a hypsometric
    /// base color. Volcanic emission is separate (it does not modify albedo) and is read directly
    /// from <see cref="CrustAccent.VolcanicEmission"/> by the caller for the shader's emission pass.
    /// </summary>
    public static RampColor Apply(RampColor baseColor, CrustAccent accent)
    {
        double r = baseColor.R * accent.AlbedoScale;
        double g = baseColor.G * accent.AlbedoScale;
        double b = baseColor.B * accent.AlbedoScale;
        r += (1.0 - r) * accent.AlbedoBrighten;
        g += (1.0 - g) * accent.AlbedoBrighten;
        b += (1.0 - b) * accent.AlbedoBrighten;
        return new RampColor(Clamp01(r), Clamp01(g), Clamp01(b));
    }

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
}
