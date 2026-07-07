using System;
using FantaSim.App.Timeline;
using FantaSim.App.World;
using Xunit;

namespace FantaSim.App.Timeline.Tests;

public sealed class TimelineFilmstripTests
{
    [Fact]
    public void PlanSlots_CoversVisibleRangeWithOneFramePerN Pixels()
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
