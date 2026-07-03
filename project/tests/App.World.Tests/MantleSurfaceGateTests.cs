using FantaSim.App.World.Composition;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Mantle visibility gate (world-view fix, 2026-07-03): at mobile-plate the plate caps are the
/// watertight planet surface (cell reassignment tiles the drifted sphere), so the mantle sphere
/// underneath must not render. Terrain below -4000 m displaces beneath the 0.96 mantle radius,
/// and with the mantle drawn the whole face-on disk read as the teal mantle ball instead of the
/// bare-rock terrain — the "no landform story face-on" failure.
/// </summary>
public sealed class MantleSurfaceGateTests
{
    [Theory]
    [InlineData(GlobeViewMode.World)]
    [InlineData(GlobeViewMode.PlateIdentity)]
    [InlineData(GlobeViewMode.HypsometricTerrain)]
    public void Mantle_hidden_when_plate_caps_own_the_surface(GlobeViewMode viewMode)
    {
        Assert.False(MantleSurfaceGate.IsVisible(viewMode, platesShown: true, hasPlateSurface: true));
    }

    [Fact]
    public void Mantle_visible_when_regime_is_not_mobile_plate()
    {
        // Magma-ocean / stagnant-lid: the mantle owns the look (regime surface materials).
        Assert.True(MantleSurfaceGate.IsVisible(GlobeViewMode.Inactive, platesShown: true, hasPlateSurface: true));
    }

    [Fact]
    public void Mantle_visible_when_plate_features_are_hidden()
    {
        // A regime that shows no plate features leaves the mantle as the only surface.
        Assert.True(MantleSurfaceGate.IsVisible(GlobeViewMode.World, platesShown: false, hasPlateSurface: true));
    }

    [Fact]
    public void Mantle_visible_when_no_plate_surface_was_built()
    {
        // No globe snapshot -> no caps -> the mantle is the only thing standing in for the planet.
        Assert.True(MantleSurfaceGate.IsVisible(GlobeViewMode.World, platesShown: true, hasPlateSurface: false));
    }
}
