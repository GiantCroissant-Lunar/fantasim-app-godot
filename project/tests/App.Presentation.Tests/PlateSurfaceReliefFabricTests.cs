using FantaSim.App.Presentation;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Globe;
using Xunit;

namespace App.Presentation.Tests;

public sealed class PlateSurfaceReliefFabricTests
{
    [Fact]
    public void ForView_gives_crust_diagnostic_a_visible_dry_rock_fabric()
    {
        var crust = PlateSurfaceReliefFabric.ForView(GlobeViewMode.HypsometricTerrain);

        Assert.True(crust.Amplitude > GlobePlateSurfaces.DefaultPeaks.Amplitude);
        Assert.True(crust.Amplitude >= 12_000.0);
        Assert.True(crust.BaseFrequency >= 14.0);
        Assert.True(crust.Octaves >= 6);
    }

    [Fact]
    public void ForView_keeps_plate_identity_flat()
    {
        var identity = PlateSurfaceReliefFabric.ForView(GlobeViewMode.PlateIdentity);

        Assert.Equal(0.0, identity.Amplitude);
    }
}
