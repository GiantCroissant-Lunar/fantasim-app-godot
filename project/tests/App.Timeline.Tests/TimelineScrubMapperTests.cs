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

    [Fact]
    public void ZoomWindowAroundLocalX_KeepsCursorTickUnderCursor()
    {
        var mapped = TimelineScrubMapper.TryZoomWindowAroundLocalX(
            localX: 25f,
            surfaceWidth: 100f,
            viewStartTick: 100L,
            viewEndTick: 500L,
            targetSpanTicks: 200L,
            minViewSpanTicks: 1L,
            maxTick: 1_000L,
            out var start,
            out var end);

        Assert.True(mapped);
        Assert.Equal(150L, start);
        Assert.Equal(350L, end);

        var cursorTickAfterZoom = start + ((end - start) * 0.25);
        Assert.Equal(200.0, cursorTickAfterZoom, precision: 6);
    }

    [Fact]
    public void ZoomWindowAroundLocalX_ClampsAtTimelineStart()
    {
        var mapped = TimelineScrubMapper.TryZoomWindowAroundLocalX(
            localX: 10f,
            surfaceWidth: 100f,
            viewStartTick: 0L,
            viewEndTick: 200L,
            targetSpanTicks: 400L,
            minViewSpanTicks: 1L,
            maxTick: 1_000L,
            out var start,
            out var end);

        Assert.True(mapped);
        Assert.Equal(0L, start);
        Assert.Equal(400L, end);
    }

    [Fact]
    public void ZoomWindowAroundLocalX_ClampsAtTimelineEnd()
    {
        var mapped = TimelineScrubMapper.TryZoomWindowAroundLocalX(
            localX: 90f,
            surfaceWidth: 100f,
            viewStartTick: 800L,
            viewEndTick: 1_000L,
            targetSpanTicks: 400L,
            minViewSpanTicks: 1L,
            maxTick: 1_000L,
            out var start,
            out var end);

        Assert.True(mapped);
        Assert.Equal(600L, start);
        Assert.Equal(1_000L, end);
    }

    [Fact]
    public void ZoomWindowAroundLocalX_ClampsTargetSpanToTimeline()
    {
        var mapped = TimelineScrubMapper.TryZoomWindowAroundLocalX(
            localX: 50f,
            surfaceWidth: 100f,
            viewStartTick: 300L,
            viewEndTick: 700L,
            targetSpanTicks: 2_000L,
            minViewSpanTicks: 1L,
            maxTick: 1_000L,
            out var start,
            out var end);

        Assert.True(mapped);
        Assert.Equal(0L, start);
        Assert.Equal(1_000L, end);
    }

    [Fact]
    public void ZoomWindowAroundLocalX_IgnoresUnavailableSurface()
    {
        var mapped = TimelineScrubMapper.TryZoomWindowAroundLocalX(
            localX: 50f,
            surfaceWidth: 0f,
            viewStartTick: 100L,
            viewEndTick: 500L,
            targetSpanTicks: 200L,
            minViewSpanTicks: 1L,
            maxTick: 1_000L,
            out var start,
            out var end);

        Assert.False(mapped);
        Assert.Equal(100L, start);
        Assert.Equal(500L, end);
    }
}
