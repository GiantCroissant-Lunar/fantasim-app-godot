using FantaSim.App.World.Composition;
using FantaSim.App.World.Rendering;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Atmosphere limb-glow state proof (sub-project W2). The rim exists ONLY because the world's
/// atmosphere sphere actually exists: no atmosphere schedule, no covering regime, or an explicit
/// pre-outgassing regime -> <c>Exists == false</c> (no rim at all -- a world without an atmosphere
/// has no glow; the no-sphere-costume rule). Intensity and tint are per atmosphere regime, chosen
/// from what each atmosphere actually scatters like (a steam/CO2 envelope reads warm; only a
/// coupled climate reads blue) so the rim never costumes another sphere's subject matter.
/// </summary>
public sealed class AtmosphereRimStateMapperTests
{
    private static readonly long Onset = SphereRegimeScheduleDefaults.PlateOnsetTick;
    private static readonly long MagmaOceanEnd = SphereRegimeScheduleDefaults.MagmaOceanEndTick;

    private static SphereRegimeSchedule Schedule(params SphereRegime[] regimes)
        => new(new SphereId("atmosphere"), regimes);

    private static SphereRegime Regime(string id, long start, long end)
        => new(id, start, end, new[] { new LayerId("atmosphere.bulk") });

    [Fact]
    public void Null_schedule_has_no_rim()
    {
        // No atmosphere schedule authored -> the atmosphere sphere does not exist -> no rim.
        var s = AtmosphereRimStateMapper.Map(null, tick: 0);
        Assert.False(s.Exists);
    }

    [Fact]
    public void Empty_schedule_has_no_rim()
    {
        var s = AtmosphereRimStateMapper.Map(Schedule(), tick: 0);
        Assert.False(s.Exists);
    }

    [Fact]
    public void Tick_outside_any_regime_has_no_rim()
    {
        // A schedule exists but no regime covers this tick (gap) -> the atmosphere is not present
        // at this time -> no rim. Honest: the schedule itself says there is no atmosphere here.
        var sched = Schedule(Regime("primordial-steam", 0, MagmaOceanEnd));
        var s = AtmosphereRimStateMapper.Map(sched, tick: MagmaOceanEnd + 1_000);
        Assert.False(s.Exists);
    }

    [Fact]
    public void Pre_outgassing_regime_has_no_rim()
    {
        // An explicit pre-outgassing regime means the atmosphere has not formed yet -> no rim,
        // even though the schedule covers the tick. This is the honest gate for a young accreting
        // world before any degassing.
        var sched = Schedule(
            Regime("pre-outgassing", 0, MagmaOceanEnd),
            Regime("primordial-steam", MagmaOceanEnd, Onset));
        Assert.False(AtmosphereRimStateMapper.Map(sched, tick: 5).Exists);
        // And the next regime, once outgassing begins, DOES glow.
        Assert.True(AtmosphereRimStateMapper.Map(sched, tick: MagmaOceanEnd + 5).Exists);
    }

    [Theory]
    [InlineData("pre-outgassing")]
    [InlineData("no-atmosphere")]
    [InlineData("vacuum")]
    public void No_atmosphere_regime_ids_have_no_rim(string id)
    {
        var sched = Schedule(Regime(id, 0, Onset));
        Assert.False(AtmosphereRimStateMapper.Map(sched, tick: 42).Exists);
    }

    [Fact]
    public void Primordial_steam_regime_exists_with_strong_warm_glow()
    {
        // Magma-ocean outgassed a thick steam envelope: high optical depth -> strong diffuse limb
        // scattering. The tint is WARM (steam scatters broadly, not blue -- blue would claim a
        // coupled climate that does not exist yet).
        var sched = Schedule(Regime("primordial-steam", 0, MagmaOceanEnd));
        var s = AtmosphereRimStateMapper.Map(sched, tick: 1_000);

        Assert.True(s.Exists);
        Assert.InRange(s.Intensity, 0.0, 1.0);
        Assert.True(s.Intensity >= 0.7, $"steam envelope must glow strongly, got {s.Intensity}");
        Assert.True(s.Tint.R >= s.Tint.B,
            $"steam tint must be warm (R>=B), got R={s.Tint.R} B={s.Tint.B}");
    }

    [Fact]
    public void Secondary_co2_regime_exists_with_warm_glow()
    {
        // Dense CO2 atmosphere (stagnant-lid world, Venus-analog): a thick CO2 haze reads
        // warm-cream (Venus is yellow-cream), NOT blue. Intensity is high (optically thick).
        var sched = Schedule(Regime("secondary-co2", 0, Onset));
        var s = AtmosphereRimStateMapper.Map(sched, tick: 1_000);

        Assert.True(s.Exists);
        Assert.InRange(s.Intensity, 0.0, 1.0);
        Assert.True(s.Tint.R >= s.Tint.B,
            $"CO2 tint must be warm (R>=B), got R={s.Tint.R} B={s.Tint.B}");
    }

    [Fact]
    public void Coupled_climate_regime_exists_with_blue_glow()
    {
        // Mobile-plate world with a coupled climate + hydrological cycle: the classic Rayleigh
        // blue limb. This is the ONLY blue tint -- blue is the Earth-climate signal, reserved for
        // the regime that has earned it, so it never costumes an earlier atmosphere.
        var sched = Schedule(Regime("coupled-climate", 0, SphereRegime.OpenEnd));
        var s = AtmosphereRimStateMapper.Map(sched, tick: Onset + 1_000);

        Assert.True(s.Exists);
        Assert.InRange(s.Intensity, 0.0, 1.0);
        Assert.True(s.Tint.B > s.Tint.R,
            $"coupled-climate tint must be blue (B>R), got R={s.Tint.R} B={s.Tint.B}");
    }

