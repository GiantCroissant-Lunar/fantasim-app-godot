using System;
using System.Collections.Generic;
using FantaSim.App.World.Dto;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.World.Contracts.Units;
using UnifyCell;
using UnifyGeometry.Spherical;
using UnifyMaths;

namespace FantaSim.App.World.Globe;

/// <summary>
/// T3 (Godot-free) builder of a seeded plate globe. Produces a <see cref="WorldGlobeSnapshot"/> —
/// the base (tick-0) per-cell triangle corners plus each plate's Euler axis + rate-per-tick — which
/// the T4 seam turns into a GPU-rotated ArrayMesh. No Godot, no IO.
/// </summary>
public sealed class GlobeReconstructor
{
    // Authored spin in rad/Ma; converted to the engine's rad/tick AngularRate at the boundary
    // (engine main is tick-native — see WorldFunctionProvider for the same conversion).
    private const double SpinRatePerMegaAnnum = 0.02;

    private readonly int _frequency;

    public GlobeReconstructor(int frequency = 3)
    {
        if (frequency < 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        _frequency = frequency;
    }

    public WorldGlobeSnapshot BuildGlobe()
    {
        var tessellation = new GeodesicSphereTessellation(_frequency);
        var plates = DefaultThreePlates();
        var topology = PlateTopologyBuilder.Build(tessellation, plates);

        int n = tessellation.CellCount;
        var cells = new List<GlobeCell>(n);
        for (int cell = 0; cell < n; cell++)
        {
            var corners = tessellation.GetBoundary(new GeodesicCoord(cell, _frequency)); // 3 unit-sphere points
            int plateId = topology.Assignment.TryGetValue(cell, out var pid) ? pid : -1;
            cells.Add(new GlobeCell(
                cell, plateId,
                ToVec3(corners[0]), ToVec3(corners[1]), ToVec3(corners[2])));
        }

        var globePlates = new List<GlobePlate>(plates.Count);
        foreach (var plate in plates)
            globePlates.Add(new GlobePlate(plate.PlateId, ToVec3(plate.Pole.Axis), plate.Pole.AngularRate));

        return new WorldGlobeSnapshot(_frequency, n, plates.Count, cells, globePlates);
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
