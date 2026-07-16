using FantaSim.App.World;
using FantaSim.Cartography.Globe;

namespace FantaSim.App.World.Globe;

/// <summary>
/// The DECLARED slab-view relief exaggeration profile for formed relief on slab TOPS (directive 3b —
/// "the flat plate is not what we want"). Slabs in the mantle-layer/exploded composition carry the
/// SAME formed relief ingredients as the World view (CellElevations broad relief +
/// <see cref="TectonicDetailSampler"/> boundary-conditioned detail + the banded ramp), but scaled by
/// the slab view's OWN exaggeration — NOT the World view's surface-relief lens.
/// </summary>
/// <remarks>
/// <para><b>Why a slab-specific profile.</b> The World view's relief exaggeration
/// (<c>~3e-5</c> metres-to-unit-radius, silhouette-capped at 0.5%R) is tuned for a FULL-GLOBE
/// silhouette read. Reusing it blindly on the mantle-layer/exploded slabs makes the relief vanish:
/// slab thickness is <c>~0.0377R</c> (<see cref="RadialSectionProfile"/>) while the World relief is
/// clamped to <c>0.005R</c> — the tops read as smooth plates, the exact defect directive 3b rejects.
/// The slab view declares its own scale here.</para>
///
/// <para><b>RATIO-LOCKED composition with <see cref="RadialSectionProfile"/> (S1/S2 discipline).</b>
/// The slab relief metres-to-unit-radius scale is a DECLARED multiple of the radial profile's
/// thickness depth scale:
/// <code>
///   MetresToUnitRadius(radial) = ReliefThicknessScaleMultiple * radial.ThicknessDepthScale()
/// </code>
/// This ties the relief scale to the SAME thickness scale <see cref="RadialSectionProfile"/> declares,
/// so the two move together: doubling <see cref="RadialSectionProfile.CrustThicknessExaggeration"/>
/// doubles both the slab thickness AND the slab relief. It is NOT an independent lens that could drift
/// or double up (the historical slab x4 double-scale lesson). The relief acts on the slab TOP cap
/// radii (inside <see cref="GlobePlateSurfaces"/>); the thickness acts ONLY on the BOTTOM inset (inside
/// <see cref="PlateSolidBuilder"/>) — disjoint vertex sets, no compounding. The test
/// <c>SlabTopReliefTests.PlateSolidBuilder_does_not_mutate_slab_top_positions</c> guards this.</para>
///
/// <para><b>No World silhouette clamp.</b> The slab path does NOT inherit the World view's 0.005R
/// silhouette budget — relief bulk at crust scale is the point of the slab presentation. The
/// <see cref="MaxDisplacementUnitRadius"/> cap is slab-declared (default <c>+inf</c>); a future
/// look-dev decision can clamp it consciously without re-introducing the "reads as smooth" bug.</para>
///
/// <para><b>Pure, Godot-free, no wall-clock.</b> The default NoiseParams + amplitude multipliers
/// mirror the World view's wiring (<c>PlateSurfaceReliefFabric</c>'s World values) so the slab samples
/// the SAME truth the same way; only the exaggeration lens is the slab's own. Two profiles with equal
/// inputs are equal.</para>
/// </remarks>
/// <param name="BaseNoise">Seeded peaks-relief parameters (mirror the World view's WorldPeaks). The detail sampler supersedes these for vertices when wired, but they anchor the interior-quiet fallback shape.</param>
/// <param name="InteriorAmplitudeMultiplier">Amplitude multiplier for featureless interior cells (mirrors the World view's interior multiplier). Keeps interiors calm so boundary belts read.</param>
/// <param name="ActiveAmplitudeMultiplier">Amplitude multiplier for active feature belts. 1.0 = undamped (belts are the drama).</param>
/// <param name="RidgeActiveFeatures">Ridge the noise for mountain/volcanic-arc/ridge features (thin ridged belts — mountain chains, not crumple).</param>
/// <param name="ReliefThicknessScaleMultiple">The DECLARED ratio-lock knob (dimensionless): the slab relief metres-to-unit-radius scale is this multiple of <see cref="RadialSectionProfile.ThicknessDepthScale"/>.</param>
/// <param name="MaxDisplacementUnitRadius">Slab-declared displacement cap (default <c>+inf</c> — no World silhouette clamp). Must stay slab-owned or relief re-clamps to smooth.</param>
public sealed record SlabTopReliefProfile(
    NoiseParams BaseNoise,
    double InteriorAmplitudeMultiplier,
    double ActiveAmplitudeMultiplier,
    bool RidgeActiveFeatures,
    double ReliefThicknessScaleMultiple,
    double MaxDisplacementUnitRadius = double.PositiveInfinity)
{
    /// <summary>
    /// Default ratio-lock multiple: the slab relief scale is 10x the thickness depth scale. At the
    /// canonical profile (thickness 0.0377R) this yields ~0.006R relief at a 500 m envelope elevation
    /// — visible bulk at crust scale without swamping the slab walls. The lead + user EYE gate tunes
    /// this; the agent does not self-certify the look.
    /// </summary>
    public const double DefaultReliefThicknessScaleMultiple = 10.0;

    /// <summary>
    /// Default seeded peaks (mirrors the World view's <c>WorldPeaks</c> so the slab samples the same
    /// truth the same way — only the exaggeration lens differs).
    /// </summary>
    public static readonly NoiseParams DefaultBaseNoise = new(
        Seed: 1337,
        BaseFrequency: 8.0,
        Octaves: 6,
        Lacunarity: 2.0,
        Gain: 0.5,
        Amplitude: 172.0,
        Ridged: false);

    /// <summary>The default slab relief profile (all defaults above).</summary>
    public static SlabTopReliefProfile Default { get; } = new(
        BaseNoise: DefaultBaseNoise,
        InteriorAmplitudeMultiplier: 0.15,
        ActiveAmplitudeMultiplier: 1.0,
        RidgeActiveFeatures: true,
        ReliefThicknessScaleMultiple: DefaultReliefThicknessScaleMultiple);

    /// <summary>
    /// The slab relief metres-to-unit-radius scale, RATIO-LOCKED to the radial profile's thickness
    /// depth scale: <c>ReliefThicknessScaleMultiple * radial.ThicknessDepthScale()</c>. This is the
    /// exaggeration <see cref="SlabTopReliefComposer"/> feeds to <see cref="GlobePlateSurfaces"/>'s
    /// top-surface displacement.
    /// </summary>
    public double MetresToUnitRadius(RadialSectionProfile radial)
        => ReliefThicknessScaleMultiple * radial.ThicknessDepthScale();
}
