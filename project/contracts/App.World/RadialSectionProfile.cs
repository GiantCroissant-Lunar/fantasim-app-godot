namespace FantaSim.App.World;

/// <summary>
/// ONE radial source of truth for how the planet's interior layers are sized for DISPLAY (D3). Real
/// quantities (crust thickness, lithosphere lid, CMB radius, planet radius) live next to two
/// independent scaling knobs — <see cref="CrustThicknessExaggeration"/> (amplifies ONLY crust
/// thickness so the slabs read) and <see cref="MantleDepthScale"/> (scales the mantle's depth) —
/// and the derived helpers (<see cref="DisplayedCrustFraction(double)"/>, <see cref="DisplayedMantleDepthFraction"/>,
/// <see cref="DisplayedCrustToMantleRatio"/>) make the displayed crust:mantle proportion legible
/// and, by default, RATIO-LOCKED.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Before D3 the crust thickness was invisible: the M-B plate solids
/// reused the SURFACE relief exaggeration (<c>PlateSolidBuilder.Build</c> took the document's
/// <c>VerticalExaggeration</c>, ~3e-5), so 30 km of crust mapped to ~0.0009R — nothing. The mantle
/// CMB was a hardcoded 0.55R. There was no crust↔mantle proportion anywhere; it fell out by
/// accident. This profile replaces both with a single declared source of truth.</para>
///
/// <para><b>RATIO LOCK (user directive, verbatim intent).</b> The crust thickness gets "its OWN
/// scaling so it is amplified — while PRESERVING the ratio to mantle thickness, which has a
/// DIFFERENT scaling." The default knobs below yield a fixed displayed crust:mantle proportion
/// (see <see cref="DisplayedCrustToMantleRatio"/>): 30 km × 8.0 / 6,371 km ≈ 0.0377R of crust
/// against (1 − 0.55) × 1.0 = 0.45R of mantle depth, i.e. ratio ≈ 0.0837. A unit test
/// (<c>RadialSectionProfileTests</c>) PINS this default; any future knob change that silently breaks
/// the proportion fails that test and forces a conscious decision (re-tune the knobs, or re-pin the
/// declared ratio if the design intent genuinely moved).</para>
///
/// <para><b>Consumers.</b> M-B plate-slab thickness (<c>PlateSolidBuilder.Build</c>) reads
/// <see cref="CrustThicknessExaggeration"/>; the mantle interior's core-sphere geometric radius
/// reads <see cref="DisplayedCoreSphereRadius"/> (CMB × mantle scale) instead of the old 0.55
/// literal. The cutaway strata (<c>CutawayStratumProfile</c>) is a follow-up consumer (its own
/// exaggeration stays separate for now — see D3 stage notes). The volumetric FIELD machinery
/// (<c>MantleIsosurfaceExtractor</c>, <c>MantleAnomalyField</c>) is METHOD-LOCKED and untouched:
/// only the presentation geometry moves to this profile.</para>
///
/// <para>The real-quantity defaults mirror the cutaway strata's declared defaults (crust 30 km,
/// lithosphere lid 90 km) and the planet-radius constant on <see cref="PlanetLayerProjectionProfile"/>
/// so this profile and the cutaway never disagree on the physical anchors. The literal values are
/// duplicated here (not referenced across assemblies) because this contract sits one tier below
/// the rendering contract that owns <c>CutawayStratumProfile</c>.</para>
///
/// <para>Pure, Godot-free, no wall-clock. Two profiles with equal inputs are equal.</para>
/// </remarks>
public sealed record RadialSectionProfile(
    /// <summary>Real crust thickness in metres. Mirrors <c>CutawayStratumProfile.DefaultCrustThicknessMetres</c> = 30 km.</summary>
    double DefaultCrustThicknessMetres = 30_000.0,

    /// <summary>Real lithosphere lid thickness in metres. Mirrors <c>CutawayStratumProfile.DefaultLithosphereLidThicknessMetres</c> = 90 km.</summary>
    double LithosphereLidMetres = 90_000.0,

    /// <summary>Core-mantle boundary as a fraction of the planet radius (0.55 — the canonical Earth-like CMB).</summary>
    double CmbRadiusFraction = 0.55,

    /// <summary>Planet radius in metres. Mirrors <see cref="PlanetLayerProjectionProfile.EarthLikePlanetRadiusMetres"/> = 6,371 km.</summary>
    double PlanetRadiusMetres = PlanetLayerProjectionProfile.EarthLikePlanetRadiusMetres,

    /// <summary>
    /// Amplifies ONLY the crust thickness for display (default 8.0). At 8.0, 30 km of crust maps to
    /// ~0.0377R — clearly visible slab walls against the 0.45R mantle depth. Independent of the
    /// surface relief exaggeration (which amplifies elevation, not thickness).
    /// </summary>
    double CrustThicknessExaggeration = 8.0,

    /// <summary>
    /// Scales the mantle's displayed depth (default 1.0 — mantle keeps physical depth). The mantle
    /// extends from the CMB (<see cref="CmbRadiusFraction"/>) to the surface; this knob scales that
    /// extent without moving the CMB fraction itself.
    /// </summary>
    double MantleDepthScale = 1.0)
{
    /// <summary>Canonical Earth-like CMB at 0.55R.</summary>
    public const double DefaultCmbRadiusFraction = 0.55;

    /// <summary>Default crust-thickness exaggeration: 30 km × 8.0 / 6,371 km ≈ 0.0377R (visible slab walls).</summary>
    public const double DefaultCrustThicknessExaggeration = 8.0;

    /// <summary>Default mantle depth scale: 1.0 (mantle keeps physical depth).</summary>
    public const double DefaultMantleDepthScale = 1.0;

    /// <summary>
    /// Default explode factor for the separated-crust slabs in the mantle-interior LAYER view (D1),
    /// as a fraction of <c>PlateSolidBuilder.DefaultMaxOffset</c>. 0.4 = 40% of the max radial
    /// offset: the slabs clearly detach but still read as a sphere (Sketchfab reference). Distinct
    /// from the <c>render.exploded</c> agent look-dev knob, which is unbounded [0,1].
    /// </summary>
    public const double MantleLayerExplodeFactor = 0.4;

    /// <summary>
    /// Displayed crust thickness as a fraction of the planet radius, after applying
    /// <see cref="CrustThicknessExaggeration"/>. <c>thicknessMetres * CrustThicknessExaggeration / PlanetRadiusMetres</c>.
    /// The default 30 km × 8.0 / 6,371 km ≈ 0.0377R.
    /// </summary>
    public double DisplayedCrustFraction(double thicknessMetres)
        => thicknessMetres * CrustThicknessExaggeration / PlanetRadiusMetres;

    /// <summary>
    /// The metres-to-unit-radius scale that <c>PlateSolidBuilder.Build</c> expects for its thickness
    /// depth parameter — i.e. <c>CrustThicknessExaggeration / PlanetRadiusMetres</c>. This is the
    /// THICKNESS depth scale (units 1/metres), distinct from both the dimensionless
    /// <see cref="CrustThicknessExaggeration"/> knob and the surface relief metres-to-unit-radius
    /// scale. The builder applies it as <c>depth = thk_metres * scale</c>.
    /// </summary>
    public double ThicknessDepthScale() => CrustThicknessExaggeration / PlanetRadiusMetres;

    /// <summary>Displayed crust fraction at the profile's own <see cref="DefaultCrustThicknessMetres"/>.</summary>
    public double DisplayedCrustFraction() => DisplayedCrustFraction(DefaultCrustThicknessMetres);

    /// <summary>
    /// Displayed mantle depth as a fraction of the planet radius: from the CMB to the surface,
    /// scaled by <see cref="MantleDepthScale"/>. <c>(1.0 - CmbRadiusFraction) * MantleDepthScale</c>.
    /// Default: (1 − 0.55) × 1.0 = 0.45R.
    /// </summary>
    public double DisplayedMantleDepthFraction() => (1.0 - CmbRadiusFraction) * MantleDepthScale;

    /// <summary>
    /// Displayed core-sphere geometric radius (the dark backdrop the mantle anomaly volumes read
    /// against), as a fraction of the unit sphere. <c>CmbRadiusFraction * MantleDepthScale</c>.
    /// The mantle-interior view's <c>BuildCoreSphere</c> reads this instead of the old 0.55 literal.
    /// </summary>
    public double DisplayedCoreSphereRadius() => CmbRadiusFraction * MantleDepthScale;

    /// <summary>
    /// The displayed crust:mantle proportion (the RATIO LOCK). Default ≈ 0.0377 / 0.45 ≈ 0.0837.
    /// A unit test pins this value; any knob change that silently breaks the proportion fails it.
    /// </summary>
    public double DisplayedCrustToMantleRatio()
    {
        double mantle = DisplayedMantleDepthFraction();
        return mantle > 0.0 ? DisplayedCrustFraction() / mantle : double.PositiveInfinity;
    }

    /// <summary>The default profile (all defaults above). The one consumers reach for unless tuning.</summary>
    public static RadialSectionProfile Default { get; } = new();
}
