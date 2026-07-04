using FantaSim.App.World.Composition;
using FantaSim.App.World.Globe;

namespace FantaSim.App.Presentation;

internal static class PlateSurfaceColorModePolicy
{
    public static PlateCapMeshColorMode ForView(GlobeViewMode viewMode)
        => viewMode == GlobeViewMode.HypsometricTerrain
            ? PlateCapMeshColorMode.SourceCellFacet
            : PlateCapMeshColorMode.VertexEnvelope;
}
