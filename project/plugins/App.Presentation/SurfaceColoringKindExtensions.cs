using FantaSim.App.World.Composition;

namespace FantaSim.App.Presentation;

internal static class SurfaceColoringKindExtensions
{
    // D5: maps the active-set surface-coloring owner to the GlobeViewMode whose plate-surface
    // build path (relief fabric, color mode, cap-mesh branch, cached slab-top state) realizes it.
    // The binder keeps a separate surface-appearance mode distinct from the composition derived mode
    // so a combo like Mantle+Crust (derived = MantleInterior for lighting/gates) builds terrain caps
    // and slab tops (HypsometricTerrain) without the regular plate surface being visible.
    public static GlobeViewMode ToSurfaceViewMode(this SurfaceColoringKind kind) => kind switch
    {
        SurfaceColoringKind.World => GlobeViewMode.World,
        SurfaceColoringKind.PlateIdentity => GlobeViewMode.PlateIdentity,
        SurfaceColoringKind.Continents => GlobeViewMode.Continents,
        SurfaceColoringKind.HypsometricTerrain => GlobeViewMode.HypsometricTerrain,
        _ => GlobeViewMode.World,
    };
}
