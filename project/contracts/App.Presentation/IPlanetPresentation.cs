using System;

namespace FantaSim.App.Presentation;

/// <summary>
/// The host-facing surface of the planet presentation (host-slim move, 2026-07-03): everything the
/// composition root needs — rebind on world-bundle availability, the cutaway wedge entry the render
/// ingress drives, and teardown. Implementation types stay internal to this assembly.
/// </summary>
public interface IPlanetPresentation : IDisposable
{
    /// <summary>Fetch the presentation document and (re)mount the planet under the stage.</summary>
    void Rebind();

    /// <summary>W3a cutaway wedge (render.cutaway ingress); width 0 clears.</summary>
    void UpdateCutaway(double azimuthDeg, double widthDeg);

    /// <summary>
    /// M-B exploded solid crust. Factor is in [0,1]; focused mode isolates the proven convergent
    /// pair and uses factor as the overriding-plate reveal translation.
    /// </summary>
    void UpdateExploded(double factor, bool focusConvergent);

    /// <summary>
    /// DEPRECATED render.mantle alias (directive 2, 2026-07-16): mantle convection is a LAYER, not an
    /// "x-ray" mode. This routes the legacy command to the <c>geosphere.mantle</c> layer selection —
    /// the exact same code path as <c>timeline.select_layer</c> (it calls
    /// <c>ITimelineController.SelectLayer</c>, which reconciles the composed mantle-interior view
    /// with separated crust slabs, no ghost shell). Returns <c>null</c> on success; a rejection
    /// message when the mantle layer is not active at the current tick (loud failure, never a silent
    /// no-op). The caller (the resident render seam) wraps this into the deprecation-noted result
    /// JSON. <paramref name="enabled"/> = false deselects the mantle layer via the layer path's toggle.
    /// </summary>
    string? RequestMantleLayerAlias(bool enabled);
}
