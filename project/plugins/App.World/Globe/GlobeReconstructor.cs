using System;
using System.Collections.Generic;
using FantaSim.App.World.Cells;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Crust;
using FantaSim.App.World.Dto;
using FantaSim.Geosphere.Crust;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.World.Contracts.Units;
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
// PLAN4-TASK3b: Priority (2) boundary-type routing noted — partially mitigated in the app port:
//   OnsetRoster now supplies non-zero Euler poles derived from convection-center drift, so the
//   existing rigid classifier has motion to classify. The richer ConvectionBoundaryClassifier
//   output from OnsetRoster.PlatesAt(tick).Boundaries is still the future source of boundary
//   doctrine when the full topology state is threaded into this reconstructor.

public sealed class GlobeReconstructor
{
    private readonly int _frequency;
    private readonly GeodesicSphereTessellation _tessellation;
    private readonly IReadOnlyList<Plate> _plates;
    private readonly PlateTopology _topology;
    private readonly IPlateRotationProvider _rotationProvider;

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
        _rotationProvider = new GeneratedEulerPoleRotationProvider(_plates, _onsetTick);
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

        return new GlobeReconstructor(
            frequency,
            plates,
            onsetTick,
            regimeSchedule,
            new GeneratedEulerPoleRotationProvider(plates, onsetTick));
    }

    /// <summary>
    /// Canonical-authority overload used by the service after truth selection. The roster still
    /// owns onset seeds/plate ids; all finite rotations and instantaneous kinematics come from the
    /// selected provider.
    /// </summary>
    internal static GlobeReconstructor FromOnsetRoster(
        OnsetRoster roster,
        long onsetTick,
        SphereRegimeSchedule regimeSchedule,
        int frequency,
        IPlateRotationProvider rotationProvider)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(regimeSchedule);
        ArgumentNullException.ThrowIfNull(rotationProvider);
        if (frequency < 0) throw new ArgumentOutOfRangeException(nameof(frequency));

        var plates = roster.SeedPlatesAt(onsetTick);
        if (plates.Count == 0)
            throw new ArgumentException(
                $"OnsetRoster returned no seed plates at onsetTick={onsetTick}. " +
                "Pass a tick >= onsetTick.", nameof(onsetTick));

        return new GlobeReconstructor(frequency, plates, onsetTick, regimeSchedule, rotationProvider);
    }

    // Private constructor shared by the factory and the legacy path.
    private GlobeReconstructor(
        int frequency,
        IReadOnlyList<Plate> plates,
        long onsetTick,
        SphereRegimeSchedule? regimeSchedule,
        IPlateRotationProvider rotationProvider)
    {
        _frequency = frequency;
        _tessellation = new GeodesicSphereTessellation(frequency);
        _plates = plates;
        _topology = PlateTopologyBuilder.Build(_tessellation, _plates);
        _onsetTick = onsetTick;
        _regimeSchedule = regimeSchedule;
        _rotationProvider = rotationProvider ?? throw new ArgumentNullException(nameof(rotationProvider));
    }

    /// <summary>
    /// The first mobile-plate tick: plate features (boundary lines, crust evolution) are emitted
    /// at/after this tick. Legacy instances (parameterless constructor) return <c>0</c> — they
    /// have no onset gating and treat all ticks as post-onset.
    /// </summary>
    public long OnsetTick => _onsetTick;

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

    // Shared plate-globe build logic at the BASE (tick-0 / onset) geometry. The cells are the
    // UNROTATED tessellation — the sphere is tiled by construction — with the onset (tick-0)
    // plate assignment from _topology. This is the snapshot BuildGlobe() (legacy path) returns,
    // and the reference BuildGlobeAt(onsetTick) reproduces under per-tick reassignment (rotation
    // is identity at the onset reference, so the reassigned assignment equals _topology.Assignment).
    private WorldGlobeSnapshot BuildGlobeCore()
    {
        int n = _tessellation.CellCount;
        int freq = _frequency;
        var cells = new List<GlobeCell>(n);
        for (int cell = 0; cell < n; cell++)
        {
            int plateId = _topology.Assignment.TryGetValue(cell, out var pid) ? pid : -1;
            var corners = _tessellation.GetBoundary(new GeodesicCoord(cell, freq));
            cells.Add(new GlobeCell(
                cell, plateId,
                ToVec3(corners[0]), ToVec3(corners[1]), ToVec3(corners[2])));
        }

        var globePlates = new List<GlobePlate>(_plates.Count);
        foreach (var plate in _plates)
        {
            var pole = _rotationProvider.InstantaneousPoleAt(plate.PlateId, _onsetTick);
            globePlates.Add(new GlobePlate(plate.PlateId, ToVec3(pole.Axis), pole.AngularRate));
        }

        long ticksPerAnchor = UnitConverter.TicksPerMegaAnnum;
        return new WorldGlobeSnapshot(
            _frequency, n, _plates.Count, ticksPerAnchor, cells, globePlates);
    }

    // Per-tick reassigned plate-globe build: the tessellation cells are FIXED on the sphere (unrotated
    // corners — watertight by construction at every tick), and WHAT CHANGES is which plate each cell
    // belongs to. The owning plate at tick T is decided by the SAME nearest-seed rule the engine's
    // PlateTopologyBuilder.AssignCells uses at onset, applied to each plate's ROTATED seed — the seed
    // is rotated by the plate's Euler rotation RELATIVE TO ONSET (delta = tick - onsetTick), so at
    // onset (delta == 0) the rotation is identity and the reassigned assignment reproduces the onset
    // roster (_topology.Assignment) exactly. Plates visibly change shape/position through membership
    // as ticks advance, which is the correct and desired presentation.
    //
    // This replaces the rigid cap-rotation approach (commit 1137f67): rigidly rotated caps cannot tile
    // a sphere once plates have drifted — they leave jagged voids at divergent boundaries and overlap
    // at convergent ones. Reassigning cells keeps the tessellation watertight trivially (the caps ARE
    // the unrotated cells) while plate membership — and therefore cap shapes, colors, and the cells
    // along each boundary — evolves with the tick.
    private WorldGlobeSnapshot BuildGlobeReassigned(long tick)
    {
        var assignment = ReassignCellsAt(tick);
        int n = _tessellation.CellCount;
        int freq = _frequency;
        var cells = new List<GlobeCell>(n);
        for (int cell = 0; cell < n; cell++)
        {
            int plateId = assignment.TryGetValue(cell, out var pid) ? pid : -1;
            var corners = _tessellation.GetBoundary(new GeodesicCoord(cell, freq));
            cells.Add(new GlobeCell(
                cell, plateId,
                ToVec3(corners[0]), ToVec3(corners[1]), ToVec3(corners[2])));
        }

        var globePlates = new List<GlobePlate>(_plates.Count);
        foreach (var plate in _plates)
        {
            var pole = _rotationProvider.InstantaneousPoleAt(plate.PlateId, tick);
            globePlates.Add(new GlobePlate(plate.PlateId, ToVec3(pole.Axis), pole.AngularRate));
        }

        long ticksPerAnchor = UnitConverter.TicksPerMegaAnnum;
        return new WorldGlobeSnapshot(
            _frequency, n, _plates.Count, ticksPerAnchor, cells, globePlates);
    }

    // Per-tick cell → plate reassignment: rotates each plate's seed by its Euler rotation relative
    // to onset (delta = tick - _onsetTick) and assigns every fixed tessellation cell to the nearest
    // rotated seed by great-circle distance (largest dot product between unit vectors). Ties break
    // toward the lower plate id, matching PlateTopologyBuilder.AssignCells exactly.
    //
    // At delta == 0 (onset, or tick == _onsetTick for the legacy path where _onsetTick == 0) the
    // rotation is identity, the rotated seeds equal the onset seeds, and the assignment reproduces
    // _topology.Assignment (the onset roster) bit-for-bit — no recompute needed, so that case short-
    // circuits and returns _topology.Assignment directly.
    //
    // O(cells x plates) simple math: one quaternion rotation per plate (cached), one dot product per
    // cell per plate. At freq 4 (5120 cells, 10 plates) this is 51200 dot products — well under a
    // frame budget on presentation refresh (not per frame).
    private IReadOnlyDictionary<int, int> ReassignCellsAt(long tick)
    {
        long delta = tick - _onsetTick;
        if (delta <= 0)
            return _topology.Assignment;

        int plateCount = _plates.Count;
        var orderedPlates = _plates.OrderBy(p => p.PlateId).ToArray();
        var rotatedSeeds = new Vector3D[plateCount];
        for (int i = 0; i < plateCount; i++)
        {
            var plate = orderedPlates[i];
            var q = _rotationProvider.RotationFromOnsetTo(plate.PlateId, tick);
            rotatedSeeds[i] = q.Rotate(SphericalVectorInterop.ToVector3D(plate.Seed));
        }

        int n = _tessellation.CellCount;
        int freq = _frequency;
        var assignment = new Dictionary<int, int>(n);

        for (int cell = 0; cell < n; cell++)
        {
            var center = _tessellation.GetCenter(new GeodesicCoord(cell, freq)).ToVector3D();

            int bestPlate = orderedPlates[0].PlateId;
            double bestCos = Vector3D.Dot(center, rotatedSeeds[0]);
            for (int s = 1; s < orderedPlates.Length; s++)
            {
                double cos = Vector3D.Dot(center, rotatedSeeds[s]);
                if (cos > bestCos) // strict ⇒ first (lowest-id) seed wins exact ties
                {
                    bestCos = cos;
                    bestPlate = orderedPlates[s].PlateId;
                }
            }
            assignment[cell] = bestPlate;
        }
        return assignment;
    }

    // Build a per-tick PlateTopology whose Assignment is the reassigned cell→plate map. The
    // Boundaries/Junctions fields are left empty — they are recomputed from the selected authority's
    // explicit current positions + instantaneous poles and are not used by the cap path. This
    // topology is what ClassifyCellsAt and BuildBoundaryArcsAt feed into that classifier so the
    // cell-membership boundary and typed arcs derive from the SAME authority at the SAME tick.
    private PlateTopology BuildReassignedTopology(long tick)
    {
        var assignment = ReassignCellsAt(tick);
        return new PlateTopology(assignment, Array.Empty<Boundary>(), Array.Empty<Junction>());
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
        return BuildGlobeReassigned(tick);
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

        // Reassign fixed world cells to plates at this tick, then classify that Eulerian ownership
        // frontier from those same fixed world positions and the authority's current poles. The
        // seed rotations already changed WHICH plate owns each world cell; rotating each newly
        // assigned cell again here would apply motion twice and mix Eulerian/Lagrangian frames.
        var topology = BuildReassignedTopology(tick);
        var boundaries = ClassifyCurrentBoundaries(topology, tick);
        var typeByPair = new Dictionary<(int, int), BoundaryType>(boundaries.Count);
        foreach (var b in boundaries)
            typeByPair[(b.PlateA, b.PlateB)] = b.Type;

        var result = new byte[n];
        var space = _tessellation.Space;
        for (int cell = 0; cell < n; cell++)
        {
            if (!topology.Assignment.TryGetValue(cell, out var pc)) continue;
            foreach (var nb in space.Neighbors(new GeodesicCoord(cell, _frequency)))
            {
                if (!topology.Assignment.TryGetValue(nb.FaceIndex, out var pn) || pn == pc) continue;
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
    /// Typed plate-boundary arcs at <paramref name="tick"/>: each inter-plate boundary from the
    /// reassigned topology (re-classified on the fixed world-cell positions) becomes a smooth great-circle
    /// polyline (<see cref="PlateBoundaryArc"/>), ready for the host seam to render. Returns an empty
    /// list before onset or in non-plate regimes (same gate as <see cref="ClassifyCellsAt"/>).
    /// </summary>
    /// <remarks>
    /// The boundary geometry comes from the SAME fixed-tessellation shared edges and per-tick cell
    /// reassignment that drive <see cref="BuildGlobeAt"/>. Motion-derived kinds come from
    /// <see cref="PlateTopologyBuilder.ClassifyBoundariesAt"/> for that reassigned ownership. Thus
    /// the visible segments lie exactly on the current cap frontier while retaining current motion
    /// classification; reconstructed material coordinates are not applied to the geometry twice.
    /// </remarks>
    /// <param name="subdivsPerSegment">Great-circle subdivisions along each real shared
    /// tessellation edge (higher = smoother, denser polyline). Defaults to 16.</param>
    public IReadOnlyList<PlateBoundaryArc> BuildBoundaryArcsAt(long tick, int subdivsPerSegment = 16)
    {
        if (!ShowsPlateFeatures(tick)) return Array.Empty<PlateBoundaryArc>();

        var topology = BuildReassignedTopology(tick);
        var boundaries = ClassifyCurrentBoundaries(topology, tick);
        var kindByPair = boundaries.ToDictionary(
            boundary => (boundary.PlateA, boundary.PlateB),
            boundary => BoundaryArcSampler.MapKind(boundary.Type));

        // Emit one local polyline for every real shared edge. Grouping all edge samples by plate
        // pair and sorting by latitude/longitude is not a valid topological ordering: disjoint
        // branches can be joined by a cross-globe segment. Edge-local arcs preserve the exact
        // frontier rendered by BuildGlobeAt and remain complete even when a pair has one edge.
        var arcs = new List<PlateBoundaryArc>();
        int n = _tessellation.CellCount;
        for (int cell = 0; cell < n; cell++)
        {
            if (!topology.Assignment.TryGetValue(cell, out int plate))
                continue;

            foreach (var neighbor in _tessellation.Space.Neighbors(new GeodesicCoord(cell, _frequency)))
            {
                int other = neighbor.FaceIndex;
                if (other <= cell
                    || !topology.Assignment.TryGetValue(other, out int otherPlate)
                    || otherPlate == plate)
                {
                    continue;
                }

                var key = (Math.Min(plate, otherPlate), Math.Max(plate, otherPlate));
                if (!kindByPair.TryGetValue(key, out var kind))
                    continue;

                var (endA, endB) = FindSharedEdgeEndpoints(cell, other);
                if (endA is null || endB is null)
                    continue;

                var points = BoundaryArcSampler.SubdivideGreatCircle(
                    endA.Value,
                    endB.Value,
                    subdivsPerSegment);
                arcs.Add(new PlateBoundaryArc(key.Item1, key.Item2, kind, points));
            }
        }
        return arcs;
    }

    private IReadOnlyList<Boundary> ClassifyCurrentBoundaries(PlateTopology topology, long tick)
    {
        var currentPositions = new Dictionary<int, Vector3D>(_tessellation.CellCount);
        for (int cell = 0; cell < _tessellation.CellCount; cell++)
        {
            currentPositions[cell] = _tessellation
                .GetCenter(new GeodesicCoord(cell, _frequency))
                .ToVector3D();
        }

        var poles = _plates.ToDictionary(
            plate => plate.PlateId,
            plate => _rotationProvider.InstantaneousPoleAt(plate.PlateId, tick));
        return PlateTopologyBuilder.ClassifyBoundariesFromKinematics(
            _tessellation,
            topology,
            currentPositions,
            poles);
    }

    // Finds the two shared tessellation-corner vertices for one known adjacent cell pair.
    private (Vector3D? EndA, Vector3D? EndB) FindSharedEdgeEndpoints(int cell, int other)
    {
        var corners = _tessellation.GetBoundary(new GeodesicCoord(cell, _frequency));
        var otherCorners = _tessellation.GetBoundary(new GeodesicCoord(other, _frequency));
        Vector3D? shared0 = null, shared1 = null;
        for (int i = 0; i < corners.Count && shared1 is null; i++)
        {
            var candidate = corners[i].ToVector3D();
            for (int j = 0; j < otherCorners.Count; j++)
            {
                if (!VertexEquals(candidate, otherCorners[j].ToVector3D()))
                    continue;
                if (shared0 is null)
                {
                    shared0 = candidate;
                    break;
                }
                if (!VertexEquals(candidate, shared0.Value))
                {
                    shared1 = candidate;
                    break;
                }
            }
        }
        return (shared0, shared1);
    }

    // Corner vertices are exact shared icosphere vertices (identical doubles across cells), so a
    // tight tolerance suffices to identify the shared pair.
    private static bool VertexEquals(Vector3D a, Vector3D b)
        => Math.Abs(a.X - b.X) < 1e-12
            && Math.Abs(a.Y - b.Y) < 1e-12
            && Math.Abs(a.Z - b.Z) < 1e-12;

    /// <summary>
    /// For each requested snapshot tick, the per-cell feature kind (0 None, 1 Mountain, 2 VolcanicArc, 3 Trench, 4 Ridge,
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

        var result = RunCrustPipeline(activeTicks);

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

        var result = RunCrustPipeline(activeTicks);

        foreach (var tick in activeTicks)
        {
            byTick[tick] = result.StateByTick.TryGetValue(tick, out var state)
                ? state
                : emptyState;
        }

        return new CrustStateRun(n, byTick, centers);
    }

    /// <summary>
    /// One crust-pipeline run over <paramref name="snapshotTicks"/> exposing BOTH the accumulated
    /// per-cell state AND the derived per-cell features (with magnitude) at each tick — from a SINGLE
    /// <see cref="CrustPipeline.RunAsync"/> call. This is the combined source for elevation derivation
    /// (<see cref="FantaSim.App.Ecs.Systems.CellElevationSystem.Derive"/> over the state) and typed
    /// feature accents (kind + magnitude). Regime-gated like <see cref="RunCrustEvolution"/>/
    /// <see cref="RunCrustFeatures"/>: pre-onset / non-plate ticks get empty entries.
    /// </summary>
    public CrustSnapshotResult RunCrustSnapshot(IReadOnlyList<long> snapshotTicks)
    {
        ArgumentNullException.ThrowIfNull(snapshotTicks);

        int n = _tessellation.CellCount;
        var emptyState = (IReadOnlyDictionary<int, CellCrustState>)new Dictionary<int, CellCrustState>();
        var emptyFeatures = (IReadOnlyDictionary<int, CrustFeature>)new Dictionary<int, CrustFeature>();

        var stateByTick = new Dictionary<long, IReadOnlyDictionary<int, CellCrustState>>(snapshotTicks.Count);
        var featuresByTick = new Dictionary<long, IReadOnlyDictionary<int, CrustFeature>>(snapshotTicks.Count);
        var activeTicks = new List<long>();
        foreach (var tick in snapshotTicks)
        {
            if (!ShowsPlateFeatures(tick))
            {
                stateByTick[tick] = emptyState;
                featuresByTick[tick] = emptyFeatures;
            }
            else
            {
                activeTicks.Add(tick);
            }
        }

        if (activeTicks.Count == 0)
            return new CrustSnapshotResult(n, stateByTick, featuresByTick);

        // rotationReferenceTick keeps the deposited boundary TYPES on the same delta-from-onset
        // rotation convention RotationDelta/ReassignCellsAt render with (snapshot ticks are absolute).
        var result = RunCrustPipeline(activeTicks);

        foreach (var tick in activeTicks)
        {
            stateByTick[tick] = result.StateByTick.TryGetValue(tick, out var state) ? state : emptyState;
            featuresByTick[tick] = result.FeaturesByTick.TryGetValue(tick, out var features) ? features : emptyFeatures;
        }

        return new CrustSnapshotResult(n, stateByTick, featuresByTick);
    }

    /// <summary>Combined crust snapshot at one or more ticks from a single pipeline run.</summary>
    public sealed record CrustSnapshotResult(
        int CellCount,
        IReadOnlyDictionary<long, IReadOnlyDictionary<int, CellCrustState>> StateByTick,
        IReadOnlyDictionary<long, IReadOnlyDictionary<int, CrustFeature>> FeaturesByTick);

    private CrustEvolutionResult RunCrustPipeline(IReadOnlyList<long> activeTicks)
    {
        var spec = WorldCrustRunSpec.ForReconstructor(
            _frequency,
            _onsetTick,
            activeTicks,
            _plates);

        return CrustPipeline.RunAsync(
            _tessellation,
            spec.Plates,
            spec.Recipe,
            startTick: spec.StartTick,
            endTick: spec.EndTick,
            snapshotTicks: spec.SnapshotTicks,
            rates: spec.Rates,
            rotationReferenceTick: spec.RotationReferenceTick,
            patchRecipe: spec.PatchRecipe).GetAwaiter().GetResult();
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
        // ONE spin-rate property end-to-end (user decision 2026-07-11): even this legacy
        // non-graph seed consumes the calibrated OnsetRoster default — the former local
        // 0.02 rad/Ma constant (uncalibrated movers p90) is gone.
        double rate = UnitConverter.RadiansPerMegaAnnumToRadiansPerTick(
            OnsetRoster.DefaultAngularDriftPerMegaAnnum);
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
