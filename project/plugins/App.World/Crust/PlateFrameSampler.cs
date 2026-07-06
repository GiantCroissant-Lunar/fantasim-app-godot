using System;
using System.Collections.Generic;
using FantaSim.App.Ecs.Cells;
using FantaSim.App.Ecs.Systems;
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
    /// the imported path receives an <see cref="ImportedRotationProvider"/> built from a parsed
    /// <c>.rot</c> model. The plate list still supplies ids + onset topology; only the per-plate
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

        int n = _tessellation.CellCount;
        var result = new Dictionary<int, CellCrustState>(n);

        long delta = tick - _onsetTick;
        if (delta <= 0)
        {
            for (int cell = 0; cell < n; cell++)
            {
                if (!_onsetAssignment.TryGetValue(cell, out _)) continue;
                if (tickState.TryGetValue(cell, out var s))
                    result[cell] = s;
            }
            return result;
        }

        // Forward rotation per plate, sourced from the active rotation provider (generated Euler
        // poles by default, or imported .rot finite rotations when the P3 seam selects imported).
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

        // Distance caps. GAP-FILL cap (1.5 mean spacings): where plates DIVERGE the space
        // between forward images exceeds it — those cells are NEWLY FORMED oceanic crust
        // (sea-floor spreading: fraction 0, age = time since onset). SUBDUCTION-OVERRIDE cap
        // (0.75 spacings, sub-cell): where plates CONVERGE their images interleave at cell
        // scale — raw nearest picking shatters land into salt-and-pepper, while letting a
        // continental candidate win anywhere inside the WIDE cap dilates every land source to
        // its whole cap disc and inflates land 5-7x mid-window (both observed in the windowed
        // gate, 2026-07-06). So: nearest image within the wide cap wins by default; a
        // continental image overrides an oceanic nearest ONLY when genuinely co-located
        // (within the tight cap) — buoyant continental crust does not subduct (the engine's
        // CrustSideClassifier rule), but it also does not grow.
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

            CellCrustState? chosen = null;
            if (nearestCell >= 0 && tickState.TryGetValue(nearestCell, out var nearest))
            {
                chosen = nearest;
                if (nearest.ContinentalFraction < 0.5 && continentalCell >= 0
                    && tickState.TryGetValue(continentalCell, out var continental))
                    chosen = continental; // co-located overlap: the continental side overrides
            }

            result[cell] = chosen ?? new CellCrustState(
                cell,
                ContinentalFraction: 0.0,
                OrogenicPressure: 0.0,
                VolcanicActivity: 0.0,
                CrustAgeTicks: delta); // ridge-born ocean floor, at most window-old
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
    /// Derived per-cell elevation at <paramref name="tick"/> in the moving frame, using
    /// <see cref="CellElevationSystem.Derive"/> with the requested hydrosphere mode and adding the
    /// boundary-profile contribution evaluated at <paramref name="tick"/>.
    /// </summary>
    public double[] SampleElevationsAt(
        long tick,
        IReadOnlyDictionary<long, IReadOnlyDictionary<int, CellCrustState>> stateByTick,
        IReadOnlyList<PlateBoundaryArc> boundaryArcs,
        BoundaryProfileParameters boundaryProfiles,
        CellElevationHydrosphereMode hydrosphereMode,
        IReadOnlyDictionary<int, int>? currentAssignment = null)
    {
        var state = SampleAt(tick, stateByTick, currentAssignment);
        var features = IReadOnlyDictionaryExtensions.Empty<int, CrustFeature>();
        return BuildElevations(state, features, boundaryArcs, boundaryProfiles, hydrosphereMode);
    }

    private double[] BuildElevations(
        IReadOnlyDictionary<int, CellCrustState> state,
        IReadOnlyDictionary<int, CrustFeature> features,
        IReadOnlyList<PlateBoundaryArc> boundaryArcs,
        BoundaryProfileParameters boundaryProfiles,
        CellElevationHydrosphereMode hydrosphereMode)
    {
        int n = _tessellation.CellCount;
        var elevations = new double[n];

        var boundaryContributions = boundaryArcs.Count > 0
            ? BoundaryProfileContribution.Build(
                _tessellation,
                boundaryArcs,
                state,
                features,
                boundaryProfiles)
            : new double[n];

        for (int cell = 0; cell < n; cell++)
        {
            if (state.TryGetValue(cell, out var s))
            {
                var sample = new CrustSample(s.ContinentalFraction, s.OrogenicPressure, s.VolcanicActivity, s.CrustAgeTicks);
                elevations[cell] = CellElevationSystem.Derive(sample, hydrosphereMode) + boundaryContributions[cell];
            }
        }

        return elevations;
    }
}

internal static class IReadOnlyDictionaryExtensions
{
    public static IReadOnlyDictionary<TKey, TValue> Empty<TKey, TValue>()
        where TKey : notnull
        => (IReadOnlyDictionary<TKey, TValue>)new Dictionary<TKey, TValue>();
}
