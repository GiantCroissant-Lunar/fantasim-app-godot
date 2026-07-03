namespace FantaSim.App.World.Rendering;

/// <summary>
/// Godot-free per-plate identity color palette for the plate-identity globe view (sub-project P1).
/// Recovered from the <c>BuildPlateMaterial</c>/<c>PlateColor</c> deleted in commit 698ecd2: the same
/// 8-color family that made plates visually distinct before the hypsometric tint replaced it. Exposed
/// as <see cref="RampColor"/> so the host converts to Godot.Color at the seam, matching the
/// <see cref="HypsometricTint"/> pattern. The palette is deterministic per plate id (modulo), so the
/// same plate always gets the same color across rebinds.
/// </summary>
public static class PlateIdentityPalette
{
    private static readonly RampColor[] Palette =
    {
        new(0.34, 0.58, 0.42),  // green
        new(0.26, 0.50, 0.58),  // teal
        new(0.55, 0.47, 0.33),  // brown
        new(0.45, 0.38, 0.55),  // purple
        new(0.30, 0.60, 0.54),  // sea green
        new(0.63, 0.58, 0.34),  // olive
        new(0.38, 0.46, 0.66),  // slate blue
        new(0.56, 0.42, 0.32),  // sienna
    };

    /// <summary>
    /// The identity color for <paramref name="plateId"/>. Deterministic: <c>Math.Abs(plateId) %
    /// Palette.Length</c>. Negative ids are handled via <see cref="Math.Abs(int)"/>.
    /// </summary>
    public static RampColor ColorFor(int plateId)
        => Palette[Math.Abs(plateId) % Palette.Length];
}
