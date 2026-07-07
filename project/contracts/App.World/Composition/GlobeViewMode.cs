using System.Collections.Generic;

namespace FantaSim.App.World.Composition;

/// <summary>
/// Which globe surface presentation the mobile-plate regime renders, driven by the focused timeline
/// layer (sub-project P1 + W1 of the planet evolution arc). The focused track selects the VIEW:
/// no selection -> <see cref="World"/> (the composed product, §5c); <c>geosphere.plate</c> ->
/// <see cref="Continents"/> by default, or <see cref="PlateIdentity"/> when the host config knob
/// <c>globe:plateView</c> is set to <c>identity</c> (M0, spec D1); <c>geosphere.crust</c> ->
/// <see cref="HypsometricTerrain"/>. Non-mobile-plate regimes are <see cref="Inactive"/> (layer
/// focus does not change their look). Pure (no Godot) so the mapping is unit-testable; the host
/// resolves it to concrete cap meshes/materials.
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
    /// not terrain), with the complete typed boundary network visible. Reached from the
    /// <c>geosphere.plate</c> track only when the host config knob <c>globe:plateView</c> is set to
    /// <c>identity</c>; otherwise the new <see cref="Continents"/> view is the default for that track
    /// (M0, spec D1).
    /// </summary>
    PlateIdentity,

    /// <summary>
    /// M0 motion-first view: two-tone land/ocean by plate membership at the playhead; flat,
    /// frontier-tinted; reached by focusing the <c>geosphere.plate</c> track. Land is the union of
    /// continental plates surfaced on the presentation document; ocean is everything else. See the
    /// 2026-07-06 M0 spec.
    /// </summary>
    Continents,

    /// <summary>
    /// Hypsometric terrain view (crust diagnostic): per-vertex elevation tint + typed accents +
    /// elevation displacement, without plate-identity colors. Boundary ribbons hidden. Reached by
    /// focusing the <c>geosphere.crust</c> track.
    /// </summary>
    HypsometricTerrain,

    /// <summary>
    /// Mantle-interior LAYER view (D1): the mantle is a selectable layer of the world stack, not an
    /// x-ray. When active, the presentation composes the M-A interior (core sphere + four anomaly
    /// isosurfaces, field method unchanged) with the M-B crust as SEPARATED THICK SLABS at a modest
    /// radial explode factor — discrete detached plates that still read as a sphere (the Sketchfab
    /// exploded-plates reference). The separated slabs (not a ghost shell) are the surface reference
    /// frame. Reached by focusing the <c>geosphere.mantle</c> track.
    /// </summary>
    MantleInterior,
}

/// <summary>
/// Pure <c>(regimeId, selectedLayer, plateViewOverride)</c> -&gt; <see cref="GlobeViewMode"/> resolver.
/// Ordinal, case-sensitive: regime and layer ids are stable lowercase strings. At mobile-plate the
/// WORLD view is the DEFAULT (no selection): a waterless world reads as a world (§5c). Focusing
/// <c>geosphere.plate</c> selects the M0 <see cref="Continents"/> view by default; focusing it with
/// <paramref name="plateViewOverride"/> set to <c>identity</c> selects the <see cref="PlateIdentity"/>
/// diagnostic. <c>geosphere.crust</c> selects the hypsometric crust diagnostic; <c>geosphere.mantle</c>
/// selects the <see cref="MantleInterior"/> layer view (D1); any other/unknown layer falls back to
/// the world view. Mirrors the <see cref="RegimeSurfaceResolver"/> pattern: a pure, Godot-free mapping
/// unit-testable from the contract tier.
/// </summary>
public static class GlobeViewModeResolver
{
    /// <summary>
    /// Resolves the globe view for the given regime and timeline layer selection. Returns
    /// <see cref="GlobeViewMode.Inactive"/> for non-mobile-plate regimes; <see cref="World"/>
    /// when mobile-plate with no selection or any non-plate/non-crust/non-mantle layer;
    /// <see cref="Continents"/> for <c>geosphere.plate</c>;
    /// <see cref="PlateIdentity"/> for <c>geosphere.plate</c> when the host config knob
    /// <c>globe:plateView</c> is <c>identity</c>;
    /// <see cref="HypsometricTerrain"/> for <c>geosphere.crust</c>;
    /// <see cref="MantleInterior"/> for <c>geosphere.mantle</c>.
    /// </summary>
    public static GlobeViewMode Resolve(string? regimeId, TimelineLayerSelection? selectedLayer)
        => Resolve(regimeId, selectedLayer, plateViewOverride: null);

    /// <summary>
    /// Resolves the globe view, allowing the caller to override the default <c>geosphere.plate</c>
    /// view. The <paramref name="plateViewOverride"/> is the value of the host config knob
    /// <c>globe:plateView</c> (env <c>globe__plateView</c>); <c>identity</c> selects
    /// <see cref="PlateIdentity"/>, any other value (including <c>null</c>) keeps the default
    /// <see cref="Continents"/>.
    /// </summary>
    public static GlobeViewMode Resolve(
        string? regimeId,
        TimelineLayerSelection? selectedLayer,
        string? plateViewOverride)
    {
        if (!string.Equals(regimeId, "mobile-plate", StringComparison.Ordinal))
            return GlobeViewMode.Inactive;

        if (selectedLayer is null)
            return GlobeViewMode.World;

        return selectedLayer.LayerId switch
        {
            "geosphere.plate" => string.Equals(plateViewOverride, "identity", StringComparison.Ordinal)
                ? GlobeViewMode.PlateIdentity
                : GlobeViewMode.Continents,
            "geosphere.crust" => GlobeViewMode.HypsometricTerrain,
            "geosphere.mantle" => GlobeViewMode.MantleInterior,
            _ => GlobeViewMode.World,
        };
    }