    [Fact]
    public void Only_coupled_climate_tint_is_blue()
    {
        // No-costume proof: the pre-climate regimes must NOT read blue. Steam and CO2 scatter warm;
        // only the coupled climate earns the blue limb. Asserting R>=B for the warm regimes and B>R
        // for climate locks the honesty of the mapping.
        var steam = AtmosphereRimStateMapper.Map(Schedule(Regime("primordial-steam", 0, 1)), 0);
        var co2 = AtmosphereRimStateMapper.Map(Schedule(Regime("secondary-co2", 0, 1)), 0);
        var climate = AtmosphereRimStateMapper.Map(Schedule(Regime("coupled-climate", 0, 1)), 0);

        Assert.True(steam.Tint.R >= steam.Tint.B, "steam must be warm");
        Assert.True(co2.Tint.R >= co2.Tint.B, "CO2 must be warm");
        Assert.True(climate.Tint.B > climate.Tint.R, "coupled-climate must be blue");
    }

    [Fact]
    public void Steam_is_thicker_than_co2_is_thicker_than_climate()
    {
        // Optical-depth honesty: a degassing steam envelope is optically thicker than a mature CO2
        // atmosphere, which is thicker than a breathable coupled climate (thinner at the limb).
        // Intensity tracks optical depth, so it decreases across the regimes.
        var steam = AtmosphereRimStateMapper.Map(Schedule(Regime("primordial-steam", 0, 1)), 0);
        var co2 = AtmosphereRimStateMapper.Map(Schedule(Regime("secondary-co2", 0, 1)), 0);
        var climate = AtmosphereRimStateMapper.Map(Schedule(Regime("coupled-climate", 0, 1)), 0);

        Assert.True(steam.Intensity > co2.Intensity, $"steam {steam.Intensity} must exceed CO2 {co2.Intensity}");
        Assert.True(co2.Intensity > climate.Intensity, $"CO2 {co2.Intensity} must exceed climate {climate.Intensity}");
    }

    [Fact]
    public void Unknown_atmosphere_regime_still_exists()
    {
        // Forward-compat: a schedule that authors an atmosphere regime we do not recognize still
        // means the atmosphere exists (the schedule is the source of truth). The rim shows with a
        // neutral intensity/tint rather than vanishing.
        var sched = Schedule(Regime("methane-haze", 0, Onset));
        var s = AtmosphereRimStateMapper.Map(sched, tick: 10);

        Assert.True(s.Exists);
        Assert.InRange(s.Intensity, 0.0, 1.0);
    }

    [Fact]
    public void All_known_regimes_have_intensity_in_unit_range()
    {
        string[] ids = { "primordial-steam", "secondary-co2", "coupled-climate" };
        foreach (var id in ids)
        {
            var s = AtmosphereRimStateMapper.Map(Schedule(Regime(id, 0, 1)), 0);
            Assert.True(s.Exists);
            Assert.InRange(s.Intensity, 0.0, 1.0);
        }
    }

    [Fact]
    public void Default_atmosphere_schedule_phases_through_all_three_regimes()
    {
        // Integration against the LIVE default schedule: the rim regime follows the atmosphere
        // schedule's phase boundaries exactly (which align with the geosphere windows).
        var sched = SphereRegimeScheduleDefaults.AtmosphereFor(Onset);

        var atMagma = AtmosphereRimStateMapper.Map(sched, tick: 1_000);
        Assert.True(atMagma.Exists);
        Assert.True(atMagma.Tint.R >= atMagma.Tint.B, "magma-ocean era -> steam -> warm");

        var atStagnant = AtmosphereRimStateMapper.Map(sched, tick: MagmaOceanEnd + 1_000);
        Assert.True(atStagnant.Exists);
        Assert.True(atStagnant.Tint.R >= atStagnant.Tint.B, "stagnant-lid era -> CO2 -> warm");

        var atMobile = AtmosphereRimStateMapper.Map(sched, tick: Onset + 1_000);
        Assert.True(atMobile.Exists);
        Assert.True(atMobile.Tint.B > atMobile.Tint.R, "mobile-plate era -> coupled climate -> blue");
    }

    [Fact]
    public void Default_schedule_intensity_ordering()
    {
        var sched = SphereRegimeScheduleDefaults.AtmosphereFor(Onset);
        var atMagma = AtmosphereRimStateMapper.Map(sched, tick: 1_000);
        var atStagnant = AtmosphereRimStateMapper.Map(sched, tick: MagmaOceanEnd + 1_000);
        var atMobile = AtmosphereRimStateMapper.Map(sched, tick: Onset + 1_000);

        Assert.True(atMagma.Intensity > atStagnant.Intensity);
        Assert.True(atStagnant.Intensity > atMobile.Intensity);
    }

    [Fact]
    public void None_state_is_a_sensible_default()
    {
        Assert.False(AtmosphereRimState.None.Exists);
        Assert.Equal(0.0, AtmosphereRimState.None.Intensity);
    }
}
