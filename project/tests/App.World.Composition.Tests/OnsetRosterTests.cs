using FantaSim.App.World.Composition;
using FantaSim.Atmosphere.Genesis.Core;
using Xunit;

namespace App.World.Composition.Tests;

public class OnsetRosterTests
{
    [Fact]
    public void Roster_EmptyBeforeOnset_NPlatesAtAndAfter()
    {
        long onset = SphereRegimeScheduleDefaults.PlateOnsetTickFor(AtmosphereForcing.Default);
        var roster = OnsetRoster.Build(worldSeed: 2024, onsetTick: onset, tessellationFrequency: 3);

        Assert.Empty(roster.PlatesAt(onset - 1).Plates);
        Assert.True(roster.PlatesAt(onset).Plates.Count >= 3);
        Assert.Equal(roster.PlatesAt(onset).Plates.Count, roster.PlatesAt(onset + 50_000_000).Plates.Count);
    }

    [Fact]
    public void Build_default_rate_equals_explicit_calibrated_rate()
    {
        long onset = SphereRegimeScheduleDefaults.PlateOnsetTickFor(AtmosphereForcing.Default);
        var implicitRoster = OnsetRoster.Build(worldSeed: 2024, onsetTick: onset, tessellationFrequency: 3);
        var explicitRoster = OnsetRoster.Build(
            worldSeed: 2024,
            onsetTick: onset,
            tessellationFrequency: 3,
            angularDriftPerMegaAnnum: OnsetRoster.DefaultAngularDriftPerMegaAnnum);

        Assert.Equal(implicitRoster.SeedPlatesAt(onset), explicitRoster.SeedPlatesAt(onset));
    }

    [Fact]
    public void Build_custom_rate_changes_seed_plate_motion()
    {
        long onset = SphereRegimeScheduleDefaults.PlateOnsetTickFor(AtmosphereForcing.Default);
        var calibrated = OnsetRoster.Build(worldSeed: 2024, onsetTick: onset, tessellationFrequency: 3);
        var lively = OnsetRoster.Build(
            worldSeed: 2024,
            onsetTick: onset,
            tessellationFrequency: 3,
            angularDriftPerMegaAnnum: 0.017);

        Assert.NotEqual(calibrated.SeedPlatesAt(onset), lively.SeedPlatesAt(onset));
    }
}
