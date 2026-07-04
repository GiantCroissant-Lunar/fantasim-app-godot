using FantaSim.App.World.Composition;
using FantaSim.App.World.Rendering;

namespace FantaSim.App.Presentation;

internal static class PlateSurfaceTintFabric
{
    public static VertexTintJitter? ForView(GlobeViewMode viewMode)
        => viewMode switch
        {
            GlobeViewMode.World => new VertexTintJitter(seed: 1337, amplitude: 0.06),
            GlobeViewMode.HypsometricTerrain => new VertexTintJitter(seed: 1777, amplitude: 0.12),
            _ => null,
        };
}
