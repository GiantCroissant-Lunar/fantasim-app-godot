using FantaSim.App.Timeline.Seam;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// Headless coverage for <see cref="TunnelScrubMapper"/> (tunnel slice-1 Task 4): the radius-
/// gated press dispatch (mirrors the wireframe's mode='time' vs 'wall' split, spec §5.1) and the
/// linear horizontal-pixel-delta-to-tick-delta drag mapping. No Godot types involved. See
/// vault/plans/2026-07-11-tunnel-slice1-plan.md Task 4.
/// </summary>
public sealed class TunnelScrubMapperTests
{
    // ---- IsWithinRingBand ----

    [Fact]
    public void IsWithinRingBand_ExactlyAtRingRadius_ReturnsTrue()
    {
        Assert.True(TunnelScrubMapper.IsWithinRingBand(screenRadiusPx: 100f, ringRadiusPx: 100f, bandPx: 8f));
    }

    [Fact]
    public void IsWithinRingBand_ExactlyBandPxAway_ReturnsTrue_InclusiveBoundary()
    {
        Assert.True(TunnelScrubMapper.IsWithinRingBand(screenRadiusPx: 108f, ringRadiusPx: 100f, bandPx: 8f));
        Assert.True(TunnelScrubMapper.IsWithinRingBand(screenRadiusPx: 92f, ringRadiusPx: 100f, bandPx: 8f));
    }

    [Fact]
    public void IsWithinRingBand_JustOverBandPxAway_ReturnsFalse()
    {
        Assert.False(TunnelScrubMapper.IsWithinRingBand(screenRadiusPx: 109f, ringRadiusPx: 100f, bandPx: 8f));
    }

    [Fact]
    public void IsWithinRingBand_NegativeScreenRadius_HandledWithoutThrowing()
    {
        // A negative press radius is nonsensical input, but must degrade gracefully (never throw)
        // -- close enough to a ring near zero still reads as a hit.
        Assert.True(TunnelScrubMapper.IsWithinRingBand(screenRadiusPx: -5f, ringRadiusPx: 0f, bandPx: 10f));
        // Far away still correctly reads as a miss, no exception either way.
        Assert.False(TunnelScrubMapper.IsWithinRingBand(screenRadiusPx: -500f, ringRadiusPx: 100f, bandPx: 8f));
    }

    // ---- DragDeltaToTick ----

    [Fact]
    public void DragDeltaToTick_ZeroDelta_ReturnsBaseTickUnchanged()
    {
        var tick = TunnelScrubMapper.DragDeltaToTick(
            pixelDeltaX: 0f, viewportWidthPx: 800f, viewStartTick: 0L, viewEndTick: 1_000L, baseTick: 400L);

        Assert.Equal(400L, tick);
    }

    [Fact]
    public void DragDeltaToTick_FullWidthDrag_MovesByFullViewSpan()
    {
        var tick = TunnelScrubMapper.DragDeltaToTick(
            pixelDeltaX: 800f, viewportWidthPx: 800f, viewStartTick: 0L, viewEndTick: 1_000L, baseTick: 0L);

        Assert.Equal(1_000L, tick);
    }

    [Fact]
    public void DragDeltaToTick_OvershootPastViewEnd_ClampsToViewEndTick()
    {
        var tick = TunnelScrubMapper.DragDeltaToTick(
            pixelDeltaX: 400f, viewportWidthPx: 800f, viewStartTick: 0L, viewEndTick: 1_000L, baseTick: 900L);

        Assert.Equal(1_000L, tick);
    }

    [Fact]
    public void DragDeltaToTick_OvershootPastViewStart_ClampsToViewStartTick()
    {
        var tick = TunnelScrubMapper.DragDeltaToTick(
            pixelDeltaX: -8_000f, viewportWidthPx: 800f, viewStartTick: 0L, viewEndTick: 1_000L, baseTick: 100L);

        Assert.Equal(0L, tick);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-10f)]
    public void DragDeltaToTick_NonPositiveViewportWidth_ReturnsBaseTickUnchanged(float viewportWidthPx)
    {
        var tick = TunnelScrubMapper.DragDeltaToTick(
            pixelDeltaX: 50f, viewportWidthPx: viewportWidthPx, viewStartTick: 0L, viewEndTick: 1_000L, baseTick: 500L);

        Assert.Equal(500L, tick);
    }
}
