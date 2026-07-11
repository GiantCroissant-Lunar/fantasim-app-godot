using System;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Pure scrub-gesture math for the tunnel's current-tick ring: radius-gated press dispatch
/// (mirrors the wireframe's mode='time' vs 'wall' split, spec §5.1) plus a linear horizontal-
/// pixel-delta-to-tick-delta drag mapping reusing the SAME view span the 2D ruler uses, so an
/// identical pixel drag moves the SAME number of ticks in either view. Godot-free; linked into
/// App.Timeline.Tests. vault/plans/2026-07-11-tunnel-slice1-plan.md.
/// </summary>
public static class TunnelScrubMapper
{
    /// <summary>True when a press at screenRadiusPx from the ring's screen-projected center
    /// falls within bandPx of ringRadiusPx -- i.e. the press targets the ring, not the wall.</summary>
    public static bool IsWithinRingBand(float screenRadiusPx, float ringRadiusPx, float bandPx)
        => MathF.Abs(screenRadiusPx - ringRadiusPx) <= bandPx;

    /// <summary>Maps a horizontal drag delta (pixels) to a new absolute tick, clamped to
    /// [viewStartTick, viewEndTick], reusing the same linear span TimelineScrubMapper.
    /// TryLocalXToTick uses for the 2D ruler.</summary>
    public static long DragDeltaToTick(
        float pixelDeltaX, float viewportWidthPx, long viewStartTick, long viewEndTick, long baseTick)
    {
        if (viewportWidthPx <= 0f)
            return baseTick;

        var span = viewEndTick - viewStartTick;
        var deltaTicks = (pixelDeltaX / viewportWidthPx) * span;
        return (long)Math.Clamp(baseTick + deltaTicks, viewStartTick, viewEndTick);
    }
}
