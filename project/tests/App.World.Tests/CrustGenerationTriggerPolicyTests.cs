using FantaSim.App.World.Composition;
using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class CrustGenerationTriggerPolicyTests
{
    private const long WindowSize = 5_000_000L;
    private const int Revision = 7;
    private const long PlateOnsetTick = 100_000_000L;

    private static CrustGenerationTriggerPolicy Policy()
        => new(WindowSize);

    [Fact]
    public void MobilePlateRegime_ReturnsShouldRunWithQuantizedTick()
    {
        var decision = Policy().Evaluate("mobile-plate", Revision, PlateOnsetTick);

        Assert.True(decision.ShouldRun);
        Assert.False(decision.ShouldCancel);
        Assert.Equal(PlateOnsetTick, decision.CanonicalTick);
        Assert.NotNull(decision.Key);
        Assert.Equal(Revision, decision.Key!.GraphRevision);
        Assert.Equal(PlateOnsetTick / WindowSize, decision.Key.WindowIndex);
    }

    [Fact]
    public void NonMobilePlateRegime_ReturnsShouldCancel()
    {
        var decision = Policy().Evaluate("magma-ocean", Revision, 500_000L);

        Assert.False(decision.ShouldRun);
        Assert.True(decision.ShouldCancel);
        Assert.Null(decision.Key);
    }

    [Fact]
    public void NullRegime_ReturnsShouldCancel()
    {
        var decision = Policy().Evaluate(null, Revision, PlateOnsetTick);

        Assert.False(decision.ShouldRun);
        Assert.True(decision.ShouldCancel);
        Assert.Null(decision.Key);
    }

    [Theory]
    [InlineData(PlateOnsetTick)]
    [InlineData(PlateOnsetTick + 1L)]
    [InlineData(PlateOnsetTick + WindowSize - 1L)]
    public void SameWindow_ReturnsSameKey(long tick)
    {
        var policy = Policy();
        var first = policy.Evaluate("mobile-plate", Revision, PlateOnsetTick);
        var second = policy.Evaluate("mobile-plate", Revision, tick);

        Assert.True(first.ShouldRun);
        Assert.True(second.ShouldRun);
        Assert.Equal(first.Key, second.Key);
        Assert.Equal(PlateOnsetTick, first.CanonicalTick);
        Assert.Equal(PlateOnsetTick, second.CanonicalTick);
    }

    [Fact]
    public void DifferentWindow_ReturnsDifferentKey()
    {
        var policy = Policy();
        var first = policy.Evaluate("mobile-plate", Revision, PlateOnsetTick);
        var second = policy.Evaluate("mobile-plate", Revision, PlateOnsetTick + WindowSize);

        Assert.True(first.ShouldRun);
        Assert.True(second.ShouldRun);
        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal(PlateOnsetTick, first.CanonicalTick);
        Assert.Equal(PlateOnsetTick + WindowSize, second.CanonicalTick);
    }

    [Fact]
    public void WindowBoundary_CanonicalTickIsWindowStart()
    {
        var tick = PlateOnsetTick + WindowSize + 1L;
        var expectedWindowStart = ((tick / WindowSize) * WindowSize);

        var decision = Policy().Evaluate("mobile-plate", Revision, tick);

        Assert.True(decision.ShouldRun);
        Assert.Equal(expectedWindowStart, decision.CanonicalTick);
    }

    [Fact]
    public void GraphRevisionIncludedInKey()
    {
        var tick = PlateOnsetTick;
        var first = Policy().Evaluate("mobile-plate", Revision, tick);
        var second = new CrustGenerationTriggerPolicy(WindowSize).Evaluate("mobile-plate", Revision + 1, tick);

        Assert.True(first.ShouldRun);
        Assert.True(second.ShouldRun);
        Assert.NotEqual(first.Key, second.Key);
    }
}
