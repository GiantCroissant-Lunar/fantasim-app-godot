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
        Assert.Equal(0.00002, resolved.MetresToUnitRadius);
        Assert.Equal(1.0, resolved.HeightExponent);
        Assert.True(resolved.UseAdaptiveSurface);
        Assert.Equal(1, resolved.AdaptiveSubdivisionMaxDepth);
        Assert.Equal(0.012, resolved.AdaptiveSubdivisionEdgeHeightDelta);
        Assert.True(resolved.PreservesCellProvenance);
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
        Assert.Equal(0.5, resolved.HeightExponent);
        Assert.True(resolved.UseAdaptiveSurface);
        Assert.Equal(1, resolved.AdaptiveSubdivisionMaxDepth);
        Assert.Equal(0.012, resolved.AdaptiveSubdivisionEdgeHeightDelta);
    }
}
