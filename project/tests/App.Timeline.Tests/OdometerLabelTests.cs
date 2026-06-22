using Xunit;
using FantaSim.World.Contracts.Quantities;
using FantaSim.World.Contracts.Units;

namespace App.Timeline.Tests;

public class OdometerLabelTests
{
    [Theory]
    [InlineData(0, "0 jv")]            // amount 0 floors to the profile's lowest ladder rung (no glyph >= 1)
    [InlineData(500_000, "5 ka")]
    [InlineData(100_000_000, "1 kb")]  // onset: 1e8 CT = 1000 Ma = 1 kb (the ka->kb rollover)
    [InlineData(150_000_000, "1.50 kb")] // fractional amounts render with 2 decimal places
    public void FormatCanonicalTick_YieldsCorrectOdometerLabel(long tick, string expected)
    {
        double kaAmount = (double)tick / UnitConverter.TicksPerMegaAnnum;
        var result = CanonicalDisplayFormatter.Format(
            kaAmount,
            BaselineScaleProfiles.GeospherePlateTimeV1,
            new CanonicalFormatterOptions(IncludeUnitSuffix: true));
        Assert.Equal(expected, result);
    }
}
