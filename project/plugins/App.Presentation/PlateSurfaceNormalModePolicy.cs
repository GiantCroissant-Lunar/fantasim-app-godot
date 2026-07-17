using FantaSim.App.World.Composition;
using FantaSim.App.World.Globe;

namespace FantaSim.App.Presentation;

internal static class PlateSurfaceNormalModePolicy
{
    // V1 "closed skin" (vault/specs/2026-07-18-visual-fidelity-slices-decision.md): the assembled
    // World envelope must be smooth-shaded so the simulation-cell grid stops reading as flat facets
    // at globe distance (design §7.1 — "no visible cell grid, chunk grid, artificial seam"). The
    // earlier "deliberately chunky/faceted" stance is superseded by that user decision; the crust
    // diagnostic (HypsometricTerrain) keeps smooth normals as before. Non-terrain diagnostics fall
    // through to flat — they are field-inspection views, not the assembled-world appearance.
    public static PlateCapMeshNormalMode ForView(GlobeViewMode viewMode)
        => viewMode is GlobeViewMode.World or GlobeViewMode.HypsometricTerrain
            ? PlateCapMeshNormalMode.Smooth
            : PlateCapMeshNormalMode.Flat;
}
