using FantaSim.App.Presentation;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Presentation.Tests;

public sealed class PlateSurfaceMaterialTuningTests
{
    [Fact]
    public void ForView_lifts_crust_diagnostic_lighting_above_world_view()
    {
        var crust = PlateSurfaceMaterialTuning.ForView(GlobeViewMode.HypsometricTerrain);
        var world = PlateSurfaceMaterialTuning.ForView(GlobeViewMode.World);

        Assert.True(crust.LightFloor >= 0.16f);
        Assert.True(crust.AlbedoGain > world.AlbedoGain);
        Assert.True(crust.LightFloor > world.LightFloor);
    }

    [Fact]
    public void ForView_keeps_crust_diagnostic_from_washing_out_facets()
    {
        var tuning = PlateSurfaceMaterialTuning.ForView(GlobeViewMode.HypsometricTerrain);

        Assert.True(tuning.AlbedoGain <= 1.05f);
        Assert.True(tuning.LightFloor <= 0.22f);
    }

    [Fact]
    public void ForView_keeps_world_view_more_directionally_lit()
    {
        var tuning = PlateSurfaceMaterialTuning.ForView(GlobeViewMode.World);

        Assert.Equal(1.0f, tuning.AlbedoGain);
        Assert.True(tuning.LightFloor <= 0.10f);
    }
}
