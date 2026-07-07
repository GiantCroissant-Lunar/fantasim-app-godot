using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.World.Contracts.Units;
using UnifyCell;
using UnifyGeometry.Spherical;
using UnifyMaths;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Per-tick cell reassignment tests. The tessellation's cells are FIXED on the sphere; what changes
/// per tick is WHICH PLATE each cell belongs to. Cap geometry stays the unrotated tessellation
/// (watertight by construction), while plate membership evolves with the tick via the SAME
/// nearest-rotated-seed rule the engine's <see cref="PlateTopologyBuilder.AssignCells"/> uses at onset.
///
/// <para>
/// <b>Onset equivalence:</b> at the onset tick the rotation is identity, so reassignment reproduces
/// the onset roster (<see cref="PlateTopologyBuilder.Build"/> assignment) exactly. This is the
/// invariant the rigid-rotation approach (commit 1137f67) broke for caps: rotated caps cannot tile
/// the sphere. Here the caps are always the unrotated tessellation, so onset is exact by
/// construction and the test pins it.
/// </para>
///
/// <para>
/// <b>Coverage (no gaps, no overlaps):</b> at a tick well past onset, every cell is assigned to
/// exactly one plate — no cell is unassigned (no gap → mantle visible) and no cell is doubly
/// assigned (no overlap → z-fighting). The rigid-rotation approach fails this because rotated cap
/// geometry leaves cells uncovered at divergent boundaries and stacks caps at convergent ones; the
/// coverage test would fail there because <see cref="WorldGlobeSnapshot.Cells"/> would carry the
/// onset assignment while the corners are rotated away from it, so the cap polygons no longer cover
/// the cells they claim to own.
/// </para>
/// </summary>
public sealed class CellReassignmentTests
{
    private const int AppSeed = 7;
    private const int AppFrequency = 3;

    private static GlobeReconstructor BuildAppReconstructor(out long onsetTick)
    {
        onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var roster = OnsetRoster.Build(AppSeed, onsetTick, AppFrequency);
        var schedule = SphereRegimeScheduleDefaults.GeosphereFor(onsetTick);
        return GlobeReconstructor.FromOnsetRoster(roster, onsetTick, schedule, AppFrequency);
    }

    // ----------------------------------------------------------------------------------------------
    // Onset equivalence — reassignment at the onset tick reproduces the onset roster exactly.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// At the onset tick, every cell's plate id in <see cref="GlobeReconstructor.BuildGlobeAt"/>
    /// must match the onset roster assignment (<see cref="PlateTopologyBuilder.Build"/> with the
    /// onset seed plates). This is the invariant the reassignment must preserve: rotation is
    /// identity at onset, so nearest-rotated-seed == nearest-seed == the onset assignment.
    /// </summary>
    [Fact]
    public void BuildGlobeAt_onset_assignment_matches_onset_roster_exactly()
    {
        var model = BuildAppReconstructor(out long onsetTick);
        var tess = new GeodesicSphereTessellation(AppFrequency);
        var roster = OnsetRoster.Build(AppSeed, onsetTick, AppFrequency);
        var seedPlates = roster.SeedPlatesAt(onsetTick);

        // The onset roster assignment: the engine's own AssignCells with the onset seed plates.
        var expected = PlateTopologyBuilder.AssignCells(tess, seedPlates);

        var snapshot = model.BuildGlobeAt(onsetTick);

        Assert.Equal(expected.Count, snapshot.CellCount);
        foreach (var cell in snapshot.Cells)
        {
            Assert.True(expected.TryGetValue(cell.CellId, out var plateId),
                $"cell {cell.CellId} missing from onset roster assignment");
            Assert.Equal(plateId, cell.PlateId);
        }
    }

    /// <summary>
    /// At the onset tick, the cap corner geometry is the UNROTATED tessellation (the rigid rotation
    /// is gone). Every corner must be unit length and match the tessellation's own GetBoundary
    /// positions — the sphere is tiled by construction.
    /// </summary>
    [Fact]
    public void BuildGlobeAt_onset_corners_are_unrotated_tessellation_vertices()
    {
        var model = BuildAppReconstructor(out long onsetTick);
        var tess = new GeodesicSphereTessellation(AppFrequency);

        var snapshot = model.BuildGlobeAt(onsetTick);

        foreach (var cell in snapshot.Cells)
        {
            var expected = tess.GetBoundary(new GeodesicCoord(cell.CellId, AppFrequency));
            AssertCornersMatch(expected[0], cell.C0);
            AssertCornersMatch(expected[1], cell.C1);
            AssertCornersMatch(expected[2], cell.C2);
        }
    }

