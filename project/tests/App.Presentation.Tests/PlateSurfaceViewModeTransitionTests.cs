using FantaSim.App.Presentation;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Presentation.Tests;

public sealed class PlateSurfaceViewModeTransitionTests
{
    [Fact]
    public void ShouldRebuild_when_layer_focus_enters_active_view_from_inactive_regime()
    {
        Assert.True(PlateSurfaceViewModeTransition.ShouldRebuild(
            GlobeViewMode.Inactive,
            GlobeViewMode.HypsometricTerrain));
    }

    [Fact]
    public void ShouldRebuild_when_switching_between_active_diagnostic_views()
    {
        Assert.True(PlateSurfaceViewModeTransition.ShouldRebuild(
            GlobeViewMode.World,
            GlobeViewMode.PlateIdentity));
    }

    [Fact]
    public void Should_not_rebuild_when_entering_inactive_regime()
    {
        Assert.False(PlateSurfaceViewModeTransition.ShouldRebuild(
            GlobeViewMode.HypsometricTerrain,
            GlobeViewMode.Inactive));
    }

    [Fact]
    public void Should_not_rebuild_when_mode_is_unchanged()
    {
        Assert.False(PlateSurfaceViewModeTransition.ShouldRebuild(
            GlobeViewMode.HypsometricTerrain,
            GlobeViewMode.HypsometricTerrain));
    }
}
