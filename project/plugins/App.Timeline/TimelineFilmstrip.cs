using System;
using System.Collections.Generic;

namespace FantaSim.App.Timeline;

public readonly record struct TimelineFilmstripFrameSlot(
    int Index,
    float X,
    float Width,
    long Tick);

public readonly record struct TimelineFilmstripCacheKey(
    string SphereId,
    string LayerId,
    long SnapshotTick,
    string ViewRung,
    int Width,
    int Height);

public static class TimelineFilmstrip
{
    public const float CompactTrackHeight = 40f;
    public const int ThumbnailWidth = 96;
    public const int ThumbnailHeight = 48;

    public static IReadOnlyList<TimelineFilmstripFrameSlot> PlanSlots(
        long viewStartTick,
        long viewEndTick,
        float contentWidth,
        int frameWidth = ThumbnailWidth)
    {
        if (viewEndTick <= viewStartTick)
            return Array.Empty<TimelineFilmstripFrameSlot>();
        if (contentWidth <= 0f)
            return Array.Empty<TimelineFilmstripFrameSlot>();
        if (frameWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameWidth));

        var slots = new List<TimelineFilmstripFrameSlot>();
        double span = viewEndTick - viewStartTick;
        int count = Math.Max(1, (int)Math.Ceiling(contentWidth / frameWidth));
        for (int i = 0; i < count; i++)
        {
            float x = i * frameWidth;
            if (x >= contentWidth)
                break;

            float width = MathF.Min(frameWidth, contentWidth - x);
            double center = (x + (width * 0.5f)) / contentWidth;
            long tick = viewStartTick + (long)Math.Round(span * center, MidpointRounding.AwayFromZero);
            slots.Add(new TimelineFilmstripFrameSlot(i, x, width, tick));
        }

        return slots;
    }
}
