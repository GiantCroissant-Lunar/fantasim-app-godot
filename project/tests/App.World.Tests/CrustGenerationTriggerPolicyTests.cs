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
        var regime = GeosphereSchedule().RegimeAt(PlateOnsetTick);
        var decision = Policy().Evaluate("mobile-plate", Revision, PlateOnsetTick, regime);

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
        var tick = 500_000L;
        var regime = GeosphereSchedule().RegimeAt(tick);
        var decision = Policy().Evaluate("magma-ocean", Revision, tick, regime);

        Assert.False(decision.ShouldRun);
        Assert.True(decision.ShouldCancel);
        Assert.Null(decision.Key);
    }

    [Fact]
    public void NullRegime_ReturnsShouldCancel()
    {
        var decision = Policy().Evaluate(null, Revision, PlateOnsetTick, null);

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
        var schedule = GeosphereSchedule();
        var first = policy.Evaluate("mobile-plate", Revision, PlateOnsetTick, schedule.RegimeAt(PlateOnsetTick));
        var second = policy.Evaluate("mobile-plate", Revision, tick, schedule.RegimeAt(tick));

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
        var schedule = GeosphereSchedule();
        var first = policy.Evaluate("mobile-plate", Revision, PlateOnsetTick, schedule.RegimeAt(PlateOnsetTick));
        var second = policy.Evaluate("mobile-plate", Revision, PlateOnsetTick + WindowSize, schedule.RegimeAt(PlateOnsetTick + WindowSize));

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
        var regime = GeosphereSchedule().RegimeAt(tick);

        var decision = Policy().Evaluate("mobile-plate", Revision, tick, regime);

        Assert.True(decision.ShouldRun);
        Assert.Equal(expectedWindowStart, decision.CanonicalTick);
    }

    [Fact]
    public void GraphRevisionIncludedInKey()
    {
        var tick = PlateOnsetTick;
        var regime = GeosphereSchedule().RegimeAt(tick);
        var first = Policy().Evaluate("mobile-plate", Revision, tick, regime);
        var second = new CrustGenerationTriggerPolicy(WindowSize).Evaluate("mobile-plate", Revision + 1, tick, regime);

        Assert.True(first.ShouldRun);
        Assert.True(second.ShouldRun);
        Assert.NotEqual(first.Key, second.Key);
    }

    [Fact]
    public void MobilePlateRegime_ReturnsSnapshotSeriesAcrossSpan()
    {
        var regime = GeosphereSchedule().RegimeAt(PlateOnsetTick)!;

        var decision = Policy().Evaluate("mobile-plate", Revision, PlateOnsetTick, regime);

        Assert.NotNull(decision.SnapshotTicks);
        var ticks = decision.SnapshotTicks!.SnapshotTicks;
        Assert.Contains(PlateOnsetTick, ticks);
        Assert.Contains(PlateOnsetTick + WindowSize, ticks);
        Assert.Contains(PlateOnsetTick + 2 * WindowSize, ticks);
    }

    [Fact]
    public void SnapshotSeries_SelectLargestTickLessThanOrEqualToPlayhead()
    {
        var regime = GeosphereSchedule().RegimeAt(PlateOnsetTick)!;
        var series = CrustSnapshotTickSeries.ForRegime(regime, WindowSize, 120_000_000L);

        Assert.Equal(PlateOnsetTick, series.SelectSnapshotForPlayhead(PlateOnsetTick));
        Assert.Equal(PlateOnsetTick, series.SelectSnapshotForPlayhead(PlateOnsetTick + 1L));
        Assert.Equal(PlateOnsetTick, series.SelectSnapshotForPlayhead(PlateOnsetTick + WindowSize - 1L));
        Assert.Equal(PlateOnsetTick + WindowSize, series.SelectSnapshotForPlayhead(PlateOnsetTick + WindowSize));
        Assert.Equal(PlateOnsetTick + WindowSize, series.SelectSnapshotForPlayhead(PlateOnsetTick + WindowSize + 1L));
    }

    [Fact]
    public void SnapshotSeries_ExcludesMobilePlateStartWhenEarlierThanZero()
    {
        var regime = new SphereRegime("mobile-plate", StartTick: -5_000_000L, EndTick: 120_000_000L, Array.Empty<LayerId>());
        var series = CrustSnapshotTickSeries.ForRegime(regime, WindowSize, 120_000_000L);

        Assert.Equal(0L, series.SnapshotTicks[0]);
        Assert.DoesNotContain(-5_000_000L, series.SnapshotTicks);
    }

    [Fact]
    public void NonMobilePlateRegime_ReturnsCancelAndNoSnapshotSeries()
    {
        var tick = 500_000L;
        var regime = GeosphereSchedule().RegimeAt(tick);

        var decision = Policy().Evaluate("magma-ocean", Revision, tick, regime);

        Assert.False(decision.ShouldRun);
        Assert.True(decision.ShouldCancel);
        Assert.Null(decision.SnapshotTicks);
    }

    [Fact]
    public void SnapshotTickSeries_SameWindow_ReturnsSameKey()
    {
        var regime = GeosphereSchedule().RegimeAt(PlateOnsetTick)!;
        var policy = Policy();
        var first = policy.Evaluate("mobile-plate", Revision, PlateOnsetTick, regime);
        var second = policy.Evaluate("mobile-plate", Revision, PlateOnsetTick + WindowSize - 1L, regime);

        Assert.True(first.ShouldRun);
        Assert.True(second.ShouldRun);
        Assert.Equal(first.Key, second.Key);
    }

    [Fact]
    public void SnapshotTickSeries_DifferentWindow_ReturnsDifferentKey()
    {
        var regime = GeosphereSchedule().RegimeAt(PlateOnsetTick)!;
        var policy = Policy();
        var first = policy.Evaluate("mobile-plate", Revision, PlateOnsetTick, regime);
        var second = policy.Evaluate("mobile-plate", Revision, PlateOnsetTick + WindowSize, regime);

        Assert.True(first.ShouldRun);
        Assert.True(second.ShouldRun);
        Assert.NotEqual(first.Key, second.Key);
    }

    private static SphereRegimeSchedule GeosphereSchedule()
        => SphereRegimeScheduleDefaults.GeosphereFor(PlateOnsetTick);
}
