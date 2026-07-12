using System;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Godot-free planet-zoom step for the tunnel view (vault/plans/
/// 2026-07-13-tunnel-visual-slice1-plan.md Part C). Multiplicative per-notch scale, clamped to a
/// sane range. A non-finite or non-positive current resets to <see cref="DefaultScale"/> before
/// stepping, so a corrupted scale can never propagate to the shared planet body.
/// </summary>
public static class TunnelPlanetZoom
{
    public const float MinScale = 0.35f;
    public const float MaxScale = 3.0f;
    public const float DefaultScale = 1.0f;
    public const float StepFactor = 1.12f;

    /// <summary>direction &gt; 0 zooms in (larger), &lt; 0 zooms out (smaller), 0 is a no-op.</summary>
    public static float Step(float current, int direction)
    {
        if (!float.IsFinite(current) || current <= 0f)
            current = DefaultScale;

        var next = direction > 0 ? current * StepFactor
            : direction < 0 ? current / StepFactor
            : current;

        return Clamp(next);
    }

    public static float Clamp(float scale) => Math.Clamp(scale, MinScale, MaxScale);
}