    // ----------------------------------------------------------------------------------------------
    // Coverage — no gaps, no overlaps, at every tick.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// At a tick well past onset, the union of all plate caps covers every cell exactly once: every
    /// cell has a valid plate id (no -1 → no gap), and no two cells share a cell id (no overlap).
    /// The rigid-rotation approach fails this because rotated cap polygons leave cells uncovered
    /// (gaps at divergent boundaries) and stack over other cells' positions (overlaps at convergent
    /// boundaries). Here the caps ARE the unrotated cells, so coverage is exact by construction.
    /// </summary>
    [Fact]
    public void BuildGlobeAt_well_past_onset_covers_every_cell_exactly_once()
    {
        var model = BuildAppReconstructor(out long onsetTick);
        // 8 Ma past onset — enough drift that rigid rotation would tear the surface (the
        // FrameAgreement test's own 8 Ma window), so this is a meaningful past-onset tick.
        long tick = onsetTick + 8 * UnitConverter.TicksPerMegaAnnum;

        var snapshot = model.BuildGlobeAt(tick);

        // Every cell is assigned to a valid plate (no gap).
        Assert.All(snapshot.Cells, c => Assert.True(c.PlateId >= 0,
            $"cell {c.CellId} is unassigned (plateId=-1) at tick {tick} — gap in the surface"));

        // Every cell id appears exactly once (no overlap / no missing).
        var seen = new HashSet<int>();
        foreach (var cell in snapshot.Cells)
        {
            Assert.True(seen.Add(cell.CellId),
                $"cell {cell.CellId} appears more than once at tick {tick} — overlap in the surface");
        }
        Assert.Equal(snapshot.CellCount, seen.Count);

        // The plate ids are a subset of the snapshot's plates (no orphan plate ids).
        var validPlateIds = new HashSet<int>(snapshot.Plates.Select(p => p.PlateId));
        Assert.All(snapshot.Cells, c => Assert.True(validPlateIds.Contains(c.PlateId),
            $"cell {c.CellId} has plate id {c.PlateId} not in the snapshot's plate list"));
    }

    /// <summary>
    /// Coverage is exact at EVERY tick, not just the one above: at several ticks across the
    /// mobile-plate span the assignment is a partition of the cell set (every cell assigned exactly
    /// once to a valid plate). This pins the watertight-by-construction invariant across time.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public void BuildGlobeAt_many_ticks_partitions_cells_into_plates(int megaAnnumPastOnset)
    {
        var model = BuildAppReconstructor(out long onsetTick);
        long tick = onsetTick + megaAnnumPastOnset * UnitConverter.TicksPerMegaAnnum;

        var snapshot = model.BuildGlobeAt(tick);

        Assert.All(snapshot.Cells, c => Assert.True(c.PlateId >= 0));
        var byCell = snapshot.Cells.ToDictionary(c => c.CellId, c => c.PlateId);
        Assert.Equal(snapshot.CellCount, byCell.Count);
    }

    // ----------------------------------------------------------------------------------------------
    // Membership evolves — the reassignment actually changes cell ownership with the tick.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// At a tick well past onset, at least one cell has changed plate ownership relative to onset.
    /// If the assignment were static (the old fixed-assignment behavior), plates would never change
    /// shape — the surface would not visibly evolve. The reassignment must move cells between
    /// plates as the seeds rotate.
    /// </summary>
    [Fact]
    public void BuildGlobeAt_well_past_onset_changes_at_least_one_cells_plate()
    {
        var model = BuildAppReconstructor(out long onsetTick);
        long tick = onsetTick + 8 * UnitConverter.TicksPerMegaAnnum;

        var onsetSnapshot = model.BuildGlobeAt(onsetTick);
        var tickSnapshot = model.BuildGlobeAt(tick);

        int changes = 0;
        foreach (var onsetCell in onsetSnapshot.Cells)
        {
            var tickCell = tickSnapshot.Cells[onsetCell.CellId];
            if (onsetCell.PlateId != tickCell.PlateId) changes++;
        }
        Assert.True(changes > 0,
            $"expected at least one cell to change plate between onset and tick {tick}, " +
            "but the assignment is identical — reassignment is not moving cells between plates");
    }

