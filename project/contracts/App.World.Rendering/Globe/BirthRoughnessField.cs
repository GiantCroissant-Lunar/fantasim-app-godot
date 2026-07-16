using System;
using UnifyMaths;
using UnifyMaths.Numerics;

namespace FantaSim.App.World.Globe;

/// <summary>
/// The pure birth-roughness elevation field (directive 3d). A deterministic, seeded fBm noise sampled
/// in the PLATE-MATERIAL frame, amplitude-CONDITIONED on crust age (declared monotone ramp, floor at
/// age 0) and gently modulated by continental fraction. This is the derived stand-in that finally
/// RIDES PLATES — retiring the sphere-fixed interior-fabric defect (the texture stayed put while plates
/// drifted) — by sampling at the plate-material (onset-frame) coordinate instead of the current sphere
/// position.
/// </summary>
/// <remarks>
/// <para><b>Pure-function identity (the determinism constraint).</b> <see cref="Sample"/> is a pure
/// function of (material-frame direction, crust age, continental fraction, profile). No global state, no
/// <see cref="Random"/>, no time. Identical inputs yield bit-identical metres; different seeds give a
/// different but equally deterministic field. The caller (the world plugin's
/// <c>PlateFrameSampler</c>) supplies the material-frame direction — the onset-frame unit center of the
/// plate material that currently occupies the vertex — so the texture is a property OF the crust, not
/// of the sphere.</para>
///
/// <para><b>Why <see cref="FbmNoise3"/> and not the cartography <c>NoiseRelief</c>.</b>
/// <see cref="FbmNoise3"/>/<see cref="GradientNoise3"/> is the house noise primitive (a bit-exact port
/// of <c>NoiseRelief</c>, shipped 2026-07-16). Birth roughness consumes it directly rather than
/// vendoring a copy or routing through the cartography wrapper, per the locked plan.</para>
///
/// <para><b>Onset continuity (design decision 5).</b> At plate onset the per-plate rotation is identity,
/// so the plate-material coordinate equals the current (base-sphere) coordinate: the mobile-plate field
/// inherits the base-frame texture at birth with no jump. Pre-onset (stagnant-lid) the material frame
/// is the base sphere frame (identity) by the same construction. The field is therefore continuous
/// across the stagnant-lid → mobile-plate transition for unmoved plates — asserted in
/// <c>BirthRoughnessFieldTests</c>.</para>
/// </remarks>
public static class BirthRoughnessField
{
    /// <summary>
    /// Birth-roughness elevation in METRES at a vertex. Pure: identical inputs → bit-identical output.
    /// The noise is sampled at <paramref name="materialFrameUnitDirection"/> (the plate-material /
    /// onset-frame coordinate), scaled by the age ramp and the continental modulator.
    /// </summary>
    /// <param name="materialFrameUnitDirection">Unit direction in the plate-material (onset) frame — the coordinate the crust's material occupies, which rides the plate. Non-unit vectors are re-normalized by <see cref="FbmNoise3.SampleDirection"/>.</param>
    /// <param name="crustAgeTicks">Crust age at the sampled tick (canonical ticks). Age 0 → floor; saturates at <see cref="BirthRoughnessProfile.AgeReferenceTicks"/>.</param>    /// <param name="continentalFraction">Continental fraction in [0,1] (0 = oceanic, 1 = continental). Gently boosts amplitude per <see cref="BirthRoughnessProfile.ContinentalModulation"/>.</param>
    /// <param name="profile">The declared birth-roughness profile.</param>
    /// <returns>Signed elevation in metres (interior fabric — small-but-present texture, not mountains).</returns>
    public static double Sample(
        Vector3D materialFrameUnitDirection,
        double crustAgeTicks,
        double continentalFraction,
        BirthRoughnessProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        double amplitude = AgeRampAmplitude(crustAgeTicks, profile) * ContinentalModulator(continentalFraction, profile.ContinentalModulation);
        if (amplitude <= 0.0)
            return 0.0;
        // Override the shape noise's amplitude with the age+cf-conditioned value. The shape fields
        // (seed/frequency/octaves/lacunarity/gain/ridged) are preserved; only the scale is recomputed.
        var p = profile.Noise with { Amplitude = amplitude };
        return FbmNoise3.SampleDirection(
            materialFrameUnitDirection.X,
            materialFrameUnitDirection.Y,
            materialFrameUnitDirection.Z,
            p);
    }

    /// <summary>
    /// The DECLARED monotone age ramp: amplitude at crust age 0 equals <see cref="BirthRoughnessProfile.FloorAmplitudeMetres"/>,
    /// grows LINEARLY and non-decreasingly to <see cref="BirthRoughnessProfile.CeilingAmplitudeMetres"/>
    /// at <see cref="BirthRoughnessProfile.AgeReferenceTicks"/>, then saturates. This is the
    /// terrain-diffusion adoption: newly solidified crust carries faint base texture; older crust
    /// carries accumulated battering. Floor-at-age-0 is the plan's locked requirement.
    /// </summary>
    public static double AgeRampAmplitude(double crustAgeTicks, BirthRoughnessProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        double floor = profile.FloorAmplitudeMetres;
        double ceiling = profile.CeilingAmplitudeMetres;
        if (ceiling < floor)
            ceiling = floor; // never decrease — a mis-authored profile cannot invert the ramp
        if (profile.AgeReferenceTicks <= 0)
            return ceiling;
        if (crustAgeTicks <= 0)
            return floor;
        double t = (double)crustAgeTicks / profile.AgeReferenceTicks;
        if (t >= 1.0)
            return ceiling;
        return floor + ((ceiling - floor) * t);
    }

    /// <summary>
    /// Continental-fraction amplitude modulator: <c>1 + gain * clamp(cf, 0, 1)</c>. Oceanic crust
    /// (cf≈0) is unmodulated; continental crust (cf≈1) is boosted by up to <paramref name="gain"/>+1.
    /// <paramref name="gain"/> of 0 disables modulation (oceanic and continental equally rough).
    /// </summary>
    public static double ContinentalModulator(double continentalFraction, double gain)
    {
        double cf = continentalFraction;
        if (cf < 0.0) cf = 0.0;
        else if (cf > 1.0) cf = 1.0;
        return 1.0 + (gain * cf);
    }
}
