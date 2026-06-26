using FantaSim.App.World.Composition;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class TimelineLayerSelectionTests
{
    [Fact]
    public void Equals_SameSphereAndLayer_AreEqual()
    {
        var a = new TimelineLayerSelection("geosphere", "geosphere.crust");
        var b = new TimelineLayerSelection("geosphere", "geosphere.crust");

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equals_DifferentLayer_AreNotEqual()
    {
        var a = new TimelineLayerSelection("geosphere", "geosphere.crust");
        var b = new TimelineLayerSelection("geosphere", "geosphere.plate");

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void Equals_DifferentSphere_AreNotEqual()
    {
        var a = new TimelineLayerSelection("geosphere", "atmosphere.cloud");
        var b = new TimelineLayerSelection("atmosphere", "atmosphere.cloud");

        Assert.NotEqual(a, b);
    }
}
