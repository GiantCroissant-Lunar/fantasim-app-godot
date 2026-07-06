using System.Collections.Generic;
using FantaSim.Geosphere.Plate.Topology;
using UnifyMaths;
using TopoPlate = FantaSim.Geosphere.Plate.Topology.Plate;

namespace FantaSim.App.World.Crust;

/// <summary>
/// Default rotation source: each plate rotates about its constant Euler-pole axis at its constant
/// angular rate (radians per canonical tick). This preserves the exact generated behavior the
/// <see cref="PlateFrameSampler"/> applied inline before the P3 rotation-source seam.
/// </summary>
internal sealed class GeneratedEulerPoleRotationProvider : IPlateRotationProvider
{
    private readonly IReadOnlyDictionary<int, EulerPole> _polesByPlate;
    private readonly long _onsetTick;

    public GeneratedEulerPoleRotationProvider(IReadOnlyList<TopoPlate> plates, long onsetTick)
    {
        ArgumentNullException.ThrowIfNull(plates);
        var poles = new Dictionary<int, EulerPole>(plates.Count);
        foreach (var plate in plates)
            poles[plate.PlateId] = plate.Pole;
        _polesByPlate = poles;
        _onsetTick = onsetTick;
    }

    public Quaternion RotationFromOnsetTo(int plateId, long tick)
    {
        long delta = tick - _onsetTick;
        if (delta <= 0)
            return Quaternion.Identity;
        if (!_polesByPlate.TryGetValue(plateId, out var pole))
            return Quaternion.Identity;
        double angleRad = pole.AngularRate * delta;
        return Quaternion.FromAxisAngle(pole.Axis.Normalize(), angleRad);
    }
}
