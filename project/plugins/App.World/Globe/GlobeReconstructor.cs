using System;
using System.Collections.Generic;
using FantaSim.App.World.Cells;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.Geosphere.Crust;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.World.Contracts.Units;
using TimeDete.Time.Primitives;
using UnifyCell;
using UnifyGeometry.Spherical;
using UnifyMaths;

namespace FantaSim.App.World.Globe;

/// <summary>
/// T3 (Godot-free) seeded plate-globe model. Holds the tessellation + plates + topology and exposes:
/// <see cref="BuildGlobe"/> (static base geometry for the mesh) and <see cref="ClassifyCellsAt"/>
/// (per-cell boundary classification re-derived at a tick — the Phase 1 reclassify step). No Godot, no IO.
/// </summary>
// PLAN4-TASK3: Step 5 integration — plates born at onset, not Genesis.
//
// WHAT NEEDS WIRING:
//   1. OnsetRoster.Build(worldSeed, onsetTick, tessellationFrequency) produces the
//      PlateTopologyState (event-fold result) — use PlatesAt(tick) to get the N-plate state
//      at/after onset and an empty state before it.
//
//   2. GlobeReconstructor currently pulls plates from DefaultPlates() (a hardcoded 4-plate
//      Genesis arrangement). It needs to instead receive the geometry Plate[] from
//      LidFractureAtOnset — NOT from PlateTopologyState.Plates (those are PlateRecord IDs only,
//      no SphericalPoint/EulerPole).
//
//      Recommended approach: expose a second static Build overload on OnsetRoster that returns
//      both the PlateTopologyState AND the geometry IReadOnlyList<Plate> from the fracture step
//      (before the emit/fold). GlobeReconstructor would accept those geometry plates in place of
//      DefaultPlates(), then call PlatesAt(tick) for the event-fold state.
//
//   3. Regime gating: feed RegimeAt(currentTick).ShowsPlateFeatures to BuildGlobe /
//      ClassifyCellsAt — return empty byte[] (all interior) when ShowsPlateFeatures=false.
//
//   4. GlobePlateSurfaces / Cartography.Globe watertight-surface path also reads _plates;
//      that path must use the same onset-derived geometry plates once wired.
//
// PLAN4-TASK3b STATUS: Priority (0) + (1) complete. Priority (2) boundary routing noted below.
//
// What changed in Task 3b:
//   - OnsetRoster.SeedPlatesAt(tick) now exposes the geometry Plate list (mirrored from
//     LidFractureAtOnset's seed loop, same upwelling order → same IDs 0..N-1).
//   - GlobeReconstructor gained a second constructor: FromOnsetRoster(roster, onsetTick,
//     regimeSchedule, frequency) — uses roster.SeedPlatesAt for plate geometry + gates output
//     on regimeSchedule.RegimeAt(tick).ShowsPlateFeatures (ClassifyCellsAt, RunCrustFeatures,
//     RunCrustEvolution all return "all-interior / empty" before onset or in non-plate regimes).
//   - DefaultPlates() fallback is KEPT for the parameterless constructor (used by existing tests
//     and Host.cs ComposeWorldView/ComposeCellElevation until Task 4 wires it end-to-end).
//
// PLAN4-TASK3b: Priority (2) boundary-type routing noted — NOT done here:
//   The onset plates carry placeholder poles (rate=0.0). ClassifyBoundariesAt uses Euler-pole
//   relative motion, so all boundaries at/after onset would be classified "Inactive" by the
//   existing rigid classifier, producing no boundary lines. The CORRECT boundary types live in
//   OnsetRoster.PlatesAt(tick).Boundaries (convection-classified by ConvectionBoundaryClassifier).
//   To feed those into the render: GlobeReconstructor.ClassifyCellsAt would need a second
//   code path that builds the typeByPair dict from PlateTopologyState.Boundaries (keyed by int
//   from PlateId.Value parsed back to int) instead of calling PlateTopologyBuilder.ClassifyBoundariesAt.
//   This is a clean mechanical change but requires adding the PlateTopologyState as a field and
//   changing the ClassifyCellsAt signature or adding an overload. Deferred to Task 4 where the
//   full tick/regime threading lands in the Godot call sites.

public sealed class GlobeReconstructor
{
    // Authored spin in rad/Ma; converted to the engine's rad/tick AngularRate at the boundary.
    private const double SpinRatePerMegaAnnum = 0.02;

