using FantaSim.App.World.Globe;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class CanonicalTimeLabelTests
{
    // geosphere.plate.time.v1 anchor: 1 ka = 1 Ma = 100_000 canonical ticks.
    private const long TicksPerKa = 100_000;

    [Fact]
    public void ForTick_never_shows_real_world_Ma()
    {
        foreach (var tick in new long[] { 0, TicksPerKa, 50 * TicksPerKa, 100 * TicksPerKa })
            Assert.DoesNotContain("Ma", CanonicalTimeLabel.ForTick(tick, TicksPerKa));
    }

    [Fact]
    public void ForTick_one_anchor_unit_is_one_ka()
    {
        Assert.Equal("1 ka", CanonicalTimeLabel.ForTick(TicksPerKa, TicksPerKa));
    }

    [Fact]
    public void ForTick_fifty_anchor_units_is_fifty_ka()
    {
        Assert.Equal("50 ka", CanonicalTimeLabel.ForTick(50 * TicksPerKa, TicksPerKa));
    }
}
