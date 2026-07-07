using FantaSim.App.Timeline.Seam;
using Xunit;

namespace App.Timeline.Tests;

public class TimelineScrubMapperTests
{
    [Theory]
    [InlineData(0f, 100L)]
    [InlineData(100f, 200L)]
    [InlineData(50f, 150L)]
    [InlineData(-25f, 100L)]
    [InlineData(125f, 200L)]
    public void LocalXToTick_MapsAndClampsAcrossVisibleSpan(float localX, long expectedTick)
    {
        var mapped = TimelineScrubMapper.TryLocalXToTick(
            localX,
            surfaceWidth: 100f,
            viewStartTick: 100L,
            viewEndTick: 200L,
            out var tick);

        Assert.True(mapped);
        Assert.Equal(expectedTick, tick);
    }

    [Fact]
    public void LocalXToTick_ReusesCurrentSpanWhenViewIsZoomed()
    {
        var mapped = TimelineScrubMapper.TryLocalXToTick(
            localX: 25f,
            surfaceWidth: 100f,
            viewStartTick: 1_000L,
            viewEndTick: 1_400L,
            out var tick);

        Assert.True(mapped);
        Assert.Equal(1_100L, tick);
    }

    [Fact]
    public void LocalXToTick_IgnoresUnavailableSurface()
    {
        var mapped = TimelineScrubMapper.TryLocalXToTick(
            localX: 50f,
            surfaceWidth: 0f,
            viewStartTick: 100L,
            viewEndTick: 200L,
            out var tick);

        Assert.False(mapped);
        Assert.Equal(100L, tick);
    }

    [Theory]
    [InlineData(100L, 100L, 200L, 0.0)]
    [InlineData(150L, 100L, 200L, 0.5)]
    [InlineData(200L, 100L, 200L, 1.0)]
    [InlineData(50L, 100L, 200L, 0.0)]
    [InlineData(250L, 100L, 200L, 1.0)]
    public void TickToFraction_MapsAndClampsForPlayhead(long tick, long start, long end, double expected)
    {
        Assert.Equal(expected, TimelineScrubMapper.TickToFraction(tick, start, end), precision: 6);
    }
}
