using FantaSim.App.World.Composition;

namespace FantaSim.App.Presentation;

internal static class PlateSurfaceEmissionPolicy
{
    public static bool ShowsVolcanicGlow(GlobeViewMode viewMode)
        => viewMode == GlobeViewMode.World;
}
