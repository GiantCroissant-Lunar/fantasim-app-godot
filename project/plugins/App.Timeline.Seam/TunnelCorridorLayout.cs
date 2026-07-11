using System.Collections.Generic;
using System.Linq;
using FantaSim.App.Timeline;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Angular wedge layout for tunnel corridors: one sphere SECTOR per TrackLaneViewModel (equal
/// angular share, first-seen order — generalizes Concept A's hardcoded 6x60deg wireframe to N
/// spheres, spec §4.1/§1 point 4), subdivided into one corridor wedge per TrackRowViewModel within
/// that sector. Godot-free by design (mirrors TimelineScrubMapper/TrackLaneViewModelBuilder) —
/// linked directly into App.Timeline.Tests. vault/plans/2026-07-11-tunnel-slice1-plan.md.
/// </summary>
public static class TunnelCorridorLayout
{
    public readonly record struct CorridorWedge(
        string SphereId,
        string LayerId,
        double StartAngleDeg,
        double SpanAngleDeg,
        bool IsDimmed,
        TrackContentPresenterKind PresenterKind);

    /// <summary>Divides the full 360deg among lanes equally, in BuildLanes' first-seen order,
    /// then each lane's span equally among its tracks, in track order. Empty input -> empty
    /// output (no lanes, no throw).</summary>
    public static IReadOnlyList<CorridorWedge> BuildWedges(IReadOnlyList<TrackLaneViewModel> lanes)
    {
        if (lanes.Count == 0)
            return System.Array.Empty<CorridorWedge>();

        var wedges = new List<CorridorWedge>();
        var sectorSpan = 360.0 / lanes.Count;

        for (var laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
        {
            var lane = lanes[laneIndex];
            var sectorStart = sectorSpan * laneIndex;
            var trackCount = lane.Tracks.Count;
            if (trackCount == 0)
                continue;

            var trackSpan = sectorSpan / trackCount;
            for (var trackIndex = 0; trackIndex < trackCount; trackIndex++)
            {
                var track = lane.Tracks[trackIndex];
                wedges.Add(new CorridorWedge(
                    lane.SphereId,
                    track.Descriptor.LayerId,
                    sectorStart + (trackSpan * trackIndex),
                    trackSpan,
                    track.IsDimmed,
                    track.PresenterKind));
            }
        }

        return wedges;
    }

    /// <summary>
    /// The first real consumer of LayerTrackDescriptor.TimeDomain.Rung (verified unconsumed
    /// elsewhere in the codebase, see this plan's Grounding facts). Resolves the track's declared
    /// native rung symbol against TimelineModel.GetLadderRungs(); an unrecognized or null symbol
    /// falls back to the caller's globally-selected rung -- the Unity round-trip degradation
    /// guarantee applied to a NEW field for the first time, never a throw.
    /// </summary>
    public static TimelineLadderRung ResolveCorridorRung(string? trackRungSymbol, TimelineLadderRung globalFallback)
    {
        if (trackRungSymbol is null)
            return globalFallback;

        return TimelineModel.GetLadderRungs().FirstOrDefault(r => r.Symbol == trackRungSymbol) ?? globalFallback;
    }
}
