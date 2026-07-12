namespace FantaSim.App.Presentation.Tunnel;

internal readonly record struct TunnelSphereTone(
    float Saturation,
    float Value);

/// <summary>
/// Deterministic shader emphasis for presentation-only fine inspection. Focused and resting spheres
/// stay at full color/value. Non-focused spheres mix their sampled texture to luminance and dim it;
/// neither path replaces the immutable cached texture reference.
/// </summary>
internal static class TunnelFineEmphasisPolicy
{
    private const float DeemphasizedValue = 0.42f;

    internal static TunnelSphereTone Resolve(bool inspectionActive, bool focused)
        => inspectionActive && !focused
            ? new TunnelSphereTone(
                Saturation: 0f,
                Value: DeemphasizedValue)
            : new TunnelSphereTone(Saturation: 1f, Value: 1f);
}
