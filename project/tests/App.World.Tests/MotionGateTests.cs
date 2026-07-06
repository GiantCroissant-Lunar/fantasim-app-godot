using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Services;
using ServiceArchi.Core;
using UnifyCell;
using UnifyGeometry.Spherical;
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
    public void GetGlobeBoundaryCellsAt_returns_a_frontier_that_moves_with_membership()
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        using var service = new Service(new ServiceRegistry());

        var a = service.GetGlobeBoundaryCellsAt(onsetTick);
        var b = service.GetGlobeBoundaryCellsAt(onsetTick + 20_000_000L);

        Assert.Contains(a, code => code != 0);
        Assert.Contains(b, code => code != 0);
        Assert.NotEqual(a, b); // the frontier follows the reassigned membership, not the onset map
    }

    [Fact]
    public void GetPlanetPresentationAsync_at_boot_tick_zero_does_not_throw()
    {
        // Regression gate: the binder's Rebind() fetches at the controller's initial tick (0,
        // pre-onset / magma era). 2026-07-06: the P2-A crust-product path threw
        // ArgumentOutOfRangeException here, which unbound the ENTIRE planet at boot — caught
        // only by the windowed gate. Pre-onset fetches must yield a document (no crust).
        using var service = new Service(new ServiceRegistry());
        var doc = service.GetPlanetPresentationAsync(0L);
        Assert.NotNull(doc);
        Assert.NotNull(doc.GlobeSnapshot);
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
    public void GetPlanetPresentationAsync_populates_continental_fraction_by_cell()
    {
        using var service = new Service(new ServiceRegistry());
        var doc = service.GetPlanetPresentationAsync();

        Assert.NotNull(doc.ContinentalFractionByCell);
        Assert.Equal(doc.GlobeSnapshot!.CellCount, doc.ContinentalFractionByCell.Count);
        Assert.All(doc.ContinentalFractionByCell.Values, f => Assert.InRange(f, 0.0, 1.0));
        Assert.Contains(doc.ContinentalFractionByCell.Values, f => f >= 0.5);
        Assert.Contains(doc.ContinentalFractionByCell.Values, f => f < 0.5);
    }

    // Spec P2 §Gates: continents must MOVE across the window (Lagrangian — land rides its plate)
    // while keeping their SHAPE (the mask at the end tick matches the onset mask rigidly rotated
    // by each cell's onset plate pole). The pair of tests below encodes both halves; a static
    // (Eulerian) field passes neither the movement test nor an honest drift demo.

    [Fact]
    public void Continental_land_mask_MOVES_across_mobile_plate_window()
    {
        var (landA, landB, _) = SampleLandMasks();

        double jaccard = Jaccard(landA, landB);
        Assert.True(jaccard < 0.7,
            $"Continents must drift: raw land-mask Jaccard across the window should be < 0.7 " +
            $"(displaced), but got {jaccard:F2} — a static (Eulerian) field.");
    }

    [Fact]
    public void Continental_land_mask_shape_is_preserved_under_plate_rotation()
    {
        var (landA, landB, ctx) = SampleLandMasks();

        // Expected mask: every onset land cell's center rotated FORWARD by its onset plate's
        // Euler pole over the window, mapped to the nearest cell. Continents ride their plates
        // rigidly; patches straddling a boundary correctly split between the plates' rotations.
        long delta = ctx.EndTick - ctx.OnsetTick;
        var rotByPlate = ctx.Plates.ToDictionary(
            p => p.PlateId,
            p => UnifyMaths.Quaternion.FromAxisAngle(
                p.Pole.Axis.Normalize(),
                p.Pole.AngularRate * delta));

        var expected = new HashSet<int>();
        foreach (var cell in landA)
        {
            if (!ctx.OnsetAssignment.TryGetValue(cell, out var plateId)) continue;
            if (!rotByPlate.TryGetValue(plateId, out var q)) continue;
            var moved = q.Rotate(ctx.Centers[cell]);
            expected.Add(NearestCell(moved, ctx.Centers));
        }

        // Area must be conserved (no smear into divergent gaps, no shrink): |landB| within 25%
        // of |landA|. Shape: rotated-expected vs actual Jaccard >= 0.6 — the honest ceiling at
        // freq 4, where default patches are only ~5 cells across so the ±1-cell-ambiguous rim
        // (nearest-target vs nearest-source constructions) is ~40% of the area; measured 0.66 on
        // 2026-07-06 for the correct Lagrangian sampler vs 0.06 for a mis-rotating one and
        // ~|landA∩landB|/|landA∪landB| ≈ 1.0 for a static one (caught separately by the MOVES
        // gate above). The windowed eye gate is the arbiter of visual shape stability.
        double areaRatio = (double)landB.Count / Math.Max(1, landA.Count);
        Assert.True(areaRatio > 0.75 && areaRatio < 1.25,
            $"Land area must be roughly conserved across the window: |landA|={landA.Count}, " +
            $"|landB|={landB.Count} (ratio {areaRatio:F2}).");

        double jaccard = Jaccard(expected, landB);
        Assert.True(jaccard >= 0.6,
            $"Continents must keep their shape while drifting: rotated-expected vs actual land " +
            $"Jaccard should be >= 0.6, but got {jaccard:F2}.");
    }

    private sealed record LandMaskContext(
        long OnsetTick,
        long EndTick,
        IReadOnlyList<FantaSim.Geosphere.Plate.Topology.Plate> Plates,
        IReadOnlyDictionary<int, int> OnsetAssignment,
        UnifyMaths.Vector3D[] Centers);

    private (HashSet<int> LandA, HashSet<int> LandB, LandMaskContext Ctx) SampleLandMasks()
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        long endTick = onsetTick + 20_000_000L;
        using var service = new Service(new ServiceRegistry());

        var a = service.GetPlanetPresentationAsync(onsetTick);
        var b = service.GetPlanetPresentationAsync(endTick);
        Assert.NotNull(a.ContinentalFractionByCell);
        Assert.NotNull(b.ContinentalFractionByCell);

        var landA = a.ContinentalFractionByCell!.Where(kv => kv.Value >= 0.5).Select(kv => kv.Key).ToHashSet();
        var landB = b.ContinentalFractionByCell!.Where(kv => kv.Value >= 0.5).Select(kv => kv.Key).ToHashSet();
        Assert.NotEmpty(landA);
        Assert.NotEmpty(landB);

        var options = WorldGenerationRenderOptions.Default;
        var roster = OnsetRoster.Build(options.Seed, onsetTick, options.TessellationFrequency);
        var plates = roster.SeedPlatesAt(onsetTick);
        var tess = new GeodesicSphereTessellation(options.TessellationFrequency);
        var centers = new UnifyMaths.Vector3D[tess.CellCount];
        for (int i = 0; i < tess.CellCount; i++)
            centers[i] = tess.GetCenter(new GeodesicCoord(i, options.TessellationFrequency)).ToVector3D().Normalize();

        var reconstructor = GlobeReconstructor.FromOnsetRoster(
            roster, onsetTick, SphereRegimeScheduleDefaults.GeosphereDefault, options.TessellationFrequency);
        var onsetAssignment = reconstructor.BuildGlobeAt(onsetTick).Cells.ToDictionary(c => c.CellId, c => c.PlateId);

        return (landA, landB, new LandMaskContext(onsetTick, endTick, plates, onsetAssignment, centers));
    }

    private static int NearestCell(UnifyMaths.Vector3D p, UnifyMaths.Vector3D[] centers)
    {
        int best = 0;
        double bestDot = double.NegativeInfinity;
        for (int i = 0; i < centers.Length; i++)
        {
            double d = UnifyMaths.Vector3D.Dot(p, centers[i]);
            if (d > bestDot) { bestDot = d; best = i; }
        }
        return best;
    }

    [Fact]
    public void Continental_fraction_is_deterministic_across_runs()
    {
        long tick = SphereRegimeScheduleDefaults.PlateOnsetTick + 10_000_000L;

        IReadOnlyDictionary<int, double> Run()
        {
            using var service = new Service(new ServiceRegistry());
            return service.GetPlanetPresentationAsync(tick).ContinentalFractionByCell!;
        }

        var a = Run();
        var b = Run();

        Assert.Equal(a.Count, b.Count);
        foreach (var (cellId, valueA) in a)
        {
            Assert.True(b.TryGetValue(cellId, out var valueB));
            Assert.Equal(valueA, valueB, 12);
        }
    }

    [Fact]
    public void Continental_fraction_frontier_exists_at_both_ticks_and_moves_with_the_land()
    {
        long tickA = SphereRegimeScheduleDefaults.PlateOnsetTick;
        long tickB = tickA + 20_000_000L;
        using var service = new Service(new ServiceRegistry());

        var docA = service.GetPlanetPresentationAsync(tickA);
        var docB = service.GetPlanetPresentationAsync(tickB);
        Assert.NotNull(docA.ContinentalFractionByCell);
        Assert.NotNull(docB.ContinentalFractionByCell);

        byte[] frontierA = BuildFractionFrontier(docA.GlobeSnapshot!, docA.ContinentalFractionByCell);
        byte[] frontierB = BuildFractionFrontier(docB.GlobeSnapshot!, docB.ContinentalFractionByCell);

        // The coastline exists at both ticks and — since the land RIDES its plates (Lagrangian
        // sampling) — the frontier is DISPLACED across the window, not identical (the identical
        // form was the Eulerian mis-model this gate previously encoded).
        Assert.Contains(frontierA, f => f != 0);
        Assert.Contains(frontierB, f => f != 0);
        Assert.NotEqual(frontierA, frontierB);
    }

    private static byte[] BuildFractionFrontier(WorldGlobeSnapshot snapshot, IReadOnlyDictionary<int, double> fractionByCell)
    {
        var result = new byte[snapshot.CellCount];
        var edgeToCells = new Dictionary<(int, int), List<int>>();
        foreach (var cell in snapshot.Cells)
        {
            AddEdge(edgeToCells, cell.CellId, cell.C0, cell.C1);
            AddEdge(edgeToCells, cell.CellId, cell.C1, cell.C2);
            AddEdge(edgeToCells, cell.CellId, cell.C2, cell.C0);
        }

        var cellSet = new HashSet<int>();
        foreach (var cell in snapshot.Cells)
        {
            if (!fractionByCell.TryGetValue(cell.CellId, out var f))
                continue;
            bool selfLand = f >= 0.5;
            cellSet.Clear();
            AddNeighbors(cellSet, edgeToCells, cell.C0, cell.C1, cell.CellId);
            AddNeighbors(cellSet, edgeToCells, cell.C1, cell.C2, cell.CellId);
            AddNeighbors(cellSet, edgeToCells, cell.C2, cell.C0, cell.CellId);
            foreach (int n in cellSet)
            {
                double nf = fractionByCell.TryGetValue(n, out var v) ? v : 0.0;
                if ((nf >= 0.5) != selfLand)
                {
                    result[cell.CellId] = 1;
                    break;
                }
            }
        }

        return result;

        static void AddEdge(Dictionary<(int, int), List<int>> map, int cellId, GlobeVec3 a, GlobeVec3 b)
        {
            var key = VertexKey(a, b);
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                map[key] = list;
            }
            list.Add(cellId);
        }

        static void AddNeighbors(HashSet<int> set, Dictionary<(int, int), List<int>> map, GlobeVec3 a, GlobeVec3 b, int self)
        {
            var key = VertexKey(a, b);
            if (map.TryGetValue(key, out var list))
            {
                foreach (var id in list)
                    if (id != self)
                        set.Add(id);
            }
        }

        static (int, int) VertexKey(GlobeVec3 a, GlobeVec3 b)
        {
            int ka = HashVertex(a);
            int kb = HashVertex(b);
            return ka < kb ? (ka, kb) : (kb, ka);
        }

        static int HashVertex(GlobeVec3 v)
            => HashCode.Combine(v.X.GetHashCode(), v.Y.GetHashCode(), v.Z.GetHashCode());
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

    private static double Jaccard(HashSet<int> a, HashSet<int> b)
    {
        if (a.Count == 0 && b.Count == 0) return 1.0;
        int intersection = a.Intersect(b).Count();
        int union = a.Union(b).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }
}
