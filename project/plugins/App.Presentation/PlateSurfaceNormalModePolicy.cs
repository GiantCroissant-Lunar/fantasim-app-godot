using FantaSim.App.World.Composition;
using FantaSim.App.World.Globe;

namespace FantaSim.App.Presentation;

internal static class PlateSurfaceNormalModePolicy
{
    // The assembled-world reference is deliberately chunky/faceted: flat normals let the existing
    // adaptive crust-volume triangles carry relief in the lighting instead of smoothing the
    // mountain/trench/ridge belts back into a ball. The crust diagnostic keeps smooth normals so it
    // remains a field-inspection view rather than a second assembled-world appearance.
    public static PlateCapMeshNormalMode ForView(GlobeViewMode viewMode)
        => viewMode == GlobeViewMode.HypsometricTerrain
            ? PlateCapMeshNormalMode.Smooth
            : PlateCapMeshNormalMode.Flat;
}