    private readonly int _frequency;
    private readonly GeodesicSphereTessellation _tessellation;
    private readonly IReadOnlyList<Plate> _plates;
    private readonly PlateTopology _topology;

    // Onset/regime gating — null means "no gating" (legacy DefaultPlates path).
    // When set, ClassifyCellsAt + RunCrustFeatures + RunCrustEvolution return empty/no-op
    // output before onset or in regimes where ShowsPlateFeatures = false.
    private readonly long _onsetTick;
    private readonly SphereRegimeSchedule? _regimeSchedule;

    /// <summary>
    /// Legacy constructor: uses the hardcoded four-plate <see cref="DefaultPlates"/> arrangement.
    /// Retained for existing tests and Host.cs call sites until Task 4 wires the onset path end-to-end.
    /// </summary>
    public GlobeReconstructor(int frequency = 3)
    {
        if (frequency < 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        _frequency = frequency;
        _tessellation = new GeodesicSphereTessellation(frequency);
        _plates = DefaultPlates();
        _topology = PlateTopologyBuilder.Build(_tessellation, _plates);
        _onsetTick = 0;          // no gating — always "at/after onset"
        _regimeSchedule = null;  // no regime gating
    }

    /// <summary>
    /// Onset-aware constructor: uses <paramref name="roster"/>.SeedPlatesAt for plate geometry
    /// (mirrored from LidFractureAtOnset's upwelling seed loop, IDs 0..N-1). Plate features
    /// (boundary lines, crust features) are gated by <paramref name="regimeSchedule"/>:
    /// <c>ClassifyCellsAt</c>, <c>RunCrustFeatures</c>, and <c>RunCrustEvolution</c> return
    /// all-interior / empty output before the onset tick or when the regime's
    /// <see cref="SphereRegime.ShowsPlateFeatures"/> is false (magma-ocean, stagnant-lid).
    /// <para>
    /// PLAN4-TASK4: Task 4 (Godot-side tick threading) should replace the Host.cs call sites
    /// that currently use the parameterless constructor with this overload, passing the onset
    /// roster and the live geosphere regime schedule.
    /// </para>
    /// </summary>
    public static GlobeReconstructor FromOnsetRoster(
        OnsetRoster roster,
        long onsetTick,
        SphereRegimeSchedule regimeSchedule,
        int frequency = 3)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(regimeSchedule);
        if (frequency < 0) throw new ArgumentOutOfRangeException(nameof(frequency));

        // Use the onset-seeded geometry plates (mirrored from LidFractureAtOnset — same IDs).
        // SeedPlatesAt(onsetTick) returns the N-plate list at/after onset. We assert here that
        // it is non-empty so a caller constructing at onset gets a real globe, not a lid globe.
        var plates = roster.SeedPlatesAt(onsetTick);
        if (plates.Count == 0)
            throw new ArgumentException(
                $"OnsetRoster returned no seed plates at onsetTick={onsetTick}. " +
                "Pass a tick >= onsetTick.", nameof(onsetTick));

        return new GlobeReconstructor(frequency, plates, onsetTick, regimeSchedule);
    }

    // Private constructor shared by the factory and the legacy path.
    private GlobeReconstructor(
        int frequency,
        IReadOnlyList<Plate> plates,
        long onsetTick,
        SphereRegimeSchedule? regimeSchedule)
    {
        _frequency = frequency;
        _tessellation = new GeodesicSphereTessellation(frequency);
        _plates = plates;
        _topology = PlateTopologyBuilder.Build(_tessellation, _plates);
        _onsetTick = onsetTick;
        _regimeSchedule = regimeSchedule;
    }

    /// <summary>
    /// True when plate features (boundary lines, crust features) should be emitted at
    /// <paramref name="tick"/>. False before onset or in pre-plate regimes (magma-ocean,
    /// stagnant-lid). When false, <c>ClassifyCellsAt</c> returns all-zero (interior) and
    /// <c>RunCrustFeatures</c> / <c>RunCrustEvolution</c> return empty/no-op results.
    /// </summary>
    private bool ShowsPlateFeatures(long tick)
    {
        if (tick < _onsetTick) return false;
        if (_regimeSchedule is null) return true; // legacy/no-gating path
        var regime = _regimeSchedule.RegimeAt(tick);
        return regime is null || regime.ShowsPlateFeatures;
    }

