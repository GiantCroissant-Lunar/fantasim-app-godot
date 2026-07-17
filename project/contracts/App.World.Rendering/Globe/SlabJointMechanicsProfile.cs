using System.Collections.Generic;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World.Globe;

/// <summary>
/// The DECLARED slab-joint mechanics parameters (assembled-world slice 2): the eye-tuned magnitudes
/// that turn a convergent / divergent / transform joint classification into slab-edge GEOMETRY.
/// Mirrors the <see cref="WorldSurfacePresentationProfile"/> pattern — every look number is a named
/// record field, never an inline literal.
/// </summary>
/// <remarks>
/// <para><b>Every value here is EYE-TUNED, not physical.</b> The north-star verdict is that the
/// assembled slabs must make the mechanics LEGIBLE ("how mountain, trench is formed. How plate A is
/// under plate b and moved"); the magnitudes are chosen so the underride, the mountain onset, and the
/// wider divergent crack READ at planet scale, then left to the lead + user EYE gate to fine-tune.
/// The agent does not self-certify the look.</para>
///
/// <para><b>Non-interpenetration is structural, not tuned.</b> The effective convergent dip is grown
/// past <see cref="SubductionDipUnitRadius"/> when a slab is thicker than the declared visual dip, so
/// the subducting top always clears the overriding bottom by at least <see cref="MinClearanceUnitRadius"/>.
/// That floor is the one non-aesthetic knob.</para>
///
/// <para>Pure, Godot-free, no wall-clock. Two profiles with equal fields are equal.</para>
/// </remarks>
/// <param name="SubductionDipUnitRadius">Radial inward dip of the SUBDUCTING slab's edge band at the
/// dive line (unit-radius units). Eye-tuned default <see cref="DefaultSubductionDipUnitRadius"/> = 0.06:
/// the subducting top drops to ~0.94R against the ~0.96R overriding bottom, so the underride reads
/// from orbit. Grown structurally when the slab is thicker than this.</param>
/// <param name="OverridingMarginRaiseUnitRadius">Radial outward raise of the OVERRIDING slab's edge
/// band — the mountain-piling onset (unit-radius units). Eye-tuned default 0.012. Also applied to
/// BOTH sides of a convergent collision (symmetric uplift).</param>
/// <param name="EdgeBandHalfWidthRad">Angular half-width of the boundary-adjacent band the ramp acts
/// over (radians). The dip/raise/widen fade smoothly from full at the arc to zero at this distance,
/// keeping each slab watertight (a pure position deformation never edits topology). Eye-tuned default
/// 0.12 rad (~7 degrees, ~1-2 cell rings at frequency 3).</param>
/// <param name="DivergentGapMultiplier">Scales the joint gap at a DIVERGENT boundary (> 1 widens).
/// The extra separation reuses the SAME centroid-direction translation the base joint gap uses, so a
/// divergent joint reads as a wider crack of the same family. Eye-tuned default 2.5.</param>
/// <param name="MinClearanceUnitRadius">Structural floor: the subducting top must clear the
/// overriding bottom by at least this much in the overlap zone. Non-aesthetic. Default 0.004
/// (half the default joint gap — a visible trench-line, not a knife edge).</param>
public sealed record SlabJointMechanicsProfile(
    double SubductionDipUnitRadius,
    double OverridingMarginRaiseUnitRadius,
    double EdgeBandHalfWidthRad,
    double DivergentGapMultiplier,
    double MinClearanceUnitRadius)
{
    /// <summary>Eye-tuned default subduction dip (0.06R).</summary>
    public const double DefaultSubductionDipUnitRadius = 0.06;

    /// <summary>Eye-tuned default overriding margin raise (0.03R; raised from 0.012R after the 2026-07-17 eye-fail — collision edges must carry visible bulk).</summary>
    public const double DefaultOverridingMarginRaiseUnitRadius = 0.03;

    /// <summary>Eye-tuned default edge-band half-width (0.12 rad).</summary>
    public const double DefaultEdgeBandHalfWidthRad = 0.12;

    /// <summary>Eye-tuned default divergent gap multiplier (2.5x the default gap).</summary>
    public const double DefaultDivergentGapMultiplier = 2.5;

    /// <summary>Structural default minimum clearance (0.004R).</summary>
    public const double DefaultMinClearanceUnitRadius = 0.004;

    /// <summary>The default profile: eye-tuned magnitudes that make the three joint kinds legible.</summary>
    public static SlabJointMechanicsProfile Default { get; } = new(
        SubductionDipUnitRadius: DefaultSubductionDipUnitRadius,
        OverridingMarginRaiseUnitRadius: DefaultOverridingMarginRaiseUnitRadius,
        EdgeBandHalfWidthRad: DefaultEdgeBandHalfWidthRad,
        DivergentGapMultiplier: DefaultDivergentGapMultiplier,
        MinClearanceUnitRadius: DefaultMinClearanceUnitRadius);
}
