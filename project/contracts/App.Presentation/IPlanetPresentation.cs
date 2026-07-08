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

    /// <summary>M-B exploded solid crust (render.exploded ingress); factor in [0,1]. Factor 0 = assembled.</summary>
    void UpdateExploded(double factor);

    /// <summary>
    /// M-A mantle x-ray view (render.mantle ingress). enabled=false clears. This is the AGENT
    /// look-dev knob; the USER-reachable path is the <c>geosphere.mantle</c> timeline layer (D1),
    /// which resolves to <c>GlobeViewMode.MantleInterior</c> and composes the M-A interior with
    /// separated crust slabs (no ghost shell). Keep this for look-dev iteration; surface the layer
    /// for end users.
    /// </summary>
    void UpdateMantle(bool enabled);
}