    // ----------------------------------------------------------------------------------------------
    // Arcs track the cell-membership boundary — both derive from the same rotations.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// At a tick well past onset, every boundary arc's two plates must be adjacent in the
    /// reassigned cell-membership: there must be at least one cell edge where one cell belongs to
    /// arc.PlateA and its neighbour belongs to arc.PlateB. If the arcs and the membership boundary
    /// disagreed (arcs from the onset assignment, membership from reassignment), some arc would
    /// reference a plate pair that no longer shares a frontier.
    /// </summary>
    [Fact]
    public void BuildBoundaryArcsAt_well_past_onset_lies_along_membership_boundary()
    {
        var model = BuildAppReconstructor(out long onsetTick);
        long tick = onsetTick + 8 * UnitConverter.TicksPerMegaAnnum;

        var snapshot = model.BuildGlobeAt(tick);
        var arcs = model.BuildBoundaryArcsAt(tick);
        Assert.NotEmpty(arcs);

        var byCell = snapshot.Cells.ToDictionary(c => c.CellId, c => c.PlateId);
        var space = new GeodesicSphereTessellation(AppFrequency).Space;

        // Build the set of plate pairs that share at least one cell edge in the reassigned membership.
        var membershipPairs = new HashSet<(int, int)>();
        foreach (var cell in snapshot.Cells)
        {
            int plate = cell.PlateId;
            foreach (var nb in space.Neighbors(new GeodesicCoord(cell.CellId, AppFrequency)))
            {
                if (!byCell.TryGetValue(nb.FaceIndex, out var nbPlate) || nbPlate == plate) continue;
                membershipPairs.Add((Math.Min(plate, nbPlate), Math.Max(plate, nbPlate)));
            }
        }

        foreach (var arc in arcs)
        {
            var pair = (arc.PlateA, arc.PlateB);
            Assert.True(membershipPairs.Contains(pair),
                $"arc {arc.PlateA}|{arc.PlateB} at tick {tick} has no adjacent cell pair in the " +
                "reassigned membership — the arc does not lie along the cell-membership boundary");
        }
    }

    // ----------------------------------------------------------------------------------------------
    // Performance — reassignment stays well under a frame budget at freq 4 (5120 cells, 10 plates).
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Reassignment runs on presentation refresh (regime/snapshot crossings), not per frame. At
    /// tessellation frequency 4 (5120 cells, 10 plates) it must stay well under a frame budget.
    /// O(cells x plates) simple math: one dot product per cell per plate.
    /// </summary>
    [Fact]
    public void BuildGlobeAt_freq4_reassignment_is_well_under_frame_budget()
    {
        // The onset roster at freq 4 produces 10 plates (the convection field's upwelling count at
        // that frequency). Use the app's default seed + onset.
        const int freq = 4;
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var roster = OnsetRoster.Build(AppSeed, onsetTick, freq);
        var schedule = SphereRegimeScheduleDefaults.GeosphereFor(onsetTick);
        var model = GlobeReconstructor.FromOnsetRoster(roster, onsetTick, schedule, freq);

        long tick = onsetTick + 8 * UnitConverter.TicksPerMegaAnnum;

        var snapshot = model.BuildGlobeAt(tick);
        Assert.Equal(5120, snapshot.CellCount);
        Assert.Equal(10, snapshot.PlateCount);

        model.BuildGlobeAt(tick);
        model.BuildGlobeAt(tick);

        // Wall-clock under the parallel test runner measures machine load as much as the
        // algorithm: a mean over one long run flakes whenever sibling collections or external
        // work contend for cores. The minimum over independent batches estimates the
        // uncontended cost — noise inflates some batches, a real regression inflates all.
        const int batches = 10;
        const int callsPerBatch = 10;
        double bestBatchMsPerCall = double.MaxValue;
        double totalMs = 0.0;
        for (int b = 0; b < batches; b++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < callsPerBatch; i++)
                model.BuildGlobeAt(tick);
            sw.Stop();
            double batchMsPerCall = sw.Elapsed.TotalMilliseconds / callsPerBatch;
            bestBatchMsPerCall = Math.Min(bestBatchMsPerCall, batchMsPerCall);
            totalMs += sw.Elapsed.TotalMilliseconds;
        }

        Assert.True(bestBatchMsPerCall < 16.0,
            $"reassignment at freq {freq} took {bestBatchMsPerCall:F3} ms/call in its fastest " +
            $"batch of {callsPerBatch} (mean {totalMs / (batches * callsPerBatch):F3} ms/call " +
            $"over {batches * callsPerBatch} calls) — exceeds the 16 ms frame budget");

        Console.WriteLine(
            $"[reassignment] freq {freq}: {snapshot.CellCount} cells x {snapshot.PlateCount} plates, " +
            $"best batch {bestBatchMsPerCall:F3} ms/call, mean {totalMs / (batches * callsPerBatch):F3} " +
            $"ms/call over {batches * callsPerBatch} calls (delta = 8 Ma past onset)");
    }

    // ----------------------------------------------------------------------------------------------
    // helpers
    // ----------------------------------------------------------------------------------------------

    private static void AssertCornersMatch(SphericalPoint expected, GlobeVec3 actual)
    {
        var ev = expected.ToVector3D();
        Assert.Equal(ev.X, actual.X, 5);
        Assert.Equal(ev.Y, actual.Y, 5);
        Assert.Equal(ev.Z, actual.Z, 5);
    }
}