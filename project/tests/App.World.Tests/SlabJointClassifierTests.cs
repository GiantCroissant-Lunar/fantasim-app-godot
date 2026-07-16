using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Topography;
using FantaSim.Geosphere.Crust;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.World.Contracts.Units;
using TimeDete.Time.Primitives;
using UnifyCell;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Assembled-world slice 2 — the slab JOINT CLASSIFIER (vault/specs/2026-07-16-assembled-world-
/// northstar.md). Slice 1 mounts per-plate solid slabs; slice 2 must classify every JOINT between
/// adjacent plates so the joint-mechanics geometry (sibling dispatch) can express subduction
/// underride, trenches, and ridges at the dive/parting lines.
///
/// <para>The classifier is a PURE, Godot-free, contracts-tier function over the SAME render-input
/// data the slab assembly consumes upstream: the typed <see cref="PlateBoundaryArc"/> set (boundary
/// TYPE + ordered unit-sphere path points — the existing source, produced by
/// <see cref="GlobeReconstructor.BuildBoundaryArcsAt"/>) and the per-convergent-pair
/// <see cref="ConvergentBoundaryPolarity"/> (subduction polarity — the existing source, produced by
/// <see cref="ConvergentPolarity.Derive"/>). No parallel data source is invented; polarity is
/// projected into the contracts-tier <see cref="SlabJointPolarity"/> input the classifier consumes.</para>
///
/// <para>Required proofs (per the dispatch):</para>
/// <list type="bullet">
/// <item><b>COMPLETENESS</b> — on the real four-plate freq-3 globe, every adjacent plate pair with a
/// shared boundary yields exactly ONE <see cref="SlabJointClassification"/> record (no duplicates,
/// no gaps).</item>
/// <item><b>POLARITY</b> — a hand-built fixture with a KNOWN subducting side on a SKEW axis (not +Z,
/// not the equator) yields the correct subducting plate id; a co-axial fixture cannot falsify
/// orientation bugs.</item>
/// <item><b>DETERMINISM</b> — identical inputs produce bit-identical outputs, including path order.</item>
/// <item><b>PATH ADJACENCY</b> — every path point lies on the shared boundary between exactly the two
/// plates of its record (each path point is adjacent to a cell of each plate).</item>
/// </list>
/// </summary>
public sealed class SlabJointClassifierTests
{
    // ─── COMPLETENESS: one record per adjacent pair on the real seed globe ─────────────────────

    [Fact]
    public void Classify_yields_exactly_one_record_per_adjacent_plate_pair_on_the_real_seed_globe()
    {
        // The REAL four-plate seed at frequency 3 (1280 cells) through the real cartography part —
        // the same fixture shape WorldSlabAssemblyTests / BoundaryNetworkCompletenessTests use. At
        // the onset tick the rotation is identity, so BuildBoundaryArcsAt yields the seed topology's
        // boundaries with motion-derived kinds.
        var (arcs, snapshot) = BuildAppArcs(out _);

        // Every adjacent pair is the set of unordered plate pairs the topology boundaries cover.
        var expectedPairs = TopologyBoundaryPairs(snapshot);

        // Polarity: derive from a uniform oceanic state so every convergent boundary resolves to a
        // non-collision subduction with the lower-id side subducting (deterministic, no engine crust
        // pipeline needed). The classifier must carry that polarity through verbatim.
        var polarity = UniformOceanicPolarity(arcs, snapshot);

        var joints = SlabJointClassifier.Classify(arcs, polarity);

        // COMPLETENESS: exactly one record per adjacent pair, no duplicates, no gaps.
        var jointPairs = joints.Select(j => (j.PlateA, j.PlateB)).ToArray();
        Assert.NotEmpty(expectedPairs);
        Assert.Equal(expectedPairs.Count, joints.Count);
        Assert.Equal(expectedPairs, new HashSet<(int, int)>(jointPairs));
        Assert.Equal(jointPairs.Distinct().Count(), jointPairs.Length); // no duplicate pairs
    }

    [Fact]
    public void Classified_kinds_match_the_boundary_arc_kind_for_each_pair()
    {
        var (arcs, snapshot) = BuildAppArcs(out _);
        var polarity = UniformOceanicPolarity(arcs, snapshot);
        var joints = SlabJointClassifier.Classify(arcs, polarity);

        // The joint kind is the boundary TYPE from the existing source — Convergent/Divergent/
        // Transform, never invented. A pair's kind must equal the kind its arcs carry (all arcs of
        // a pair share one kind from the topology classifier).
        var kindByPair = arcs
            .GroupBy(a => (a.PlateA, a.PlateB))
            .ToDictionary(g => g.Key, g => g.First().Kind);

        foreach (var joint in joints)
        {
            Assert.True(kindByPair.TryGetValue((joint.PlateA, joint.PlateB), out var kind));
            Assert.Equal((int)kind, (int)joint.Kind);
        }
    }

    // ─── POLARITY: a hand-built SKEW-axis fixture with a known subducting side ────────────────

    [Fact]
    public void Convergent_joint_carries_the_subducting_plate_id_from_the_polarity_input_on_a_skew_axis()
    {
        // SKEW axis (not +Z, not the equator): the shared edge midpoint sits along (1,1,1)/sqrt(3)
        // and the two plates' cells sit on opposite sides of the great circle through that edge.
        // A co-axial fixture (edge on +Z, plates on the equator) cannot falsify an orientation bug
        // where the subducting side is picked from the wrong hemisphere.
        var axis = new GlobeVec3(
            X: 0.57735026f,  // 1/sqrt(3)
            Y: 0.57735026f,
            Z: 0.57735026f);

        // Two cells of two plates sharing edge v0-v1. The edge midpoint is along the skew axis.
        // v0 and v1 are unit vectors spanning the edge; v2 (plate 0's third corner) and v3
        // (plate 1's third corner) lie on opposite sides of the edge's great circle.
        var v0 = new GlobeVec3(0.70710678f, 0f, 0.70710678f);          // (1,0,1)/sqrt(2)
        var v1 = new GlobeVec3(0f, 0.70710678f, 0.70710678f);          // (0,1,1)/sqrt(2)
        var v2 = new GlobeVec3(0.40824829f, 0.40824829f, 0.81649658f); // (1,1,2)/sqrt(6) — plate 0 side
        var v3 = new GlobeVec3(0.57735026f, 0.57735026f, 0.57735026f); // skew axis — plate 1 side (further out)

        var snapshot = new WorldGlobeSnapshot(
            Frequency: 1,
            CellCount: 2,
            PlateCount: 2,
            TicksPerAnchor: 100_000,
            Cells: new List<GlobeCell>
            {
                new(0, 0, v0, v1, v2),
                new(1, 1, v0, v1, v3),
            },
            Plates: new List<GlobePlate>
            {
                new(0, axis, 0.0),
                new(1, axis, 0.0),
            });

        // The arc: plate 0 | plate 1, Convergent, two-point path along the shared edge.
        var arcs = new List<PlateBoundaryArc>
        {
            new(0, 1, PlateBoundaryKind.Convergent, new[] { v0, v1 }),
        };

        // KNOWN subducting side: plate 1 subducts under plate 0. The classifier must carry this
        // through — NOT re-derive, NOT flip by hemisphere.
        var polarity = new Dictionary<(int, int), SlabJointPolarity>
        {
            [(0, 1)] = new(PlateA: 0, PlateB: 1, SubductingPlateId: 1, IsCollision: false),
        };

        var joints = SlabJointClassifier.Classify(arcs, polarity);

        Assert.Single(joints);
        var joint = joints[0];
        Assert.Equal(0, joint.PlateA);
        Assert.Equal(1, joint.PlateB);
        Assert.Equal(SlabJointKind.Convergent, joint.Kind);
        Assert.NotNull(joint.SubductingPlateId);
        Assert.Equal(1, joint.SubductingPlateId); // plate 1 subducts — the known side
        Assert.False(joint.IsCollision);
    }

    [Fact]
    public void Convergent_collision_joint_has_no_subducting_plate_id()
    {
        // Continent-continent collision: IsCollision = true, no subduction. The classifier must
        // surface IsCollision and leave SubductingPlateId null (no dive line).
        var v0 = new GlobeVec3(0.70710678f, 0f, 0.70710678f);
        var v1 = new GlobeVec3(0f, 0.70710678f, 0.70710678f);
        var v2 = new GlobeVec3(0.40824829f, 0.40824829f, 0.81649658f);
        var v3 = new GlobeVec3(0.57735026f, 0.57735026f, 0.57735026f);

        var arcs = new List<PlateBoundaryArc>
        {
            new(0, 1, PlateBoundaryKind.Convergent, new[] { v0, v1 }),
        };
        var polarity = new Dictionary<(int, int), SlabJointPolarity>
        {
            [(0, 1)] = new(PlateA: 0, PlateB: 1, SubductingPlateId: null, IsCollision: true),
        };

        var joints = SlabJointClassifier.Classify(arcs, polarity);

        Assert.Single(joints);
        Assert.Equal(SlabJointKind.Convergent, joints[0].Kind);
        Assert.Null(joints[0].SubductingPlateId);
        Assert.True(joints[0].IsCollision);
    }

    [Fact]
    public void Divergent_and_transform_joints_have_no_subducting_plate_id()
    {
        // Only convergent boundaries have a subducting side. Divergent/transform joints must carry
        // null polarity regardless of the polarity input (which only covers convergent pairs).
        var v0 = new GlobeVec3(1f, 0f, 0f);
        var v1 = new GlobeVec3(0f, 1f, 0f);

        var arcs = new List<PlateBoundaryArc>
        {
            new(0, 1, PlateBoundaryKind.Divergent, new[] { v0, v1 }),
            new(0, 2, PlateBoundaryKind.Transform, new[] { v0, v1 }),
        };
        var polarity = new Dictionary<(int, int), SlabJointPolarity>();

        var joints = SlabJointClassifier.Classify(arcs, polarity);

        Assert.Equal(2, joints.Count);
        var div = joints.Single(j => j.Kind == SlabJointKind.Divergent);
        var trf = joints.Single(j => j.Kind == SlabJointKind.Transform);
        Assert.Null(div.SubductingPlateId);
        Assert.False(div.IsCollision);
        Assert.Null(trf.SubductingPlateId);
        Assert.False(trf.IsCollision);
    }

    // ─── DETERMINISM: identical inputs => bit-identical output incl. path order ───────────────

    [Fact]
    public void Classify_is_bit_identical_across_two_calls_on_the_same_inputs()
    {
        var (arcs, snapshot) = BuildAppArcs(out _);
        var polarity = UniformOceanicPolarity(arcs, snapshot);

        var first = SlabJointClassifier.Classify(arcs, polarity);
        var second = SlabJointClassifier.Classify(arcs, polarity);

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].PlateA, second[i].PlateA);
            Assert.Equal(first[i].PlateB, second[i].PlateB);
            Assert.Equal(first[i].Kind, second[i].Kind);
            Assert.Equal(first[i].SubductingPlateId, second[i].SubductingPlateId);
            Assert.Equal(first[i].IsCollision, second[i].IsCollision);
            // PATH ORDER bit-identical: same point count, same exact float coordinates in order.
            Assert.Equal(first[i].Path.Count, second[i].Path.Count);
            for (int p = 0; p < first[i].Path.Count; p++)
            {
                Assert.Equal(first[i].Path[p], second[i].Path[p]); // struct equality on GlobeVec3
            }
        }
    }

    [Fact]
    public void Classify_is_stable_when_arcs_arrive_in_shuffled_order()
    {
        // The joint SET is a function of the plate pairs, not the input enumeration order. Two
        // calls with the same arcs in DIFFERENT order must yield the same SET of joints (same
        // per-pair records with the same path). Path order WITHIN a pair is the merged-edge order
        // and must be stable.
        var (arcs, snapshot) = BuildAppArcs(out _);
        var polarity = UniformOceanicPolarity(arcs, snapshot);

        var shuffled = arcs.OrderBy(a => a.PlateB).ThenBy(a => a.PlateA).ToList();
        var baseline = SlabJointClassifier.Classify(arcs, polarity);
        var fromShuffled = SlabJointClassifier.Classify(shuffled, polarity);

        var byPairBaseline = baseline.ToDictionary(j => (j.PlateA, j.PlateB));
        var byPairShuffled = fromShuffled.ToDictionary(j => (j.PlateA, j.PlateB));

        Assert.Equal(byPairBaseline.Keys, byPairShuffled.Keys);
        foreach (var key in byPairBaseline.Keys)
        {
            var b = byPairBaseline[key];
            var s = byPairShuffled[key];
            Assert.Equal(b.Kind, s.Kind);
            Assert.Equal(b.SubductingPlateId, s.SubductingPlateId);
            Assert.Equal(b.IsCollision, s.IsCollision);
            Assert.Equal(b.Path.Count, s.Path.Count);
            for (int p = 0; p < b.Path.Count; p++)
                Assert.Equal(b.Path[p], s.Path[p]);
        }
    }

    // ─── PATH ADJACENCY: every path point is adjacent to both plates' cells ───────────────────

    [Fact]
    public void Every_path_point_is_adjacent_to_a_cell_of_each_plate_of_its_joint()
    {
        // On the real seed globe, every path point the classifier emits for a joint (PlateA, PlateB)
        // must lie on the shared boundary between exactly those two plates. The path points are
        // great-circle SUBDIVISION samples along the shared tessellation edge — not the cell corners
        // themselves — so adjacency is "near the centroid of a cell of each plate", not "equals a
        // corner". At frequency 3 one cell subtends ~0.099 rad; a point on a shared edge is within
        // ~one cell width of the centroids of the two cells that share that edge. A 0.5 rad
        // near-radius (generous) proves the path stays on the pair's real frontier and never crosses
        // the globe to an unrelated plate's cells.
        var (arcs, snapshot) = BuildAppArcs(out _);
        var polarity = UniformOceanicPolarity(arcs, snapshot);
        var joints = SlabJointClassifier.Classify(arcs, polarity);

        var cellsByPlate = snapshot.Cells
            .GroupBy(c => c.PlateId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var joint in joints)
        {
            Assert.True(cellsByPlate.ContainsKey(joint.PlateA));
            Assert.True(cellsByPlate.ContainsKey(joint.PlateB));
            var plateA = cellsByPlate[joint.PlateA];
            var plateB = cellsByPlate[joint.PlateB];

            foreach (var point in joint.Path)
            {
                Assert.True(IsNearSomeCellCentroid(point, plateA, maxDistanceRad: 0.5),
                    $"path point ({point.X},{point.Y},{point.Z}) of joint {joint.PlateA}|{joint.PlateB} "
                    + "is not adjacent to any cell of plate " + joint.PlateA);
                Assert.True(IsNearSomeCellCentroid(point, plateB, maxDistanceRad: 0.5),
                    $"path point ({point.X},{point.Y},{point.Z}) of joint {joint.PlateA}|{joint.PlateB} "
                    + "is not adjacent to any cell of plate " + joint.PlateB);
            }
        }
    }

    [Fact]
    public void Every_path_point_is_unit_length()
    {
        // The path points are unit-sphere points along the shared boundary. Each must be unit
        // length (the same invariant BoundaryArcSampler enforces on its arc points).
        var (arcs, snapshot) = BuildAppArcs(out _);
        var polarity = UniformOceanicPolarity(arcs, snapshot);
        var joints = SlabJointClassifier.Classify(arcs, polarity);

        foreach (var joint in joints)
        {
            Assert.NotEmpty(joint.Path);
            foreach (var p in joint.Path)
            {
                double len = Math.Sqrt((double)p.X * p.X + (double)p.Y * p.Y + (double)p.Z * p.Z);
                Assert.InRange(len, 1.0 - 1e-4, 1.0 + 1e-4);
            }
        }
    }

    // ─── Stable ordering: lower plate id first ────────────────────────────────────────────────

    [Fact]
    public void Every_joint_has_plateA_lower_than_plateB()
    {
        var (arcs, snapshot) = BuildAppArcs(out _);
        var polarity = UniformOceanicPolarity(arcs, snapshot);
        var joints = SlabJointClassifier.Classify(arcs, polarity);

        Assert.All(joints, j => Assert.True(j.PlateA < j.PlateB,
            $"joint {j.PlateA}|{j.PlateB} violates the lower-id-first ordering"));
    }

    // ─── Multi-segment arcs merge into one path per pair ──────────────────────────────────────

    [Fact]
    public void Multiple_arcs_for_the_same_pair_merge_into_one_joint_with_a_concatenated_path()
    {
        // A plate pair normally has multiple boundary segments (one per shared tessellation edge).
        // The classifier must merge them into ONE joint record per pair, with the path points
        // concatenated in a stable order (the order the arcs arrive, deduplicated of overlapping
        // endpoints — the boundary sampler subdivides endpoints-inclusive, so consecutive segments
        // share the junction point).
        var v0 = new GlobeVec3(1f, 0f, 0f);
        var v1 = new GlobeVec3(0f, 1f, 0f);
        var v2 = new GlobeVec3(0f, 0f, 1f);

        var arcs = new List<PlateBoundaryArc>
        {
            new(0, 1, PlateBoundaryKind.Divergent, new[] { v0, v1 }),
            new(0, 1, PlateBoundaryKind.Divergent, new[] { v1, v2 }),
        };

        var joints = SlabJointClassifier.Classify(arcs, polarity: new Dictionary<(int, int), SlabJointPolarity>());

        Assert.Single(joints);
        Assert.Equal(0, joints[0].PlateA);
        Assert.Equal(1, joints[0].PlateB);
        // Three unique points (v1 is shared between the two segments and must be deduped).
        Assert.Equal(3, joints[0].Path.Count);
        Assert.Equal(v0, joints[0].Path[0]);
        Assert.Equal(v1, joints[0].Path[1]);
        Assert.Equal(v2, joints[0].Path[2]);
    }

    // ─── Argument validation ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_rejects_null_arcs()
    {
        Assert.Throws<ArgumentNullException>(() => SlabJointClassifier.Classify(
            null!,
            new Dictionary<(int, int), SlabJointPolarity>()));
    }

    [Fact]
    public void Classify_rejects_null_polarity()
    {
        Assert.Throws<ArgumentNullException>(() => SlabJointClassifier.Classify(
            new List<PlateBoundaryArc>(),
            null!));
    }

    // === fixtures ===============================================================================

    // The REAL app fixture: the four-plate seed at frequency 3 (the same one
    // BoundaryNetworkCompletenessTests builds). Uses the onset-aware GlobeReconstructor +
    // BuildBoundaryArcsAt so the arcs are the real motion-classified boundary set.
    private const int AppSeed = 7;
    private const int AppFrequency = 3;

    private static (IReadOnlyList<PlateBoundaryArc> Arcs, WorldGlobeSnapshot Snapshot) BuildAppArcs(out long tick)
    {
        tick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var schedule = SphereRegimeScheduleDefaults.GeosphereFor(tick);
        var roster = OnsetRoster.Build(AppSeed, tick, AppFrequency);
        var model = GlobeReconstructor.FromOnsetRoster(roster, tick, schedule, AppFrequency);

        var snapshot = model.BuildGlobeAt(tick);
        var arcs = model.BuildBoundaryArcsAt(tick);
        return (arcs, snapshot);
    }

    // The set of unordered plate pairs the topology boundaries cover — the COMPLETE adjacency the
    // classifier must match. Mirrors BoundaryNetworkCompletenessTests.EveryTopologyBoundaryYieldsAnArc.
    private static HashSet<(int, int)> TopologyBoundaryPairs(WorldGlobeSnapshot snapshot)
    {
        var tess = new GeodesicSphereTessellation(snapshot.Frequency);
        var roster = OnsetRoster.Build(AppSeed, SphereRegimeScheduleDefaults.PlateOnsetTick, snapshot.Frequency);
        var plates = roster.SeedPlatesAt(SphereRegimeScheduleDefaults.PlateOnsetTick);
        var topology = PlateTopologyBuilder.Build(tess, plates);
        var boundaries = PlateTopologyBuilder.ClassifyBoundariesAt(
            tess, plates, topology, new CanonicalTick(SphereRegimeScheduleDefaults.PlateOnsetTick));

        var pairs = new HashSet<(int, int)>();
        foreach (var b in boundaries)
        {
            int lo = Math.Min(b.PlateA, b.PlateB);
            int hi = Math.Max(b.PlateA, b.PlateB);
            pairs.Add((lo, hi));
        }
        return pairs;
    }

    // Uniform-oceanic polarity: every convergent pair resolves to the lower-id plate subducting
    // (ContinentalFraction = 0 for all cells → both sides oceanic → tie-break: lower id subducts).
    // This lets the completeness/polarity-through tests run without the engine crust pipeline while
    // still exercising the real ConvergentPolarity.Derive + the classifier's polarity attachment.
    private static IReadOnlyDictionary<(int, int), SlabJointPolarity> UniformOceanicPolarity(
        IReadOnlyList<PlateBoundaryArc> arcs, WorldGlobeSnapshot snapshot)
    {
        var state = new Dictionary<int, CellCrustState>();
        foreach (var cell in snapshot.Cells)
            state[cell.CellId] = new CellCrustState(
                CellId: cell.CellId,
                ContinentalFraction: 0.0,
                OrogenicPressure: 0.0,
                VolcanicActivity: 0.0,
                CrustAgeTicks: 0.0);

        var enginePolarity = ConvergentPolarity.Derive(
            arcs, snapshot.Cells, features: null, state, nearRadiusRad: 0.5);

        // Project the engine polarity into the contracts-tier input the classifier consumes — the
        // exact DATA source (ConvergentPolarity.Derive), no re-derivation.
        var result = new Dictionary<(int, int), SlabJointPolarity>(enginePolarity.Count);
        foreach (var (key, p) in enginePolarity)
        {
            result[key] = new SlabJointPolarity(
                PlateA: key.PlateA,
                PlateB: key.PlateB,
                SubductingPlateId: p.IsCollision ? null : p.SubductingPlateId,
                IsCollision: p.IsCollision);
        }
        return result;
    }

    private static bool IsNearSomeCellCentroid(GlobeVec3 point, IReadOnlyList<GlobeCell> cells, double maxDistanceRad)
    {
        double cosMax = Math.Cos(maxDistanceRad);
        double px = point.X, py = point.Y, pz = point.Z;
        foreach (var cell in cells)
        {
            double cx = (cell.C0.X + cell.C1.X + cell.C2.X) / 3.0;
            double cy = (cell.C0.Y + cell.C1.Y + cell.C2.Y) / 3.0;
            double cz = (cell.C0.Z + cell.C1.Z + cell.C2.Z) / 3.0;
            double cl = Math.Sqrt((cx * cx) + (cy * cy) + (cz * cz));
            if (cl < 1e-12) continue;
            double inv = 1.0 / cl;
            double dot = (px * cx + py * cy + pz * cz) * inv;
            if (dot >= cosMax) return true;
        }
        return false;
    }
}