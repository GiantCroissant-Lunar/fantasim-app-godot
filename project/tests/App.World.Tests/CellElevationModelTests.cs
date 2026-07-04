using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.Ecs.Systems;
using FantaSim.App.World.Cells;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.Geosphere.Crust;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Population + read-out proof for <see cref="CellElevationModel"/> (sub-project B). From a small
/// hand-built <see cref="CrustStateRun"/>, <see cref="CellElevationModel.UpdateForTick"/> must populate
/// one ECS cell entity per cell with the right crust fields, and <see cref="CellElevationModel.GetElevations"/>
/// must return an array of length = cell count with the mountain (collision) cell highest. A second
/// test runs the REAL crust pipeline through <see cref="GlobeReconstructor"/> end-to-end (Godot-free).
/// </summary>
public sealed class CellElevationModelTests
{
    // 4 crafted cells: 0 = collision mountain, 1 = continental interior, 2 = young ocean, 3 = old ocean.
    private static CrustStateRun BuildRun(long tick)
    {
        var atTick = new Dictionary<int, CellCrustState>
        {
            [0] = new CellCrustState(0, ContinentalFraction: 1.0, OrogenicPressure: 100.0, VolcanicActivity: 0.0, CrustAgeTicks: 0.0),
            [1] = new CellCrustState(1, ContinentalFraction: 1.0, OrogenicPressure: 0.0,   VolcanicActivity: 0.0, CrustAgeTicks: 0.0),
            [2] = new CellCrustState(2, ContinentalFraction: 0.0, OrogenicPressure: 0.0,   VolcanicActivity: 0.0, CrustAgeTicks: 0.0),
            [3] = new CellCrustState(3, ContinentalFraction: 0.0, OrogenicPressure: 0.0,   VolcanicActivity: 0.0, CrustAgeTicks: 1.0e7),
        };
        var stateByTick = new Dictionary<long, IReadOnlyDictionary<int, CellCrustState>> { [tick] = atTick };
        var centers = new[]
        {
            new GlobeVec3(1, 0, 0), new GlobeVec3(0, 1, 0),
            new GlobeVec3(0, 0, 1), new GlobeVec3(-1, 0, 0),
        };
        return new CrustStateRun(CellCount: 4, stateByTick, centers);
    }

    [Fact]
    public void UpdateForTick_dry_mode_exposes_oceanic_crust_without_age_deepening()
    {
        const long tick = 500_000L;
        using var model = new CellElevationModel(
            BuildRun(tick),
            plateIdByCell: new[] { 0, 0, 1, 1 },
            CellElevationHydrosphereMode.Absent);

        model.UpdateForTick(tick);
        var elevations = model.GetElevations();

        Assert.True(elevations[2] >= 0, $"expected exposed young oceanic crust, got {elevations[2]}");
        Assert.Equal(elevations[2], elevations[3], precision: 6);
    }

    [Fact]
    public void UpdateForTick_populates_one_entity_per_cell_with_mountain_highest()
    {
        const long tick = 500_000L;
        using var model = new CellElevationModel(BuildRun(tick), plateIdByCell: new[] { 0, 0, 1, 1 });

        model.UpdateForTick(tick);
        var elevations = model.GetElevations();

        // One value per cell, indexed by cell id.
        Assert.Equal(4, model.CellCount);
        Assert.Equal(4, elevations.Length);

        // Collision (cell 0) is the single highest; old ocean (cell 3) is the single lowest.
        int highest = Array.IndexOf(elevations, elevations.Max());
        int lowest = Array.IndexOf(elevations, elevations.Min());
        Assert.Equal(0, highest);
        Assert.Equal(3, lowest);

        // Full tier ordering: collision > continental interior > 0 > young ocean > old ocean.
        Assert.True(elevations[0] > elevations[1], $"{elevations[0]} !> {elevations[1]}");
        Assert.True(elevations[1] > 0, $"{elevations[1]} !> 0");
        Assert.True(0 > elevations[2], $"0 !> {elevations[2]}");
        Assert.True(elevations[2] > elevations[3], $"{elevations[2]} !> {elevations[3]}");
    }

