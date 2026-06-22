using FantaSim.App.World.Composition;
using FantaSim.Atmosphere.Genesis.Core;
using Xunit;

namespace App.World.Composition.Tests;

public class SphereRegimeScheduleTests
{
    [Fact]
    public void GeosphereFor_BoundariesAndPlateVisibility()
    {
        long onset = SphereRegimeScheduleDefaults.PlateOnsetTick; // 1e8 for default forcing
        var sched = SphereRegimeScheduleDefaults.GeosphereFor(onset);

        Assert.Equal("magma-ocean", sched.RegimeAt(0)!.RegimeId);
        Assert.False(sched.RegimeAt(0)!.ShowsPlateFeatures);
        Assert.Equal("stagnant-lid", sched.RegimeAt(SphereRegimeScheduleDefaults.MagmaOceanEndTick)!.RegimeId);
        Assert.False(sched.RegimeAt(onset - 1)!.ShowsPlateFeatures);
        Assert.Equal("mobile-plate", sched.RegimeAt(onset)!.RegimeId);
        Assert.True(sched.RegimeAt(onset)!.ShowsPlateFeatures);
    }

    [Fact]
    public void StrongerForcing_MovesOnsetEarlier()
    {
        long baseOnset = SphereRegimeScheduleDefaults.PlateOnsetTickFor(new AtmosphereForcing(1.0));
        long strongOnset = SphereRegimeScheduleDefaults.PlateOnsetTickFor(new AtmosphereForcing(2.0));
        Assert.Equal(100_000_000, baseOnset);
        Assert.True(strongOnset < baseOnset);
    }
}
