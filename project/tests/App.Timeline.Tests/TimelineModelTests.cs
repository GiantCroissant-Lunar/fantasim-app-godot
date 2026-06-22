using System.Linq;
using FantaSim.App.World.Composition;
using FantaSim.App.Timeline;
using Xunit;

namespace App.Timeline.Tests;

public class TimelineModelTests
{
    private static SphereRegimeSchedule Geo() =>
        SphereRegimeScheduleDefaults.GeosphereFor(SphereRegimeScheduleDefaults.PlateOnsetTick); // onset = 1e8

    [Fact]
    public void Bands_AreProportional_AndCoverZeroToOne()
    {
        var bands = TimelineModel.Bands(Geo(), maxTick: 120_000_000, currentTick: 0);
        Assert.Equal(3, bands.Count);                              // magma / lid / mobile
        Assert.Equal(0.0, bands[0].StartFraction, 6);             // magma starts at 0
        // widths sum to maxTick coverage (mobile clamped to maxTick): ~1.0
        Assert.Equal(1.0, bands.Sum(b => b.WidthFraction), 3);
        Assert.True(bands[0].WidthFraction < bands[1].WidthFraction); // magma (1e6) << lid (1e6..1e8)
    }

    [Fact]
    public void Bands_MarkActiveRegime()
    {
        var bands = TimelineModel.Bands(Geo(), 120_000_000, currentTick: 500_000); // magma-ocean
        Assert.Equal("magma-ocean", bands.Single(b => b.IsActive).RegimeId);

        var atOnset = TimelineModel.Bands(Geo(), 120_000_000, currentTick: 100_000_000); // mobile-plate
        Assert.Equal("mobile-plate", atOnset.Single(b => b.IsActive).RegimeId);
    }

    [Fact]
    public void Tracks_ListAllLayers_HighlightActive()
    {
        var tracks = TimelineModel.Tracks(Geo(), currentTick: 500_000); // magma regime active
        Assert.Contains(tracks, t => t.LayerId == "geosphere.magma-ocean" && t.IsActive);
        Assert.Contains(tracks, t => t.LayerId == "geosphere.plate" && !t.IsActive);
    }
}
