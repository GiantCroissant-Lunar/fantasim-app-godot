using UnifyMaths.Numerics;

namespace FantaSim.App.World.Globe;

/// <summary>
/// The DECLARED birth-roughness profile (directive 3d — "it is not egg shell after all"). Crust is
/// BORN ROUGH: a deterministic, plate-anchored noise field present from the first tick crust exists,
/// in every crust-bearing regime. Collision-grown orogeny (the belts) then grows ON TOP of this. This
/// record declares every knob; no magic constants live at call sites (mirrors
/// <see cref="SlabTopReliefProfile"/>'s shape).
/// </summary>
/// <remarks>
/// <para><b>What this is.</b> A DERIVED, DETERMINISTIC per-vertex elevation component: a pure function
/// of (plate-material coordinate, crust age at tick, continental fraction, world seed, these params).
/// It is NOT truth — no query-history dependence, no wall-clock — and it adds onto the SAME
/// elevation-side composition (<see cref="GlobePlateSurfaces"/>'s per-cell <c>elevationsByCell</c>)
/// that both the World path and the slab path already consume, so it retires the sphere-fixed
/// interior-fabric defect (the noise rode the sphere, not the plates) by sampling in the PLATE-MATERIAL
/// frame instead.</para>
///
/// <para><b>Amplitude = AGE-CONDITIONED ramp (terrain-diffusion §3.4).</b> The noise's own
/// <see cref="FbmNoiseParams.Amplitude"/> carries the SHAPE only; the actual metre scale is the declared
/// monotone ramp <see cref="FloorAmplitudeMetres"/> (at crust age 0 — newly solidified, faint base
/// texture) growing to <see cref="CeilingAmplitudeMetres"/> (at <see cref="AgeReferenceTicks"/> —
/// accumulated battering), never decreasing. See <see cref="BirthRoughnessField.AgeRampAmplitude"/>.
/// Continental crust is gently boosted by <see cref="ContinentalModulation"/> (buoyier/thicker crust
/// reads slightly rougher); oceanic crust is unmodulated.</para>
///
/// <para><b>North-star budget (the lock stands).</b> In the World view birth roughness IS the interior
/// fabric, so it MUST live inside the north-star budget "interior &lt;= 0.15x belts" and the 0.5%R
/// silhouette clamp stays in charge (the existing <c>MaxDisplacementUnitRadius</c> path clamps the
/// finalized displacement; birth roughness enters as metres and is tiny relative to the budget). The
/// defaults below keep extremes (≈ ±0.45 × ceiling × (1 + modulation)) well under both bounds. In the
/// slab/exploded view it composes with <see cref="SlabTopReliefProfile"/>'s declared scale (it is part
/// of <c>elevationsByCell</c>, which the slab path already reads).</para>
///
/// <para><b>Pure, Godot-free, no wall-clock.</b> Two profiles with equal fields are equal; the field is
/// reproduced on demand from the seed, never stored.</para>
/// </remarks>
/// <param name="Noise">Seeded fBm shape (seed + frequency + octaves + lacunarity + gain + ridged). Its <see cref="FbmNoiseParams.Amplitude"/> is OVERRIDDEN by the age ramp — only the shape fields are used.</param>
/// <param name="FloorAmplitudeMetres">DECLARED floor: the noise amplitude at crust age 0 (newly solidified crust). Must be &gt;= 0.</param>
/// <param name="CeilingAmplitudeMetres">DECLARED ceiling: the noise amplitude once crust age reaches <paramref name="AgeReferenceTicks"/>. Must be &gt;= <paramref name="FloorAmplitudeMetres"/> (monotone ramp).</param>
/// <param name="AgeReferenceTicks">Crust age (canonical ticks) at which the ramp saturates at the ceiling. Must be &gt; 0. <c>double</c> to match the engine's <c>CellCrustState.CrustAgeTicks</c> field type.</param>
/// <param name="ContinentalModulation">Amplitude boost for continental crust in [0,1]-ish: output scales by <c>(1 + ContinentalModulation * continentalFraction)</c>. 0 = no modulation (oceanic and continental equally rough); 1 = continental crust up to 2x oceanic. Gentle default keeps the interior calm.</param>
public sealed record BirthRoughnessProfile(
    FbmNoiseParams Noise,
    double FloorAmplitudeMetres,
    double CeilingAmplitudeMetres,
    double AgeReferenceTicks,
    double ContinentalModulation)
{
    /// <summary>
    /// Default birth-roughness profile. The seed mirrors <c>WorldGenerationRenderOptions.Default.Seed</c>
    /// (7); the caller in the world plugin overrides <see cref="Noise"/>.<see cref="FbmNoiseParams.Seed"/>
    /// with the resolved world seed so each world gets its own realization. Floor 60 m / ceiling 200 m
    /// keeps extremes (~±112 m continental, ~±90 m oceanic) well inside the north-star interior budget
    /// (~0.15x the ~2000 m orogenic belts = 300 m) and utterly negligible vs the 0.5%R silhouette
    /// budget (~31.8 km). Age reference mirrors <c>CellElevationSystem.CrustAgeReferenceTicks</c>.
    /// </summary>
    public static readonly BirthRoughnessProfile Default = new(
        Noise: new FbmNoiseParams(
            Seed: 7,
            BaseFrequency: 6.0,
            Octaves: 5,
            Lacunarity: 2.0,
            Gain: 0.5,
            Amplitude: 1.0, // overridden by the age ramp — shape only
            Ridged: false), // texture, not mountain chains
        FloorAmplitudeMetres: 60.0,
        CeilingAmplitudeMetres: 200.0,
        AgeReferenceTicks: 10_000_000.0, // matches CellElevationSystem.CrustAgeReferenceTicks (1e7)
        ContinentalModulation: 0.25); // continental crust up to 1.25x; gentle, keeps interiors calm
}
