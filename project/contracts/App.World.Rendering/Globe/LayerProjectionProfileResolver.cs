using FantaSim.App.World.Composition;
using FantaSim.Cartography.Globe;

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

        // The 0.5%-radius silhouette budget belongs to the secondary watertight/diagnostic crust
        // projection. The assembled World is now a crust-volume outer envelope: its approved
        // north-star explicitly retires that old smooth-sphere cap so the existing World lens can
        // make tectonic consequences legible. Both views still sample the same geological state.
        double maxDisplacementUnitRadius = worldLens
            ? double.PositiveInfinity
            : crust.MaxDisplacementUnitRadius;

        // A view that declares a silhouette budget fits its scale so reference relief maps within
        // that budget; the final clamp then only guards outliers. The adaptive edge threshold is
        // declared in post-lens display units, so it follows the same ratio.
        double fitCap = double.IsInfinity(maxDisplacementUnitRadius)
            ? double.PositiveInfinity
            : maxDisplacementUnitRadius / Math.Pow(crust.ReferenceMaxReliefMetres, heightExponent);
        double metresToUnitRadius = Math.Min(declaredScale, fitCap);
        double fitRatio = metresToUnitRadius / declaredScale;

        // Directive 4 slice 1 (vault/plans/2026-07-16-visible-adaptive-lod-slice-plan.md): the
        // WORLD view adopts the declared nonuniform profile — splits driven ONLY by the coarse
        // causal feature-weight criterion, so the boundary band refines (>=3x density,
        // headless-tested) while interiors stay coarse under the declared budget. Diagnostic
        // views keep their declared profile values. Migrating these numbers into control-plane
        // world parameters is the recorded follow-up.
        AdaptiveSubdivisionOptions? visibleLod = worldLens ? VisibleLodProfile.BuildOptions() : null;

        return new ResolvedLayerProjection(
            Profile: crust,
            BaseRadius: crust.BaseRadius,
            MetresToUnitRadius: metresToUnitRadius,
            PlanetRadiusMetres: crust.PlanetRadiusMetres,
            TrueScaleMetresToUnitRadius: crust.TrueScaleMetresToUnitRadius,
            ReliefAmplification: metresToUnitRadius / crust.TrueScaleMetresToUnitRadius,
            HeightExponent: heightExponent,
            UseAdaptiveSurface: terrainView && crust.SurfaceSubdivision == SurfaceSubdivisionMode.Adaptive,
            AdaptiveSubdivisionMaxDepth: visibleLod?.MaxDepth ?? crust.AdaptiveSubdivisionMaxDepth,
            AdaptiveSubdivisionEdgeHeightDelta: visibleLod?.EdgeHeightDeltaThreshold ?? crust.AdaptiveSubdivisionEdgeHeightDelta * fitRatio,
            AdaptiveSubdivisionFeatureWeightDelta: visibleLod?.FeatureWeightDeltaThreshold ?? crust.AdaptiveSubdivisionFeatureWeightDelta,
            PreservesCellProvenance: crust.PreservesCellProvenance,
            MaxDisplacementUnitRadius: maxDisplacementUnitRadius);
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
