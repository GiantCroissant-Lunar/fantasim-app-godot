using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Crust;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Globe;
using FantaSim.Geosphere.Crust;
using FantaSim.World.Contracts.Units;
using UnifyCell;
using UnifyGeometry.Spherical;
using UnifyMaths;
using Xunit;
using TopoPlate = FantaSim.Geosphere.Plate.Topology.Plate;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Directive 3d ("it is not egg shell after all") — TDD proofs for plate-anchored birth roughness.
/// Crust must read rough from the FIRST tick it exists: a DERIVED, DETERMINISTIC noise field sampled
/// in the PLATE-MATERIAL frame (so texture rides drifting plates — retiring the sphere-fixed
/// interior-fabric defect), amplitude-CONDITIONED on crust age (declared monotone ramp, floor at age 0),
/// present at onset with no discontinuous jump (stagnant-lid identity frame == mobile-plate onset frame).
///
/// <para>The plan's TDD order is the work order:
/// <list type="number">
/// <item><b>Purity/determinism</b> — <see cref="Sample_is_pure_identical_inputs_bit_identical_different_seeds_decorrelate"/>.</item>
/// <item><b>Plate-anchoring</b> — <see cref="Birth_roughness_rides_plates_same_material_bit_identical_across_ticks"/>.</item>
/// <item><b>Age ramp</b> — <see cref="Age_ramp_floor_at_zero_monotone_to_ceiling"/>.</item>
/// <item><b>Onset continuity</b> — <see cref="Onset_frame_is_the_base_sphere_frame_no_jump"/>.</item>
/// </list>
/// Tests 2 and 4 exercise the REAL sampler (<see cref="PlateFrameSampler.SampleBirthRoughnessAt"/>) so
/// they prove the field flows through the same plate-frame transport the elevation path uses.</para>
/// </summary>
public sealed class BirthRoughnessFieldTests
{
    // ───────────────────────────────────────────────────────────────────────────────────────────
    //  TDD step 1 — PURITY / DETERMINISM. The field is a pure function of (material-frame direction,
    //  crust age, continental fraction, profile): identical inputs → BIT-IDENTICAL metres; a different
    //  seed yields a DECORRELATED realization. No wall-clock, no query-order dependence.
    // ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sample_is_pure_identical_inputs_bit_identical_different_seeds_decorrelate()
    {
        var dir = new Vector3D(0.3, -0.7, 0.9);
        var profile = BirthRoughnessProfile.Default;

        double a = BirthRoughnessField.Sample(dir, crustAgeTicks: 5_000_000, continentalFraction: 0.8, profile);
        double b = BirthRoughnessField.Sample(dir, crustAgeTicks: 5_000_000, continentalFraction: 0.8, profile);

        // Bit-identical for identical inputs (purity).
        Assert.Equal(a, b);

        // A different seed produces a DIFFERENT field at the same position (decorrelation). The two
        // profiles differ ONLY in seed, so a value change isolates the seed's effect.
        var otherSeed = profile with { Noise = profile.Noise with { Seed = profile.Noise.Seed + 99 } };
        double c = BirthRoughnessField.Sample(dir, 5_000_000, 0.8, otherSeed);
        Assert.NotEqual(a, c);
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────
    //  TDD step 2 — PLATE-ANCHORING. Sample a vertex's material at tick T and at tick T+dT (the plate
    //  rotated in between). Expressed in the PLATE-MATERIAL frame the birth-roughness value is
    //  BIT-IDENTICAL — the texture rides the plate. Proven through the real sampler: the value for the
    //  same source material is equal at the two ticks even though that material sits at DIFFERENT
    //  current cells. This is the sphere-fixed defect's disproof.
    // ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Birth_roughness_rides_plates_same_material_bit_identical_across_ticks()
    {
        var fixture = await BirthRoughnessFixture.BuildAsync();
        long t1 = fixture.OnsetTick + UnitConverter.MegaAnnumToTickDelta(2.0);
        long t2 = fixture.OnsetTick + UnitConverter.MegaAnnumToTickDelta(3.0);
        var profile = BirthRoughnessProfile.Default;

        // Source maps at each tick: current cell -> onset-frame source cell (the plate-material anchor).
        var sourceAtT1 = fixture.Sampler.SampleSourceAssignmentAt(t1, fixture.StateKeyed(t1));
        var sourceAtT2 = fixture.Sampler.SampleSourceAssignmentAt(t2, fixture.StateKeyed(t2));

        // Birth roughness per cell through the REAL sampler at each tick.
        var brAtT1 = fixture.Sampler.SampleBirthRoughnessAt(t1, fixture.StateKeyed(t1), profile);
        var brAtT2 = fixture.Sampler.SampleBirthRoughnessAt(t2, fixture.StateKeyed(t2), profile);

        // Find a source cell whose material is present at BOTH ticks at DIFFERENT current cells (the
        // plate rotated). That witness is the plate-anchoring proof: same material, new position.
        int witnessSource = FirstSharedMovedSource(sourceAtT1, sourceAtT2);
        Assert.True(witnessSource >= 0,
            "No shared source cell found at different current cells across the two ticks — fixture cannot prove anchoring.");

        int cellAtT1 = sourceAtT1.First(kv => kv.Value == witnessSource).Key;
        int cellAtT2 = sourceAtT2.First(kv => kv.Value == witnessSource).Key;

        Assert.NotEqual(cellAtT1, cellAtT2); // the material genuinely moved
        // Same material, different current cell: the texture must be bit-identical (it rode the plate).
        Assert.Equal(brAtT1[cellAtT1], brAtT2[cellAtT2]);
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────
    //  TDD step 3 — AGE CONDITIONING. Amplitude at crust age 0 equals the declared FLOOR; it grows
    //  monotonically to the declared CEILING at AgeReferenceTicks and saturates beyond. Floor-at-age-0
    //  is the plan's locked requirement (newly solidified crust still carries faint base texture).
    // ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Age_ramp_floor_at_zero_monotone_to_ceiling()
    {
        var profile = BirthRoughnessProfile.Default;

        // Floor at age 0.
        Assert.Equal(profile.FloorAmplitudeMetres, BirthRoughnessField.AgeRampAmplitude(0, profile));

        // Ceiling at and beyond the reference age.
        Assert.Equal(
            profile.CeilingAmplitudeMetres,
            BirthRoughnessField.AgeRampAmplitude(profile.AgeReferenceTicks, profile));
        Assert.Equal(
            profile.CeilingAmplitudeMetres,
            BirthRoughnessField.AgeRampAmplitude(profile.AgeReferenceTicks * 10, profile));

        // Monotone non-decreasing across the ramp.
        double prev = BirthRoughnessField.AgeRampAmplitude(0, profile);
        double step = Math.Max(1.0, profile.AgeReferenceTicks / 16.0);
        for (double age = 1; age <= profile.AgeReferenceTicks; age += step)
        {
            double amp = BirthRoughnessField.AgeRampAmplitude(age, profile);
            Assert.True(amp >= prev, $"age ramp must be monotone non-decreasing; age {age} yielded {amp} < previous {prev}");
            prev = amp;
        }

        // Ceiling stays above floor (the declared interior-fabric budget: small-but-present texture).
        Assert.True(profile.CeilingAmplitudeMetres > profile.FloorAmplitudeMetres,
            "ceiling must exceed floor or crust never roughens with age");
        Assert.True(profile.FloorAmplitudeMetres > 0.0, "floor must be positive — crust is born rough, not flat");
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────
    //  TDD step 4 — ONSET CONTINUITY. The field at the first mobile-plate tick (onset, rotation =
    //  identity) uses the BASE-SPHERE frame: the plate-material coordinate equals the current cell
    //  center. The sampler's birth roughness at onset therefore equals a direct base-frame evaluation
    //  of the pure field — proving no discontinuous jump at the stagnant-lid -> mobile-plate boundary
    //  (the stagnant-lid identity frame == the onset frame).
    // ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Onset_frame_is_the_base_sphere_frame_no_jump()
    {
        var fixture = await BirthRoughnessFixture.BuildAsync();
        var profile = BirthRoughnessProfile.Default;

        // At onset (delta == 0) the source map is identity for cells with state: source == current cell.
        var stateKeyed = fixture.StateKeyed(fixture.OnsetTick);
        var sourceAtOnset = fixture.Sampler.SampleSourceAssignmentAt(fixture.OnsetTick, stateKeyed);
        var identityCells = sourceAtOnset.Where(kv => kv.Key == kv.Value).Select(kv => kv.Key).ToList();
        Assert.NotEmpty(identityCells);

        var brAtOnset = fixture.Sampler.SampleBirthRoughnessAt(fixture.OnsetTick, stateKeyed, profile);
        var stateAtOnset = stateKeyed[fixture.OnsetTick];

        // The birth roughness at onset must equal a direct BASE-FRAME evaluation of the pure field at
        // that cell's onset center (the base-sphere position). This is the continuity proof: the
        // mobile-plate onset frame IS the base sphere / stagnant-lid identity frame.
        foreach (var cell in identityCells.Take(5))
        {
            var state = stateAtOnset[cell];
            var baseFrameCenter = fixture.OnsetCellCenter(cell);
            double expected = BirthRoughnessField.Sample(
                baseFrameCenter, state.CrustAgeTicks, state.ContinentalFraction, profile);
            Assert.Equal(expected, brAtOnset[cell]);
        }
    }

    // === helpers =================================================================================

    // First source cell that is the material origin for some current cell at BOTH ticks, where the
    // material has actually moved (current cell differs) — the plate-anchoring witness.
    private static int FirstSharedMovedSource(
        IReadOnlyDictionary<int, int> sourceAtT1,
        IReadOnlyDictionary<int, int> sourceAtT2)
    {
        var cellBySourceT1 = sourceAtT1
            .Where(kv => kv.Value >= 0)
            .GroupBy(kv => kv.Value)
            .ToDictionary(g => g.Key, g => g.First().Key);

        foreach (var s in sourceAtT2.Values.Where(s => s >= 0).Distinct())
        {
            if (!cellBySourceT1.TryGetValue(s, out var cellAtT1)) continue;
            int cellAtT2 = sourceAtT2.First(kv => kv.Value == s).Key;
            if (cellAtT1 != cellAtT2) return s;
        }
        return -1;
    }

    // === shared async fixture (mirrors PlateFrameSamplerSmoothnessTests) ========================

    private sealed class BirthRoughnessFixture
    {
        private readonly GeodesicSphereTessellation _tessellation;
        private readonly int _frequency;
        private readonly IReadOnlyDictionary<int, CellCrustState> _snapshotState;

        private BirthRoughnessFixture(
            GeodesicSphereTessellation tessellation,
            int frequency,
            IReadOnlyDictionary<int, CellCrustState> snapshotState,
            PlateFrameSampler sampler,
            long onsetTick)
        {
            _tessellation = tessellation;
            _frequency = frequency;
            _snapshotState = snapshotState;
            Sampler = sampler;
            OnsetTick = onsetTick;
        }

        public PlateFrameSampler Sampler { get; }
        public long OnsetTick { get; }

        public static async Task<BirthRoughnessFixture> BuildAsync()
        {
            long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
            int frequency = WorldGenerationRenderOptions.Default.TessellationFrequency;
            var tessellation = new GeodesicSphereTessellation(frequency);
            var roster = OnsetRoster.Build(WorldGenerationRenderOptions.Default.Seed, onsetTick, frequency);
            var plates = roster.SeedPlatesAt(onsetTick);

            var spec = WorldCrustRunSpec.ForPresentation(
                WorldGenerationRenderOptions.Default, onsetTick, onsetTick + UnitConverter.MegaAnnumToTickDelta(8.0));
            var materialization = await WorldCrustMaterializer.MaterializeAsync(spec);
            var snapshotState = materialization.Result.StateByTick[spec.SnapshotTicks[0]];

            // Default (generated constant Euler-pole) rotation: deterministic, synchronous, enough to
            // move plates across ticks — exactly what plate-anchoring must survive.
            var sampler = new PlateFrameSampler(tessellation, plates, materialization.Topology, onsetTick);
            return new BirthRoughnessFixture(tessellation, frequency, snapshotState, sampler, onsetTick);
        }

        // State keyed at the query tick (the sampler looks state up by exact tick).
        public IReadOnlyDictionary<long, IReadOnlyDictionary<int, CellCrustState>> StateKeyed(long tick)
            => new Dictionary<long, IReadOnlyDictionary<int, CellCrustState>> { [tick] = _snapshotState };

        // Onset-frame (base-sphere) unit center of a cell — the same construction PlateFrameSampler
        // uses for its internal _centers[].
        public Vector3D OnsetCellCenter(int cellId)
            => _tessellation.GetCenter(new GeodesicCoord(cellId, _frequency)).ToVector3D().Normalize();
    }
}
