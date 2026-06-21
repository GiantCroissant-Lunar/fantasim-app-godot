using System;
using System.Linq;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class GlobeReconstructorTests
{
    [Fact]
    public void BuildGlobe_frequency3_has_1280_cells_each_with_three_unit_length_corners()
    {
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();

        Assert.Equal(1280, snapshot.CellCount);          // 20 * 4^3
        Assert.Equal(1280, snapshot.Cells.Count);
        foreach (var cell in snapshot.Cells)
        {
            AssertUnitLength(cell.C0);
            AssertUnitLength(cell.C1);
            AssertUnitLength(cell.C2);
        }
    }

    [Fact]
    public void BuildGlobe_assigns_every_cell_to_one_of_at_least_three_plates()
    {
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();

        Assert.True(snapshot.PlateCount >= 3, $"expected >= 3 plates, got {snapshot.PlateCount}");
        Assert.Equal(snapshot.PlateCount, snapshot.Plates.Count);
        foreach (var cell in snapshot.Cells)
            Assert.InRange(cell.PlateId, 0, snapshot.PlateCount - 1);
    }

    [Fact]
    public void BuildGlobe_default_seed_has_exactly_one_spinning_plate_in_rad_per_tick()
    {
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();

        var spinners = snapshot.Plates.Where(p => Math.Abs(p.RatePerTick) > 0).ToList();
        Assert.Single(spinners);
        // 0.02 rad/Ma / 100_000 ticks-per-Ma = 2e-7 rad/tick — tick-native, NOT the rad/Ma value
        // (catches a missing rad/Ma -> rad/tick conversion, which would read ~0.02).
        Assert.InRange(Math.Abs(spinners[0].RatePerTick), 1e-7, 1e-6);
    }

    [Fact]
    public void ClassifyCellsAt_seed_has_convergent_and_divergent_boundary_cells()
    {
        var model = new GlobeReconstructor(frequency: 3);

        var types = model.ClassifyCellsAt(0);

        Assert.Equal(model.BuildGlobe().CellCount, types.Length);
        Assert.All(types, t => Assert.InRange(t, (byte)0, (byte)3));
        Assert.Contains((byte)1, types); // convergent boundary cells (0|1)
        Assert.Contains((byte)2, types); // divergent boundary cells (0|2)
        // The vast majority are plate-interior (type 0).
        Assert.True(System.Array.FindAll(types, t => t == 0).Length > types.Length / 2);
    }

    [Fact]
    public void ClassifyCellsAt_is_deterministic()
    {
        var model = new GlobeReconstructor(frequency: 3);
        Assert.Equal(model.ClassifyCellsAt(500_000), model.ClassifyCellsAt(500_000));
    }

    private static void AssertUnitLength(GlobeVec3 v)
    {
        double len = Math.Sqrt((double)v.X * v.X + (double)v.Y * v.Y + (double)v.Z * v.Z);
        Assert.InRange(len, 1.0 - 1e-5, 1.0 + 1e-5);
    }
}
