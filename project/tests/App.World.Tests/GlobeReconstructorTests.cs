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
    public void BuildGlobe_default_seed_spinners_are_in_rad_per_tick()
    {
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();

        var spinners = snapshot.Plates.Where(p => Math.Abs(p.RatePerTick) > 0).ToList();
        // The four-plate seed has three spinning plates (plate 1 is still).
        Assert.Equal(3, spinners.Count);
        // 0.02 rad/Ma / 100_000 ticks-per-Ma = 2e-7 rad/tick — tick-native, NOT the rad/Ma value
        // (catches a missing rad/Ma -> rad/tick conversion, which would read ~0.02).
        Assert.All(spinners, p => Assert.InRange(Math.Abs(p.RatePerTick), 1e-7, 1e-6));
    }

    [Fact]
    public void ClassifyCellsAt_seed_has_convergent_divergent_and_transform_boundary_cells()
    {
        var model = new GlobeReconstructor(frequency: 3);

        var types = model.ClassifyCellsAt(0);

        Assert.Equal(model.BuildGlobe().CellCount, types.Length);
        Assert.All(types, t => Assert.InRange(t, (byte)0, (byte)3));
        Assert.Contains((byte)1, types); // convergent boundary cells (continental collision + subduction)
        Assert.Contains((byte)2, types); // divergent boundary cells (mid-ocean ridge)
        Assert.Contains((byte)3, types); // transform boundary cells (shear faults)
        // The vast majority are plate-interior (type 0).
        Assert.True(System.Array.FindAll(types, t => t == 0).Length > types.Length / 2);
    }

    [Fact]
    public void ClassifyCellsAt_is_deterministic()
    {
        var model = new GlobeReconstructor(frequency: 3);
        Assert.Equal(model.ClassifyCellsAt(500_000), model.ClassifyCellsAt(500_000));
    }

    [Fact]
    public void RunCrustFeatures_grows_a_mountain_by_eight_mega_annum()
    {
        var model = new GlobeReconstructor(frequency: 3);
        const long eightMa = 8 * 100_000; // 8 OdometerLadder anchors (1 anchor = 100_000 ticks)

        var byTick = model.RunCrustFeatures(new long[] { 0L, eightMa });

        Assert.DoesNotContain((byte)1, byTick[0L]);   // no Mountain at genesis (no accumulation yet)
        Assert.Contains((byte)1, byTick[eightMa]);    // a Mountain (kind 1) has emerged by 8 Ma
        Assert.All(byTick[eightMa], k => Assert.InRange(k, (byte)0, (byte)5));
    }

    [Fact]
    public void RunCrustFeatures_seed_produces_the_full_feature_vocabulary()
    {
        var model = new GlobeReconstructor(frequency: 3);
        // Match the app's snapshot density (every 5 anchors): the field-driven volcanic arc accumulates
        // during the EARLY convergent phase of the 0|2 / 0|3 boundaries (they reclassify toward transform
        // as the plates drift), which a coarse two-snapshot run would integrate away.
        var snapshots = new System.Collections.Generic.List<long>();
        for (long anchor = 0; anchor <= 30; anchor += 5) snapshots.Add(anchor * 100_000);

        var features = model.RunCrustFeatures(snapshots)[snapshots[^1]];

        // Every crust feature kind appears at once on the four-plate seed:
        Assert.Contains((byte)1, features); // Mountain     — continent–continent collision (0|1)
        Assert.Contains((byte)2, features); // VolcanicArc  — subduction overriding side (0|2, 0|3)
        Assert.Contains((byte)3, features); // Trench       — subduction down-going side
        Assert.Contains((byte)4, features); // Ridge        — mid-ocean spreading (2|3)
        Assert.Contains((byte)5, features); // Fault        — transform (1|2, 1|3)
    }

    private static void AssertUnitLength(GlobeVec3 v)
    {
        double len = Math.Sqrt((double)v.X * v.X + (double)v.Y * v.Y + (double)v.Z * v.Z);
        Assert.InRange(len, 1.0 - 1e-5, 1.0 + 1e-5);
    }
}
