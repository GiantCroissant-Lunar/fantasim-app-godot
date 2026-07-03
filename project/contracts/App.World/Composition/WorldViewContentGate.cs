namespace FantaSim.App.World.Composition;

/// <summary>
/// Whether the composed WORLD view (the default, no-layer-focused globe presentation) is currently
/// active, so world-view-owned content -- the atmosphere limb glow (W2) and anything else that
/// belongs to the composed product rather than to a diagnostic layer view -- renders only here.
///
/// <para><b>Coordination with the concurrent World-view agent.</b> <see cref="GlobeViewMode.World"/>
/// is being added in parallel (sub-project W1) as the DEFAULT view when no timeline layer is focused
/// (per <c>vault/specs/2026-07-02-planet-evolution-arc-design.md</c> §5c). Until that merge lands,
/// this helper gates on the raw condition that will BECOME <c>viewMode == GlobeViewMode.World</c>:
/// no diagnostic layer is focused. It is intentionally REGIME-AGNOSTIC because the atmosphere limb
/// glow (W2) may render in pre-mobile-plate regimes too -- the atmosphere exists before plates do
/// (spec §5c "waterless worlds are worlds"; W2 goal 3) -- so the rim's composed-view gate must not
/// inherit the W1 terrain composition's current mobile-plate scoping.</para>
///
/// <para><b>Diagnostic views.</b> <see cref="GlobeViewMode.PlateIdentity"/> (focused
/// <c>geosphere.plate</c>) and <see cref="GlobeViewMode.HypsometricTerrain"/> (focused
/// <c>geosphere.crust</c>) are reached by focusing a timeline layer; with a layer focused, this gate
/// returns <c>false</c> and world-view content stays hidden. <see cref="GlobeViewMode.Inactive"/>
/// (non-mobile-plate regimes, mantle owns the look) pairs with no layer focus and returns
/// <c>true</c> -- the composed world view includes the mantle-era surfaces.</para>
///
/// <para><b>Merge plan.</b> When <see cref="GlobeViewMode.World"/> lands and is resolved for every
/// regime's no-selection case, replace this helper's body with a delegation to the resolver:
/// <c>GlobeViewModeResolver.Resolve(regimeId, selectedLayer) == GlobeViewMode.World</c>. The signature
/// already takes the layer selection so the swap is a one-line change; callers (the rim, and any
/// future world-view content) are unaffected.</para>
/// </summary>
public static class WorldViewContentGate
{
    /// <summary>
    /// True when no diagnostic timeline layer is focused -- i.e. the composed world view owns the
    /// globe. Pure (no Godot); ordinal/stable. When <paramref name="selectedLayer"/> is non-null a
    /// diagnostic view is focused and world-view-owned content (the atmosphere rim) stays hidden.
    /// </summary>
    public static bool IsActive(TimelineLayerSelection? selectedLayer) => selectedLayer is null;
}