    /// <summary>
    /// D5 stacked-layer composition resolver. Maps the per-sphere ACTIVE SET of timeline layers to a
    /// deterministic <see cref="LayerCompositionDecision"/>: which existing GlobeViewMode the binder
    /// plumbs for transition/lighting/gates, whether the mantle-interior geometry mounts, and which
    /// coloring owns the surface (plate caps + separated-slab tops).
    ///
    /// <para><b>Composition rules (deterministic, the D5 spec):</b></para>
    /// <list type="bullet">
    /// <item>Non-<c>mobile-plate</c> regime: <see cref="GlobeViewMode.Inactive"/> (layer focus does
    /// not change the mantle-owned look of pre-mobile-plate regimes).</item>
    /// <item>Empty active set: <see cref="GlobeViewMode.World"/>, no mantle, World coloring, terrain
    /// relief (the composed product view, section 5c).</item>
    /// <item>MANTLE active: mounts the interior + separated slabs (the wave-5 MantleInterior path).
    /// The mantle owns GEOMETRY; it does not own the surface coloring.</item>
    /// <item>Surface-coloring owner precedence: PLATE active wins (identity/continents); else CRUST
    /// active (hypsometric); else the WORLD default.</item>
    /// <item>DerivedViewMode precedence (nearest existing mode): mantle wins (MantleInterior); else
    /// plate (Continents, or PlateIdentity under the <c>identity</c> override); else crust
    /// (HypsometricTerrain); else World.</item>
    /// </list>
    ///
    /// <para><b>Combos (the cases the binder + this resolver test must honor):</b></para>
    /// <list type="bullet">
    /// <item>Mantle+Crust: interior + slabs whose TOPS use terrain coloring (Hypsometric).</item>
    /// <item>Mantle+Plate: interior + slabs with identity/continents tops.</item>
    /// <item>Plate+Crust: identity/continents coloring wins the surface; terrain relief geometry
    /// stays (encoded as <see cref="LayerCompositionDecision.TerrainRelief"/>=true; the first-slice
    /// binder realizes the identity coloring and leaves the combined relief for a follow-up).</item>
    /// <item>Empty set: World view.</item>
    /// </list>
    ///
    /// <para>Layers outside the geosphere (e.g. atmosphere) do not affect the globe surface decision;
    /// only <c>geosphere.{plate,crust,mantle}</c> are inspected. Unknown geosphere layer ids fall
    /// back to the World coloring. Mirrors the single-select <see cref="Resolve"/>: ordinal,
    /// case-sensitive, pure, Godot-free, unit-testable from the contract tier.</para>
    /// </summary>
    /// <param name="regimeId">The current geosphere regime id (only <c>mobile-plate</c> engages).</param>
    /// <param name="activeLayers">The per-sphere active layer set (insertion-ordered by the controller).</param>
    /// <param name="plateViewOverride">The host config knob <c>globe:plateView</c> (<c>identity</c> selects
    /// PlateIdentity; any other value keeps Continents).</param>
    public static LayerCompositionDecision ResolveComposition(
        string? regimeId,
        IReadOnlyList<TimelineLayerSelection> activeLayers,
        string? plateViewOverride = null)
    {
        if (!string.Equals(regimeId, "mobile-plate", StringComparison.Ordinal))
        {
            return new LayerCompositionDecision(
                DerivedViewMode: GlobeViewMode.Inactive,
                MountMantleInterior: false,
                SurfaceColoring: SurfaceColoringKind.World,
                TerrainRelief: false);
        }

        bool mantle = false, plate = false, crust = false;
        for (int i = 0; i < activeLayers.Count; i++)
        {
            var layer = activeLayers[i];
            if (!string.Equals(layer.SphereId, "geosphere", StringComparison.Ordinal))
                continue;
            switch (layer.LayerId)
            {
                case "geosphere.mantle": mantle = true; break;
                case "geosphere.plate": plate = true; break;
                case "geosphere.crust": crust = true; break;
            }
        }

        bool mountMantle = mantle;

        SurfaceColoringKind surfaceColoring;
        if (plate)
            surfaceColoring = string.Equals(plateViewOverride, "identity", StringComparison.Ordinal)
                ? SurfaceColoringKind.PlateIdentity
                : SurfaceColoringKind.Continents;
        else if (crust)
            surfaceColoring = SurfaceColoringKind.HypsometricTerrain;
        else
            surfaceColoring = SurfaceColoringKind.World;

        // Terrain relief follows the surface coloring, plus the declared Plate+Crust exception
        // (identity/continents coloring WITH terrain relief geometry on the plate surface, per D5).
        // The exception is a NON-MANTLE plate-surface phenomenon: when mantle is active the surface
        // is the separated slabs, whose tops follow the coloring owner strictly (Continents = flat).
        bool terrainRelief = surfaceColoring is SurfaceColoringKind.World or SurfaceColoringKind.HypsometricTerrain
            || (plate && crust && !mantle);

        GlobeViewMode derivedViewMode;
        if (mantle)
            derivedViewMode = GlobeViewMode.MantleInterior;
        else if (plate)
            derivedViewMode = string.Equals(plateViewOverride, "identity", StringComparison.Ordinal)
                ? GlobeViewMode.PlateIdentity
                : GlobeViewMode.Continents;
        else if (crust)
            derivedViewMode = GlobeViewMode.HypsometricTerrain;
        else
            derivedViewMode = GlobeViewMode.World;

        return new LayerCompositionDecision(
            DerivedViewMode: derivedViewMode,
            MountMantleInterior: mountMantle,
            SurfaceColoring: surfaceColoring,
            TerrainRelief: terrainRelief);
    }
}
