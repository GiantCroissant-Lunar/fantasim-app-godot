using FantaSim.App.Presentation;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Presentation.Tests;

public sealed class BoundarySectionVisibilityTests
{
    [Theory]
    [InlineData(GlobeViewMode.World, true)]
    [InlineData(GlobeViewMode.HypsometricTerrain, true)]
    [InlineData(GlobeViewMode.PlateIdentity, false)]
    [InlineData(GlobeViewMode.Inactive, false)]
    public void ShouldShow_keeps_sections_visible_for_world_and_crust_views(
        GlobeViewMode viewMode,
        bool expected)
    {
        Assert.Equal(expected, BoundarySectionVisibility.ShouldShow(showsPlateFeatures: true, viewMode));
    }

    [Theory]
    [InlineData(GlobeViewMode.World)]
    [InlineData(GlobeViewMode.HypsometricTerrain)]
    public void ShouldShow_hides_sections_when_plate_features_are_gated_off(GlobeViewMode viewMode)
    {
        Assert.False(BoundarySectionVisibility.ShouldShow(showsPlateFeatures: false, viewMode));
    }
}
