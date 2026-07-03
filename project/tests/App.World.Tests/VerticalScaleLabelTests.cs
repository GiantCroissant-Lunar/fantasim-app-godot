using FantaSim.App.World.Composition;
using FantaSim.App.World.Globe;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class VerticalScaleLabelTests
{
    [Theory]
    [InlineData(0.00001, "relief x1e-5 units")]
    [InlineData(0.0001, "relief x1e-4 units")]
    [InlineData(0.001, "relief x1e-3 units")]
    [InlineData(0.00012, "relief x1.2e-4 units")]
    [InlineData(1.0, "relief x1 units")]
    [InlineData(300.0, "relief x300 units")]
    [InlineData(1234.5, "relief x1234.5 units")]
    public void Format_ForRawFactor_HumanizesScientificNotation(double exaggeration, string expected)
    {
        Assert.Equal(expected, VerticalScaleLabel.FormatRawFactor(exaggeration));
    }

    [Fact]
    public void FormatRawFactor_ThrowsForNonPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VerticalScaleLabel.FormatRawFactor(0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => VerticalScaleLabel.FormatRawFactor(-1.0));
    }

    [Theory]
    [InlineData(GlobeViewMode.HypsometricTerrain, true)]
    [InlineData(GlobeViewMode.PlateIdentity, false)]
    [InlineData(GlobeViewMode.Inactive, false)]
    public void ShouldShowIndicator_OnlyForHypsometricTerrain(GlobeViewMode mode, bool expected)
    {
        Assert.Equal(expected, VerticalScaleLabel.ShouldShowIndicator(mode));
    }

    [Fact]
    public void BuildStatusLabelSuffix_AppendsIndicatorToRegimeAndTick()
    {
        var suffix = VerticalScaleLabel.BuildIndicatorSuffix(0.00001);
        Assert.Equal("  |  vertical x1e-5 units", suffix);
    }
}