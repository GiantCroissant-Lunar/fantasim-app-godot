namespace FantaSim.App.World.Composition;

/// <summary>
/// Pure gate deciding whether the mantle sphere renders (world-view fix, 2026-07-03). At
/// mobile-plate the plate caps are the WATERTIGHT planet surface — cell reassignment tiles the
/// drifted sphere every tick — so the mantle underneath must not render: terrain below -4000 m
/// displaces beneath the mantle's 0.96 radius, and a drawn mantle swallows every basin, leaving
/// only boundary ranges poking through (the whole face-on disk read as the mantle ball, not the
/// terrain). The mantle stays visible when it OWNS the look: non-mobile-plate regimes
/// (<see cref="GlobeViewMode.Inactive"/> — magma-ocean, stagnant-lid), a regime that hides plate
/// features, or a document with no plate surface at all.
/// Mirrors the <see cref="WorldViewContentGate"/> pattern: pure, Godot-free, host-consumed.
/// </summary>
public static class MantleSurfaceGate
{
    /// <summary>
    /// Whether the mantle sphere should render given the resolved view mode, whether the regime
    /// currently shows plate features, and whether a plate surface was built for the document.
    /// </summary>
    public static bool IsVisible(GlobeViewMode viewMode, bool platesShown, bool hasPlateSurface)
        => viewMode == GlobeViewMode.Inactive || !platesShown || !hasPlateSurface;
}
