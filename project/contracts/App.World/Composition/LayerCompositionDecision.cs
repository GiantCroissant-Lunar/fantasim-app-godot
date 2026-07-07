using System.Collections.Generic;

namespace FantaSim.App.World.Composition;

/// <summary>
/// Which coloring owns the globe surface, as resolved from the stacked active layer set (D5).
/// Decoupled from <see cref="GlobeViewMode"/>: the mode drives lighting/boundary/cutaway gates and
/// the binder's transition plumbing, while the coloring drives the plate-cap mesh branch and the
/// separated-slab TOP appearance. Several distinct active-set combos map onto the same derived
/// <see cref="GlobeViewMode"/> but demand different surface colorings (e.g. Mantle+Crust vs
/// Mantle+Plate both resolve to <see cref="GlobeViewMode.MantleInterior"/>, yet the slab tops must
/// follow the crust-vs-plate coloring owner).
/// </summary>
public enum SurfaceColoringKind
{
    /// <summary>
    /// The composed WORLD look (north-star spec section 5c): bare-rock terrain ramp + sub-cell
    /// detail noise. Owner when the active set is empty, or when mantle is active alone (the
    /// separated slabs carry the default terrain reading of the surface).
    /// </summary>
    World,

    /// <summary>
    /// Flat per-plate identity colors (the tectonic-map diagnostic). Owner when the
    /// <c>geosphere.plate</c> layer is active and the host config knob <c>globe:plateView</c> is
    /// <c>identity</c>.
    /// </summary>
    PlateIdentity,

    /// <summary>
    /// M0 motion-first land/ocean coloring by per-cell continental fraction. Owner when
    /// <c>geosphere.plate</c> is active (default, no identity override).
    /// </summary>
    Continents,

    /// <summary>
    /// Hypsometric elevation tint (the crust diagnostic). Owner when <c>geosphere.crust</c> is
    /// active and <c>geosphere.plate</c> is not (plate wins the surface when both are stacked).
    /// </summary>
    HypsometricTerrain,
}

/// <summary>
/// The deterministic composition decision resolved from a stacked active layer set (D5): which
/// existing <see cref="GlobeViewMode"/> the binder should plumb for transition/lighting/gates, whether
/// the mantle-interior geometry should be mounted, and which coloring owns the surface (plate caps
/// and separated-slab tops). Pure (no Godot); the binder is the only host consumer.
/// </summary>
/// <param name="DerivedViewMode">
/// The nearest existing <see cref="GlobeViewMode"/> for the active set. This is a DERIVED value kept
/// only to bound binder churn: the binder's transition table (<see cref="PlateSurfaceViewModeTransition"/>),
/// lighting (<c>ApplyLightingForView</c>), cutaway gate, status indicator and boundary visibility all
/// read it. A full dissolution (per-layer presentation contributions replacing the enum entirely) is
/// the D7b follow-up; until then this field lets the stacked-set path reuse the single-mode plumbing.
/// </param>
/// <param name="MountMantleInterior">
/// True when <c>geosphere.mantle</c> is in the active set: the binder mounts the composed interior
/// (core sphere + four isosurfaces) plus the separated crust slabs via
/// <c>MantleInteriorViewComposer</c>, and hides the regular plate surface. False frees that root.
/// </param>
/// <param name="SurfaceColoring">
/// The owner of the surface coloring, per the D5 composition rules. Drives the plate-cap mesh branch
/// and the separated-slab TOP DTO: PLATE active wins (identity/continents); else CRUST active
/// (hypsometric); else the WORLD default.
/// </param>
/// <param name="TerrainRelief">
/// Whether the surface relief is elevation-driven (terrain displacement) per the D5 combos.
/// Realized by the binder when the coloring itself is terrain (<see cref="SurfaceColoringKind.World"/>
/// or <see cref="SurfaceColoringKind.HypsometricTerrain"/>). The D5 <c>Plate+Crust</c> combo (identity
/// coloring WITH terrain relief geometry) is encoded here as <c>true</c> for spec fidelity, but the
/// first-slice binder realizes the identity coloring and leaves the combined identity+terrain relief
/// for a follow-up (tracked in AGENT-SUMMARY); the resolver test asserts the declared intent.
/// </param>
public sealed record LayerCompositionDecision(
    GlobeViewMode DerivedViewMode,
    bool MountMantleInterior,
    SurfaceColoringKind SurfaceColoring,
    bool TerrainRelief);
