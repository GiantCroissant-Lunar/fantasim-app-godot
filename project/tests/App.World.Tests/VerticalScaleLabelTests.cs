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
    [InlineData(GlobeViewMode.World, true)]
    [InlineData(GlobeViewMode.HypsometricTerrain, true)]
    [InlineData(GlobeViewMode.PlateIdentity, false)]
    [InlineData(GlobeViewMode.Inactive, false)]
    public void ShouldShowIndicator_ForDisplacingViews(GlobeViewMode mode, bool expected)
    {
        Assert.Equal(expected, VerticalScaleLabel.ShouldShowIndicator(mode));
    }

    [Fact]
    public void BuildStatusLabelSuffix_AppendsIndicatorToRegimeAndTick()
    {
        var suffix = VerticalScaleLabel.BuildIndicatorSuffix(0.00001);
        Assert.Equal("  |  vertical x1e-5 units", suffix);
    }

    [Fact]
    public void BuildIndicatorSuffix_WithLinearExponent_MatchesPlainForm()
    {
        // Exponent 1 IS the linear lens — the label must not invent a profile that isn't there.
        var suffix = VerticalScaleLabel.BuildIndicatorSuffix(0.00001, heightExponent: 1.0);
        Assert.Equal("  |  vertical x1e-5 units", suffix);
    }

    [Fact]
    public void BuildIndicatorSuffix_WithProfileExponent_NamesTheProfile()
    {
        // S2 honesty for the non-linear lens: the indicator must say the displacement is
        // sign(h)*|h|^0.5 * scale, not pretend it is a linear factor.
        var suffix = VerticalScaleLabel.BuildIndicatorSuffix(0.0005, heightExponent: 0.5);
        Assert.Equal("  |  vertical h^0.5 x5e-4 units", suffix);
    }
}