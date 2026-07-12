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

        Assert.True(gate.CompleteDeferredAttempt(runtimeChangeInProgress: false));

        Assert.True(gate.IsPending);
        Assert.True(gate.TryScheduleDeferredAttempt());
    }

    [Fact]
    public void MarkMounted_ClearsPendingAndStopsFurtherScheduling()
    {
        var gate = new PlanetPresentationReloadGate();
        gate.MarkRuntimeChanging();
        Assert.True(gate.TryScheduleDeferredAttempt());
        Assert.True(gate.CompleteDeferredAttempt(runtimeChangeInProgress: false));

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

        Assert.True(gate.CompleteDeferredAttempt(runtimeChangeInProgress: false));
        Assert.True(gate.IsPending);
        Assert.True(gate.TryScheduleDeferredAttempt());

        gate.MarkMounted();
        Assert.False(gate.IsPending);
    }

    [Fact]
    public void ConcurrentStageReloadsMountOnlyAfterTheFinalCountClears()
    {
        var gate = new PlanetPresentationReloadGate();
        gate.MarkRuntimeChanging();
        gate.MarkRuntimeChanging();

        Assert.True(gate.TryScheduleDeferredAttempt());
        Assert.False(gate.CompleteDeferredAttempt(runtimeChangeInProgress: true));
        Assert.True(gate.IsPending);

        Assert.True(gate.TryScheduleDeferredAttempt());
        Assert.True(gate.CompleteDeferredAttempt(runtimeChangeInProgress: false));
        gate.MarkMounted();

        Assert.False(gate.IsPending);
        Assert.False(gate.TryScheduleDeferredAttempt());
    }
}
