using FantaSim.App.Timeline.Seam;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// Headless coverage for <see cref="TunnelDepthMapper"/> (tunnel slice-1 Task 1): pins the SHAPE
/// of the fraction-to-radius falloff (monotonic, parameterized, asymptotic toward the throat) --
/// not final numbers, per spec Decision Point 12's deferred real-data tuning pass. See
/// vault/plans/2026-07-11-tunnel-slice1-plan.md Task 1.
/// </summary>
public sealed class TunnelDepthMapperTests
{
    private const double ThroatRadius = 50.0;
    private const double OuterRadius = 400.0;

    [Fact]
    public void RadiusForFraction_AtZero_ReturnsOuterRadiusExactly()
    {
        var radius = TunnelDepthMapper.RadiusForFraction(0.0, ThroatRadius, OuterRadius);

        Assert.Equal(OuterRadius, radius, precision: 9);
    }

    [Fact]
    public void RadiusForFraction_AtOne_IsStrictlyGreaterThanThroatButCloserToThroatThanMidpoint()
    {
        var radius = TunnelDepthMapper.RadiusForFraction(1.0, ThroatRadius, OuterRadius);
        var midpoint = (ThroatRadius + OuterRadius) / 2.0;

        Assert.True(radius > ThroatRadius, $"radius {radius} should be strictly greater than throat {ThroatRadius}");
        Assert.True(radius < midpoint, $"radius {radius} should be closer to throat than the midpoint {midpoint}");
    }

    [Fact]
    public void RadiusForFraction_IsMonotonicallyDecreasingAsFractionIncreases()
    {
        double[] fractions = { 0.0, 0.1, 0.25, 0.4, 0.5, 0.6, 0.75, 0.9, 1.0 };
        double previous = double.PositiveInfinity;

        foreach (var fraction in fractions)
        {
            var radius = TunnelDepthMapper.RadiusForFraction(fraction, ThroatRadius, OuterRadius);
            Assert.True(radius < previous, $"radius at fraction {fraction} ({radius}) should be strictly less than the previous sample ({previous})");
            previous = radius;
        }
    }

    [Fact]
    public void RadiusForFraction_LargerFalloffK_ProducesSmallerRadiusAtFixedMidFraction()
    {
        var radiusSmallK = TunnelDepthMapper.RadiusForFraction(0.5, ThroatRadius, OuterRadius, falloffK: 1.0);
        var radiusLargeK = TunnelDepthMapper.RadiusForFraction(0.5, ThroatRadius, OuterRadius, falloffK: 6.0);

        Assert.True(radiusLargeK < radiusSmallK, $"larger falloffK ({radiusLargeK}) should crowd tighter toward the throat than smaller falloffK ({radiusSmallK})");
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(-100.0)]
    public void RadiusForFraction_NegativeFraction_ClampsToOuterRadius(double fraction)
    {
        var radius = TunnelDepthMapper.RadiusForFraction(fraction, ThroatRadius, OuterRadius);

        Assert.Equal(OuterRadius, radius, precision: 9);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(100.0)]
    public void RadiusForFraction_FractionAboveOne_ClampsToFractionOneRadius(double fraction)
    {
        var radiusAtOne = TunnelDepthMapper.RadiusForFraction(1.0, ThroatRadius, OuterRadius);
        var radius = TunnelDepthMapper.RadiusForFraction(fraction, ThroatRadius, OuterRadius);

        Assert.Equal(radiusAtOne, radius, precision: 9);
    }

    [Theory]
    [InlineData(-10.0)]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(10.0)]
    public void RadiusForFraction_NeverEscapesTheThroatToOuterRange(double fraction)
    {
        var radius = TunnelDepthMapper.RadiusForFraction(fraction, ThroatRadius, OuterRadius);

        Assert.InRange(radius, ThroatRadius, OuterRadius);
    }
}
