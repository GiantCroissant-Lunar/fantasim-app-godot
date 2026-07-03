using System.Collections.Generic;

using FantaSim.App.World.Composition;

namespace FantaSim.App.World.Rendering;

/// <summary>
/// The presentation state of the world's atmosphere LIMB GLOW (sub-project W2): a thin fresnel rim
/// that exists ONLY because the world's atmosphere sphere actually exists. The rim is honest about
/// the atmosphere's real state -- it vanishes entirely when there is no atmosphere (no-sphere-costume
/// rule: a view belonging to sphere X must not render imagery that reads as sphere Y's subject matter;
/// here the "imagery" is the glow itself, and it is gated on the atmosphere sphere's existence).
/// </summary>
/// <param name="Exists">
/// Whether the atmosphere exists at this tick. <c>false</c> when there is no atmosphere schedule, no
/// covering regime, or an explicit pre-outgassing regime. When <c>false</c> the host renders NO rim.
/// </param>
/// <param name="Intensity">
/// Limb glow strength in <c>[0,1]</c>, tracking the atmosphere's optical depth per regime. Clamped;
/// the host scales the fresnel term by this.
/// </param>
/// <param name="Tint">
/// Rim color, chosen from what the regime's atmosphere actually scatters like (warm for steam/CO2,
/// blue only for a coupled climate) so the tint never costumes another sphere.
/// </param>
public readonly record struct AtmosphereRimState(bool Exists, double Intensity, RampColor Tint)
{
    /// <summary>The no-atmosphere state: the host hides the rim entirely.</summary>
    public static readonly AtmosphereRimState None =
        new(Exists: false, Intensity: 0.0, Tint: new RampColor(0.0, 0.0, 0.0));
}

/// <summary>
/// Pure <c>(atmosphereSchedule, tick)</c> -&gt; <see cref="AtmosphereRimState"/> mapper (sub-project W2).
/// Godot-free (lives in the T3 world plugin alongside <see cref="CrustAccentMapper"/>) so the regime
/// -&gt; rim mapping is unit-testable. Mirrors the <see cref="RegimeSurfaceResolver"/> pattern: a pure,
/// data-driven-ready mapping the host consumes.
///
/// <para><b>Honesty / no-costume gating.</b> The rim exists only when the atmosphere sphere exists:
/// a null schedule, a tick no regime covers, or an explicit no-atmosphere regime id yields
/// <see cref="AtmosphereRimState.None"/>. A world without an atmosphere has no glow.</para>
///
/// <para><b>Intensity = optical depth.</b> A degassing steam envelope is optically thicker than a
/// mature CO2 atmosphere, which is thicker than a breathable coupled climate -- so intensity decreases
/// across the regimes. This is physically motivated, not decorative.</para>
///
/// <para><b>Tint = what that atmosphere scatters like.</b> The classic blue limb is the Earth-climate
/// signal (Rayleigh scattering in a coupled, hydrological atmosphere); it is reserved for the regime
/// that has earned it. A steam or CO2 envelope scatters warm, so its tint is warm -- giving those eras
/// a blue limb would be costume (the no-sphere-costume rule, applied to tint).</para>
///
/// <para><b>Data-driven-ready.</b> All regime ids, intensities and tints are grouped constants below.
/// They are candidates for promotion to world/view parameters (a JSON <c>atmosphere-rim</c> profile)
/// once the control plane wires parameter fields -- recorded as a follow-up, not built here.</para>
/// </summary>
public static class AtmosphereRimStateMapper
{
    // --- Atmosphere regime ids ------------------------------------------------------------
    // Mirror SphereRegimeScheduleDefaults.AtmosphereFor (primordial-steam / secondary-co2 /
    // coupled-climate), authored by the world bundle's atmosphere schedule.
    internal const string RegimePrimordialSteam = "primordial-steam";
    internal const string RegimeSecondaryCo2 = "secondary-co2";
    internal const string RegimeCoupledClimate = "coupled-climate";

    // --- No-atmosphere regime ids ---------------------------------------------------------
    // A schedule may author a regime meaning the atmosphere has not formed / does not exist. Such a
    // regime covers the tick yet yields NO rim -- the honest gate for a young accreting world before
    // any degassing, or an airless body. Ordinal match (regime ids are stable lowercase strings).
    private static readonly HashSet<string> NoAtmosphereRegimeIds = new(System.StringComparer.Ordinal)
    {
        "pre-outgassing",
        "no-atmosphere",
        "vacuum",
    };

    // --- Per-regime intensity (optical depth proxy) --------------------------------------
    // A thick steam envelope (magma-ocean era) scatters most at the limb; a dense CO2 atmosphere
    // (stagnant-lid era) somewhat less; a coupled climate (mobile-plate era) is the thinnest at the
    // limb. Monotonically decreasing across the regimes -> intensity tracks the real optical depth.
    private const double IntensityPrimordialSteam = 0.85;
    private const double IntensitySecondaryCo2 = 0.65;
    private const double IntensityCoupledClimate = 0.50;
    private const double IntensityUnknownRegime = 0.50; // forward-compat neutral

    // --- Per-regime tint (what that atmosphere scatters like) -----------------------------
    // primordial-steam: warm pale amber. Steam scatters broadly; a magma-ocean outgassed envelope
    //   reads warm, not blue (blue would claim a coupled climate that does not exist yet).
    // secondary-co2: warm cream. A thick CO2 atmosphere (Venus-analog haze) reads yellow-cream, and
    //   CO2 Rayleigh is too weak to carry a blue limb honestly for a dry world.
    // coupled-climate: blue. The classic Rayleigh blue limb -- earned by the regime with a hydrological
    //   cycle. This is the ONLY blue tint, so it never costumes an earlier atmosphere.
    private static readonly RampColor TintPrimordialSteam = new(0.96, 0.86, 0.66);
    private static readonly RampColor TintSecondaryCo2 = new(0.92, 0.84, 0.70);
    private static readonly RampColor TintCoupledClimate = new(0.46, 0.68, 1.00);
    private static readonly RampColor TintUnknownRegime = new(0.80, 0.80, 0.80); // neutral pale white

    /// <summary>
    /// Maps the atmosphere regime active at <paramref name="tick"/> to a rim state. Returns
    /// <see cref="AtmosphereRimState.None"/> when the atmosphere does not exist (null schedule, no
    /// covering regime, or a no-atmosphere regime id).
    /// </summary>
    public static AtmosphereRimState Map(SphereRegimeSchedule? atmosphereSchedule, long tick)
    {
        if (atmosphereSchedule is null)
            return AtmosphereRimState.None;

        var regime = atmosphereSchedule.RegimeAt(tick);
        if (regime is null)
            return AtmosphereRimState.None;

        if (NoAtmosphereRegimeIds.Contains(regime.RegimeId))
            return AtmosphereRimState.None;

        return regime.RegimeId switch
        {
            RegimePrimordialSteam => new AtmosphereRimState(true, IntensityPrimordialSteam, TintPrimordialSteam),
            RegimeSecondaryCo2 => new AtmosphereRimState(true, IntensitySecondaryCo2, TintSecondaryCo2),
            RegimeCoupledClimate => new AtmosphereRimState(true, IntensityCoupledClimate, TintCoupledClimate),
            // Forward-compat: a schedule that authors an unrecognized atmosphere regime still means the
            // atmosphere exists (the schedule is the source of truth). A neutral intensity/tint keeps
            // the rim honest without vanishing on a future regime the mapper has not learned.
            _ => new AtmosphereRimState(true, IntensityUnknownRegime, TintUnknownRegime),
        };
    }
}
