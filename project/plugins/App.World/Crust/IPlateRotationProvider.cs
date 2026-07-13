using UnifyMaths;
using FantaSim.Geosphere.Plate.Topology;

namespace FantaSim.App.World.Crust;

/// <summary>
/// Provides the per-plate rigid rotation that carries an onset-frame unit vector to its position
/// at a given tick. Abstraction over the generated (constant Euler-pole) and imported (GPlates
/// <c>.rot</c>, time-varying finite rotations) rotation sources so the Lagrangian sampler's
/// transport math is source-agnostic (P3 rotation-source seam).
/// </summary>
/// <remarks>
/// At <c>tick == onsetTick</c> the rotation MUST be identity (onset is the motion reference frame).
/// Implementations are deterministic: no wall-clock, no unseeded random.
/// </remarks>
internal interface IPlateRotationProvider
{
    /// <summary>
    /// The rotation that maps an onset-frame unit vector for <paramref name="plateId"/> to its
    /// orientation at <paramref name="tick"/>.
    /// </summary>
    Quaternion RotationFromOnsetTo(int plateId, long tick);

    /// <summary>
    /// Instantaneous world-frame Euler pole at <paramref name="tick"/>. Imported finite rotations
    /// derive this from a stable forward difference; generated constant-pole motion returns its
    /// authored pole exactly. Unserved/stationary plates return a zero-rate pole.
    /// </summary>
    EulerPole InstantaneousPoleAt(int plateId, long tick);
}
