using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.Ecs.Cells;
using FantaSim.App.Ecs.Systems;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Topography;
using FantaSim.Geosphere.Crust;
using FantaSim.Geosphere.Plate.Topology;
using UnifyCell;
using UnifyGeometry.Spherical;
using TopoPlate = FantaSim.Geosphere.Plate.Topology.Plate;

namespace FantaSim.App.World.Crust;

public sealed class PlateFrameSampler
{
    private readonly GeodesicSphereTessellation _tessellation;
    private readonly IReadOnlyDictionary<int, int> _onsetAssignment;
    private readonly IReadOnlyList<TopoPlate> _plates;
    private readonly long _onsetTick;
    private readonly IPlateRotationProvider _rotationProvider;
    private readonly UnifyMaths.Vector3D[] _centers; // unit cell centers, index = cell id

    public PlateFrameSampler(
        GeodesicSphereTessellation tessellation,
        IReadOnlyList<TopoPlate> plates,
        PlateTopology onsetTopology,
        long onsetTick)
        : this(tessellation, plates, onsetTopology, onsetTick,
               new GeneratedEulerPoleRotationProvider(plates, onsetTick))
    {
    }

    /// <summary>
    /// Constructs the sampler with an explicit rotation provider (P3 rotation-source seam). The
    /// default (generated) path wraps the onset plates' Euler poles via the parameterless overload;
    /// the imported path receives a <c>MaterializedRotationProvider</c> built from a materialized
    /// rotation model. The plate list still supplies ids + onset topology; only the per-plate
    /// rotation at tick is delegated.
    /// </summary>
    internal PlateFrameSampler(
        GeodesicSphereTessellation tessellation,
        IReadOnlyList<TopoPlate> plates,
        PlateTopology onsetTopology,
        long onsetTick,
        IPlateRotationProvider rotationProvider)
    {
        ArgumentNullException.ThrowIfNull(tessellation);
        ArgumentNullException.ThrowIfNull(plates);
        ArgumentNullException.ThrowIfNull(onsetTopology);
        ArgumentNullException.ThrowIfNull(rotationProvider);
        if (onsetTick < 0) throw new ArgumentOutOfRangeException(nameof(onsetTick));

        _tessellation = tessellation;
        _onsetAssignment = onsetTopology.Assignment;
        _plates = plates;
        _onsetTick = onsetTick;
        _rotationProvider = rotationProvider;

        int n = tessellation.CellCount;
        _centers = new UnifyMaths.Vector3D[n];
        for (int i = 0; i < n; i++)
            _centers[i] = tessellation.GetCenter(new GeodesicCoord(i, tessellation.Frequency)).ToVector3D().Normalize();
    }

    /// <summary>
    /// LAGRANGIAN sampling (P2 spec / station contract 6): the state a cell shows at
    /// <paramref name="tick"/> is the state of the SOURCE cell whose material arrived there —
    /// material is carried by its ONSET plate (plates neither split nor merge in this window;
    /// that is P5), so every onset cell's center is rotated FORWARD by its onset plate's Euler
    /// pole over (tick − onset), and each target cell samples the nearest forward-rotated
    /// source. Continents therefore RIDE their plates with rigid shapes — the nearest-forward-
    /// image inverse is hole-free and shape-preserving by construction, unlike keying the
    /// inverse on the tick's seed-Voronoi membership (Voronoi regions are not rigid; at 2–4 rad
    /// of rotation that mismaps ~half the land mask — measured 2026-07-06). At/before onset the
    /// mapping is identity. Brute-force nearest lookup (O(cells²) ≈ 26M dots at freq 4,
    /// tens of ms per refresh) — spatial-hash upgrade is a P3 perf item.
    /// <paramref name="currentAssignment"/> is accepted for signature compatibility but does
    /// not drive material transport (it describes the seed-Voronoi VIEW, not carriage).
    /// </summary>
    public IReadOnlyDictionary<int, CellCrustState> SampleAt(
        long tick,
        IReadOnlyDictionary<long, IReadOnlyDictionary<int, CellCrustState>> stateByTick,
        IReadOnlyDictionary<int, int>? currentAssignment = null)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        ArgumentNullException.ThrowIfNull(stateByTick);