    [Fact]
    public void UpdateForTick_snaps_to_nearest_snapshot_and_is_idempotent()
    {
        const long snapTick = 1_000_000L;
        using var model = new CellElevationModel(BuildRun(snapTick), plateIdByCell: new[] { 0, 0, 1, 1 });

        // A tick far from the only snapshot still resolves to that snapshot (nearest).
        model.UpdateForTick(snapTick + 12_345L);
        var a = model.GetElevations();
        model.UpdateForTick(snapTick + 12_345L); // repeat: no change
        var b = model.GetElevations();

        Assert.Equal(a, b);
        Assert.Equal(0, Array.IndexOf(a, a.Max())); // mountain still highest off the nearest snapshot
    }

    [Fact]
    public void Build_runs_the_real_crust_pipeline_and_yields_one_elevation_per_cell_with_relief()
    {
        var reconstructor = new GlobeReconstructor(frequency: 2); // 320 cells — small but real
        var globe = reconstructor.BuildGlobe();

        // A couple of snapshots so the collision orogenic-pressure has accumulated by the later tick.
        var snapshotTicks = new List<long> { 0L, 50L * globe.TicksPerAnchor };
        using var model = CellElevationModel.Build(reconstructor, snapshotTicks);

        Assert.Equal(globe.CellCount, model.CellCount);

        model.UpdateForTick(snapshotTicks[^1]);
        var elevations = model.GetElevations();

        Assert.Equal(globe.CellCount, elevations.Length);
        // The seed exhibits the full boundary vocabulary, so by 50 anchors there is real relief:
        // some cells uplifted well above sea level, some ocean floor below it (range is non-trivial).
        Assert.True(elevations.Max() > 0, $"expected uplifted land, max={elevations.Max()}");
        Assert.True(elevations.Min() < 0, $"expected ocean below sea level, min={elevations.Min()}");
        Assert.True(elevations.Max() - elevations.Min() > 1.0, "expected a non-trivial relief range");
    }

    // ── Regression: Build must not call BuildGlobe() on an onset-aware reconstructor (PLAN4-TASK4) ──
    // Before Fix 2 (BuildGlobeAt), CellElevationModel.Build called reconstructor.BuildGlobe() which
    // throws InvalidOperationException on onset-aware instances (constructed via FromOnsetRoster).
    // This test fails without Fix 2 and passes after.

    [Fact]
    public void Build_with_onset_reconstructor_does_not_throw_and_yields_cells()
    {
        // Mirror the exact construction used by Host.ComposeCellElevation.
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var roster = OnsetRoster.Build(worldSeed: 2024, onsetTick: onsetTick, tessellationFrequency: 3);
        var schedule = SphereRegimeScheduleDefaults.GeosphereDefault;
        var reconstructor = GlobeReconstructor.FromOnsetRoster(roster, onsetTick, schedule, frequency: 3);

        // Verify the new OnsetTick accessor (Fix 1).
        Assert.Equal(onsetTick, reconstructor.OnsetTick);

        // A few post-onset snapshot ticks (8 Ma + 16 Ma past onset).
        long ma8  = onsetTick + 8  * 100_000L;
        long ma16 = onsetTick + 16 * 100_000L;
        var snapshotTicks = new List<long> { ma8, ma16 };

        // Fix 2: Build now calls BuildGlobeAt(reconstructor.OnsetTick) instead of BuildGlobe(),
        // so this must NOT throw InvalidOperationException.
        CellElevationModel? model = null;
        var ex = Record.Exception(() => model = CellElevationModel.Build(reconstructor, snapshotTicks));
        Assert.Null(ex);
        Assert.NotNull(model);

        using (model)
        {
            // Must have at least one cell (the N-plate globe was built correctly at onset).
            Assert.True(model!.CellCount > 0, $"Expected cells > 0, got {model.CellCount}");

            // Elevation read-out must not throw; array length must match cell count.
            model.UpdateForTick(ma16);
            var elevations = model.GetElevations();
            Assert.Equal(model.CellCount, elevations.Length);
        }
    }
}
