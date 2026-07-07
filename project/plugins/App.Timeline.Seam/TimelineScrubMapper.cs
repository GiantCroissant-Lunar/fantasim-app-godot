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

    public static bool TryZoomWindowAroundLocalX(
        float localX,
        float surfaceWidth,
        long viewStartTick,
        long viewEndTick,
        long targetSpanTicks,
        long minViewSpanTicks,
        long maxTick,
        out long nextStartTick,
        out long nextEndTick)
    {
        nextStartTick = viewStartTick;
        nextEndTick = viewEndTick;
        if (surfaceWidth <= 0f || viewEndTick < viewStartTick)
            return false;

        var anchorFraction = Math.Clamp(localX / (double)surfaceWidth, 0.0, 1.0);
        ZoomWindowAroundFraction(
            viewStartTick,
            viewEndTick,
            targetSpanTicks,
            minViewSpanTicks,
            maxTick,
            anchorFraction,
            out nextStartTick,
            out nextEndTick);
        return true;
    }

    public static void ZoomWindowAroundFraction(
        long viewStartTick,
        long viewEndTick,
        long targetSpanTicks,
        long minViewSpanTicks,
        long maxTick,
        double anchorFraction,
        out long nextStartTick,
        out long nextEndTick)
    {
        var maxSpan = Math.Max(0L, maxTick);
        if (maxSpan == 0L)
        {
            nextStartTick = 0L;
            nextEndTick = 0L;
            return;
        }

        var minSpan = Math.Clamp(minViewSpanTicks, 1L, maxSpan);
        var nextSpan = Math.Clamp(targetSpanTicks, minSpan, maxSpan);
        var currentSpan = Math.Max(1L, viewEndTick - viewStartTick);
        var fraction = Math.Clamp(anchorFraction, 0.0, 1.0);
        var anchorTick = Math.Clamp(viewStartTick + (currentSpan * fraction), 0.0, maxSpan);
        var unclampedStart = (long)(anchorTick - (nextSpan * fraction));
        var maxStart = maxSpan - nextSpan;
        nextStartTick = Math.Clamp(unclampedStart, 0L, maxStart);
        nextEndTick = nextStartTick + nextSpan;
    }
}
