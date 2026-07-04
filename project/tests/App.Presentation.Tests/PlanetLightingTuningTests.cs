using FantaSim.App.Presentation;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Presentation.Tests;

public sealed class PlanetLightingTuningTests
{
    [Fact]
    public void ForView_keeps_world_view_warm_for_bare_rock()
    {
        var tuning = PlanetLightingTuning.ForView(GlobeViewMode.World);

        Assert.True(tuning.SunColor.R > tuning.SunColor.G);
        Assert.True(tuning.SunColor.G > tuning.SunColor.B);
        Assert.True(tuning.AmbientColor.R > tuning.AmbientColor.G);
        Assert.True(tuning.AmbientColor.G > tuning.AmbientColor.B);
    }

    [Fact]
    public void ForView_uses_neutral_light_for_crust_diagnostic()
    {
        var tuning = PlanetLightingTuning.ForView(GlobeViewMode.HypsometricTerrain);

        Assert.True(Math.Abs(tuning.SunColor.R - tuning.SunColor.G) <= 0.02f);
        Assert.True(Math.Abs(tuning.SunColor.G - tuning.SunColor.B) <= 0.04f);
        Assert.True(Math.Abs(tuning.AmbientColor.R - tuning.AmbientColor.G) <= 0.02f);
        Assert.True(Math.Abs(tuning.AmbientColor.G - tuning.AmbientColor.B) <= 0.03f);
        Assert.True(tuning.AmbientLightEnergy >= 0.45f);
    }
}
