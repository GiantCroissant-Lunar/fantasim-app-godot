namespace FantaSim.App.World.Composition;

/// <summary>
/// Which globe surface presentation the mobile-plate regime renders, driven by the focused timeline
/// layer (sub-project P1 of the planet evolution arc). The focused track selects the VIEW:
/// <c>geosphere.plate</c> (or no selection) -> <see cref="PlateIdentity"/>; <c>geosphere.crust</c> ->
/// <see cref="HypsometricTerrain"/>. Non-mobile-plate regimes are <see cref="Inactive"/> (layer focus
/// does not change their look). Pure (no Godot) so the mapping is unit-testable; the host resolves it
/// to concrete cap meshes/materials.
/// </summary>
public enum GlobeViewMode
{
    /// <summary>
    /// Layer focus does not apply: the regime is not mobile-plate (magma-ocean, stagnant-lid), so no
    /// plate-cap view switching occurs. The mantle owns the look.
    /// </summary>
    Inactive,

    /// <summary>
    /// Per-plate identity caps: distinct flat colors per plate, flat-zero displacement (a tectonic map,
    /// not terrain), with the complete typed boundary network visible.
    /// </summary>
    PlateIdentity,

    /// <summary>
    /// Hypsometric terrain view: per-vertex elevation tint + typed accents + elevation displacement,
    /// without plate-identity colors. Boundary ribbons hidden.
    /// </summary>
    HypsometricTerrain,
}

/// <summary>
/// Pure <c>(regimeId, selectedLayer)</c> -&gt; <see cref="GlobeViewMode"/> resolver. Ordinal,
/// case-sensitive: regime and layer ids are stable lowercase strings. At mobile-plate the plate view
/// is the DEFAULT (no selection or unknown layer). Mirrors the
/// <see cref="RegimeSurfaceResolver"/> pattern: a pure, Godot-free mapping unit-testable from the
/// contract tier.
/// </summary>
public static class GlobeViewModeResolver
{
    /// <summary>
    /// Resolves the globe view for the given regime and timeline layer selection. Returns
    /// <see cref="GlobeViewMode.Inactive"/> for non-mobile-plate regimes; <see cref="PlateIdentity"/>
    /// when mobile-plate with no selection, <c>geosphere.plate</c>, or any non-crust layer;
    /// <see cref="HypsometricTerrain"/> only for <c>geosphere.crust</c>.
    /// </summary>
    public static GlobeViewMode Resolve(string? regimeId, TimelineLayerSelection? selectedLayer)
    {
        if (!string.Equals(regimeId, "mobile-plate", StringComparison.Ordinal))
            return GlobeViewMode.Inactive;

        if (selectedLayer is null)
            return GlobeViewMode.PlateIdentity;

        return selectedLayer.LayerId switch
        {
            "geosphere.crust" => GlobeViewMode.HypsometricTerrain,
            _ => GlobeViewMode.PlateIdentity,
        };
    }
}