    /// <summary>
    /// Static base geometry snapshot: tessellation cells + plates. Plate IDs are assigned from
    /// the geometry topology; plate poles are included for the GPU rotation shader.
    /// This is the <b>legacy / non-onset path</b> only — valid when <c>_regimeSchedule == null</c>
    /// (constructed via the parameterless constructor). On onset-aware instances constructed via
    /// <see cref="FromOnsetRoster"/>, calling this method throws <see cref="InvalidOperationException"/>:
    /// use <see cref="BuildGlobeAt"/> instead so onset/regime gating is applied.
    /// This overload ignores regime/onset gating (base geometry is always the full N-plate mesh).
    /// Use <see cref="ClassifyCellsAt"/> to get tick-gated boundary feature output.
    /// </summary>
    public WorldGlobeSnapshot BuildGlobe()
    {
        if (_regimeSchedule is not null)
            throw new InvalidOperationException(
                "BuildGlobe() is the legacy/non-onset path and must not be called on an onset-aware " +
                "GlobeReconstructor (one constructed via FromOnsetRoster). " +
                "Call BuildGlobeAt(tick) instead so onset/regime gating is applied.");

        return BuildGlobeCore();
    }

    // Shared plate-globe build logic: builds the full N-cap WorldGlobeSnapshot from
    // the current _tessellation, _plates, and _topology. Called by BuildGlobe() (legacy
    // path, after the onset guard) and by BuildGlobeAt() (onset path, when ShowsPlateFeatures = true).
    private WorldGlobeSnapshot BuildGlobeCore()
    {
        int n = _tessellation.CellCount;
        var cells = new List<GlobeCell>(n);
        for (int cell = 0; cell < n; cell++)
        {
            var corners = _tessellation.GetBoundary(new GeodesicCoord(cell, _frequency)); // 3 unit-sphere points
            int plateId = _topology.Assignment.TryGetValue(cell, out var pid) ? pid : -1;
            cells.Add(new GlobeCell(
                cell, plateId,
                ToVec3(corners[0]), ToVec3(corners[1]), ToVec3(corners[2])));
        }

        var globePlates = new List<GlobePlate>(_plates.Count);
        foreach (var plate in _plates)
            globePlates.Add(new GlobePlate(plate.PlateId, ToVec3(plate.Pole.Axis), plate.Pole.AngularRate));

        // Authoring boundary: the engine still measures the anchor in real-world Ma, but past this
        // point the app is tick-native — the snapshot carries ticks-per-anchor, never "Ma".
        long ticksPerAnchor = UnitConverter.TicksPerMegaAnnum;
        return new WorldGlobeSnapshot(
            _frequency, n, _plates.Count, ticksPerAnchor, cells, globePlates);
    }

    /// <summary>
    /// Tick-gated globe snapshot: returns a full N-cap globe at/after onset (when
    /// <see cref="ShowsPlateFeatures"/> is true), or a lid globe with no plate assignments
    /// (all cells have plateId=-1, no plate list) before onset or in pre-plate regimes.
    /// <para>
    /// PLAN4-TASK4: Task 4 (Godot-side tick threading) wires the current Godot display tick
    /// into this overload so the scrubber shows the lid globe before onset and the plate globe
    /// at/after. Until then, Host.cs uses the parameterless <see cref="BuildGlobe()"/> which
    /// always returns the N-plate mesh.
    /// </para>
    /// </summary>
    public WorldGlobeSnapshot BuildGlobeAt(long tick)
    {
        if (!ShowsPlateFeatures(tick))
        {
            // Lid globe: watertight tessellation, no plate caps, no plate assignments.
            int n = _tessellation.CellCount;
            var cells = new List<GlobeCell>(n);
            for (int cell = 0; cell < n; cell++)
            {
                var corners = _tessellation.GetBoundary(new GeodesicCoord(cell, _frequency));
                cells.Add(new GlobeCell(cell, -1,
                    ToVec3(corners[0]), ToVec3(corners[1]), ToVec3(corners[2])));
            }
            long ticksPerAnchor = UnitConverter.TicksPerMegaAnnum;
            return new WorldGlobeSnapshot(
                _frequency, n, 0, ticksPerAnchor, cells, new List<GlobePlate>());
        }
        return BuildGlobeCore();
    }

