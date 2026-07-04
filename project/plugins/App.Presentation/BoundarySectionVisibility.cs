using FantaSim.App.World.Composition;

namespace FantaSim.App.Presentation;

internal static class BoundarySectionVisibility
{
    public static bool ShouldShow(bool showsPlateFeatures, GlobeViewMode viewMode)
        => showsPlateFeatures
            && (viewMode == GlobeViewMode.World || viewMode == GlobeViewMode.HypsometricTerrain);
}
