using System;
using System.Collections.Generic;
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
public sealed class GlobeReconstructor
{
    // Authored spin in rad/Ma; converted to the engine's rad/tick AngularRate at the boundary.
    private const double SpinRatePerMegaAnnum = 0.02;

    private readonly int _frequency;
    private readonly GeodesicSphereTessellation _tessellation;
    private readonly IReadOnlyList<Plate> _plates;
    private readonly PlateTopology _topology;

    public GlobeReconstructor(int frequency = 3)
    {
        if (frequency < 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        _frequency = frequency;
        _tessellation = new GeodesicSphereTessellation(frequency);
        _plates = DefaultThreePlates();
        _topology = PlateTopologyBuilder.Build(_tessellation, _plates);
    }

    public WorldGlobeSnapshot BuildGlobe()
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
    /// Per-cell boundary classification at <paramref name="tick"/>: 0 = plate interior, 1 = convergent,
    /// 2 = divergent, 3 = transform. A cell is a boundary cell if any neighbour belongs to a different
    /// plate; its code is the (re)classified type of that plate-pair boundary at the tick.
    /// </summary>
    public byte[] ClassifyCellsAt(long tick)
    {
        var boundaries = PlateTopologyBuilder.ClassifyBoundariesAt(
            _tessellation, _plates, _topology, new CanonicalTick(tick));
        var typeByPair = new Dictionary<(int, int), BoundaryType>(boundaries.Count);
        foreach (var b in boundaries)
            typeByPair[(b.PlateA, b.PlateB)] = b.Type;

        int n = _tessellation.CellCount;
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

        long endTick = 0;
        foreach (var t in snapshotTicks) if (t > endTick) endTick = t;

        var result = CrustPipeline.RunAsync(
            _tessellation, _plates, CrustInitRecipe.Continental(0, 1),
            startTick: 0, endTick: endTick,
            snapshotTicks: snapshotTicks,
            rates: DefaultRates()).GetAwaiter().GetResult();

        int n = _tessellation.CellCount;
        var byTick = new Dictionary<long, byte[]>(snapshotTicks.Count);
        foreach (var tick in snapshotTicks)
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
    /// The proven 3-plate equatorial seed (mirrors the crust fixtures): three seeds 120 deg apart;
    /// plate 0 spins eastward about +Z (0|1 converges, 0|2 diverges), plates 1 and 2 are still.
    /// </summary>
    private static IReadOnlyList<Plate> DefaultThreePlates()
    {
        double ratePerTick = UnitConverter.RadiansPerMegaAnnumToRadiansPerTick(SpinRatePerMegaAnnum);
        return new[]
        {
            new Plate(0, SphericalPoint.FromDegrees(0, 0),    new EulerPole(new Vector3D(0, 0, 1), +ratePerTick)),
            new Plate(1, SphericalPoint.FromDegrees(0, 120),  new EulerPole(new Vector3D(0, 0, 1), 0.0)),
            new Plate(2, SphericalPoint.FromDegrees(0, -120), new EulerPole(new Vector3D(0, 0, 1), 0.0)),
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
