namespace FantaSim.App.World.Composition;

/// <summary>
/// Which globe surface presentation the mobile-plate regime renders, driven by the focused timeline
/// layer (sub-project P1 + W1 of the planet evolution arc). The focused track selects the VIEW:
/// no selection -> <see cref="World"/> (the composed product, §5c); <c>geosphere.plate</c> ->
/// <see cref="PlateIdentity"/>; <c>geosphere.crust</c> -> <see cref="HypsometricTerrain"/>. Non-
/// mobile-plate regimes are <see cref="Inactive"/> (layer focus does not change their look). Pure
/// (no Godot) so the mapping is unit-testable; the host resolves it to concrete cap meshes/materials.
/// </summary>
public enum GlobeViewMode
{
    /// <summary>
    /// Layer focus does not apply: the regime is not mobile-plate (magma-ocean, stagnant-lid), so no
    /// plate-cap view switching occurs. The mantle owns the look.
    /// </summary>
    Inactive,

    /// <summary>
    /// The composed product view (W1, §5c): a waterless world reads as a world — bare-rock terrain
    /// ramp (dark basalt lowlands -> rust/ochre plains -> pale highlands), boundary landforms,
    /// volcanic vent glow, sub-cell detail noise that buries the cell grid, and (separate node)
    /// atmosphere rim glow. Boundary ribbons OFF (they are diagnostics). This is the DEFAULT at
    /// mobile-plate with no layer focused.
    /// </summary>
    World,

    /// <summary>
    /// Per-plate identity caps: distinct flat colors per plate, flat-zero displacement (a tectonic map,
    /// not terrain), with the complete typed boundary network visible. Reached by focusing the
    /// <c>geosphere.plate</c> track.
    /// </summary>
    PlateIdentity,

    /// <summary>
    /// Hypsometric terrain view (crust diagnostic): per-vertex elevation tint + typed accents +
    /// elevation displacement, without plate-identity colors. Boundary ribbons hidden. Reached by
    /// focusing the <c>geosphere.crust</c> track.
    /// </summary>
    HypsometricTerrain,
}

/// <summary>
/// Pure <c>(regimeId, selectedLayer)</c> -&gt; <see cref="GlobeViewMode"/> resolver. Ordinal,
/// case-sensitive: regime and layer ids are stable lowercase strings. At mobile-plate the WORLD
/// view is the DEFAULT (no selection): a waterless world reads as a world (§5c). Focusing
/// <c>geosphere.plate</c> selects the plate-identity diagnostic; <c>geosphere.crust</c> selects
/// the hypsometric crust diagnostic; any other/unknown layer falls back to the world view.
/// Mirrors the <see cref="RegimeSurfaceResolver"/> pattern: a pure, Godot-free mapping unit-
/// testable from the contract tier.
/// </summary>
public static class GlobeViewModeResolver
{
    /// <summary>
    /// Resolves the globe view for the given regime and timeline layer selection. Returns
    /// <see cref="GlobeViewMode.Inactive"/> for non-mobile-plate regimes; <see cref="World"/>
    /// when mobile-plate with no selection or any non-plate/non-crust layer;
    /// <see cref="PlateIdentity"/> for <c>geosphere.plate</c>;
    /// <see cref="HypsometricTerrain"/> for <c>geosphere.crust</c>.
    /// </summary>
    public static GlobeViewMode Resolve(string? regimeId, TimelineLayerSelection? selectedLayer)
    {
        if (!string.Equals(regimeId, "mobile-plate", StringComparison.Ordinal))
            return GlobeViewMode.Inactive;

        if (selectedLayer is null)
            return GlobeViewMode.World;

        return selectedLayer.LayerId switch
        {
            "geosphere.plate" => GlobeViewMode.PlateIdentity,
            "geosphere.crust" => GlobeViewMode.HypsometricTerrain,
            _ => GlobeViewMode.World,
        };
    }
}
