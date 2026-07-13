using FantaSim.App.Presentation.Tunnel;
using FantaSim.App.Timeline.Seam;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

/// <summary>
/// Headless coverage for the planet-occlusion decision used during wall picking. The effective
/// visual radius must reflect the current zoom scale and the body's original scale so zoomed-in
/// edges reject wall picks and zoomed-out empty space remains pickable. Non-finite/bad inputs fail
/// closed without propagating NaN/Infinity.
/// </summary>
public sealed class TunnelPlanetOcclusionPolicyTests
{
    [Fact]
    public void EffectiveRadius_UnitScale_IsContractRadius()
    {
        var radius = TunnelPlanetOcclusionPolicy.EffectiveRadius(
            originalScale: 1.0f,
            zoomScale: 1.0f,
            baseRadius: 2.0f);

        Assert.Equal(2.0f, radius);
    }

    [Theory]
    [InlineData(0.35f, 0.70f)]
    [InlineData(1.0f, 2.0f)]
    [InlineData(3.0f, 6.0f)]
    public void EffectiveRadius_ScalesWithZoom(float zoom, float expected)
    {
        var radius = TunnelPlanetOcclusionPolicy.EffectiveRadius(
            originalScale: 1.0f,
            zoomScale: zoom,
            baseRadius: 2.0f);

        Assert.Equal(expected, radius, precision: 5);
    }

    [Theory]
    [InlineData(0.5f, 1.0f)]
    [InlineData(2.0f, 4.0f)]
    public void EffectiveRadius_ScalesWithOriginalBodyScale(float original, float expected)
    {
        var radius = TunnelPlanetOcclusionPolicy.EffectiveRadius(
            originalScale: original,
            zoomScale: 1.0f,
            baseRadius: 2.0f);

        Assert.Equal(expected, radius, precision: 5);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void EffectiveRadius_BadOriginalScale_FallsBackToBaseRadius(float bad)
    {
        var radius = TunnelPlanetOcclusionPolicy.EffectiveRadius(
            originalScale: bad,
            zoomScale: 1.0f,
            baseRadius: 2.0f);

        Assert.Equal(2.0f, radius, precision: 5);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void EffectiveRadius_BadZoomScale_FallsBackToDefault(float bad)
    {
        var radius = TunnelPlanetOcclusionPolicy.EffectiveRadius(
            originalScale: 1.0f,
            zoomScale: bad,
            baseRadius: 2.0f);

        Assert.Equal(2.0f * TunnelPlanetZoom.DefaultScale, radius, precision: 5);
    }

    [Fact]
    public void TryResolveEffectiveRadius_NonUniformScale_UsesMaxAxis()
    {
        var result = TunnelPlanetOcclusionPolicy.TryResolveEffectiveRadius(
            originalScale: new System.Numerics.Vector3(1.0f, 2.0f, 0.5f),
            zoomScale: 1.0f,
            baseRadius: 2.0f);

        Assert.True(result.HasValue);
        Assert.Equal(4.0f, result.Value, precision: 5);
    }

    [Fact]
    public void TryResolveEffectiveRadius_NonUniformScale_BadAxisMakesNoValue()
    {
        var result = TunnelPlanetOcclusionPolicy.TryResolveEffectiveRadius(
            originalScale: new System.Numerics.Vector3(1.0f, float.NaN, 0.5f),
            zoomScale: 1.0f,
            baseRadius: 2.0f);

        Assert.False(result.HasValue);
    }
}
