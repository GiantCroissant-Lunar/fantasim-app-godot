using System;
using System.Linq;
using FantaSim.App.World.Composition;
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

    // ── Typed boundary arcs (topology truth → smooth great-circle polylines) ────────────────────

    [Fact]
    public void BuildBoundaryArcsAt_seed_returns_nonempty_arcs_with_unit_length_ordered_points()
    {
        var model = new GlobeReconstructor(frequency: 3);

        var arcs = model.BuildBoundaryArcsAt(0);

        Assert.NotEmpty(arcs);
        foreach (var arc in arcs)
        {
            Assert.True(arc.PlateA < arc.PlateB, "PlateA must be the lower id");
            Assert.True(arc.Kind == PlateBoundaryKind.Convergent
                     || arc.Kind == PlateBoundaryKind.Divergent
                     || arc.Kind == PlateBoundaryKind.Transform
                     || arc.Kind == PlateBoundaryKind.Inactive);
            Assert.True(arc.Points.Count >= 2, "an arc needs at least two points");
            foreach (var p in arc.Points)
                AssertUnitLength(p);
        }
    }

    [Fact]
    public void BuildBoundaryArcsAt_seed_has_convergent_divergent_and_transform_arcs()
    {
        var model = new GlobeReconstructor(frequency: 3);

        var kinds = model.BuildBoundaryArcsAt(0).Select(a => a.Kind).Distinct().ToArray();

        Assert.Contains(PlateBoundaryKind.Convergent, kinds);
        Assert.Contains(PlateBoundaryKind.Divergent, kinds);
        Assert.Contains(PlateBoundaryKind.Transform, kinds);
    }

    [Fact]
    public void BuildBoundaryArcsAt_returns_empty_before_onset()
    {
        var model = BuildOnsetReconstructor(out long onsetTick);

        Assert.Empty(model.BuildBoundaryArcsAt(onsetTick - 1));
    }

    [Fact]
    public void BuildBoundaryArcsAt_returns_arcs_at_onset()
    {
        var model = BuildOnsetReconstructor(out long onsetTick);

        Assert.NotEmpty(model.BuildBoundaryArcsAt(onsetTick));
    }

    [Fact]
    public void BuildBoundaryArcsAt_is_deterministic()
    {
        var model = new GlobeReconstructor(frequency: 3);

        var first = model.BuildBoundaryArcsAt(500_000);
        var second = model.BuildBoundaryArcsAt(500_000);

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].PlateA, second[i].PlateA);
            Assert.Equal(first[i].PlateB, second[i].PlateB);
            Assert.Equal(first[i].Kind, second[i].Kind);
            Assert.Equal(first[i].Points.Count, second[i].Points.Count);
        }
    }

    [Fact]
    public void BuildBoundaryArcsAt_point_count_grows_with_subdivs()
    {
        var model = new GlobeReconstructor(frequency: 3);

        var coarse = model.BuildBoundaryArcsAt(0, subdivsPerSegment: 4);
        var fine = model.BuildBoundaryArcsAt(0, subdivsPerSegment: 32);

        Assert.Equal(coarse.Count, fine.Count);
        for (int i = 0; i < coarse.Count; i++)
            Assert.True(fine[i].Points.Count > coarse[i].Points.Count);
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

    // ── Onset-aware gating tests ──────────────────────────────────────────────────────────────────

    private static GlobeReconstructor BuildOnsetReconstructor(out long onsetTick)
    {
        onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick; // 1e8 ticks
        var roster = OnsetRoster.Build(worldSeed: 2024, onsetTick: onsetTick, tessellationFrequency: 3);
        var schedule = SphereRegimeScheduleDefaults.GeosphereFor(onsetTick);
        return GlobeReconstructor.FromOnsetRoster(roster, onsetTick, schedule, frequency: 3);
    }

    [Fact]
    public void BuildGlobe_throws_on_onset_aware_instance()
    {
        var model = BuildOnsetReconstructor(out _);
        Assert.Throws<InvalidOperationException>(() => model.BuildGlobe());
    }

    [Fact]
    public void BuildGlobeAt_returns_lid_globe_before_onset()
    {
        var model = BuildOnsetReconstructor(out long onsetTick);
        var snapshot = model.BuildGlobeAt(onsetTick - 1);

        // Lid globe: no plates, all cells unassigned (plateId == -1).
        Assert.Equal(0, snapshot.PlateCount);
        Assert.All(snapshot.Cells, c => Assert.Equal(-1, c.PlateId));
    }

    [Fact]
    public void BuildGlobeAt_returns_plate_globe_at_onset()
    {
        var model = BuildOnsetReconstructor(out long onsetTick);
        var snapshot = model.BuildGlobeAt(onsetTick);

        Assert.True(snapshot.PlateCount >= 3,
            $"Expected >= 3 plates at onset; got {snapshot.PlateCount}");
    }

    [Fact]
    public void RunCrustEvolution_returns_empty_state_before_onset_and_real_state_after()
    {
        var model = BuildOnsetReconstructor(out long onsetTick);
        // One pre-onset tick + one post-onset tick well into the mobile-plate regime.
        long preTick  = onsetTick - 1;
        long postTick = onsetTick + 8 * 100_000; // 8 Ma after onset

        var run = model.RunCrustEvolution(new long[] { preTick, postTick });

        // Cell centers are always populated (pure geometry).
        Assert.NotEmpty(run.CellCenters);
        Assert.Equal(run.CellCount, run.CellCenters.Count);

        // Pre-onset tick: state dict present but empty (no crust activity before plates exist).
        Assert.True(run.StateByTick.ContainsKey(preTick),
            "StateByTick must include the pre-onset key");
        Assert.Empty(run.StateByTick[preTick]);

        // Post-onset tick: state dict present and non-empty (crust pipeline ran for active ticks).
        Assert.True(run.StateByTick.ContainsKey(postTick),
            "StateByTick must include the post-onset key");
        Assert.NotEmpty(run.StateByTick[postTick]);
    }

    [Fact]
    public void RunCrustEvolution_legacy_path_returns_nonempty_state_at_tick_zero()
    {
        // The parameterless (legacy) constructor has no gating — pipeline always runs.
        var model = new GlobeReconstructor(frequency: 3);
        long eightMa = 8 * 100_000;

        var run = model.RunCrustEvolution(new long[] { 0L, eightMa });

        Assert.Equal(2, run.StateByTick.Count);
        // Even at tick 0 the legacy path runs the pipeline (no gating).
        Assert.True(run.StateByTick.ContainsKey(0L));
    }

    private static void AssertUnitLength(GlobeVec3 v)
    {
        double len = Math.Sqrt((double)v.X * v.X + (double)v.Y * v.Y + (double)v.Z * v.Z);
        Assert.InRange(len, 1.0 - 1e-5, 1.0 + 1e-5);
    }
}
