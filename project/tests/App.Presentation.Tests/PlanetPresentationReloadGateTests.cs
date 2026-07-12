using FantaSim.App.Presentation;
using Xunit;

namespace App.Presentation.Tests;

public sealed class PlanetPresentationReloadGateTests
{
    [Fact]
    public void RuntimeChanged_CoalescesUntilDeferredAttemptRuns()
    {
        var gate = new PlanetPresentationReloadGate();

        gate.MarkRuntimeChanging();

        Assert.True(gate.TryScheduleDeferredAttempt());
        Assert.False(gate.TryScheduleDeferredAttempt());
    }

    [Fact]
    public void DeferredAttempt_WithoutMountKeepsPendingAndAllowsRetry()
    {
        var gate = new PlanetPresentationReloadGate();
        gate.MarkRuntimeChanging();
        Assert.True(gate.TryScheduleDeferredAttempt());

        gate.CompleteDeferredAttempt();

        Assert.True(gate.IsPending);
        Assert.True(gate.TryScheduleDeferredAttempt());
    }

    [Fact]
    public void MarkMounted_ClearsPendingAndStopsFurtherScheduling()
    {
        var gate = new PlanetPresentationReloadGate();
        gate.MarkRuntimeChanging();
        Assert.True(gate.TryScheduleDeferredAttempt());
        gate.CompleteDeferredAttempt();

        gate.MarkMounted();

        Assert.False(gate.IsPending);
        Assert.False(gate.TryScheduleDeferredAttempt());
    }

    [Fact]
    public void IndependentStageReload_RemainsArmedUntilTheReplacementMountBinds()
    {
        var gate = new PlanetPresentationReloadGate();

        gate.MarkRuntimeChanging();
        Assert.True(gate.TryScheduleDeferredAttempt());
        Assert.False(gate.TryScheduleDeferredAttempt());

        gate.CompleteDeferredAttempt();
        Assert.True(gate.IsPending);
        Assert.True(gate.TryScheduleDeferredAttempt());

        gate.MarkMounted();
        Assert.False(gate.IsPending);
    }
}
