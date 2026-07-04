using FantaSim.App.World.Composition;

namespace FantaSim.App.World.Globe;

/// <summary>
/// Resolved mesh-building parameters for a globe layer projection. Values here are render policy,
/// not simulation truth.
/// </summary>
public sealed record ResolvedLayerProjection(
    PlanetLayerProjectionProfile Profile,
    double MetresToUnitRadius,
    double HeightExponent,
    bool UseAdaptiveSurface,
    int AdaptiveSubdivisionMaxDepth,
    double AdaptiveSubdivisionEdgeHeightDelta,
    double AdaptiveSubdivisionFeatureWeightDelta,
    bool PreservesCellProvenance);

/// <summary>Maps presentation view mode to the layer projection profile used by globe cap builders.</summary>
public static class LayerProjectionProfileResolver
{
    public static ResolvedLayerProjection ResolveForView(
        PlanetPresentationDocument document,
        GlobeViewMode viewMode,
        double worldMetresToUnitRadius,
        double worldHeightExponent)
    {
        ArgumentNullException.ThrowIfNull(document);

        var crust = ResolveCrustProfile(document);
        bool terrainView = viewMode is GlobeViewMode.World or GlobeViewMode.HypsometricTerrain;
        bool worldLens = viewMode == GlobeViewMode.World;

        return new ResolvedLayerProjection(
            Profile: crust,
            MetresToUnitRadius: worldLens ? worldMetresToUnitRadius : crust.MetresToUnitRadius,
            HeightExponent: worldLens ? worldHeightExponent : crust.HeightExponent,
            UseAdaptiveSurface: terrainView && crust.SurfaceSubdivision == SurfaceSubdivisionMode.Adaptive,
            AdaptiveSubdivisionMaxDepth: crust.AdaptiveSubdivisionMaxDepth,
            AdaptiveSubdivisionEdgeHeightDelta: crust.AdaptiveSubdivisionEdgeHeightDelta,
            AdaptiveSubdivisionFeatureWeightDelta: crust.AdaptiveSubdivisionFeatureWeightDelta,
            PreservesCellProvenance: crust.PreservesCellProvenance);
    }

    private static PlanetLayerProjectionProfile ResolveCrustProfile(PlanetPresentationDocument document)
    {
        foreach (var profile in document.LayerProjectionProfiles)
        {
            if (string.Equals(profile.LayerId, PlanetLayerProjectionProfile.CrustLayerId, StringComparison.Ordinal)
                && profile.ProjectionKind == PlanetLayerProjectionKind.GlobeSurface)
            {
                return profile;
            }
        }

        return PlanetLayerProjectionProfile.Crust(
            document.VerticalExaggeration,
            document.SurfaceSubdivision,
            document.AdaptiveSubdivisionMaxDepth,
            document.AdaptiveSubdivisionEdgeHeightDelta,
            document.AdaptiveSubdivisionFeatureWeightDelta);
}
}
