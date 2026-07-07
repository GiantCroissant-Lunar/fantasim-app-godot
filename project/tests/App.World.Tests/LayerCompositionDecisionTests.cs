using System.Collections.Generic;
using FantaSim.App.World.Composition;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// D5 stacked-layer composition rules (vault/specs/2026-07-07-...-directives.md, section D5). The
/// active set resolves to a deterministic <see cref="LayerCompositionDecision"/>: derived
/// GlobeViewMode for binder plumbing, mantle-interior mount flag, and the surface-coloring owner
/// that drives the plate caps + separated-slab tops. Every combo the spec lists is asserted here.
/// </summary>
public sealed class LayerCompositionDecisionTests
{
    private const string MobilePlate = "mobile-plate";

    private static IReadOnlyList<TimelineLayerSelection> Set(params TimelineLayerSelection[] layers) => layers;

    private static TimelineLayerSelection Geo(string layerId) => new("geosphere", layerId);

    // Non-mobile-plate regimes are Inactive regardless of the active set (layer focus does not
    // change the mantle-owned look of magma-ocean / stagnant-lid).
    [Theory]
    [InlineData(null)]
    [InlineData("magma-ocean")]
    [InlineData("stagnant-lid")]
    public void Non_mobile_plate_regime_is_inactive(string? regimeId)
    {
        var d = GlobeViewModeResolver.ResolveComposition(regimeId, Set(Geo("geosphere.mantle")));
        Assert.Equal(GlobeViewMode.Inactive, d.DerivedViewMode);
        Assert.False(d.MountMantleInterior);
        Assert.Equal(SurfaceColoringKind.World, d.SurfaceColoring);
        Assert.False(d.TerrainRelief);
    }

    [Fact]
    public void Empty_set_is_world_view()
    {
        var d = GlobeViewModeResolver.ResolveComposition(MobilePlate, Set());
        Assert.Equal(GlobeViewMode.World, d.DerivedViewMode);
        Assert.False(d.MountMantleInterior);
        Assert.Equal(SurfaceColoringKind.World, d.SurfaceColoring);
        Assert.True(d.TerrainRelief);
    }

    [Fact]
    public void Mantle_alone_mounts_interior_with_world_coloring()
    {
        var d = GlobeViewModeResolver.ResolveComposition(MobilePlate, Set(Geo("geosphere.mantle")));
        Assert.Equal(GlobeViewMode.MantleInterior, d.DerivedViewMode);
        Assert.True(d.MountMantleInterior);
        Assert.Equal(SurfaceColoringKind.World, d.SurfaceColoring);
        Assert.True(d.TerrainRelief);
    }

    [Fact]
    public void Crust_alone_is_hypsometric()
    {
        var d = GlobeViewModeResolver.ResolveComposition(MobilePlate, Set(Geo("geosphere.crust")));
        Assert.Equal(GlobeViewMode.HypsometricTerrain, d.DerivedViewMode);
        Assert.False(d.MountMantleInterior);
        Assert.Equal(SurfaceColoringKind.HypsometricTerrain, d.SurfaceColoring);
        Assert.True(d.TerrainRelief);
    }

    [Fact]
    public void Plate_alone_is_continents_by_default()
    {
        var d = GlobeViewModeResolver.ResolveComposition(MobilePlate, Set(Geo("geosphere.plate")));
        Assert.Equal(GlobeViewMode.Continents, d.DerivedViewMode);
        Assert.False(d.MountMantleInterior);
        Assert.Equal(SurfaceColoringKind.Continents, d.SurfaceColoring);
        Assert.False(d.TerrainRelief);
    }

    [Fact]
    public void Plate_alone_identity_override_is_plate_identity()
    {
        var d = GlobeViewModeResolver.ResolveComposition(MobilePlate, Set(Geo("geosphere.plate")), "identity");
        Assert.Equal(GlobeViewMode.PlateIdentity, d.DerivedViewMode);
        Assert.False(d.MountMantleInterior);
        Assert.Equal(SurfaceColoringKind.PlateIdentity, d.SurfaceColoring);
        Assert.False(d.TerrainRelief);
    }