    /// <summary>
    /// Per-cell boundary classification at <paramref name="tick"/>: 0 = plate interior, 1 = convergent,
    /// 2 = divergent, 3 = transform. A cell is a boundary cell if any neighbour belongs to a different
    /// plate; its code is the (re)classified type of that plate-pair boundary at the tick.
    /// Returns all-zero (all interior) before onset or when the regime shows no plate features.
    /// </summary>
    public byte[] ClassifyCellsAt(long tick)
    {
        int n = _tessellation.CellCount;
        if (!ShowsPlateFeatures(tick)) return new byte[n]; // all interior — pre-onset or non-plate regime

        var boundaries = PlateTopologyBuilder.ClassifyBoundariesAt(
            _tessellation, _plates, _topology, new CanonicalTick(tick));
        var typeByPair = new Dictionary<(int, int), BoundaryType>(boundaries.Count);
        foreach (var b in boundaries)
            typeByPair[(b.PlateA, b.PlateB)] = b.Type;

        var result = new byte[n];
        var space = _tessellation.Space;
        for (int cell = 0; cell < n; cell++)
        {
            if (!_topology.Assignment.TryGetValue(cell, out var pc)) continue;
            foreach (var nb in space.Neighbors(new GeodesicCoord(cell, _frequency)))
            {
                if (!_topology.Assignment.TryGetValue(nb.FaceIndex, out var pn) || pn == pc) continue;
                var key = pc < pn ? (pc, pn) : (pn, pc);
                if (typeByPair.TryGetValue(key, out var type))
                {
                    result[cell] = MapType(type);
                    break;
                }
            }
        }
        return result;
    }

    private static byte MapType(BoundaryType type) => type switch
    {
        BoundaryType.Convergent => 1,
        BoundaryType.Divergent => 2,
        BoundaryType.Transform => 3,
        _ => 0, // Inactive / interior
    };

    /// <summary>
    /// One crust-pipeline run over <paramref name="snapshotTicks"/> (continental recipe 0,1) → per
    /// snapshot, the per-cell feature kind (0 None, 1 Mountain, 2 VolcanicArc, 3 Trench, 4 Ridge,
    /// 5 Fault). Fields accumulate from genesis, so a feature emerges at the tick its magnitude crosses
    /// threshold (a mountain "grows in" as orogenic-pressure passes τ).
    /// </summary>
    public IReadOnlyDictionary<long, byte[]> RunCrustFeatures(IReadOnlyList<long> snapshotTicks)
    {
        ArgumentNullException.ThrowIfNull(snapshotTicks);

        int n = _tessellation.CellCount;
        var byTick = new Dictionary<long, byte[]>(snapshotTicks.Count);

        // Ticks that are gated out (pre-onset or non-plate regime) get all-zero (no features).
        var activeTicks = new List<long>();
        foreach (var tick in snapshotTicks)
        {
            if (!ShowsPlateFeatures(tick))
                byTick[tick] = new byte[n];
            else
                activeTicks.Add(tick);
        }

        if (activeTicks.Count == 0) return byTick;

        long endTick = 0;
        foreach (var t in activeTicks) if (t > endTick) endTick = t;

        var result = CrustPipeline.RunAsync(
            _tessellation, _plates, CrustInitRecipe.Continental(0, 1),
            startTick: 0, endTick: endTick,
            snapshotTicks: activeTicks,
            rates: DefaultRates()).GetAwaiter().GetResult();

        foreach (var tick in activeTicks)
        {
            var cells = new byte[n];
            if (result.FeaturesByTick.TryGetValue(tick, out var features))
                foreach (var kv in features)
                    if (kv.Key >= 0 && kv.Key < n)
                        cells[kv.Key] = (byte)kv.Value.Kind;
            byTick[tick] = cells;
        }
        return byTick;
    }

