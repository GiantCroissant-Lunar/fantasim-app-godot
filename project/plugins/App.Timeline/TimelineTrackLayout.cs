using System;
using System.Collections.Generic;
using System.Linq;

namespace FantaSim.App.Timeline;

public sealed record TimelineTrackLayoutInput(string TrackKey, bool IsExpanded);

public sealed record TimelineTrackLayoutRow(string TrackKey, float Y, float Height);

public sealed record TimelineTrackLayoutPlan(IReadOnlyList<TimelineTrackLayoutRow> Rows, float TotalHeight);

public static class TimelineTrackLayout
{
    public const float CompactTrackHeight = TimelineFilmstrip.CompactTrackHeight;
    public const float ExpandedTrackHeight = 200f;

    public static TimelineTrackLayoutPlan Plan(
        IEnumerable<TimelineTrackLayoutInput> tracks,
        float compactHeight = CompactTrackHeight,
        float expandedHeight = ExpandedTrackHeight)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        if (compactHeight <= 0f)
            throw new ArgumentOutOfRangeException(nameof(compactHeight));
        if (expandedHeight < compactHeight)
            throw new ArgumentOutOfRangeException(nameof(expandedHeight));

        var y = 0f;
        var rows = new List<TimelineTrackLayoutRow>();
        foreach (var track in tracks)
        {
            var height = track.IsExpanded ? expandedHeight : compactHeight;
            rows.Add(new TimelineTrackLayoutRow(track.TrackKey, y, height));
            y += height;
        }

        return new TimelineTrackLayoutPlan(rows, y);
    }

    public static IReadOnlyDictionary<string, TimelineTrackLayoutRow> ToRowMap(TimelineTrackLayoutPlan plan)
        => plan.Rows.ToDictionary(row => row.TrackKey, StringComparer.Ordinal);
}
