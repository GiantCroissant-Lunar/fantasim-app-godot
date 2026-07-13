using FantaSim.App.World.Composition;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Dto;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Integration guard for the presentation-edge → mantle-forcing seam. Visual arcs remain one
/// record per exact tessellation edge; the mantle adapter must not multiply volumetric ribbons by
/// that visual record count.
/// </summary>
public sealed class MantleHistoryIntegrationTests
{
    [Fact]
    public void RealFrequencyFourFrontier_CoalescesToBoundedMantleForcing()
    {
        const long tick = 67_000_000L;
        var arcs = new GlobeReconstructor(frequency: 4).BuildBoundaryArcsAt(tick);
        int forcingArcCount = arcs.Count(arc =>
            arc.Kind is PlateBoundaryKind.Convergent or PlateBoundaryKind.Divergent
            && arc.Points.Count >= 2);

        var history = MantleHistoryAdapter.Build(arcs, plateOnsetTick: 0L);

        Assert.True(forcingArcCount >= 40, $"Expected a non-trivial edge-local frontier, got {forcingArcCount} forcing arcs.");
        Assert.NotEmpty(history.Segments);
        Assert.True(
            history.Segments.Count * 3 <= forcingArcCount,
            $"Mantle forcing stayed too close to visual edge granularity: {history.Segments.Count} segments from {forcingArcCount} forcing arcs.");
    }
}
