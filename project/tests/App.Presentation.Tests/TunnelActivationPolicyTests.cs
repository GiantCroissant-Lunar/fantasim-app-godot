using FantaSim.App.Presentation;
using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace App.Presentation.Tests;

public sealed class TunnelActivationPolicyTests
{
    [Fact]
    public void ReadyActivation_HasNoFailureReason()
    {
        var readiness = new TunnelActivationReadiness(
            BinderAvailable: true,
            WorldLoaded: true,
            StageLoaded: true,
            HasController: true,
            HasMount: true,
            HasCamera: true,
            HasPlanetBody: true);

        Assert.Equal(string.Empty, TunnelActivationPolicy.FailureReason(readiness));
    }

    [Theory]
    [InlineData(true, false, true, true, true, true, true, "world unavailable")]
    [InlineData(true, true, false, true, true, true, true, "stage unavailable")]
    [InlineData(true, true, true, false, true, true, true, "timeline controller unavailable")]
    [InlineData(true, true, true, true, false, true, true, "tunnel mount unavailable")]
    [InlineData(true, true, true, true, true, false, true, "tunnel camera unavailable")]
    [InlineData(true, true, true, true, true, true, false, "planet body unavailable")]
    public void MissingDependency_FailsClosed(
        bool binderAvailable,
        bool worldLoaded,
        bool stageLoaded,
        bool hasController,
        bool hasMount,
        bool hasCamera,
        bool hasPlanetBody,
        string expected)
    {
        var readiness = new TunnelActivationReadiness(
            binderAvailable,
            worldLoaded,
            stageLoaded,
            hasController,
            hasMount,
            hasCamera,
            hasPlanetBody);

        Assert.Equal(expected, TunnelActivationPolicy.FailureReason(readiness));
    }

    [Fact]
    public void ReloadingBinderFailsClosedBeforeInspectingSceneDependencies()
    {
        var readiness = new TunnelActivationReadiness(
            BinderAvailable: false,
            WorldLoaded: true,
            StageLoaded: true,
            HasController: true,
            HasMount: true,
            HasCamera: true,
            HasPlanetBody: true);

        Assert.Equal("tunnel presentation reloading", TunnelActivationPolicy.FailureReason(readiness));
    }

    [Fact]
    public void TimelineReload_PreservesEffectiveTunnelAndCancelsOnlyCommandWork()
    {
        var decision = TunnelModePolicy.Decide(TunnelModeEvent.TimelineReload, true, 7L);

        Assert.Equal(8L, decision.ModeEpoch);
        Assert.True(decision.EffectiveEnabled);
        Assert.False(decision.HudVisible);
        Assert.False(decision.CancelInteractionWork);
        Assert.True(decision.CancelCommandWork);
        Assert.False(decision.RestoreCamera);
        Assert.False(decision.AutoReenable);
    }

    [Theory]
    [InlineData(TunnelModeEvent.EnableFailed)]
    [InlineData(TunnelModeEvent.DisableRequested)]
    [InlineData(TunnelModeEvent.WorldChanging)]
    [InlineData(TunnelModeEvent.StageChanging)]
    [InlineData(TunnelModeEvent.ControllerLost)]
    [InlineData(TunnelModeEvent.Disposed)]
    public void LossOrDisable_AlwaysRestoresSafeTwoDimensionalMode(TunnelModeEvent modeEvent)
    {
        var decision = TunnelModePolicy.Decide(modeEvent, true, 11L);

        Assert.False(decision.EffectiveEnabled);
        Assert.True(decision.HudVisible);
        Assert.True(decision.CancelInteractionWork);
        Assert.True(decision.CancelCommandWork);
        Assert.True(decision.RestoreCamera);
        Assert.False(decision.AutoReenable);
    }

    [Fact]
    public void EnableSuccess_IsTheOnlyDecisionThatHidesHud()
    {
        var decision = TunnelModePolicy.Decide(TunnelModeEvent.EnableSucceeded, false, 0L);

        Assert.True(decision.EffectiveEnabled);
        Assert.False(decision.HudVisible);
        Assert.False(decision.AutoReenable);
    }

    [Fact]
    public void Epoch_SaturatesInsteadOfWrapping()
    {
        var decision = TunnelModePolicy.Decide(TunnelModeEvent.DisableRequested, true, long.MaxValue);

        Assert.Equal(long.MaxValue, decision.ModeEpoch);
    }

    [Theory]
    [InlineData(4, 4, false, true, true, 3L, 2L, true)]
    [InlineData(3, 4, false, true, true, 99L, 2L, false)]
    [InlineData(4, 4, true, true, true, 3L, 2L, false)]
    [InlineData(4, 4, false, false, true, 3L, 2L, false)]
    [InlineData(4, 4, false, true, false, 3L, 2L, false)]
    [InlineData(4, 4, false, true, true, 1L, 2L, false)]
    public void F9Completion_IsAcceptedOnlyForCurrentSuccessfulWellFormedEpoch(
        int expectedGeneration,
        int currentGeneration,
        bool cancelled,
        bool transportOk,
        bool responseValid,
        long responseEpoch,
        long lastAcceptedEpoch,
        bool expected)
    {
        var completion = new TunnelF9CommandCompletion(
            expectedGeneration,
            currentGeneration,
            cancelled,
            transportOk,
            responseValid,
            responseEpoch,
            lastAcceptedEpoch);

        Assert.Equal(expected, TunnelF9CommandPolicy.CanAccept(completion));
    }
}
