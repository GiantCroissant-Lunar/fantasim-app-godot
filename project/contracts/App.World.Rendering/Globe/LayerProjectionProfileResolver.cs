using FantaSim.App.World.Composition;

namespace FantaSim.App.World.Globe;

/// <summary>
/// Resolved mesh-building parameters for a globe layer projection. Values here are render policy,
/// not simulation truth.
/// </summary>
public sealed record ResolvedLayerProjection(
    PlanetLayerProjectionProfile Profile,
    double BaseRadius,
    double MetresToUnitRadius,
    double PlanetRadiusMetres,
    double TrueScaleMetresToUnitRadius,
    double ReliefAmplification,
    double HeightExponent,
    bool UseAdaptiveSurface,
    int AdaptiveSubdivisionMaxDepth,
    /// <summary>Unit-sphere radius displacement delta for adaptive subdivision; coupled to lens parameters.</summary>
    double AdaptiveSubdivisionEdgeHeightDelta,
    double AdaptiveSubdivisionFeatureWeightDelta,
    bool PreservesCellProvenance,
    /// <summary>
    /// Silhouette budget (north-star spec §1): maximum absolute radial displacement in unit-radius
    /// units, applied as a pure clamp on the finalized displacement. +inf preserves the legacy
    /// unclamped behaviour for views that do not declare a budget.
    /// </summary>
    double MaxDisplacementUnitRadius = double.PositiveInfinity);

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
        double declaredScale = worldLens ? worldMetresToUnitRadius : crust.MetresToUnitRadius;
        double heightExponent = worldLens ? worldHeightExponent : crust.HeightExponent;

        // Silhouette budget fit (north-star spec §1): the declared scale is FITTED so the profile's
        // reference max relief maps to at most the budget — the clamp in GlobePlateSurfaces then
        // only guards outliers instead of saturating the whole field into a plateau shell. Pure
        // arithmetic from declared values (budget / refMax^exponent); no eye-tuned numbers. The
        // adaptive edge threshold is declared in post-lens display units, so it scales by the same
        // ratio to keep the split behaviour invariant under the fit.
        double fitCap = double.IsInfinity(crust.MaxDisplacementUnitRadius)
            ? double.PositiveInfinity
            : crust.MaxDisplacementUnitRadius / Math.Pow(crust.ReferenceMaxReliefMetres, heightExponent);
        double metresToUnitRadius = Math.Min(declaredScale, fitCap);
        double fitRatio = metresToUnitRadius / declaredScale;

        return new ResolvedLayerProjection(
            Profile: crust,
            BaseRadius: crust.BaseRadius,
            MetresToUnitRadius: metresToUnitRadius,
            PlanetRadiusMetres: crust.PlanetRadiusMetres,
            TrueScaleMetresToUnitRadius: crust.TrueScaleMetresToUnitRadius,
            ReliefAmplification: metresToUnitRadius / crust.TrueScaleMetresToUnitRadius,
            HeightExponent: heightExponent,
            UseAdaptiveSurface: terrainView && crust.SurfaceSubdivision == SurfaceSubdivisionMode.Adaptive,
            AdaptiveSubdivisionMaxDepth: crust.AdaptiveSubdivisionMaxDepth,
            AdaptiveSubdivisionEdgeHeightDelta: crust.AdaptiveSubdivisionEdgeHeightDelta * fitRatio,
            AdaptiveSubdivisionFeatureWeightDelta: crust.AdaptiveSubdivisionFeatureWeightDelta,
            PreservesCellProvenance: crust.PreservesCellProvenance,
            MaxDisplacementUnitRadius: crust.MaxDisplacementUnitRadius);
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
