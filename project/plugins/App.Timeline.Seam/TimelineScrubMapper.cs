using System;

namespace FantaSim.App.Timeline.Seam;

internal static class TimelineScrubMapper
{
    public static bool TryLocalXToTick(
        float localX,
        float surfaceWidth,
        long viewStartTick,
        long viewEndTick,
        out long tick)
    {
        tick = viewStartTick;
        if (surfaceWidth <= 0f || viewEndTick < viewStartTick)
            return false;

        var fraction = localX / surfaceWidth;
        tick = (long)Math.Clamp(
            viewStartTick + (fraction * (viewEndTick - viewStartTick)),
            viewStartTick,
            viewEndTick);
        return true;
    }

    public static double TickToFraction(long tick, long viewStartTick, long viewEndTick)
    {
        var span = Math.Max(1L, viewEndTick - viewStartTick);
        return Math.Clamp((tick - viewStartTick) / (double)span, 0.0, 1.0);
    }
}
