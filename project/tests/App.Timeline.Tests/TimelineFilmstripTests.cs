using System;
using System.Linq;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World;
using Xunit;

namespace FantaSim.App.Timeline.Tests;

public sealed class TimelineFilmstripTests
{
    [Fact]
    public void PlanSlots_CoversVisibleRangeWithOneFramePerNPixels()
    {
        var slots = TimelineFilmstrip.PlanSlots(1_000, 2_000, contentWidth: 250f, frameWidth: 96);

        Assert.Collection(
            slots,
            slot =>
            {
                Assert.Equal(0, slot.Index);
                Assert.Equal(0f, slot.X);
                Assert.Equal(96f, slot.Width);
                Assert.Equal(1_192, slot.Tick);
            },
            slot =>
            {
                Assert.Equal(1, slot.Index);
                Assert.Equal(96f, slot.X);
                Assert.Equal(96f, slot.Width);
                Assert.Equal(1_576, slot.Tick);
            },
            slot =>
            {
                Assert.Equal(2, slot.Index);
                Assert.Equal(192f, slot.X);
                Assert.Equal(58f, slot.Width);
                Assert.Equal(1_884, slot.Tick);
            });
    }

    [Fact]
    public void PlanSlots_NarrowContentStillSpansTheVisibleRangeWithOneSlot()
    {
        var slots = TimelineFilmstrip.PlanSlots(100, 200, contentWidth: 48f, frameWidth: 96);

        var slot = Assert.Single(slots);
        Assert.Equal(0f, slot.X);
        Assert.Equal(48f, slot.Width);
        Assert.Equal(150, slot.Tick);
    }

    [Fact]
    public void TunnelPopulation_FiveVisibleTracksAreBoundedToTwentySamples()
    {
        var samples = TunnelCorridorFilmstripPolicy.PlanVisibleTracks(
            visibleTrackCount: 6,
            currentTick: 0,
            maxTick: 1_000,
            coarseUnitTicks: 1_000);

        Assert.Equal(20, samples.Count);
        Assert.Equal(5, samples.Select(sample => sample.TrackIndex).Distinct().Count());
        Assert.All(
            samples.GroupBy(sample => sample.TrackIndex),
            track => Assert.Equal(4, track.Count()));
    }

    [Fact]
    public void TunnelPopulation_PartialMaxTickRangeKeepsFourSamplesOnFixedCoarseDivisor()
    {
        const long currentTick = 950_000_000;
        const long maxTick = 975_000_000;
        var kb = TimelineModel.GetLadderRungs().Single(rung => rung.Symbol == "kb");
        var coarseUnitTicks = TimelineModel.SpanTicksForRung(kb, units: 1);

        Assert.Equal(100_000_000L, coarseUnitTicks);

        var samples = TunnelCorridorFilmstripPolicy.PlanVisibleTracks(
            visibleTrackCount: 1,
            currentTick,
            maxTick,
            coarseUnitTicks);

        Assert.Equal(4, samples.Count);
        Assert.All(
            samples,
            sample => Assert.Equal(
                (sample.Slot.Tick - currentTick) / (double)coarseUnitTicks,
                sample.DepthFraction,
                precision: 10));
        Assert.True(samples[^1].DepthFraction < 0.25d);
    }

    [Fact]
    public void TunnelDepthCues_AreSeparatedBetweenCurrentPlaneAndThroat()
    {
        const double currentPlaneZ = -5d;
        const double throatZ = -20d;

        var cues = TunnelCorridorFilmstripPolicy.PlanAxialCues(currentPlaneZ, throatZ);

        Assert.True(cues.Count >= 2);
        Assert.All(cues, cue => Assert.InRange(cue.Z, throatZ, currentPlaneZ));
        Assert.Equal(cues.Count, cues.Select(cue => cue.Z).Distinct().Count());
        Assert.True(cues.Max(cue => cue.Z) - cues.Min(cue => cue.Z) >= 5d);
    }

    [Fact]
    public void TunnelDepthCues_TrackIdentityLabelSitsJustAheadOfCurrentPlane()
    {
        const double currentPlaneZ = -5d;

        var labelZ = TunnelCorridorFilmstripPolicy.TrackIdentityLabelZ(currentPlaneZ);

        Assert.InRange(labelZ, currentPlaneZ + 0.10d, currentPlaneZ + 0.30d);
        Assert.True(labelZ < 0d, "An inside-camera corridor label must not return to MouthZ.");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TunnelFrameReset_SupersedesWaitersUntilControllerIsDisposed(
        bool controllerDisposed,
        bool expectedSupersede)
    {
        Assert.Equal(
            expectedSupersede,
            TunnelCorridorFilmstripPolicy.ShouldSupersedeWaitersOnReset(controllerDisposed));
    }

    [Fact]
    public void OrderSlotsNearestToTick_ExpandsOutwardFromPlayhead()
    {
        var slots = TimelineFilmstrip.PlanSlots(0, 1_000, contentWidth: 500f, frameWidth: 100);

        var ordered = TimelineFilmstrip.OrderSlotsNearestToTick(slots, playheadTick: 520);

        Assert.Equal(new[] { 2, 3, 1, 4, 0 }, ordered.Select(slot => slot.Index).ToArray());
    }

    [Fact]
    public void SupersedeQueuedRequests_DropsFramesFromOlderViewGenerations()
    {
        var queued = new[]
        {
            new TimelineFilmstripFrameRequestPlan(10, 0, 100),
            new TimelineFilmstripFrameRequestPlan(11, 1, 200),
            new TimelineFilmstripFrameRequestPlan(10, 2, 300),
        };

        var current = TimelineFilmstrip.SupersedeQueuedRequests(queued, currentGeneration: 11);

        var request = Assert.Single(current);
        Assert.Equal(1, request.SlotIndex);
        Assert.Equal(200, request.Tick);
    }

    [Fact]
    public void CacheKey_UsesLayerSnapshotRungAndSize()
    {
        var key = new TimelineFilmstripCacheKey("geosphere", "geosphere.crust", 105_000_000, "kb", 96, 48);

        Assert.Equal(key, new TimelineFilmstripCacheKey("geosphere", "geosphere.crust", 105_000_000, "kb", 96, 48));
        Assert.NotEqual(key, new TimelineFilmstripCacheKey("geosphere", "geosphere.crust", 110_000_000, "kb", 96, 48));
        Assert.NotEqual(key, new TimelineFilmstripCacheKey("geosphere", "geosphere.crust", 105_000_000, "kc", 96, 48));
    }

    [Fact]
    public void EquirectMapping_MapsCenterPixelsToNearestCellHemisphere()
    {
        var cells = new[]
        {
            new LayerFilmstripCellSample(10, 1f, 0f, 0f, 0, 0.0, 0.0),
            new LayerFilmstripCellSample(20, -1f, 0f, 0f, 1, 0.0, 0.0),
        };

        var west = LayerFilmstripEquirect.PixelToDirection(0, 1, 4, 2);
        var east = LayerFilmstripEquirect.PixelToDirection(2, 1, 4, 2);

        Assert.Equal(20, cells[LayerFilmstripEquirect.NearestCellIndex(west, cells)].CellId);
        Assert.Equal(10, cells[LayerFilmstripEquirect.NearestCellIndex(east, cells)].CellId);
    }

    [Fact]
    public void EquirectMapping_RejectsInvalidImageSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LayerFilmstripEquirect.PixelToDirection(0, 0, 0, 48));
    }
}