    /// <summary>
    /// One crust-pipeline run over <paramref name="snapshotTicks"/> exposing the engine's accumulated
    /// per-cell crust STATE at each snapshot — <c>StateByTick[tick][cellId] = CellCrustState</c> — plus
    /// the (tick-0) unit-sphere center of every cell. This is the data <see cref="Cells.CellElevationModel"/>
    /// folds into ECS cell entities to derive elevation; the same tessellation/plates/recipe/rates as
    /// <see cref="RunCrustFeatures"/>, so "the run" stays one config. Cell centers are Lagrangian for the
    /// app's purposes here (the seed geometry C uploads), matching how BuildGlobe authors the mesh.
    /// </summary>
    public CrustStateRun RunCrustEvolution(IReadOnlyList<long> snapshotTicks)
    {
        ArgumentNullException.ThrowIfNull(snapshotTicks);

        int n = _tessellation.CellCount;
        var centers = new GlobeVec3[n];
        for (int cell = 0; cell < n; cell++)
            centers[cell] = ToVec3(_tessellation.GetCenter(new GeodesicCoord(cell, _frequency)));

        // Ticks that are gated out (pre-onset or non-plate regime) get an empty state dict — same
        // gating contract as RunCrustFeatures. Only active ticks are passed to the pipeline.
        var byTick = new Dictionary<long, IReadOnlyDictionary<int, CellCrustState>>(snapshotTicks.Count);
        var activeTicks = new List<long>();
        var emptyState = (IReadOnlyDictionary<int, CellCrustState>)new Dictionary<int, CellCrustState>();
        foreach (var tick in snapshotTicks)
        {
            if (!ShowsPlateFeatures(tick))
                byTick[tick] = emptyState;
            else
                activeTicks.Add(tick);
        }

        if (activeTicks.Count == 0)
            return new CrustStateRun(n, byTick, centers);

        long endTick = 0;
        foreach (var t in activeTicks) if (t > endTick) endTick = t;

        var result = CrustPipeline.RunAsync(
            _tessellation, _plates, CrustInitRecipe.Continental(0, 1),
            startTick: 0, endTick: endTick,
            snapshotTicks: activeTicks,
            rates: DefaultRates()).GetAwaiter().GetResult();

        foreach (var tick in activeTicks)
        {
            byTick[tick] = result.StateByTick.TryGetValue(tick, out var state)
                ? state
                : emptyState;
        }

        return new CrustStateRun(n, byTick, centers);
    }

    private static CrustEvolutionRates DefaultRates()
    {
        static double PerTick(double perMa) => perMa / UnitConverter.TicksPerMegaAnnum;
        return new CrustEvolutionRates(
            OrogenicPerTick: PerTick(1.0),
            ArcVolcanismPerTick: PerTick(0.6),
            IslandArcVolcanismPerTick: PerTick(0.4),
            RidgeVolcanismPerTick: PerTick(0.5));
    }

    /// <summary>
    /// The default tectonic seed — a four-plate arrangement that exhibits the FULL boundary vocabulary
    /// under the relative classifier (engine ≥ 0.1.5), so every crust feature kind appears at once:
    /// <list type="bullet">
    ///   <item>Plate 0 (continental) spins east about +Z into still continental plate 1 ⇒ the 0|1
    ///   boundary is a continent–continent collision (Mountain); plate 0 also overrides the oceanic
    ///   plates 2,3 ⇒ 0|2 / 0|3 subduct (Trench on the ocean side + volcanic Arc on the continent side).</item>
    ///   <item>Plates 2,3 (oceanic) rotate oppositely about +Y ⇒ their shared 2|3 boundary spreads
    ///   (mid-ocean Ridge), while their boundaries with plate 1 are boundary-parallel shear (Transform ⇒ Fault).</item>
    /// </list>
    /// The crust recipe <c>Continental(0,1)</c> (see <see cref="RunCrustFeatures"/>) makes plates 0,1
    /// continental and 2,3 oceanic — the assignment these roles assume.
    /// </summary>
    private static IReadOnlyList<Plate> DefaultPlates()
    {
        double rate = UnitConverter.RadiansPerMegaAnnumToRadiansPerTick(SpinRatePerMegaAnnum);
        var zAxis = new Vector3D(0, 0, 1);
        var yAxis = new Vector3D(0, 1, 0);
        return new[]
        {
            new Plate(0, SphericalPoint.FromDegrees(0, 0),     new EulerPole(zAxis, +rate)), // continental, spins east
            new Plate(1, SphericalPoint.FromDegrees(0, 90),    new EulerPole(zAxis, 0.0)),   // continental, still
            new Plate(2, SphericalPoint.FromDegrees(40, 180),  new EulerPole(yAxis, +rate)), // oceanic
            new Plate(3, SphericalPoint.FromDegrees(-40, 180), new EulerPole(yAxis, -rate)), // oceanic
        };
    }

    private static GlobeVec3 ToVec3(SphericalPoint p)
    {
        var v = p.ToVector3D();
        return new GlobeVec3((float)v.X, (float)v.Y, (float)v.Z);
    }

    private static GlobeVec3 ToVec3(Vector3D v)
        => new GlobeVec3((float)v.X, (float)v.Y, (float)v.Z);
}
