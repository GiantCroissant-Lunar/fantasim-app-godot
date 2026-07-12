using FantaSim.App.Timeline.Seam;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// Headless coverage for the tunnel planet-zoom step (vault/plans/
/// 2026-07-13-tunnel-visual-slice1-plan.md Part C). Multiplicative per-notch scale, clamped to a
/// sane range; a non-finite or non-positive current resets to the default before stepping so a
/// corrupted scale can never propagate to the shared planet body.
/// </summary>
public sealed class TunnelPlanetZoomTests
{
    [Fact]
    public void Step_In_MultipliesByStepFactor()
    {
        var next = TunnelPlanetZoom.Step(1.0f, direction: 1);
        Assert.Equal(TunnelPlanetZoom.StepFactor, next, precision: 5);
    }

    [Fact]
    public void Step_Out_DividesByStepFactor()
    {
        var next = TunnelPlanetZoom.Step(1.0f, direction: -1);
        Assert.Equal(1.0f / TunnelPlanetZoom.StepFactor, next, precision: 5);
    }

    [Fact]
    public void Step_Zero_LeavesScaleUnchanged()
    {
        Assert.Equal(1.4f, TunnelPlanetZoom.Step(1.4f, direction: 0), precision: 5);
    }

    [Fact]
    public void Step_RepeatedIn_SaturatesAtMax()
    {
        var s = 1.0f;
        for (var i = 0; i < 100; i++)
            s = TunnelPlanetZoom.Step(s, direction: 1);
        Assert.Equal(TunnelPlanetZoom.MaxScale, s, precision: 5);
    }

    [Fact]
    public void Step_RepeatedOut_SaturatesAtMin()
    {
        var s = 1.0f;
        for (var i = 0; i < 100; i++)
            s = TunnelPlanetZoom.Step(s, direction: -1);
        Assert.Equal(TunnelPlanetZoom.MinScale, s, precision: 5);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(0f)]
    [InlineData(-2f)]
    public void Step_NonFiniteOrNonPositiveCurrent_ResetsToDefaultThenSteps(float bad)
    {
        var next = TunnelPlanetZoom.Step(bad, direction: 1);
        Assert.Equal(TunnelPlanetZoom.DefaultScale * TunnelPlanetZoom.StepFactor, next, precision: 5);
    }
}
