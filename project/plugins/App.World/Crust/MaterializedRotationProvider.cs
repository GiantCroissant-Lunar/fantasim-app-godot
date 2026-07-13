using System.Globalization;
using FantaSim.Geosphere.Plate.Reconstruction;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.World.Contracts.Units;
using UnifyMaths;

namespace FantaSim.App.World.Crust;

/// <summary>
/// Onset-relative rotation provider backed by a materialized <see cref="RotationModel"/>. Replaces
/// the direct <c>.rot</c> reparsing path (the deleted <c>ImportedRotationProvider</c>) with committed-truth
/// playback: the model is materialized from the hash-chained truth stream through a verified cursor,
/// and queries resolve absolute rotations via the plate circuit's SLERP, then cancel the onset frame.
/// </summary>
/// <remarks>
/// <para><b>Onset-relative convention:</b>
/// <c>R_inc(tick) = R_abs(timeMa(tick)) * inverse(R_abs(onsetMa))</c>, where
/// <c>timeMa(tick) = UnitConverter.TickDeltaToMegaAnnum(tick - onsetTick)</c> and
/// <c>onsetMa = 0 Ma</c> (the playback convention pins the onset to present).</para>
/// <para><b>Plate id mapping:</b> authored string ids are normalized to integers (leading zeros
/// stripped, invariant culture) so the app's integer plate ids resolve to the circuit's authored
/// ids. Unserved plates get identity.</para>
/// <para>Deterministic: no wall-clock, no unseeded random.</para>
/// </remarks>
internal sealed class MaterializedRotationProvider : IPlateRotationProvider
{
    private const long KinematicsSampleTicks = 1_000L;
    private readonly RotationModel _model;
    private readonly long _onsetTick;
    private readonly Dictionary<int, string> _plateIdMap;
    private readonly Dictionary<int, Quaternion> _onsetInverse;

    public MaterializedRotationProvider(RotationModel model, long onsetTick)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        _onsetTick = onsetTick;

        _plateIdMap = new Dictionary<int, string>();
        _onsetInverse = new Dictionary<int, Quaternion>();

        var onsetTime = new TimeCode(0.0);
        foreach (var authoredId in model.Circuit.PlateIds)
        {
            if (string.Equals(authoredId, model.Circuit.AnchorPlateId, StringComparison.Ordinal))
                continue;
            if (!TryNormalizePlateId(authoredId, out int numericId))
                continue;

            _plateIdMap[numericId] = authoredId;
            var absAtOnset = model.Circuit.ReconstructOrientation(authoredId, onsetTime);
            _onsetInverse[numericId] = absAtOnset.Inverse();
        }
    }

    public IReadOnlyCollection<int> ServedPlateIds => _plateIdMap.Keys;

    public Quaternion RotationFromOnsetTo(int plateId, long tick)
    {
        if (!_plateIdMap.TryGetValue(plateId, out var authoredId))
            return Quaternion.Identity;

        double timeMa = UnitConverter.TickDeltaToMegaAnnum(tick - _onsetTick);
        var absolute = _model.Circuit.ReconstructOrientation(authoredId, new TimeCode(timeMa));
        return absolute * _onsetInverse[plateId];
    }

    public EulerPole InstantaneousPoleAt(int plateId, long tick)
    {
        if (!_plateIdMap.ContainsKey(plateId))
            return StationaryPole();

        var current = RotationFromOnsetTo(plateId, tick);
        var next = RotationFromOnsetTo(plateId, checked(tick + KinematicsSampleTicks));
        var delta = (next * current.Inverse()).Normalize();

        // q and -q encode the same rotation. Select the short representative before extracting
        // angle so interpolation sign choices cannot turn a tiny step into an almost-2π velocity.
        if (delta.W < 0.0)
            delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);

        double vectorLength = Math.Sqrt(
            delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
        if (vectorLength < 1e-15)
            return StationaryPole();

        var axis = new Vector3D(
            delta.X / vectorLength,
            delta.Y / vectorLength,
            delta.Z / vectorLength);
        double angle = 2.0 * Math.Atan2(vectorLength, Math.Clamp(delta.W, 0.0, 1.0));
        return new EulerPole(axis, angle / KinematicsSampleTicks);
    }

    private static EulerPole StationaryPole()
        => new(new Vector3D(0.0, 0.0, 1.0), 0.0);

    private static bool TryNormalizePlateId(string id, out int parsed)
    {
        string s = id.Trim();
        if (s.Length == 0)
        {
            parsed = 0;
            return false;
        }

        string stripped = s.TrimStart('0');
        if (stripped.Length == 0)
        {
            parsed = 0;
            return true;
        }

        return int.TryParse(stripped, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }
}
