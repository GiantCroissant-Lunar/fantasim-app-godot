using System.Text.Json.Nodes;

namespace FantaSim.App.Render;

/// <summary>
/// The DEPRECATED <c>render.mantle</c> alias (directive 2, 2026-07-16): mantle convection is a
/// LAYER, not an "x-ray" mode. <c>render.mantle</c> no longer toggles a ghost-shell x-ray
/// presentation; it routes to the <c>geosphere.mantle</c> LAYER selection — the exact same code path
/// as <c>timeline.select_layer</c> (the binder calls <c>ITimelineController.SelectLayer</c>, which
/// fires <c>LayerSelectionChanged</c> and reconciles the composed mantle-interior view with separated
/// crust slabs, no ghost shell).
///
/// <para>This type owns the Godot-free RESULT semantics only — the deprecation note carried on every
/// successful result and the loud <c>ok:false</c> + message on rejection (NEVER a silent no-op; the
/// <c>select_layer</c> silent-failure gotcha burned gates twice, so the alias is loud). The
/// layer-driving + rejection predicate live on the presentation/contract side
/// (<c>LayerActivation</c>, the binder's <c>RequestMantleLayerAlias</c>), which keeps this assembly
/// Godot-free and unit-testable. The resident render seam's handler stays a thin parse + delegate +
/// build-result.</para>
/// </summary>
public static class MantleAlias
{
    /// <summary>The sphere the alias selects (the geosphere).</summary>
    public const string TargetSphereId = "geosphere";

    /// <summary>The layer the alias selects — peer to plate and crust, the wave-5 D1 mantle layer.</summary>
    public const string TargetLayerId = "geosphere.mantle";

    /// <summary>The deprecation note carried on every successful alias result, pointing at the successor command.</summary>
    public const string DeprecationNote = "use timeline.select_layer";

    /// <summary>
    /// Builds the <c>render.mantle</c> alias result JSON. A successful activation/deselection carries
    /// the <see cref="DeprecationNote"/>, the requested <paramref name="enabled"/> flag, and a
    /// <c>redirect</c> pointing at the equivalent <c>timeline.select_layer</c> call. A rejection
    /// (<paramref name="ok"/> = false) carries <c>ok:false</c> + the caller-supplied
    /// <paramref name="error"/> message — never a silent no-op.
    /// </summary>
    /// <param name="ok">Whether the layer activation/deselection succeeded.</param>
    /// <param name="enabled">The requested <c>enabled</c> flag from the parsed payload.</param>
    /// <param name="error">The rejection message when <paramref name="ok"/> is false; ignored otherwise.</param>
    public static string BuildResultJson(bool ok, bool enabled, string? error)
    {
        var obj = new JsonObject { ["ok"] = ok };
        if (ok)
        {
            obj["enabled"] = enabled;
            obj["deprecated"] = DeprecationNote;
            obj["redirect"] = "timeline.select_layer {\"sphereId\":\""
                + TargetSphereId + "\",\"layerId\":\"" + TargetLayerId + "\"}";
        }
        else
        {
            obj["error"] = string.IsNullOrWhiteSpace(error)
                ? "render.mantle alias rejected: mantle layer is not active at the current tick."
                : error;
        }
        return obj.ToJsonString();
    }
}
