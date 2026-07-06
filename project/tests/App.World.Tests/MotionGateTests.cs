using FantaSim.App.World.Composition;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Services;
using ServiceArchi.Core;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Motion regression gate (M0, spec §4.1): permanent unit checks that plate membership drifts across
/// the 200 Ma presentation window and that the new light path through <see cref="IService"/> surfaces
/// that drift without materializing crust.
/// </summary>
public sealed class MotionGateTests
{
    [Fact]
    public void Membership_changes_by_at_least_30_percent_across_window()
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var options = WorldGenerationRenderOptions.Default;
        var roster = OnsetRoster.Build(options.Seed, onsetTick, options.TessellationFrequency);
        var reconstructor = GlobeReconstructor.FromOnsetRoster(
            roster, onsetTick, SphereRegimeScheduleDefaults.GeosphereDefault, options.TessellationFrequency);

        var a = reconstructor.BuildGlobeAt(onsetTick);
        var b = reconstructor.BuildGlobeAt(onsetTick + 20_000_000L);

        int changed = 0;
        for (int i = 0; i < a.Cells.Count; i++)
            if (a.Cells[i].PlateId != b.Cells[i].PlateId) changed++;

        double pct = 100.0 * changed / a.Cells.Count;
        Assert.True(pct >= 30.0,
            $"Expected >= 30% of cells to change plate between onset and onset+20M, but only {pct:F1}% changed ({changed}/{a.Cells.Count}).");
    }

    [Fact]
    public void GetGlobeSnapshotAt_reflects_motion_across_window()
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        using var service = new Service(new ServiceRegistry());

        var a = service.GetGlobeSnapshotAt(onsetTick);
        var b = service.GetGlobeSnapshotAt(onsetTick + 20_000_000L);

        int changed = 0;
        for (int i = 0; i < a.Cells.Count; i++)
            if (a.Cells[i].PlateId != b.Cells[i].PlateId) changed++;

        Assert.True(changed > 0,
            "Expected IService.GetGlobeSnapshotAt to return different cell->plate assignments across the window.");
    }

    [Fact]
    public void GetPlanetPresentationAsync_default_populates_continental_plate_ids()
    {
        using var service = new Service(new ServiceRegistry());
        var doc = service.GetPlanetPresentationAsync();

        Assert.NotNull(doc.ContinentalPlateIds);
        AssertSetEqual(new[] { 0, 1 }, doc.ContinentalPlateIds);
    }

    [Fact]
    public void Resolver_geosphere_plate_defaults_to_continents_identity_override_selects_plate_identity()
    {
        var sel = new TimelineLayerSelection("geosphere", "geosphere.plate");
        Assert.Equal(GlobeViewMode.Continents,
            GlobeViewModeResolver.Resolve("mobile-plate", sel));
        Assert.Equal(GlobeViewMode.PlateIdentity,
            GlobeViewModeResolver.Resolve("mobile-plate", sel, "identity"));
    }

    private static void AssertSetEqual(IEnumerable<int> expected, IReadOnlySet<int> actual)
        => Assert.True(expected.ToHashSet().SetEquals(actual));
}
