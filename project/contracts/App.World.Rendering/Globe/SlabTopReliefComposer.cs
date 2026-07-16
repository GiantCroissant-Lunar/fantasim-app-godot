using System.Collections.Generic;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Globe.Core;

namespace FantaSim.App.World.Globe;

/// <summary>
/// Pure, Godot-free composer that marries the World view's formed-relief ingredients onto slab TOPS
/// for the mantle-layer/exploded presentation (directive 3b). It wires the SAME
/// <see cref="TectonicDetailSampler"/> + <see cref="GlobePlateSurfaces"/> path the World view uses —
/// reusing the sampler type and wiring pattern, NOT forking the sampler — and feeds the slab view's
/// DECLARED exaggeration (<see cref="SlabTopReliefProfile"/>, ratio-locked with
/// <see cref="RadialSectionProfile"/>) as the displacement lens.
/// </summary>
/// <remarks>
/// <para><b>Terrain-diffusion conditioning schema (documented at this call site, per the locked plan).</b>
/// The detail sampler is conditioned on the PER-CELL CAUSAL FEATURE BUNDLE, never on position-only
/// noise. Concretely:
/// <list type="bullet">
/// <item>The sampler is constructed over the snapshot's cell tessellation; for every sampled position
/// it resolves the NEAREST cell (max dot product on the unit sphere, deterministic tie-break by cell
/// id — see <see cref="TectonicDetailSampler"/>.</item>
/// <item>That cell's <see cref="CellCrustFeature"/> (Kind + Magnitude) selects the relief profile:
/// Mountain/VolcanicArc/Ridge → thin RIDGED belts (mountain chains); Trench/Fault → subdued relief;
/// featureless interior (Kind 0) → the calm interior profile. The feature Magnitude weights the
/// blend between interior and belt amplitudes.</item>
/// <item>This is "formed by mantle convection made visible" — the relief character is a pure function
/// of which boundary process owns each cell, not of where the camera looks.</item>
/// </list>
/// </para>
/// <para><b>Pure-function identity (the determinism constraint).</b> Every sampled product is a pure
/// function of (cell/position, tick, seed, declared params): the snapshot is the tick-bound truth
/// (<c>WorldGlobeSnapshot</c> at the playhead tick), the seed lives in
/// <see cref="SlabTopReliefProfile.BaseNoise"/>, and no wall-clock, query-order, or camera state
/// enters. Two compositions from identical inputs produce bit-identical cap positions (asserted in
/// <c>SlabTopReliefTests</c>).</para>
/// </remarks>
public static class SlabTopReliefComposer
{
    /// <summary>
    /// Wires the <see cref="TectonicDetailSampler"/> over the snapshot's per-cell feature bundle,
    /// mirroring the World path's <c>PlateSurfaceMeshFactory.BuildTectonicDetailSampler</c> wiring
    /// (same sampler type, same constructor inputs — reusing the sampler, not forking it). The slab
    /// profile's NoiseParams + amplitude multipliers feed the sampler so the slab samples the SAME
    /// truth the same way as the World view; only the displacement lens (applied later in
    /// <see cref="BuildCaps"/>) is the slab's own.
    /// </summary>
    /// <param name="snapshot">The tick-bound globe tessellation (carries the cell geometry the nearest-cell lookup uses).</param>
    /// <param name="features">Per-cell <see cref="CellCrustFeature"/> bundle (Kind + Magnitude), indexed by cell id. Null/empty degrades gracefully to the interior profile.</param>
    /// <param name="profile">The declared slab relief profile (NoiseParams + amplitude multipliers + ridge policy).</param>
    /// <returns>A <see cref="TectonicDetailSampler"/> conditioned on the cell feature bundle, ready to drive slab-top relief.</returns>
    public static TectonicDetailSampler BuildDetailSampler(
        WorldGlobeSnapshot snapshot,
        IReadOnlyList<CellCrustFeature>? features,
        SlabTopReliefProfile profile)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(profile);
        return new TectonicDetailSampler(
            snapshot,
            features,
            profile.BaseNoise,
            profile.InteriorAmplitudeMultiplier,
            profile.RidgeActiveFeatures,
            profile.ActiveAmplitudeMultiplier);
    }

    /// <summary>
    /// Builds the formed-relief slab TOP caps: the SAME elevation truth (<paramref name="elevationsByCell"/>
    /// — CellElevations broad relief) + detail sampler (<see cref="TectonicDetailSampler"/>) path the
    /// World view uses, displaced by the slab profile's DECLARED, ratio-locked exaggeration. Returns
    /// watertight <see cref="PlateCap"/>s ready to feed <see cref="PlateSolidBuilder"/> (the thickness
    /// exaggeration is a SEPARATE concern, owned by <see cref="RadialSectionProfile"/>).
    /// </summary>
    /// <param name="snapshot">The tick-bound globe tessellation.</param>
    /// <param name="features">Per-cell crust feature bundle (the conditioning context for the detail sampler).</param>
    /// <param name="elevationsByCell">Per-cell elevation in metres (CellElevations — the broad signed relief), indexed by cell id.</param>
    /// <param name="radialProfile">The radial section profile (owns the thickness scale the relief is ratio-locked to).</param>
    /// <param name="slabProfile">The declared slab relief profile (owns the relief scale + NoiseParams).</param>
    /// <param name="baseRadius">The unit-sphere base radius (default 1.0, matching the World path and <see cref="PlateSolidBuilder"/>).</param>
    /// <returns>One watertight <see cref="PlateCap"/> per non-empty plate (ascending plate-id order), with formed relief on the top surface.</returns>
    public static IReadOnlyList<PlateCap> BuildCaps(
        WorldGlobeSnapshot snapshot,
        IReadOnlyList<CellCrustFeature>? features,
        IReadOnlyList<double> elevationsByCell,
        RadialSectionProfile radialProfile,
        SlabTopReliefProfile slabProfile,
        double baseRadius = GlobeSurfaceBuilder.DefaultRadius)
    {
        ArgumentNullException.ThrowIfNull(elevationsByCell);
        var surfaces = BuildPlateSurfaces(snapshot, features, slabProfile);

        // The ratio-locked slab relief lens: a declared multiple of the radial profile's thickness
        // depth scale. Relief acts on the TOP cap radii here; thickness acts on the BOTTOM inset in
        // PlateSolidBuilder — disjoint vertex sets, no compounding.
        double exaggeration = slabProfile.MetresToUnitRadius(radialProfile);

        return surfaces.BuildSurfaces(
            elevationsByCell,
            exaggeration: exaggeration,
            baseRadius: baseRadius,
            maxDisplacementUnitRadius: slabProfile.MaxDisplacementUnitRadius);
    }

    /// <summary>
    /// Builds the <see cref="GlobePlateSurfaces"/> instance wired with the slab profile's detail
    /// sampler + base noise (same wiring as the World path). Exposed so the mantle/exploded binder
    /// can build slab-top caps AND topology-aligned vertex colors from the SAME instance — the slab
    /// caps' shared-vertex topology must match the coloring arrays, which holds by construction when
    /// both come from one <see cref="GlobePlateSurfaces"/> (robust to the World path using adaptive
    /// surfaces, which would give <c>_lastCaps</c> a different vertex count).
    /// </summary>
    public static GlobePlateSurfaces BuildPlateSurfaces(
        WorldGlobeSnapshot snapshot,
        IReadOnlyList<CellCrustFeature>? features,
        SlabTopReliefProfile slabProfile)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(slabProfile);

        // Same wiring as the World path: construct the TectonicDetailSampler over the feature bundle,
        // feed its .Sample as GlobePlateSurfaces' detailSampler. The sampler supersedes the base noise
        // for vertices; base noise anchors the fallback shape. Reusing the sampler, not forking it.
        var sampler = BuildDetailSampler(snapshot, features, slabProfile);
        return new GlobePlateSurfaces(
            snapshot,
            noise: slabProfile.BaseNoise,
            detailSampler: sampler.Sample);
    }
}
