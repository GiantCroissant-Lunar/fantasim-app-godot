using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace App.Presentation.Tests;

public sealed class TunnelStagePreparationOrderingTests
{
    [Fact]
    public void TunnelFirst_WithoutPlanetBody_RetriesWithoutCommittingMount()
    {
        var action = TunnelStagePreparationPolicy.Decide(new TunnelStagePreparationReadiness(
            ExpectedGeneration: 4L,
            CurrentGeneration: 4L,
            BinderAlive: true,
            WorldLoaded: true,
            StageLoaded: true,
            HasEnvironment: true,
            HasValidPlanetBody: false,
            PlanetBodyInsideTree: false));

        Assert.Equal(TunnelStagePreparationAction.RetryNextFrame, action);
    }

    [Fact]
    public void SameGenerationPlanetBodyInsideTree_PreparesHiddenMount()
    {
        var action = TunnelStagePreparationPolicy.Decide(new TunnelStagePreparationReadiness(
            ExpectedGeneration: 4L,
            CurrentGeneration: 4L,
            BinderAlive: true,
            WorldLoaded: true,
            StageLoaded: true,
            HasEnvironment: true,
            HasValidPlanetBody: true,
            PlanetBodyInsideTree: true));

        Assert.Equal(TunnelStagePreparationAction.PrepareHidden, action);
    }

    [Theory]
    [InlineData(3L, 4L, true)]
    [InlineData(4L, 4L, false)]
    public void StaleGenerationOrDeadBinder_IsIgnored(long expected, long current, bool alive)
    {
        var action = TunnelStagePreparationPolicy.Decide(new TunnelStagePreparationReadiness(
            ExpectedGeneration: expected,
            CurrentGeneration: current,
            BinderAlive: alive,
            WorldLoaded: true,
            StageLoaded: true,
            HasEnvironment: true,
            HasValidPlanetBody: true,
            PlanetBodyInsideTree: true));

        Assert.Equal(TunnelStagePreparationAction.Ignore, action);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void InvalidOrDetachedPlanetBody_CannotPrepare(bool valid, bool insideTree)
    {
        var action = TunnelStagePreparationPolicy.Decide(new TunnelStagePreparationReadiness(
            ExpectedGeneration: 9L,
            CurrentGeneration: 9L,
            BinderAlive: true,
            WorldLoaded: true,
            StageLoaded: true,
            HasEnvironment: true,
            HasValidPlanetBody: valid,
            PlanetBodyInsideTree: insideTree));

        Assert.Equal(TunnelStagePreparationAction.RetryNextFrame, action);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void MissingLoadedDependency_IsIgnored(bool worldLoaded, bool stageLoaded)
    {
        var action = TunnelStagePreparationPolicy.Decide(new TunnelStagePreparationReadiness(
            ExpectedGeneration: 2L,
            CurrentGeneration: 2L,
            BinderAlive: true,
            WorldLoaded: worldLoaded,
            StageLoaded: stageLoaded,
            HasEnvironment: true,
            HasValidPlanetBody: true,
            PlanetBodyInsideTree: true));

        Assert.Equal(TunnelStagePreparationAction.Ignore, action);
    }
}
