using System.Linq;
using FantaSim.App.World.Composition;
using FantaSim.Atmosphere.Genesis.Core;
using Xunit;

namespace App.World.Composition.Tests;

/// <summary>
/// Task 3b: verifies OnsetRoster.SeedPlatesAt gate + ID alignment with PlatesAt.
/// </summary>
public class SeedPlatesAtTests
{
    private static OnsetRoster BuildRoster()
    {
        long onset = SphereRegimeScheduleDefaults.PlateOnsetTickFor(AtmosphereForcing.Default);
        return OnsetRoster.Build(worldSeed: 2024, onsetTick: onset, tessellationFrequency: 3);
    }

    [Fact]
    public void SeedPlatesAt_EmptyBeforeOnset()
    {
        long onset = SphereRegimeScheduleDefaults.PlateOnsetTickFor(AtmosphereForcing.Default);
        var roster = OnsetRoster.Build(worldSeed: 2024, onsetTick: onset, tessellationFrequency: 3);

        var seeds = roster.SeedPlatesAt(onset - 1);

        Assert.Empty(seeds);
    }

    [Fact]
    public void SeedPlatesAt_NPlatesAtOnset_NGreaterThanOrEqualTo3()
    {
        long onset = SphereRegimeScheduleDefaults.PlateOnsetTickFor(AtmosphereForcing.Default);
        var roster = OnsetRoster.Build(worldSeed: 2024, onsetTick: onset, tessellationFrequency: 3);

        var seeds = roster.SeedPlatesAt(onset);

        Assert.True(seeds.Count >= 3,
            $"Expected at least 3 seed plates at onset; got {seeds.Count}");
    }

    [Fact]
    public void SeedPlatesAt_SameCountAsAfterOnset()
    {
        long onset = SphereRegimeScheduleDefaults.PlateOnsetTickFor(AtmosphereForcing.Default);
        var roster = OnsetRoster.Build(worldSeed: 2024, onsetTick: onset, tessellationFrequency: 3);

        var at = roster.SeedPlatesAt(onset);
        var after = roster.SeedPlatesAt(onset + 50_000_000);

        // SeedPlatesAt is time-stable — same list at and after onset.
        Assert.Equal(at.Count, after.Count);
    }

    [Fact]
    public void SeedPlatesAt_IntegerIdsMatchPlatesAt()
    {
        // PlateTopologyState.Plates keys are PlateId("0"), PlateId("1"), …, PlateId("N-1").
        // SeedPlatesAt should return Plate.PlateId values 0..N-1 in the same upwelling order.
        long onset = SphereRegimeScheduleDefaults.PlateOnsetTickFor(AtmosphereForcing.Default);
        var roster = OnsetRoster.Build(worldSeed: 2024, onsetTick: onset, tessellationFrequency: 3);

        var seeds = roster.SeedPlatesAt(onset);
        var state = roster.PlatesAt(onset);

        int n = seeds.Count;
        Assert.Equal(n, state.Plates.Count);

        // Seeds are ordered 0..N-1 by upwelling index.
        for (int i = 0; i < n; i++)
            Assert.Equal(i, seeds[i].PlateId);

        // Every seed ID maps to a PlateId in the topology state.
        var stateIds = state.Plates.Keys.Select(k => int.Parse(k.Value)).OrderBy(x => x).ToList();
        var seedIds  = seeds.Select(p => p.PlateId).OrderBy(x => x).ToList();
        Assert.Equal(seedIds, stateIds);
    }

    [Fact]
    public void SeedPlatesAt_PlaceholderPolesHaveZeroRate()
    {
        // LidFractureAtOnset gives each seed EulerPole(axis, 0.0) — a placeholder pole.
        long onset = SphereRegimeScheduleDefaults.PlateOnsetTickFor(AtmosphereForcing.Default);
        var roster = OnsetRoster.Build(worldSeed: 2024, onsetTick: onset, tessellationFrequency: 3);

        foreach (var plate in roster.SeedPlatesAt(onset))
            Assert.Equal(0.0, plate.Pole.AngularRate);
    }
}