    // D5 combo: Mantle+Crust => interior + slabs whose TOPS use terrain coloring.
    [Fact]
    public void Mantle_plus_crust_mounts_interior_with_hypsometric_slab_tops()
    {
        var d = GlobeViewModeResolver.ResolveComposition(MobilePlate, Set(Geo("geosphere.mantle"), Geo("geosphere.crust")));
        Assert.Equal(GlobeViewMode.MantleInterior, d.DerivedViewMode);
        Assert.True(d.MountMantleInterior);
        Assert.Equal(SurfaceColoringKind.HypsometricTerrain, d.SurfaceColoring);
        Assert.True(d.TerrainRelief);
    }

    // D5 combo: Mantle+Plate => interior + slabs with identity/continents tops.
    [Fact]
    public void Mantle_plus_plate_mounts_interior_with_continents_slab_tops()
    {
        var d = GlobeViewModeResolver.ResolveComposition(MobilePlate, Set(Geo("geosphere.mantle"), Geo("geosphere.plate")));
        Assert.Equal(GlobeViewMode.MantleInterior, d.DerivedViewMode);
        Assert.True(d.MountMantleInterior);
        Assert.Equal(SurfaceColoringKind.Continents, d.SurfaceColoring);
        Assert.False(d.TerrainRelief);
    }

    // D5 combo: Plate+Crust => identity wins the surface, terrain relief geometry stays. The
    // resolver encodes the declared intent (TerrainRelief=true) even though the first-slice binder
    // realizes the identity coloring and leaves the combined relief for a follow-up.
    [Fact]
    public void Plate_plus_crust_continents_coloring_with_declared_terrain_relief()
    {
        var d = GlobeViewModeResolver.ResolveComposition(MobilePlate, Set(Geo("geosphere.plate"), Geo("geosphere.crust")));
        Assert.Equal(GlobeViewMode.Continents, d.DerivedViewMode);
        Assert.False(d.MountMantleInterior);
        Assert.Equal(SurfaceColoringKind.Continents, d.SurfaceColoring);
        Assert.True(d.TerrainRelief);
    }

    // All three stacked: mantle mounts the interior; plate wins the surface coloring over crust.
    [Fact]
    public void Mantle_plate_crust_mounts_interior_with_continents_coloring()
    {
        var d = GlobeViewModeResolver.ResolveComposition(MobilePlate, Set(Geo("geosphere.mantle"), Geo("geosphere.plate"), Geo("geosphere.crust")));
        Assert.Equal(GlobeViewMode.MantleInterior, d.DerivedViewMode);
        Assert.True(d.MountMantleInterior);
        Assert.Equal(SurfaceColoringKind.Continents, d.SurfaceColoring);
        Assert.False(d.TerrainRelief);
    }

    // Layers outside the geosphere do not affect the globe surface decision.
    [Fact]
    public void Atmosphere_only_layer_falls_back_to_world()
    {
        var d = GlobeViewModeResolver.ResolveComposition(MobilePlate, Set(new TimelineLayerSelection("atmosphere", "atmosphere.weather")));
        Assert.Equal(GlobeViewMode.World, d.DerivedViewMode);
        Assert.False(d.MountMantleInterior);
        Assert.Equal(SurfaceColoringKind.World, d.SurfaceColoring);
        Assert.True(d.TerrainRelief);
    }

    // Unknown geosphere layer ids fall back to the World coloring.
    [Fact]
    public void Unknown_geosphere_layer_falls_back_to_world()
    {
        var d = GlobeViewModeResolver.ResolveComposition(MobilePlate, Set(Geo("geosphere.mystery")));
        Assert.Equal(GlobeViewMode.World, d.DerivedViewMode);
        Assert.False(d.MountMantleInterior);
        Assert.Equal(SurfaceColoringKind.World, d.SurfaceColoring);
        Assert.True(d.TerrainRelief);
    }

    // Insertion order does not change the decision (the resolver is order-invariant; ordering only
    // matters for the primary reported by the controller).
    [Fact]
    public void Order_invariant_crust_then_plate_still_continents()
    {
        var d = GlobeViewModeResolver.ResolveComposition(MobilePlate, Set(Geo("geosphere.crust"), Geo("geosphere.plate")));
        Assert.Equal(SurfaceColoringKind.Continents, d.SurfaceColoring);
        Assert.Equal(GlobeViewMode.Continents, d.DerivedViewMode);
    }
}
