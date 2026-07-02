using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.World.Contracts.Units;
using UnifyCell;
using UnifyGeometry.Spherical;
using UnifyMaths;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Frame-agreement regression: the typed boundary arcs and the plate caps must be derived from the
/// SAME per-tick cell reassignment. The failure mode this pins: arcs built at the playhead tick
/// while caps sit at onset (or are faked with a clamped cosmetic rotation) — a convergent ribbon then
/// crosses a plate's interior by up to several degrees.
///
/// <para>
/// <b>Mechanism (per-tick cell reassignment):</b> the tessellation's cells are FIXED on the sphere;
/// what changes per tick is which plate each cell belongs to. Cap geometry is the UNROTATED
/// tessellation (watertight by construction — the sphere is tiled at every tick), while plate
/// membership evolves with the tick via the same nearest-rotated-seed rule the engine's
/// <see cref="FantaSim.Geosphere.Plate.Topology.PlateTopologyBuilder.AssignCells"/> uses at onset.
/// The boundary arcs are derived from the same reassigned membership, so arcs and caps always
/// agree: an arc's two plates are exactly the two plates that share a frontier in the reassigned
/// cell ownership at that tick.
/// </para>
///
/// <para>
/// The previous delta-distance assertion (caps-at-tick closer to arcs than caps-at-onset) was
/// tailored to the rigid-rotation approach this test replaces. Under reassignment the caps are
/// always the unrotated tessellation, so the geometric delta is no longer the right signal; instead
/// we assert the structural agreement: arcs lie along the membership boundary, and the union of
/// caps covers every cell exactly once (no gaps, no overlaps) at every tick.
/// </para>
/// </summary>
public sealed class FrameAgreementTests
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

    /// <summary>
    /// At a tick well past onset, every boundary arc's two plates must be adjacent in the
    /// reassigned cell-membership: there must be at least one cell edge where one cell belongs to
    /// arc.PlateA and its neighbour belongs to arc.PlateB. If the frames disagreed (arcs from the
    /// onset assignment, caps from reassignment), some arc would reference a plate pair that no
    /// longer shares a frontier — the arc would cut through a plate's interior.
    /// </summary>
    [Fact]
    public void Boundary_arcs_lie_along_the_reassigned_membership_boundary()
    {
        var model = BuildAppReconstructor(out long onsetTick);
        // 8 Ma past onset — well beyond the retired 0.08 rad preview cap, so the old frame
        // disagreement (caps at onset, arcs at tick) produces a clear multi-degree gap.
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
                "reassigned membership — the arc does not lie along the cell-membership boundary " +
                "(frames disagree: arcs and caps are not derived from the same tick)");
        }
    }

    /// <summary>
    /// The union of all plate caps covers every cell exactly once at the arc tick: no cell is
    /// unassigned (no gap → mantle visible through the surface) and no cell is doubly assigned
    /// (no overlap → z-fighting). This is the watertight-by-construction invariant under
    /// reassignment: the caps ARE the unrotated tessellation cells, so coverage is exact.
    /// </summary>
    [Fact]
    public void Caps_at_arc_tick_cover_every_cell_exactly_once()
    {
        var model = BuildAppReconstructor(out long onsetTick);
        long tick = onsetTick + 8 * UnitConverter.TicksPerMegaAnnum;

        var snapshot = model.BuildGlobeAt(tick);

        // No gap: every cell has a valid plate id.
        Assert.All(snapshot.Cells, c => Assert.True(c.PlateId >= 0,
            $"cell {c.CellId} is unassigned at tick {tick} — gap in the surface"));

        // No overlap: every cell id appears exactly once.
        var seen = new HashSet<int>();
        foreach (var cell in snapshot.Cells)
            Assert.True(seen.Add(cell.CellId),
                $"cell {cell.CellId} appears more than once at tick {tick} — overlap in the surface");

        Assert.Equal(snapshot.CellCount, seen.Count);
    }
}