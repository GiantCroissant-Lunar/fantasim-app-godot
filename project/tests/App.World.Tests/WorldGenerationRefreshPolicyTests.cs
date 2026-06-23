using FantaSim.App.World.Dto;
using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class WorldGenerationRefreshPolicyTests
{
    [Fact]
    public void ShouldRefreshGlobe_ReturnsTrueForGenerationChanges()
    {
        var evt = new WorldGenerationChangedEvent(
            WorldId: "default",
            ChangeType: "generation",
            Detail: "world-generation.graph");

        Assert.True(WorldGenerationRefreshPolicy.ShouldRefreshGlobe(evt));
    }

    [Fact]
    public void ShouldRefreshGlobe_ReturnsFalseForNonGenerationChanges()
    {
        var evt = new WorldGenerationChangedEvent(
            WorldId: "default",
            ChangeType: "field",
            Detail: "app.elevation-m");

        Assert.False(WorldGenerationRefreshPolicy.ShouldRefreshGlobe(evt));
    }
}
