namespace FantaSim.App.World.Rendering;

/// <summary>
/// Godot-free two-tone palette for the M0 <c>Continents</c> globe view (motion-first slice,
/// 2026-07-06 spec §3.1): land = union of continental plates surfaced on the presentation
/// document, ocean = everything else, and a darker frontier tint on cells whose neighbour
/// belongs to a different plate so the membership seam reads as a coastline/boundary without
/// typed arc polylines (spec D4). Exposed as <see cref="RampColor"/> so the host converts to
/// Godot.Color at the seam, matching the <see cref="PlateIdentityPalette"/> pattern.
/// </summary>
public static class ContinentsPalette
{
    /// <summary>Saturated warm tan — continental plates (Scotese-map land tone).</summary>
    public static RampColor Land { get; } = new(0.66, 0.55, 0.34);

    /// <summary>Saturated steel blue — oceanic plates.</summary>
    public static RampColor Ocean { get; } = new(0.16, 0.34, 0.52);

    /// <summary>
    /// Multiplier applied per channel to frontier cells (any neighbour on another plate). Strictly
    /// below 1 so the seam is darker than both tones and moves with the membership it derives from.
    /// </summary>
    public const double FrontierFactor = 0.68;

    /// <summary>The tone for a cell: land/ocean base, frontier-darkened when flagged.</summary>
    public static RampColor ToneFor(bool isLand, bool isFrontier)
    {
        var baseTone = isLand ? Land : Ocean;
        return isFrontier
            ? new RampColor(baseTone.R * FrontierFactor, baseTone.G * FrontierFactor, baseTone.B * FrontierFactor)
            : baseTone;
    }
}
