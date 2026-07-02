using FantaSim.App.World.Composition;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class RegimeTransitionTests
{
    private static readonly long Onset = SphereRegimeScheduleDefaults.PlateOnsetTick;
    private static readonly long MagmaEnd = SphereRegimeScheduleDefaults.MagmaOceanEndTick;
    private static readonly SphereRegimeSchedule Schedule = SphereRegimeScheduleDefaults.GeosphereFor(Onset);

    [Fact]
    public void NullPreviousRegime_ReturnsFalse_OnFirstMount()
    {
        // Mount at t=0: no prior regime, so there is nothing to transition from — must NOT refresh.
        Assert.False(Schedule.IsRegimeTransition(previousRegimeId: null, tick: 0));
    }

    [Fact]
    public void NullPreviousRegime_ReturnsFalse_EvenWhenTickIsDeepInMobilePlate()
    {
        // Scrub straight into mobile-plate on first bind: still null prior, still no transition.
        Assert.False(Schedule.IsRegimeTransition(previousRegimeId: null, tick: Onset + 5_000_000));
    }

    [Fact]
    public void SameRegimeId_ReturnsFalse_MagmaOceanScrub()
    {
        Assert.False(Schedule.IsRegimeTransition("magma-ocean", tick: 0));
        Assert.False(Schedule.IsRegimeTransition("magma-ocean", tick: MagmaEnd - 1));
    }

    [Fact]
    public void SameRegimeId_ReturnsFalse_MobilePlateScrub()
    {
        Assert.False(Schedule.IsRegimeTransition("mobile-plate", tick: Onset));
        Assert.False(Schedule.IsRegimeTransition("mobile-plate", tick: Onset + 5_000_000));
    }

    [Fact]
    public void ForwardCrossing_MagmaOceanToMobilePlate_ReturnsTrue()
    {
        Assert.True(Schedule.IsRegimeTransition("magma-ocean", tick: Onset));
    }

    [Fact]
    public void ForwardCrossing_StagnantLidToMobilePlate_ReturnsTrue()
    {
        Assert.True(Schedule.IsRegimeTransition("stagnant-lid", tick: Onset));
    }

    [Fact]
    public void ForwardCrossing_MagmaOceanToStagnantLid_ReturnsTrue()
    {
        Assert.True(Schedule.IsRegimeTransition("magma-ocean", tick: MagmaEnd));
    }

    [Fact]
    public void BackwardCrossing_MobilePlateToMagmaOcean_ReturnsTrue()
    {
        // Scrubbing backwards across a boundary is still a regime change.
        Assert.True(Schedule.IsRegimeTransition("mobile-plate", tick: 0));
    }

    [Fact]
    public void TransitionDetected_AtExactBoundaryTick()
    {
        // The boundary tick belongs to the NEW regime (half-open [StartTick, EndTick)).
        Assert.True(Schedule.IsRegimeTransition("stagnant-lid", tick: Onset));
        Assert.False(Schedule.IsRegimeTransition("mobile-plate", tick: Onset));
    }
}
