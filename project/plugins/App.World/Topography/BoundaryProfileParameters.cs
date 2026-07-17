namespace FantaSim.App.World.Topography;

/// <summary>
/// World-parameter shape numbers for boundary-profile topography (P4). Every value is a PARAMETER on
/// the control plane (surfaced via <see cref="FantaSim.App.World.GenerationGraph.WorldGenerationRenderOptions"/>
/// and the <c>world.options</c> node catalog), never a code constant: a different world legitimately has
/// a different tectonic expression. <see cref="Default"/> is calibrated to Earth-like reference relief
/// (USGS cross-sections / GEBCO relief order of magnitude) on the <c>CellElevationSystem</c> scale
/// (ContinentalAmp=1000, OceanDeepening=1000; the base elevation tier spans roughly +500 continental
/// interior to −1500 old abyssal ocean).
///
/// <para>Angular extents are GREAT-CIRCLE radians on the unit sphere, so they are frame- and
/// tessellation-frequency-independent: a half-width of 0.06 rad spans the same physical band whether the
/// globe has 1280 or 5120 cells. At frequency 3 (1280 cells) one cell subtends ≈0.099 rad, so the default
/// convergent trench (half-width 0.06) + arc (setback 0.12) pair spans ≈2 cells; at frequency 4 it spans
/// ≈4 — which is why the presentation LOD bump makes the profiles read.</para>
/// </summary>
public sealed record BoundaryProfileParameters(
    // ── Convergent (subduction: asymmetric trench + arc) ───────────────────────────────────────
    /// <summary>Additional elevation of the trench floor on the subducting (down-going) side. Negative.
    /// Default −2000 puts the trench near −3500 on top of the −1500 abyssal base.</summary>
    double ConvergentTrenchDepth,
    /// <summary>Great-circle half-width of the trench band (rad). The trench tapers to zero by this distance.</summary>
    double ConvergentTrenchHalfWidthRad,
    /// <summary>Uplift of the volcanic arc on the overriding side. Positive. Default +1500 clears the
    /// continental interior (+500) so the arc reads as a ridge.</summary>
    double ConvergentArcHeight,
    /// <summary>Setback of the arc peak from the boundary (rad): the arc crests this far onto the overriding plate.</summary>
    double ConvergentArcSetbackRad,
    /// <summary>Half-width of the arc band (rad), measured from the setback peak.</summary>
    double ConvergentArcHalfWidthRad,

    // ── Convergent (continent–continent collision: symmetric uplift, no subduction) ────────────
    /// <summary>Symmetric uplift for a continent–continent collision boundary (no trench/arc). Default +2000.</summary>
    double ConvergentCollisionHeight,
    /// <summary>Half-width of the collision uplift band (rad).</summary>
    double ConvergentCollisionHalfWidthRad,

    // ── Divergent (symmetric swell + axial rift notch) ─────────────────────────────────────────
    /// <summary>Height of the symmetric ridge swell flanks. Default +800. Far-field subsidence is already
    /// handled by CellElevationSystem's crust-age deepening.</summary>
    double DivergentSwellHeight,
    /// <summary>Half-width of the swell band (rad).</summary>
    double DivergentSwellHalfWidthRad,
    /// <summary>Depth of the narrow axial rift graben at the spreading axis. Negative. Default −400 notches
    /// the swell crest so the axis sits below the flanks (+800 − 400 = +400).</summary>
    double DivergentRiftNotchDepth,
    /// <summary>Half-width of the axial rift notch (rad). Narrower than the swell.</summary>
    double DivergentRiftHalfWidthRad,

    // ── Transform (subtle narrow-band linear scarps) ───────────────────────────────────────────
    /// <summary>Half-amplitude of the transform scarps. Small relative to convergent/divergent.</summary>
    double TransformScarpAmplitude,
    /// <summary>Half-width of the transform scarp band (rad).</summary>
    double TransformHalfWidthRad,
    /// <summary>Longitudinal scarp wavelength measured in arc-sample points along the boundary polyline
    /// (the boundary arcs are pre-subdivided at 16 points/segment). Period 32 ≈ 2 segments/scarp.</summary>
    double TransformScarpPeriodPoints)
{
    /// <summary>Visual shell thickness in normalized planet-radius units.</summary>
    public double VisualCrustThicknessUnitRadius { get; init; } = 0.075;

    /// <summary>Tangential reach of the attached down-going material lobe.</summary>
    public double ConvergentSlabUnderlapLengthUnitRadius { get; init; } = 0.18;

    /// <summary>Inward depth of the attached down-going material lobe.</summary>
    public double ConvergentSlabDepthUnitRadius { get; init; } = 0.14;

    /// <summary>Additional visual root depth beneath the overriding wedge.</summary>
    public double ConvergentOverridingRootDepthUnitRadius { get; init; } = 0.04;

    /// <summary>Medium-scale cone height on the existing volcanic-arc foundation.</summary>
    public double ConvergentVolcanoConeHeight { get; init; } = 1800.0;

    /// <summary>World-stable phase period for the volcanic cone chain.</summary>
    public double ConvergentVolcanoPeriodPoints { get; init; } = 42.0;

    /// <summary>Exponent controlling individual cone sharpness.</summary>
    public double ConvergentVolcanoSharpness { get; init; } = 8.0;

    /// <summary>
    /// Earth-like reference profile. See the type-level doc for the calibration rationale per field.
    /// </summary>
    public static BoundaryProfileParameters Default { get; } = new(
        ConvergentTrenchDepth: -2000.0,
        ConvergentTrenchHalfWidthRad: 0.06,
        ConvergentArcHeight: 1500.0,
        ConvergentArcSetbackRad: 0.12,
        ConvergentArcHalfWidthRad: 0.05,
        ConvergentCollisionHeight: 2000.0,
        ConvergentCollisionHalfWidthRad: 0.08,
        DivergentSwellHeight: 800.0,
        DivergentSwellHalfWidthRad: 0.10,
        DivergentRiftNotchDepth: -400.0,
        DivergentRiftHalfWidthRad: 0.02,
        TransformScarpAmplitude: 250.0,
        TransformHalfWidthRad: 0.04,
        TransformScarpPeriodPoints: 32.0);

    /// <summary>
    /// All amplitudes zeroed so the profile contribution vanishes (regression pin: zero parameters
    /// reproduce the pre-profile <c>CellElevationSystem.Derive</c> elevation exactly). Widths are kept
    /// non-zero so the shape math stays well-defined (no division by zero).
    /// </summary>
    public static BoundaryProfileParameters Zero { get; } = Default with
    {
        ConvergentTrenchDepth = 0.0,
        ConvergentArcHeight = 0.0,
        ConvergentCollisionHeight = 0.0,
        ConvergentSlabUnderlapLengthUnitRadius = 0.0,
        ConvergentSlabDepthUnitRadius = 0.0,
        ConvergentOverridingRootDepthUnitRadius = 0.0,
        ConvergentVolcanoConeHeight = 0.0,
        DivergentSwellHeight = 0.0,
        DivergentRiftNotchDepth = 0.0,
        TransformScarpAmplitude = 0.0,
    };
}
