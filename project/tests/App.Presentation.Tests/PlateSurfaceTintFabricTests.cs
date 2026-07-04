using FantaSim.App.Presentation;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Rendering;
using FantaSim.Cartography.Shared;
using Xunit;

namespace App.Presentation.Tests;

public sealed class PlateSurfaceTintFabricTests
{
    [Theory]
    [InlineData(GlobeViewMode.World)]
    [InlineData(GlobeViewMode.HypsometricTerrain)]
    public void ForView_adds_deterministic_tint_fabric_to_terrain_views(GlobeViewMode viewMode)
    {
        var jitter = PlateSurfaceTintFabric.ForView(viewMode);

        Assert.NotNull(jitter);
        var baseColor = new RampColor(0.55, 0.54, 0.51);
        var sample = new CartesianPoint3(0.31, 0.59, 0.74);
        var tinted = jitter!.Apply(sample, baseColor);
        Assert.NotEqual(baseColor, tinted);
        Assert.True(tinted.R >= tinted.G);
        Assert.True(tinted.G >= tinted.B);
    }

    [Fact]
    public void ForView_keeps_plate_identity_flat()
    {
        Assert.Null(PlateSurfaceTintFabric.ForView(GlobeViewMode.PlateIdentity));
    }
}
