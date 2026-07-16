using System.Collections.Generic;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World.Globe;

/// <summary>
/// One plate pair's resolved joint classification consumed by the slab-edge shaper (assembled-world
/// slice 2, vault/specs/2026-07-16-assembled-world-northstar.md clause 3). This is the NARROW SEAM
/// between <b>joint classification</b> (which pair, which kind, which side subducts) and <b>edge
/// shaping</b> (geometry): the shaper reads ONLY this record and never the boundary-data sources.
/// </summary>
/// <remarks>
/// <para>A sibling dispatch is building a fuller classifier; the lead session swaps it in at
/// integration. Until then <see cref="SlabJointClassifier"/> produces these records from the
/// existing boundary data (<see cref="PlateBoundaryArc"/> + <see cref="Composition.BoundarySectionDocument"/>),
/// and tests construct them directly so the geometry proofs are independent of how classifications
/// are produced.</para>
/// <para>Pure, Godot-free value. Two classifications with equal fields are equal.</para>
/// </remarks>
/// <param name="PlateA">The lower plate id of the pair (<c>PlateA &lt; PlateB</c>), matching <see cref="PlateBoundaryArc"/>.</param>
/// <param name="PlateB">The higher plate id of the pair.</param>
/// <param name="Kind">Motion-derived boundary type at the snapshot tick. <see cref="PlateBoundaryKind.Inactive"/>
/// joints are ignored by the shaper (no geometry change).</param>
/// <param name="SubductingPlateId">For a convergent SUBDUCTION only: the plate id that is subducting
/// (down-going). Null for collision, divergent, transform, or a convergent pair whose polarity the
/// pipeline has not yet resolved (the shaper then treats it as collision-free symmetric uplift).</param>
/// <param name="IsCollision">True for a continent-continent convergent boundary (symmetric uplift,
/// no trench/arc). Both sides raise; neither subducts.</param>
/// <param name="ArcPoints">Ordered unit-sphere points along the joint's boundary arc (at least two).
/// The shaper localises each slab's edge band by angular distance to these points.</param>
public sealed record SlabJointClassification(
    int PlateA,
    int PlateB,
    PlateBoundaryKind Kind,
    int? SubductingPlateId,
    bool IsCollision,
    IReadOnlyList<GlobeVec3> ArcPoints);

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

    /// <summary>Eye-tuned default overriding margin raise (0.012R).</summary>
    public const double DefaultOverridingMarginRaiseUnitRadius = 0.012;

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
