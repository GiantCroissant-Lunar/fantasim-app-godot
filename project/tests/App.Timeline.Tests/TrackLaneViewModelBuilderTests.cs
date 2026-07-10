using System.Linq;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// Headless coverage for <see cref="TrackLaneViewModelBuilder"/> (Task 4): the pure grouping/
/// ordering/dimming decisions that used to be baked into TimelineFace's hardcoded
/// geosphere/atmosphere PopulateLane pair. No Godot types involved -- see
/// vault/plans/2026-07-10-layer-track-registry-slice1-plan.md Task 4.
/// </summary>
public sealed class TrackLaneViewModelBuilderTests
{
    private static LayerTrackDescriptor Descriptor(
        string sphereId,
        string layerId,
        string state = "declared",
        string contentType = "filmstrip") => new(
        SphereId: sphereId,
        LayerId: layerId,
        StreamId: new LayerTrackStreamId("main", "default", "L0", "world", "default"),
        DisplayName: layerId,
        State: state,
        TimeDomain: new LayerTrackTimeDomain(0L, null, "ka"),
        Content: new LayerTrackContent(contentType),
        Capabilities: new[] { "scrub", "toggle" },
        SourceRef: layerId);

    [Fact]
    public void BuildLanes_GroupsTracksBySphereId_InFirstSeenOrder()
    {
        var snapshot = new LayerTrackRegistrySnapshot(1, new[]
        {
            Descriptor("atmosphere", "atmosphere.bulk"),
            Descriptor("geosphere", "geosphere.crust"),
            Descriptor("geosphere", "geosphere.plate"),
        });

        var lanes = TrackLaneViewModelBuilder.BuildLanes(snapshot);

        Assert.Equal(new[] { "atmosphere", "geosphere" }, lanes.Select(l => l.SphereId));
        Assert.Single(lanes[0].Tracks);
        Assert.Equal(2, lanes[1].Tracks.Count);
    }

    [Fact]
    public void BuildLanes_DropsArchivedTracks()
    {
        var snapshot = new LayerTrackRegistrySnapshot(1, new[]
        {
            Descriptor("geosphere", "geosphere.crust"),
            Descriptor("geosphere", "geosphere.plate", state: LayerTrackStates.Archived),
        });

        var lanes = TrackLaneViewModelBuilder.BuildLanes(snapshot);

        var geosphere = Assert.Single(lanes);
        var track = Assert.Single(geosphere.Tracks);
        Assert.Equal("geosphere.crust", track.Descriptor.LayerId);
    }

    [Fact]
    public void BuildLanes_ArchivingEveryTrackOfASphere_DropsTheLaneEntirely()
    {
        var snapshot = new LayerTrackRegistrySnapshot(1, new[]
        {
            Descriptor("hydrosphere", "hydrosphere.ocean", state: LayerTrackStates.Archived),
            Descriptor("geosphere", "geosphere.crust"),
        });

        var lanes = TrackLaneViewModelBuilder.BuildLanes(snapshot);

        Assert.Single(lanes);
        Assert.Equal("geosphere", lanes[0].SphereId);
    }

    [Fact]
    public void BuildLanes_EmptySnapshot_ReturnsNoLanes()
    {
        var lanes = TrackLaneViewModelBuilder.BuildLanes(new LayerTrackRegistrySnapshot(1, System.Array.Empty<LayerTrackDescriptor>()));

        Assert.Empty(lanes);
    }

    [Theory]
    [InlineData("filmstrip", TrackContentPresenterKind.Filmstrip)]
    [InlineData("graph", TrackContentPresenterKind.Graph)]
    [InlineData("declared-empty", TrackContentPresenterKind.Generic)]
    [InlineData("some-future-content-type", TrackContentPresenterKind.Generic)]
    public void ResolvePresenterKind_MapsKnownTypesAndFallsBackToGeneric(string contentType, TrackContentPresenterKind expected)
    {
        Assert.Equal(expected, TrackLaneViewModelBuilder.ResolvePresenterKind(contentType));
    }

    [Fact]
    public void BuildLanes_MarksGenericPresenterTracksAsDimmed()
    {
        var snapshot = new LayerTrackRegistrySnapshot(1, new[]
        {
            Descriptor("atmosphere", "atmosphere.bulk", contentType: "filmstrip"),
            Descriptor("hydrosphere", "hydrosphere.ocean", contentType: "declared-empty"),
        });

        var lanes = TrackLaneViewModelBuilder.BuildLanes(snapshot);

        var filmstripTrack = lanes.Single(l => l.SphereId == "atmosphere").Tracks.Single();
        var declaredEmptyTrack = lanes.Single(l => l.SphereId == "hydrosphere").Tracks.Single();

        Assert.False(filmstripTrack.IsDimmed);
        Assert.True(declaredEmptyTrack.IsDimmed);
    }
}
