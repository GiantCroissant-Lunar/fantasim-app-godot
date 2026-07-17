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
///
/// <para><b>Slice 3 — subduction TONGUE (assembled-world slice 3).</b> The tongue parameters are
/// declared as init-only properties (NOT positional record parameters) so the existing 5-argument
/// constructor and <see cref="Default"/> factory stay byte-compatible with slices 1+2. They carry
/// the eye-tuned magnitudes for the watertight thick strip the subducting slab grows along a
/// convergent non-collision joint — the "diving tongue beneath" of the v4/v5 reference
/// (<c>vault/reference/2026-07-17-assembled-world-image-prompt.md</c>).</para>
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

    /// <summary>
    /// Eye-tuned default lateral REACH of the subduction tongue (unit-radius units). The tongue's
    /// far edge reaches this far across the joint path toward / under the overriding side — the
    /// "diving tongue beneath" of the v4 reference. 0.05R reads as a clear underride lip from orbit
    /// without crossing the whole overriding plate. The tongue ramps smoothly from 0 reach at the
    /// plate edge to the full reach at the far edge. Eye-tuned; the lead + user EYE gate tunes this.
    /// </summary>
    public const double DefaultTongueReachUnitRadius = 0.05;

    /// <summary>
    /// Eye-tuned default radial DROP of the subduction tongue at its far edge (unit-radius units),
    /// with a smooth ramp from 0 at the plate edge. Same scale as
    /// <see cref="DefaultSubductionDipUnitRadius"/> (0.06R): the tongue descends past the dipped rim
    /// so its far edge sits visibly below the overriding plate's underside — the lit gap of the v5
    /// overlap reference. Grown structurally (same pattern as <see cref="SubductionDipUnitRadius"/>)
    /// when the slab is thicker than the declared drop, so the tongue top always clears the
    /// overriding bottom by at least <see cref="MinClearanceUnitRadius"/>.
    /// </summary>
    public const double DefaultTongueDropUnitRadius = 0.06;

    /// <summary>
    /// Eye-tuned default number of strip subdivisions along the reach direction (the tongue's
    /// lateral extent). 2 segments give a smooth ramp silhouette without ballooning the index
    /// buffer; the lead + user EYE gate may raise it for closer views. Must be >= 1.
    /// </summary>
    public const int DefaultTongueSegments = 2;

    /// <summary>
    /// Lateral reach of the subduction tongue across the joint path toward the overriding side
    /// (unit-radius units). Eye-tuned; see <see cref="DefaultTongueReachUnitRadius"/>.
    /// </summary>
    public double TongueReachUnitRadius { get; init; } = DefaultTongueReachUnitRadius;

    /// <summary>
    /// Radial drop of the subduction tongue at its far edge (unit-radius units), ramped from the
    /// plate edge. Eye-tuned; see <see cref="DefaultTongueDropUnitRadius"/>.
    /// </summary>
    public double TongueDropUnitRadius { get; init; } = DefaultTongueDropUnitRadius;

    /// <summary>
    /// Number of strip subdivisions along the tongue's reach direction (>= 1). Eye-tuned; see
    /// <see cref="DefaultTongueSegments"/>.
    /// </summary>
    public int TongueSegments { get; init; } = DefaultTongueSegments;

    /// <summary>The default profile: eye-tuned magnitudes that make the three joint kinds legible.</summary>
    public static SlabJointMechanicsProfile Default { get; } = new(
        SubductionDipUnitRadius: DefaultSubductionDipUnitRadius,
        OverridingMarginRaiseUnitRadius: DefaultOverridingMarginRaiseUnitRadius,
        EdgeBandHalfWidthRad: DefaultEdgeBandHalfWidthRad,
        DivergentGapMultiplier: DefaultDivergentGapMultiplier,
        MinClearanceUnitRadius: DefaultMinClearanceUnitRadius);
}
