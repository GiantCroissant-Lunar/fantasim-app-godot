using FantaSim.App.Presentation;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Presentation.Tests;

public sealed class PlateSurfaceEmissionPolicyTests
{
    [Fact]
    public void ShowsVolcanicGlow_keeps_crust_diagnostic_as_bare_rock()
    {
        Assert.False(PlateSurfaceEmissionPolicy.ShowsVolcanicGlow(GlobeViewMode.HypsometricTerrain));
    }

    [Fact]
    public void ShowsVolcanicGlow_keeps_world_surface_features_visible()
    {
        Assert.True(PlateSurfaceEmissionPolicy.ShowsVolcanicGlow(GlobeViewMode.World));
    }
}
