using FantaSim.App.World.Composition;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// View-mode resolution proof (sub-project P1 + W1): the focused timeline layer selects the globe
/// VIEW at mobile-plate. The WORLD view is the default (no selection or non-plate/non-crust layer)
/// — a waterless world reads as a world (§5c); the plate-identity diagnostic is reached by focusing
/// <c>geosphere.plate</c>; the hypsometric crust diagnostic by focusing <c>geosphere.crust</c>;
/// non-mobile-plate regimes are inactive (layer focus does not apply).
/// </summary>
public sealed class GlobeViewModeResolverTests
{
    [Fact]
    public void Null_regime_is_inactive()
    {
        Assert.Equal(GlobeViewMode.Inactive, GlobeViewModeResolver.Resolve(null, null));
    }

    [Theory]
    [InlineData("magma-ocean")]
    [InlineData("stagnant-lid")]
    [InlineData("unknown-regime")]
    public void Non_mobile_plate_regimes_are_inactive_regardless_of_layer(string regimeId)
    {
        Assert.Equal(GlobeViewMode.Inactive,
            GlobeViewModeResolver.Resolve(regimeId, null));
        Assert.Equal(GlobeViewMode.Inactive,
            GlobeViewModeResolver.Resolve(regimeId, new TimelineLayerSelection("geosphere", "geosphere.plate")));
        Assert.Equal(GlobeViewMode.Inactive,
            GlobeViewModeResolver.Resolve(regimeId, new TimelineLayerSelection("geosphere", "geosphere.crust")));
    }

    [Fact]
    public void Mobile_plate_no_selection_defaults_to_world()
    {
        Assert.Equal(GlobeViewMode.World,
            GlobeViewModeResolver.Resolve("mobile-plate", null));
    }

    [Fact]
    public void Mobile_plate_geosphere_plate_layer_is_continents_by_default()
    {
        var sel = new TimelineLayerSelection("geosphere", "geosphere.plate");
        Assert.Equal(GlobeViewMode.Continents,
            GlobeViewModeResolver.Resolve("mobile-plate", sel));
    }

    [Fact]
    public void Mobile_plate_geosphere_plate_layer_identity_override_selects_plate_identity()
    {
        var sel = new TimelineLayerSelection("geosphere", "geosphere.plate");
        Assert.Equal(GlobeViewMode.PlateIdentity,
            GlobeViewModeResolver.Resolve("mobile-plate", sel, "identity"));
    }

    [Fact]
    public void Mobile_plate_geosphere_crust_layer_is_hypsometric_terrain()
    {
        var sel = new TimelineLayerSelection("geosphere", "geosphere.crust");
        Assert.Equal(GlobeViewMode.HypsometricTerrain,
            GlobeViewModeResolver.Resolve("mobile-plate", sel));
    }

    [Fact]
    public void Mobile_plate_unknown_layer_defaults_to_world()
    {
        var sel = new TimelineLayerSelection("geosphere", "geosphere.mystery");
        Assert.Equal(GlobeViewMode.World,
            GlobeViewModeResolver.Resolve("mobile-plate", sel));
    }

    [Fact]
    public void Mobile_plate_non_geosphere_layer_defaults_to_world()
    {
        var sel = new TimelineLayerSelection("atmosphere", "atmosphere.weather");
        Assert.Equal(GlobeViewMode.World,
            GlobeViewModeResolver.Resolve("mobile-plate", sel));
    }

    [Fact]
    public void Mobile_plate_empty_layer_id_defaults_to_world()
    {
        var sel = new TimelineLayerSelection("geosphere", "");
        Assert.Equal(GlobeViewMode.World,
            GlobeViewModeResolver.Resolve("mobile-plate", sel));
    }
}
