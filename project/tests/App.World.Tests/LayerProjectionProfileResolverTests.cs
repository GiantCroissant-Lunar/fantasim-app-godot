using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class LayerProjectionProfileResolverTests
{
    [Fact]
    public void ResolveForView_HypsometricTerrain_UsesCrustProjectionScaleAndAdaptivePolicy()
    {
        var profile = PlanetLayerProjectionProfile.Crust(
            metresToUnitRadius: 0.00002,
            surfaceSubdivision: SurfaceSubdivisionMode.Adaptive,
            adaptiveSubdivisionMaxDepth: 1,
            adaptiveSubdivisionEdgeHeightDelta: 0.012);
        var document = new PlanetPresentationDocument(
            PlanetId: "p",
            SourceWorldId: "w",
            ReferenceTick: 0,
            Revision: 1,
            Layers: Array.Empty<PlanetPresentationLayer>(),
            RenderEntities: Array.Empty<RenderEntityDto>())
        {
            LayerProjectionProfiles = new[] { profile },
        };

        var resolved = LayerProjectionProfileResolver.ResolveForView(
            document,
            GlobeViewMode.HypsometricTerrain,
            worldMetresToUnitRadius: 0.00012,
            worldHeightExponent: 0.5);

        Assert.Equal("geosphere.crust", resolved.Profile.LayerId);
        Assert.Equal(1.0, resolved.BaseRadius);
        Assert.Equal(0.00002, resolved.MetresToUnitRadius);
        Assert.Equal(6_371_000.0, resolved.PlanetRadiusMetres);
        Assert.Equal(1.0 / 6_371_000.0, resolved.TrueScaleMetresToUnitRadius, 12);
        Assert.Equal(0.00002 / (1.0 / 6_371_000.0), resolved.ReliefAmplification, 9);
        Assert.Equal(1.0, resolved.HeightExponent);
        Assert.True(resolved.UseAdaptiveSurface);
        Assert.Equal(1, resolved.AdaptiveSubdivisionMaxDepth);
        Assert.Equal(0.012, resolved.AdaptiveSubdivisionEdgeHeightDelta);
        Assert.Equal(0.25, resolved.AdaptiveSubdivisionFeatureWeightDelta);
        Assert.True(resolved.PreservesCellProvenance);
    }

    [Fact]
    public void ResolveForView_ThreadsMaxDisplacementUnitRadiusFromProfile()
    {
        // Silhouette budget (north-star spec §1): the profile's declared cap threads through to
        // the resolved projection so the binder can pass it to the surface builders.
        var profile = PlanetLayerProjectionProfile.Crust(
            metresToUnitRadius: 0.00002,
            surfaceSubdivision: SurfaceSubdivisionMode.Adaptive,
            adaptiveSubdivisionMaxDepth: 1,
            adaptiveSubdivisionEdgeHeightDelta: 0.012,
            maxDisplacementUnitRadius: 0.005);
        var document = new PlanetPresentationDocument(
            PlanetId: "p",
            SourceWorldId: "w",
            ReferenceTick: 0,
            Revision: 1,
            Layers: Array.Empty<PlanetPresentationLayer>(),
            RenderEntities: Array.Empty<RenderEntityDto>())
        {
            LayerProjectionProfiles = new[] { profile },
        };

        var resolved = LayerProjectionProfileResolver.ResolveForView(
            document,
            GlobeViewMode.HypsometricTerrain,
            worldMetresToUnitRadius: 0.00012,
            worldHeightExponent: 0.5);

        Assert.Equal(0.005, resolved.MaxDisplacementUnitRadius);
    }

    [Fact]
    public void ResolveForView_DefaultMaxDisplacementIsInfinityWhenProfileOmitsIt()
    {
        var profile = PlanetLayerProjectionProfile.Crust(
            metresToUnitRadius: 0.00002,
            surfaceSubdivision: SurfaceSubdivisionMode.Adaptive,
            adaptiveSubdivisionMaxDepth: 1,
            adaptiveSubdivisionEdgeHeightDelta: 0.012);
        var document = new PlanetPresentationDocument(
            PlanetId: "p",
            SourceWorldId: "w",
            ReferenceTick: 0,
            Revision: 1,
            Layers: Array.Empty<PlanetPresentationLayer>(),
            RenderEntities: Array.Empty<RenderEntityDto>())
        {
            LayerProjectionProfiles = new[] { profile },
        };

        var resolved = LayerProjectionProfileResolver.ResolveForView(
            document,
            GlobeViewMode.HypsometricTerrain,
            worldMetresToUnitRadius: 0.00012,
            worldHeightExponent: 0.5);

        Assert.Equal(double.PositiveInfinity, resolved.MaxDisplacementUnitRadius);
    }

    [Fact]
    public void ResolveForView_World_UsesWorldLensButCrustAdaptivePolicy()
    {
        var profile = PlanetLayerProjectionProfile.Crust(
            metresToUnitRadius: 0.00002,
            surfaceSubdivision: SurfaceSubdivisionMode.Adaptive,
            adaptiveSubdivisionMaxDepth: 1,
            adaptiveSubdivisionEdgeHeightDelta: 0.012);
        var document = new PlanetPresentationDocument(
            PlanetId: "p",
            SourceWorldId: "w",
            ReferenceTick: 0,
            Revision: 1,
            Layers: Array.Empty<PlanetPresentationLayer>(),
            RenderEntities: Array.Empty<RenderEntityDto>())
        {
            LayerProjectionProfiles = new[] { profile },
        };

        var resolved = LayerProjectionProfileResolver.ResolveForView(
            document,
            GlobeViewMode.World,
            worldMetresToUnitRadius: 0.00012,
            worldHeightExponent: 0.5);

        Assert.Equal(0.00012, resolved.MetresToUnitRadius);
        Assert.Equal(6_371_000.0, resolved.PlanetRadiusMetres);
        Assert.Equal(0.00012 / (1.0 / 6_371_000.0), resolved.ReliefAmplification, 9);
        Assert.Equal(0.5, resolved.HeightExponent);
        Assert.True(resolved.UseAdaptiveSurface);
        Assert.Equal(1, resolved.AdaptiveSubdivisionMaxDepth);
        Assert.Equal(0.012, resolved.AdaptiveSubdivisionEdgeHeightDelta);
        Assert.Equal(0.25, resolved.AdaptiveSubdivisionFeatureWeightDelta);
    }
}
