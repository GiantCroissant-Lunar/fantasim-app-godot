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
}
