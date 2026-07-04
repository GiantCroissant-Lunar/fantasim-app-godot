using FantaSim.App.World.Composition;

namespace FantaSim.App.Presentation;

internal static class PlateSurfaceViewModeTransition
{
    public static bool ShouldRebuild(GlobeViewMode current, GlobeViewMode next)
        => current != next && next != GlobeViewMode.Inactive;
}
