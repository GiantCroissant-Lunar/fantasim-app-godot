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

        // Factory default budget 0.005 over reference relief 18 000 m (linear lens) fits the scale
        // to 0.005/18000 — below the declared 2e-5 — and the edge threshold scales by the same ratio.
        double fitted = 0.005 / 18_000.0;
        double fitRatio = fitted / 0.00002;

        Assert.Equal("geosphere.crust", resolved.Profile.LayerId);
        Assert.Equal(1.0, resolved.BaseRadius);
        Assert.Equal(fitted, resolved.MetresToUnitRadius, 15);
        Assert.Equal(6_371_000.0, resolved.PlanetRadiusMetres);
        Assert.Equal(1.0 / 6_371_000.0, resolved.TrueScaleMetresToUnitRadius, 12);
        Assert.Equal(fitted / (1.0 / 6_371_000.0), resolved.ReliefAmplification, 9);
        Assert.Equal(1.0, resolved.HeightExponent);
        Assert.True(resolved.UseAdaptiveSurface);
        Assert.Equal(1, resolved.AdaptiveSubdivisionMaxDepth);
        Assert.Equal(0.012 * fitRatio, resolved.AdaptiveSubdivisionEdgeHeightDelta, 15);
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
    public void ResolveForView_FactoryDefaultsToSpecSilhouetteBudget()
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

        // The Crust factory now defaults to the north-star spec budget (§1), so a profile that
        // omits the cap still ships an ACTIVE silhouette budget — the spec is the default, not an
        // opt-in. Legacy unclamped behaviour requires an explicit +inf.
        Assert.Equal(PlanetLayerProjectionProfile.DefaultSilhouetteBudgetUnitRadius, resolved.MaxDisplacementUnitRadius);
        Assert.Equal(PlanetLayerProjectionProfile.DefaultReferenceMaxReliefMetres, resolved.Profile.ReferenceMaxReliefMetres);
    }

    [Fact]
    public void ResolveForView_ExplicitInfiniteBudgetPreservesLegacyUnfittedScale()
    {
        var profile = PlanetLayerProjectionProfile.Crust(
            metresToUnitRadius: 0.00002,
            surfaceSubdivision: SurfaceSubdivisionMode.Adaptive,
            adaptiveSubdivisionMaxDepth: 1,
            adaptiveSubdivisionEdgeHeightDelta: 0.012,
            maxDisplacementUnitRadius: double.PositiveInfinity);
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

        Assert.Equal(0.00002, resolved.MetresToUnitRadius);
        Assert.Equal(0.012, resolved.AdaptiveSubdivisionEdgeHeightDelta);
        Assert.Equal(double.PositiveInfinity, resolved.MaxDisplacementUnitRadius);
    }

    [Fact]
    public void ResolveForView_FittedScaleMapsReferenceReliefWithinBudget()
    {
        // The whole point of the fit (spec §1): at the declared reference max relief, the resolved
        // scale produces a displacement at or below the budget — the clamp guards outliers only.
        var profile = PlanetLayerProjectionProfile.Crust(
            metresToUnitRadius: 0.00003,
            surfaceSubdivision: SurfaceSubdivisionMode.Adaptive,
            adaptiveSubdivisionMaxDepth: 1,
            adaptiveSubdivisionEdgeHeightDelta: 0.02);
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

        foreach (var view in new[] { GlobeViewMode.HypsometricTerrain, GlobeViewMode.World })
        {
            var resolved = LayerProjectionProfileResolver.ResolveForView(
                document,
                view,
                worldMetresToUnitRadius: 0.0005,
                worldHeightExponent: 0.5);

            double refDisplacement =
                Math.Pow(profile.ReferenceMaxReliefMetres, resolved.HeightExponent) * resolved.MetresToUnitRadius;
            Assert.True(
                refDisplacement <= resolved.MaxDisplacementUnitRadius + 1e-12,
                $"{view}: reference relief maps to {refDisplacement}, over budget {resolved.MaxDisplacementUnitRadius}.");
        }
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

        // World lens (sqrt): fit cap = 0.005 / 18000^0.5, below the declared 1.2e-4 world scale.
        double fitted = 0.005 / Math.Pow(18_000.0, 0.5);
        double fitRatio = fitted / 0.00012;

        Assert.Equal(fitted, resolved.MetresToUnitRadius, 15);
        Assert.Equal(6_371_000.0, resolved.PlanetRadiusMetres);
        Assert.Equal(fitted / (1.0 / 6_371_000.0), resolved.ReliefAmplification, 9);
        Assert.Equal(0.5, resolved.HeightExponent);
        Assert.True(resolved.UseAdaptiveSurface);
        Assert.Equal(1, resolved.AdaptiveSubdivisionMaxDepth);
        Assert.Equal(0.012 * fitRatio, resolved.AdaptiveSubdivisionEdgeHeightDelta, 15);
        Assert.Equal(0.25, resolved.AdaptiveSubdivisionFeatureWeightDelta);
    }
}
