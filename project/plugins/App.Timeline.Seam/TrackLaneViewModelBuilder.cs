using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Timeline.Seam;

/// <summary>Which existing rendering path a track's collapsed content strip goes through.
/// Any content.Type with no dedicated presenter here falls back to
/// <see cref="Generic"/> -- a labeled, dimmed strip that never crashes and never goes
/// invisible (the Unity round-trip degradation guarantee,
/// vault/specs/2026-07-10-layer-track-registry-design.md).</summary>
public enum TrackContentPresenterKind
{
    /// <summary>Existing compact-filmstrip path (image thumbnails).</summary>
    Filmstrip,

    /// <summary>Existing chip/graph path (a small label summarizing the resolved layer graph).
    /// The chevron expand-to-full-GraphEdit affordance is separate and works for every track
    /// regardless of presenter kind -- see TimelineFace.BuildExpandedGraph.</summary>
    Graph,

    /// <summary>No dedicated presenter registered for the content type: a generic dimmed label.</summary>
    Generic,
}

/// <summary>One track row, ready for rendering: the registry descriptor plus the two decisions
/// the view needs and used to derive ad hoc (which presenter, whether to dim).</summary>
public sealed record TrackRowViewModel(
    LayerTrackDescriptor Descriptor,
    TrackContentPresenterKind PresenterKind,
    bool IsDimmed);

/// <summary>One lane: every non-archived track declared for a single sphereId.</summary>
public sealed record TrackLaneViewModel(string SphereId, IReadOnlyList<TrackRowViewModel> Tracks);

/// <summary>
/// Pure grouping/ordering/dimming logic pulled out of TimelineFace's old hardcoded
/// geosphere/atmosphere PopulateLane pair (Task 4). Godot-free by design: linked directly into
/// App.Timeline.Tests (see that project's .csproj) so this stays testable without a Godot
/// runtime, exactly like TimelineScrubMapper.
/// </summary>
public static class TrackLaneViewModelBuilder
{
    /// <summary>
    /// Groups a registry snapshot into one lane per distinct SphereId, in first-seen order.
    /// Archived tracks are dropped entirely (from every lane); a sphere with no remaining
    /// non-archived tracks does not get an empty lane.
    /// </summary>
    public static IReadOnlyList<TrackLaneViewModel> BuildLanes(LayerTrackRegistrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var rowsBySphere = new Dictionary<string, List<TrackRowViewModel>>(StringComparer.Ordinal);
        var sphereOrder = new List<string>();

        foreach (var descriptor in snapshot.Tracks)
        {
            // Archived tracks disappear from every lane; the registry itself never deletes the
            // underlying data (SetArchived only flips State), so restoring is always possible.
            if (string.Equals(descriptor.State, LayerTrackStates.Archived, StringComparison.Ordinal))
                continue;

            if (!rowsBySphere.TryGetValue(descriptor.SphereId, out var rows))
            {
                rows = new List<TrackRowViewModel>();
                rowsBySphere[descriptor.SphereId] = rows;
                sphereOrder.Add(descriptor.SphereId);
            }

            var presenterKind = ResolvePresenterKind(descriptor.Content.Type);
            rows.Add(new TrackRowViewModel(
                descriptor,
                presenterKind,
                IsDimmed: presenterKind == TrackContentPresenterKind.Generic));
        }

        return sphereOrder
            .Select(sphereId => new TrackLaneViewModel(sphereId, rowsBySphere[sphereId]))
            .ToList();
    }

    /// <summary>Maps a <see cref="LayerTrackContent.Type"/> string to the presenter kind that
    /// renders it. Data-keyed, not a switch on layer or sphere ids -- an unregistered type is
    /// never an error, only a generic fallback.</summary>
    public static TrackContentPresenterKind ResolvePresenterKind(string contentType) => contentType switch
    {
        LayerTrackContentTypes.Filmstrip => TrackContentPresenterKind.Filmstrip,
        LayerTrackContentTypes.Graph => TrackContentPresenterKind.Graph,
        _ => TrackContentPresenterKind.Generic,
    };
}
