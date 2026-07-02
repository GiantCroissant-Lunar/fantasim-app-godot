using FantaSim.App.World.Composition;
using Xunit;

namespace App.World.Composition.Tests;

public class RegimeSurfaceResolverTests
{
    [Theory]
    [InlineData("magma-ocean", RegimeSurfaceKind.MagmaOcean)]
    [InlineData("stagnant-lid", RegimeSurfaceKind.StagnantLid)]
    [InlineData("mobile-plate", RegimeSurfaceKind.MobilePlate)]
    public void Resolve_MapsKnownRegimeIds(string regimeId, RegimeSurfaceKind expected)
    {
        Assert.Equal(expected, RegimeSurfaceResolver.Resolve(regimeId));
    }

    [Fact]
    public void Resolve_NullRegimeIdFallsBackToDefault()
    {
        Assert.Equal(RegimeSurfaceKind.Default, RegimeSurfaceResolver.Resolve(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("planetesimal-swarm")]
    // Stable regime ids are lowercase; a differently-cased id is NOT a match (host compares ordinally).
    [InlineData("Magma-Ocean")]
    [InlineData("MOBILE-PLATE")]
    public void Resolve_UnknownOrMismatchedCaseFallsBackToDefault(string regimeId)
    {
        Assert.Equal(RegimeSurfaceKind.Default, RegimeSurfaceResolver.Resolve(regimeId));
    }
}
