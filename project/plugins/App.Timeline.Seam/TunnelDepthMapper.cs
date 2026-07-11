using System;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Maps a linear view-fraction (TimelineModel.Ruler's Fraction, 0=near/viewStart, 1=far/viewEnd)
/// onto a tunnel ring radius via an inverse falloff, so rings crowd toward the throat exactly like
/// Concept A's wireframe (ringR(z) = R*k/(k+z)) — reparameterized on fraction so it composes with
/// the EXISTING Ruler call instead of a second tick-domain function (spec §2.2: no new domain
/// math). falloffK is a slice-1 placeholder pending DP12's real-data tuning pass — vault/specs/
/// 2026-07-11-tunnel-timeline-design.md.
/// </summary>
public static class TunnelDepthMapper
{
    public const double DefaultFalloffK = 3.0;

    /// <summary>
    /// fraction=0 maps to outerRadius exactly; as fraction increases toward 1 the radius shrinks
    /// toward (but never reaches) throatRadius via 1/(1 + falloffK*fraction) — a LARGER falloffK
    /// crowds the rings tighter toward the throat at any fixed fraction (sharper falloff), a
    /// SMALLER falloffK spreads them out closer to the outer radius. fraction is clamped to
    /// [0,1] first so an out-of-range caller value degrades to the nearest valid endpoint instead
    /// of throwing.
    /// </summary>
    public static double RadiusForFraction(
        double fraction, double throatRadius, double outerRadius, double falloffK = DefaultFalloffK)
    {
        var clampedFraction = Math.Clamp(fraction, 0.0, 1.0);
        var range = outerRadius - throatRadius;
        var t = 1.0 / (1.0 + (falloffK * clampedFraction));
        return throatRadius + (range * t);
    }
}