        if (!stateByTick.TryGetValue(tick, out var tickState) || tickState.Count == 0)
            return new Dictionary<int, CellCrustState>();

        var sourceMap = SampleSourceAssignmentCore(tick, stateByTick, out var delta, out var tickStateResolved);
        int n = _tessellation.CellCount;
        var result = new Dictionary<int, CellCrustState>(sourceMap.Count);
        foreach (var (targetCell, sourceCell) in sourceMap)
        {
            if (sourceCell >= 0 && tickStateResolved.TryGetValue(sourceCell, out var s))
                result[targetCell] = s;
            else
                result[targetCell] = new CellCrustState(
                    targetCell,
                    ContinentalFraction: 0.0,
                    OrogenicPressure: 0.0,
                    VolcanicActivity: 0.0,
                    CrustAgeTicks: delta);
        }
        return result;
    }

    /// <summary>
    /// Returns the target-cell to source-cell ID mapping at <paramref name="tick"/> using the same
    /// Lagrangian transport as <see cref="SampleAt"/>. For each target cell the returned source cell
    /// is the onset-frame cell whose material arrived there under forward plate rotation. Identity at
    /// or before onset. Gap-filled target cells (newly formed oceanic crust with no source) map to -1.
    /// Exposes the exact material-carriage mapping for diagnostics and downstream consumers that
    /// need to relate onset-frame material to the reconstructed globe.
    /// </summary>
    public IReadOnlyDictionary<int, int> SampleSourceAssignmentAt(
        long tick,
        IReadOnlyDictionary<long, IReadOnlyDictionary<int, CellCrustState>> stateByTick,
        IReadOnlyDictionary<int, int>? currentAssignment = null)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        ArgumentNullException.ThrowIfNull(stateByTick);

        if (!stateByTick.TryGetValue(tick, out var tickState) || tickState.Count == 0)
            return new Dictionary<int, int>();

        return SampleSourceAssignmentCore(tick, stateByTick, out _, out _);
    }

    /// <summary>
    /// Plate-anchored BIRTH ROUGHNESS (directive 3d) per cell at <paramref name="tick"/>: a derived,
    /// deterministic elevation component (metres) sampled in the PLATE-MATERIAL frame. For each cell
    /// the source map (<see cref="SampleSourceAssignmentAt"/>) identifies the onset-frame cell whose
    /// material arrived there under plate rotation; that source cell's onset center
    /// (<c>_centers[sourceCell]</c>) IS the plate-material coordinate, so the texture RIDES the plate
    /// by construction — retiring the sphere-fixed interior-fabric defect. Amplitude is conditioned on
    /// the MATERIAL's crust age + continental fraction (both carried with the material, like
    /// <see cref="SampleAt"/>). At/before onset the source map is identity, so the material frame
    /// equals the base-sphere frame (no discontinuous jump at the stagnant-lid -> mobile-plate boundary).
    /// </summary>
    /// <param name="tick">The query tick (drives the forward rotation that the source map inverts).</param>
    /// <param name="stateByTick">Crust state keyed by tick (the same map <see cref="SampleAt"/> consumes).</param>
    /// <param name="profile">The declared birth-roughness profile (floor/ceiling/age-ref + noise shape).</param>
    /// <returns>Per-cell birth-roughness elevation in metres (length = CellCount). Zero for gap-filled cells with no source material.</returns>
    public double[] SampleBirthRoughnessAt(
        long tick,
        IReadOnlyDictionary<long, IReadOnlyDictionary<int, CellCrustState>> stateByTick,
        BirthRoughnessProfile profile)
    {
        ArgumentNullException.ThrowIfNull(stateByTick);
        ArgumentNullException.ThrowIfNull(profile);
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));

        int n = _tessellation.CellCount;
        var result = new double[n];
        if (!stateByTick.TryGetValue(tick, out var tickState) || tickState.Count == 0)
            return result;

        var sourceMap = SampleSourceAssignmentCore(tick, stateByTick, out var delta, out var tickStateResolved);
        foreach (var (cell, sourceCell) in sourceMap)
        {
            if (sourceCell < 0)
                continue;
            var materialCoord = _centers[sourceCell];
            // Age + continental fraction travel with the MATERIAL (read off the source cell), matching
            // SampleAt's transport — reading the target cell's state would advect texture away from
            // the material that owns it.
            if (tickStateResolved.TryGetValue(sourceCell, out var s))
            {
                result[cell] = BirthRoughnessField.Sample(materialCoord, s.CrustAgeTicks, s.ContinentalFraction, profile);
            }
            else
            {
                result[cell] = BirthRoughnessField.Sample(materialCoord, delta, 0.0, profile);
            }
        }
        return result;
    }

    private Dictionary<int, int> SampleSourceAssignmentCore(
        long tick,
        IReadOnlyDictionary<long, IReadOnlyDictionary<int, CellCrustState>> stateByTick,
        out long delta,
        out IReadOnlyDictionary<int, CellCrustState> tickState)
    {
        tickState = stateByTick.TryGetValue(tick, out var ts) && ts.Count > 0
            ? ts
            : new Dictionary<int, CellCrustState>();
        int n = _tessellation.CellCount;
        var result = new Dictionary<int, int>(n);

        delta = tick - _onsetTick;
        if (delta <= 0)
        {
            for (int cell = 0; cell < n; cell++)
            {
                if (!_onsetAssignment.TryGetValue(cell, out _)) continue;
                if (tickState.TryGetValue(cell, out _))
                    result[cell] = cell;
            }
            return result;
        }

        var forwardByPlate = new Dictionary<int, UnifyMaths.Quaternion>(_plates.Count);
        foreach (var plate in _plates)
            forwardByPlate[plate.PlateId] = _rotationProvider.RotationFromOnsetTo(plate.PlateId, tick);

        var forward = new UnifyMaths.Vector3D[n];
        for (int i = 0; i < n; i++)
        {
            forward[i] = _onsetAssignment.TryGetValue(i, out var plateId)
                         && forwardByPlate.TryGetValue(plateId, out var q)
                ? q.Rotate(_centers[i])
                : _centers[i];
        }

        double meanSpacingRad = Math.Sqrt(4.0 * Math.PI / n);
        double gapFillDot = Math.Cos(1.5 * meanSpacingRad);
        double overrideDot = Math.Cos(0.75 * meanSpacingRad);

        for (int cell = 0; cell < n; cell++)
        {
            int nearestCell = -1;
            double nearestDot = double.NegativeInfinity;
            int continentalCell = -1;
            double continentalDot = double.NegativeInfinity;

            for (int i = 0; i < n; i++)
            {
                double d = UnifyMaths.Vector3D.Dot(_centers[cell], forward[i]);
                if (d < gapFillDot) continue;
                if (d > nearestDot) { nearestDot = d; nearestCell = i; }
                if (d >= overrideDot && d > continentalDot
                    && tickState.TryGetValue(i, out var cand) && cand.ContinentalFraction >= 0.5)
                {
                    continentalDot = d;
                    continentalCell = i;
                }
            }

            int source = -1;
            if (nearestCell >= 0)
            {
                source = nearestCell;
                if (tickState.TryGetValue(nearestCell, out var nearest) && nearest.ContinentalFraction < 0.5
                    && continentalCell >= 0)
                    source = continentalCell;
            }

            result[cell] = source;
        }

        return result;
    }

    /// <summary>
    /// Per-cell crust sample (presentation subset) at <paramref name="tick"/> in the moving frame.
    /// </summary>
    public IReadOnlyDictionary<int, CrustSample> SampleCrustSamplesAt(
        long tick,
        IReadOnlyDictionary<long, IReadOnlyDictionary<int, CellCrustState>> stateByTick,
        IReadOnlyDictionary<int, int>? currentAssignment = null)
    {
        var raw = SampleAt(tick, stateByTick, currentAssignment);
        var result = new Dictionary<int, CrustSample>(raw.Count);
        foreach (var (cell, s) in raw)
            result[cell] = new CrustSample(s.ContinentalFraction, s.OrogenicPressure, s.VolcanicActivity, s.CrustAgeTicks);
        return result;
    }

    /// <summary>
    /// Re-derives the engine feature map in the current Eulerian boundary frame from the already
    /// transported material state. Accumulated orogenic/volcanic fields therefore move with their
    /// material, while trench/ridge/fault markers and continent-collision arc exclusion come from
    /// the current plate assignment and current typed boundaries. A <c>VolcanicArc</c> label here is
    /// the engine's field-driven classification (transported volcanic activity on continental-leaning
    /// crust, not blocked by current collision/trench priority); by itself it does not prove that the
    /// cell currently lies on the overriding side of an active convergent boundary. This avoids
    /// advecting topology-bound markers away from the boundary that gives them meaning without
    /// overstating the transported volcanic label.
    /// </summary>
    public IReadOnlyDictionary<int, CrustFeature> SampleFeaturesAt(
        WorldGlobeSnapshot currentGlobe,
        IReadOnlyDictionary<int, CellCrustState> sampledState,
        IReadOnlyList<PlateBoundaryArc> currentArcs)
    {
        ArgumentNullException.ThrowIfNull(currentGlobe);
        ArgumentNullException.ThrowIfNull(sampledState);
        ArgumentNullException.ThrowIfNull(currentArcs);
        if (sampledState.Count == 0)
            return new Dictionary<int, CrustFeature>();

        var currentAssignment = currentGlobe.Cells.ToDictionary(cell => cell.CellId, cell => cell.PlateId);
        var currentBoundariesByPair = new Dictionary<(int A, int B), Boundary>();
        foreach (var arc in currentArcs)
        {
            var key = (Math.Min(arc.PlateA, arc.PlateB), Math.Max(arc.PlateA, arc.PlateB));
            currentBoundariesByPair[key] = new Boundary(
                arc.PlateA,
                arc.PlateB,
                Array.Empty<SphericalPoint>(),
                arc.Kind switch
                {
                    PlateBoundaryKind.Convergent => BoundaryType.Convergent,
                    PlateBoundaryKind.Divergent => BoundaryType.Divergent,
                    PlateBoundaryKind.Transform => BoundaryType.Transform,
                    _ => BoundaryType.Inactive,
                },
                MeanNormalRate: 0.0,
                MeanTangentialRate: 0.0);
        }

        var currentTopology = new PlateTopology(
            currentAssignment,
            currentBoundariesByPair.Values
                .OrderBy(boundary => boundary.PlateA)
                .ThenBy(boundary => boundary.PlateB)
                .ToArray(),
            Array.Empty<Junction>());
        return CrustFeatures.Derive(sampledState, _tessellation, currentTopology);
    }

    /// <summary>
    /// Derived per-cell elevation from already transported current-frame state/features, using
    /// <see cref="CellElevationSystem.Derive"/> with the requested hydrosphere mode and adding the
    /// boundary-profile contribution against the real current globe (including exact PlateIds).
    /// </summary>
    public double[] SampleElevationsAt(
        WorldGlobeSnapshot currentGlobe,
        IReadOnlyDictionary<int, CellCrustState> sampledState,
        IReadOnlyDictionary<int, CrustFeature> sampledFeatures,
        IReadOnlyList<PlateBoundaryArc> boundaryArcs,
        BoundaryProfileParameters boundaryProfiles,
        CellElevationHydrosphereMode hydrosphereMode,
        double[]? birthRoughnessByCell = null)
        => SampleSurfaceDataAt(
            currentGlobe,
            sampledState,
            sampledFeatures,
            boundaryArcs,
            boundaryProfiles,
            hydrosphereMode,
            birthRoughnessByCell).Elevations;

    /// <summary>
    /// Builds the authoritative current-frame surface projection: elevations and their typed visible tectonic
    /// consequences from one resolved boundary field. Callers must consume both results together so the rendered
    /// relief, feature-aware subdivision, and appearance accents cannot drift onto parallel derivation paths.
    /// </summary>
    public (double[] Elevations, CellCrustFeature[] Features) SampleSurfaceDataAt(
        WorldGlobeSnapshot currentGlobe,
        IReadOnlyDictionary<int, CellCrustState> sampledState,
        IReadOnlyDictionary<int, CrustFeature> sampledFeatures,
        IReadOnlyList<PlateBoundaryArc> boundaryArcs,
        BoundaryProfileParameters boundaryProfiles,
        CellElevationHydrosphereMode hydrosphereMode,
        double[]? birthRoughnessByCell = null)
    {
        ArgumentNullException.ThrowIfNull(currentGlobe);
        ArgumentNullException.ThrowIfNull(sampledState);
        ArgumentNullException.ThrowIfNull(sampledFeatures);
        ArgumentNullException.ThrowIfNull(boundaryArcs);
        ArgumentNullException.ThrowIfNull(boundaryProfiles);
        if (currentGlobe.CellCount != _tessellation.CellCount)
        {
            throw new ArgumentException(
                $"Current globe cell count {currentGlobe.CellCount} does not match sampler tessellation {_tessellation.CellCount}.",
                nameof(currentGlobe));
        }

        return BuildSurfaceData(
            currentGlobe,
            sampledState,
            sampledFeatures,
            boundaryArcs,
            boundaryProfiles,
            hydrosphereMode,
            birthRoughnessByCell);
    }

    internal static (double[] Elevations, CellCrustFeature[] Features) BuildSurfaceData(
        WorldGlobeSnapshot currentGlobe,
        IReadOnlyDictionary<int, CellCrustState> state,
        IReadOnlyDictionary<int, CrustFeature>? features,
        IReadOnlyList<PlateBoundaryArc> boundaryArcs,
        BoundaryProfileParameters boundaryProfiles,
        CellElevationHydrosphereMode hydrosphereMode,
        double[]? birthRoughnessByCell)
    {
        int n = currentGlobe.CellCount;
        var elevations = new double[n];
        var surfaceFeatures = new CellCrustFeature[n];

        IReadOnlyList<CellBoundarySample>? boundaryField = null;
        var boundaryContributions = boundaryArcs.Count == 0
            ? new double[n]
            : BoundaryProfileContribution.Build(
                currentGlobe,
                boundaryArcs,
                state,
                features,
                boundaryProfiles,
                out boundaryField);

        for (int cell = 0; cell < n; cell++)
        {
            if (state.TryGetValue(cell, out var s))
            {
                var sample = new CrustSample(s.ContinentalFraction, s.OrogenicPressure, s.VolcanicActivity, s.CrustAgeTicks);
                double birth = birthRoughnessByCell is not null && cell < birthRoughnessByCell.Length
                    ? birthRoughnessByCell[cell]
                    : 0.0;
                elevations[cell] = CellElevationSystem.Derive(sample, hydrosphereMode) + boundaryContributions[cell] + birth;
            }

            if (features is not null && features.TryGetValue(cell, out var feature))
                surfaceFeatures[cell] = CrustFeatureContractMapper.ToCellFeature(feature);

            if (boundaryField is not null)
            {
                var boundaryFeature = BoundaryProfileShape.SurfaceFeature(
                    boundaryField[cell],
                    boundaryProfiles);
                if (boundaryFeature.Kind != TectonicFeatureKind.None.ToWireByte())
                    surfaceFeatures[cell] = boundaryFeature;
            }
        }

        return (elevations, surfaceFeatures);
    }
}
