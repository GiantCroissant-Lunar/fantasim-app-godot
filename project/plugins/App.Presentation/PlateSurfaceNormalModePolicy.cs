using FantaSim.App.World.Composition;
using FantaSim.App.World.Globe;

namespace FantaSim.App.Presentation;

internal static class PlateSurfaceNormalModePolicy
{
    // North-star spec §4: smooth (or blended) normals for World + crust diagnostic; flat faceting is
    // reserved for explicitly diagnostic views (PlateIdentity).
    public static PlateCapMeshNormalMode ForView(GlobeViewMode viewMode)
        => viewMode is GlobeViewMode.World or GlobeViewMode.HypsometricTerrain
            ? PlateCapMeshNormalMode.Smooth
            : PlateCapMeshNormalMode.Flat;
}