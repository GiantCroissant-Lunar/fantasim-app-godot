using System.Collections.Generic;
using System.Linq;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World.Composition;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// Headless coverage for <see cref="TunnelCorridorLayout"/> (tunnel slice-1 Task 3): the pure
/// angular-wedge layout over <see cref="TrackLaneViewModelBuilder"/>'s existing output, plus the
/// FIRST real consumer of <see cref="LayerTrackTimeDomain.Rung"/> (verified unconsumed elsewhere
/// in the codebase -- see vault/plans/2026-07-11-tunnel-slice1-plan.md Grounding facts). No
/// Godot types involved.
/// </summary>
public sealed class TunnelCorridorLayoutTests
{
    private static LayerTrackDescriptor Descriptor(
        string sphereId,
        string layerId,
        string contentType = "filmstrip") => new(
        SphereId: sphereId,
        LayerId: layerId,
        StreamId: new LayerTrackStreamId("main", "default", "L0", "world", "default"),
        DisplayName: layerId,
        State: LayerTrackStates.Declared,
        TimeDomain: new LayerTrackTimeDomain(0L, null, "ka"),
        Content: new LayerTrackContent(contentType),
        Capabilities: new[] { "scrub", "toggle" },
        SourceRef: layerId);

    private static TrackRowViewModel Row(
        string sphereId,
        string layerId,
        TrackContentPresenterKind presenterKind = TrackContentPresenterKind.Filmstrip,
        bool isDimmed = false) => new(
        Descriptor(sphereId, layerId, presenterKind == TrackContentPresenterKind.Graph ? "graph" : "filmstrip"),
        presenterKind,
        isDimmed);

    // ---- BuildWedges ----

    [Fact]
    public void BuildWedges_OneLaneOneTrack_SpansFull360StartingAtZero()
    {
        var lanes = new[]
        {
            new TrackLaneViewModel("geosphere", new[] { Row("geosphere", "geosphere.crust") }),
        };

        var wedges = TunnelCorridorLayout.BuildWedges(lanes);

        var wedge = Assert.Single(wedges);
        Assert.Equal("geosphere", wedge.SphereId);
        Assert.Equal("geosphere.crust", wedge.LayerId);
        Assert.Equal(0.0, wedge.StartAngleDeg, precision: 6);
        Assert.Equal(360.0, wedge.SpanAngleDeg, precision: 6);
    }

    [Fact]
    public void BuildWedges_TwoLanesOneTrackEach_SplitsIntoTwo180DegSectorsInBuildLanesOrder()
    {
        var lanes = new[]
        {
            new TrackLaneViewModel("atmosphere", new[] { Row("atmosphere", "atmosphere.bulk") }),
            new TrackLaneViewModel("geosphere", new[] { Row("geosphere", "geosphere.crust") }),
        };

        var wedges = TunnelCorridorLayout.BuildWedges(lanes);

        Assert.Equal(2, wedges.Count);
        Assert.Equal("atmosphere", wedges[0].SphereId);
        Assert.Equal(0.0, wedges[0].StartAngleDeg, precision: 6);
        Assert.Equal(180.0, wedges[0].SpanAngleDeg, precision: 6);
        Assert.Equal("geosphere", wedges[1].SphereId);
        Assert.Equal(180.0, wedges[1].StartAngleDeg, precision: 6);
        Assert.Equal(180.0, wedges[1].SpanAngleDeg, precision: 6);
    }

    [Fact]
    public void BuildWedges_OneLaneThreeTracks_SplitsFullSectorIntoThreeContiguous120DegWedges()
    {
        var lanes = new[]
        {
            new TrackLaneViewModel("geosphere", new[]
            {
                Row("geosphere", "geosphere.crust"),
                Row("geosphere", "geosphere.plate"),
                Row("geosphere", "geosphere.mantle"),
            }),
        };

        var wedges = TunnelCorridorLayout.BuildWedges(lanes);

        Assert.Equal(3, wedges.Count);
        Assert.All(wedges, w => Assert.Equal(120.0, w.SpanAngleDeg, precision: 6));

        // Contiguous, no gaps/overlap: cumulative start angles chain start-to-start.
        Assert.Equal(0.0, wedges[0].StartAngleDeg, precision: 6);
        Assert.Equal(120.0, wedges[1].StartAngleDeg, precision: 6);
        Assert.Equal(240.0, wedges[2].StartAngleDeg, precision: 6);

        var totalSectorSpan = wedges.Sum(w => w.SpanAngleDeg);
        Assert.Equal(360.0, totalSectorSpan, precision: 6);
    }

    [Fact]
    public void BuildWedges_PassesThroughIsDimmedAndPresenterKindUnchanged()
    {
        var lanes = new[]
        {
            new TrackLaneViewModel("hydrosphere", new[]
            {
                Row("hydrosphere", "hydrosphere.ocean", TrackContentPresenterKind.Generic, isDimmed: true),
            }),
        };

        var wedges = TunnelCorridorLayout.BuildWedges(lanes);

        var wedge = Assert.Single(wedges);
        Assert.True(wedge.IsDimmed);
        Assert.Equal(TrackContentPresenterKind.Generic, wedge.PresenterKind);
    }

    [Fact]
    public void BuildWedges_EmptyLanes_ReturnsEmptyResult_NoThrow()
    {
        var wedges = TunnelCorridorLayout.BuildWedges(System.Array.Empty<TrackLaneViewModel>());

        Assert.Empty(wedges);
    }

    // ---- ResolveCorridorRung ----

    [Fact]
    public void ResolveCorridorRung_KnownSymbol_ReturnsThatRungNotTheFallback()
    {
        var allRungs = TimelineModel.GetLadderRungs();
        var expected = allRungs.Single(r => r.Symbol == "ka");
        // Picked independent of ladder ordering: any OTHER rung than "ka" makes a valid fallback
        // to prove the resolved value is the matched rung, not the fallback.
        var fallback = allRungs.First(r => r.Symbol != "ka");

        var resolved = TunnelCorridorLayout.ResolveCorridorRung("ka", fallback);

        Assert.Equal(expected.Symbol, resolved.Symbol);
        Assert.NotEqual(fallback.Symbol, resolved.Symbol);
    }

    [Fact]
    public void ResolveCorridorRung_NullSymbol_ReturnsFallbackExactly()
    {
        var fallback = TimelineModel.GetLadderRungs().First();

        var resolved = TunnelCorridorLayout.ResolveCorridorRung(null, fallback);

        Assert.Equal(fallback.Symbol, resolved.Symbol);
    }

    [Fact]
    public void ResolveCorridorRung_UnrecognizedSymbol_DegradesToFallback_NeverThrows()
    {
        var fallback = TimelineModel.GetLadderRungs().First();

        var resolved = TunnelCorridorLayout.ResolveCorridorRung("bogus", fallback);

        Assert.Equal(fallback.Symbol, resolved.Symbol);
    }
}
