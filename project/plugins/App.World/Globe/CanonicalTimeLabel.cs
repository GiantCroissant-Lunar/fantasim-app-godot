using FantaSim.World.Contracts.Quantities;

namespace FantaSim.App.World.Globe;

/// <summary>
/// T3 (Godot-free) helper that renders a canonical tick as an OdometerLadder display string via the
/// engine's <see cref="CanonicalDisplayFormatter"/> (geosphere.plate.time.v1 profile) — ladder
/// symbols only (ka/kb/jz…), NEVER real-world units (Ma). The T4 seam consumes it as a plain string.
/// </summary>
public static class CanonicalTimeLabel
{
    /// <param name="tick">Canonical tick.</param>
    /// <param name="ticksPerAnchor">Ticks per the profile's anchor symbol (1 ka = 1 Ma = TicksPerMegaAnnum ticks).</param>
    public static string ForTick(long tick, long ticksPerAnchor)
    {
        double anchorAmount = ticksPerAnchor > 0 ? tick / (double)ticksPerAnchor : 0.0;
        return CanonicalDisplayFormatter.Format(
            anchorAmount,
            BaselineScaleProfiles.GeospherePlateTimeV1,
            new CanonicalFormatterOptions { IncludeUnitSuffix = true });
    }
}
