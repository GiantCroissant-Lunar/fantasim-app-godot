using FantaSim.App.World.Composition;

namespace FantaSim.App.World.Globe;

/// <summary>
/// Which surface presentation the DEFAULT World view renders (assembled-world north-star,
/// <c>vault/specs/2026-07-16-assembled-world-northstar.md</c>): "the normal complete sphere could
/// not see how convergent, divergent, transform being presented. But the split part with thickness
/// can." The per-plate solid slab assembly is THE world; the watertight sphere survives only as an
/// explicit fallback behind this declared parameter family.
/// </summary>
public enum WorldSurfacePresentation
{
    /// <summary>
    /// The per-plate SOLID slab assembly (default): one closed crust slab per plate — formed-relief
    /// tops, lit strata walls — separated by the declared joint gap so the joints read as visible
    /// seams from orbit (sketchfab exploded-plates family, ASSEMBLED state).
    /// </summary>
    SlabAssembly,

    /// <summary>
    /// The pre-north-star monolithic watertight displaced sphere (the single-surface plate-cap
    /// path, silhouette-clamped). Kept available as an explicit fallback — never the default.
    /// </summary>
    WatertightSphere,
}

/// <summary>
/// The DECLARED World-surface presentation parameters (S1 discipline: every look number is a named
/// record field, never an inline literal). Owns which presentation the World view renders and the
/// slab JOINT GAP in unit-radius units.
/// </summary>
/// <remarks>
/// <para><b>Joint-gap semantics.</b> The gap reuses the EXISTING slab separation math
/// (<see cref="PlateSolidBuilder.ApplyExplodedFactor"/>): each slab translates by
/// <c>gap × centroidDirection</c> — a pure translation, the slab itself undeformed. Adjacent slabs'
/// formerly-coincident boundary vertices open by <c>gap × |dirA − dirB|</c>, so the joints read as
/// thin visible seams at orbit distance. The gap must be far smaller than the exploded view's
/// radial translation (<see cref="PlateSolidBuilder.DefaultMaxOffset"/> = 0.35R) — a JOINT, not an
/// explosion; <c>WorldSlabAssemblyTests</c> pins the relationship.</para>
///
/// <para><b>No World silhouette clamp on the slab path.</b> The slab tops keep
/// <see cref="SlabTopReliefProfile"/>'s formed relief with its declared <c>+inf</c> displacement
/// cap; the 0.005R silhouette budget applies to the <see cref="WorldSurfacePresentation.WatertightSphere"/>
/// path only (where it lives today, in the layer projection profile).</para>
///
/// <para>Pure, Godot-free, no wall-clock. Two profiles with equal inputs are equal.</para>
/// </remarks>
/// <param name="Presentation">Which presentation the World view renders. Default <see cref="WorldSurfacePresentation.SlabAssembly"/>.</param>
/// <param name="SlabJointGapUnitRadius">
/// The declared joint gap in unit-radius units (> 0, finite). Default
/// <see cref="DefaultSlabJointGapUnitRadius"/>; the composer rejects non-positive/non-finite values
/// — a seamless sphere is the <see cref="WorldSurfacePresentation.WatertightSphere"/> presentation,
/// never a zero gap.
/// </param>
public sealed record WorldSurfacePresentationProfile(
    WorldSurfacePresentation Presentation,
    double SlabJointGapUnitRadius)
{
    /// <summary>
    /// Default joint gap: 0.006R — the middle of the slice's locked 0.004–0.008 band. Against the
    /// canonical slab thickness (~0.0377R) the joint reads as a thin visible seam from orbit, and
    /// at ~1.7% of the exploded view's 0.35R max translation it stays unmistakably a JOINT. The
    /// lead + user EYE gate tunes this; the agent does not self-certify the look.
    /// </summary>
    /// <summary>
    /// V1 "closed skin" (vault/specs/2026-07-18-visual-fidelity-slices-decision.md): slab TOPS render
    /// with the cap's smoothed per-vertex normals so the assembled globe stops reading as flat
    /// triangular facets (design §7.1). The earlier faceted-chunky default is superseded by that user
    /// decision. Side walls stay flat (hard crease) — they are rendered through
    /// <c>BuildExplodedSolidDto</c>, which computes per-face normals independently of this switch.
    /// Eye-tuned presentation switch, not physics.
    /// </summary>
    public bool FacetedSlabTops { get; init; } = false;

    public const double DefaultSlabJointGapUnitRadius = 0.035;

    /// <summary>The default profile: slab assembly with the default joint gap.</summary>
    public static WorldSurfacePresentationProfile Default { get; } = new(
        Presentation: WorldSurfacePresentation.SlabAssembly,
        SlabJointGapUnitRadius: DefaultSlabJointGapUnitRadius);
}

/// <summary>
/// Pure gate: does the given view render the World slab assembly? Only the World view (default
/// globe, no layer focused) under the <see cref="WorldSurfacePresentation.SlabAssembly"/>
/// presentation mounts the assembly — layer-focused views own their compositions (Continents caps,
/// crust diagnostic, the mantle layer's own separated slabs), and the
/// <see cref="WorldSurfacePresentation.WatertightSphere"/> fallback keeps the old single-surface
/// path. Mirrors the <see cref="Composition.GlobeViewModeResolver"/> pattern: pure, Godot-free,
/// unit-testable from the contract tier.
/// </summary>
public static class WorldSurfacePresentationPolicy
{
    /// <summary>True when <paramref name="viewMode"/> is <see cref="GlobeViewMode.World"/> and the profile presents the slab assembly.</summary>
    public static bool ShowsSlabAssembly(WorldSurfacePresentationProfile profile, GlobeViewMode viewMode)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Presentation == WorldSurfacePresentation.SlabAssembly
            && viewMode == GlobeViewMode.World;
    }
}
