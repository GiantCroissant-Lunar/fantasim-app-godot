using FantaSim.App.Presentation;
using FantaSim.App.World.Composition;
using Godot;
using Xunit;

namespace App.Presentation.Tests;

public sealed class PlateSurfaceMaterialTuningTests
{
    [Fact]
    public void ForView_makes_crust_diagnostic_more_facet_driven_than_world_view()
    {
        var crust = PlateSurfaceMaterialTuning.ForView(GlobeViewMode.HypsometricTerrain);
        var world = PlateSurfaceMaterialTuning.ForView(GlobeViewMode.World);

        Assert.True(crust.AlbedoGain <= world.AlbedoGain);
        Assert.True(crust.LightFloor >= 0.18f);
        Assert.True(crust.LightFloor <= 0.24f);
        Assert.True(crust.WrapStrength < world.WrapStrength);
        Assert.True(crust.LightContrast <= world.LightContrast + 0.08f);
        Assert.True(crust.ColorBalance.Z > crust.ColorBalance.X);
    }

    [Fact]
    public void ForView_keeps_crust_diagnostic_from_washing_out_facets()
    {
        var tuning = PlateSurfaceMaterialTuning.ForView(GlobeViewMode.HypsometricTerrain);

        Assert.True(tuning.AlbedoGain <= 1.0f);
        Assert.True(tuning.LightFloor >= 0.18f);
        Assert.True(tuning.LightFloor <= 0.24f);
        Assert.True(tuning.WrapStrength <= 0.45f);
        Assert.True(tuning.LightContrast <= 1.08f);
        Assert.True(tuning.ColorBalance.Z <= 1.06f);
    }

    [Fact]
    public void ForView_keeps_world_view_more_directionally_lit()
    {
        var tuning = PlateSurfaceMaterialTuning.ForView(GlobeViewMode.World);

        Assert.Equal(1.0f, tuning.AlbedoGain);
        Assert.True(tuning.LightFloor <= 0.10f);
        Assert.Equal(1.0f, tuning.WrapStrength);
        Assert.Equal(1.0f, tuning.LightContrast);
        Assert.Equal(Vector3.One, tuning.ColorBalance);
    }
}
