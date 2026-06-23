using FantaSim.App.Timeline;
using Xunit;

namespace App.Timeline.Tests;

public class TimelineDtosTests
{
    [Fact]
    public void TimelineViewSnapshot_RecordsTickAndState()
    {
        var snap = new TimelineViewSnapshot(
            Tick: 500_000,
            State: TimelinePlaybackState.Playing,
            ActiveRegimeId: "magma-ocean",
            MaxTick: 120_000_000);
        Assert.Equal(500_000, snap.Tick);
        Assert.Equal(TimelinePlaybackState.Playing, snap.State);
        Assert.Equal("magma-ocean", snap.ActiveRegimeId);
        Assert.Equal(120_000_000, snap.MaxTick);
    }

    [Fact]
    public void TimelineBand_RecordHoldsAllFields()
    {
        var band = new TimelineBand(
            RegimeId: "magma-ocean",
            StartFraction: 0.0,
            WidthFraction: 0.5,
            Variant: "danger",
            IsActive: true,
            StartTick: 0,
            EndTick: 1_000_000);
        Assert.Equal("magma-ocean", band.RegimeId);
        Assert.True(band.IsActive);
        Assert.Equal(0, band.StartTick);
    }

    [Fact]
    public void TimelinePlaybackState_HasThreeStates()
    {
        Assert.Equal(3, System.Enum.GetNames(typeof(TimelinePlaybackState)).Length);
        Assert.True(System.Enum.IsDefined(typeof(TimelinePlaybackState), "Idle"));
        Assert.True(System.Enum.IsDefined(typeof(TimelinePlaybackState), "Playing"));
        Assert.True(System.Enum.IsDefined(typeof(TimelinePlaybackState), "Scrubbing"));
    }
}
