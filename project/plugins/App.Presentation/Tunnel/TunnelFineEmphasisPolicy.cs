namespace FantaSim.App.Presentation.Tunnel;

internal readonly record struct TunnelSphereTone(
    float Red,
    float Green,
    float Blue,
    float Alpha);

/// <summary>
/// Deterministic material modulation for presentation-only fine inspection. The modulation changes
/// only <c>StandardMaterial3D.AlbedoColor</c>; the sphere's immutable cached AlbedoTexture reference
/// remains untouched. Focused and resting spheres stay neutral, while non-focused spheres receive
/// an equal-channel gray multiplier during inspection.
/// </summary>
internal static class TunnelFineEmphasisPolicy
{
    private const float DeemphasizedValue = 0.42f;

    internal static TunnelSphereTone Resolve(bool inspectionActive, bool focused)
        => inspectionActive && !focused
            ? new TunnelSphereTone(
                DeemphasizedValue,
                DeemphasizedValue,
                DeemphasizedValue,
                1f)
            : new TunnelSphereTone(1f, 1f, 1f, 1f);
}
