using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FantaSim.Geosphere.Plate.Rotation;
using FantaSim.World.Contracts.Units;
using UnifyMaths;

namespace FantaSim.App.World.Crust;

/// <summary>
/// Imported rotation source (P3): per-plate rotations come from a parsed GPlates <c>.rot</c> model
/// of total finite rotations. At each query tick the provider resolves the absolute (anchor-frame)
/// total rotation by composing the fixed-plate chain, interpolates between keyframe times via
/// quaternion SLERP, and returns the incremental rotation that carries an onset-frame point to its
/// tick-frame position: <c>R_inc(tick) = R_abs(timeMa(tick)) * R_abs(onsetMa)^-1</c>.
/// </summary>
/// <remarks>
/// <para><b>Tick &harr; Ma mapping:</b> <c>timeMa(tick) = UnitConverter.TickDeltaToMegaAnnum(tick - onsetTick)</c>,
/// so the onset tick maps to 0 Ma (present). A well-formed <c>.rot</c> has identity total rotation
/// at 0 Ma, making <c>R_abs(onsetMa)</c> identity and <c>R_inc</c> equal to the interpolated total
/// rotation. When 0 Ma is absent the inverse of the extrapolated onset rotation still cancels the
/// onset frame correctly.</para>
/// <para><b>Chain composition:</b> a plate's total rotation may be relative to another moving plate
/// rather than the anchor (PLATES4 convention). The absolute rotation is the chain
/// <c>R_abs(P) = R_abs(parent(P)) * R_rel(P)</c>, terminating at the anchor (a plate id with no
/// entry of its own). Cycles are detected and rejected.</para>
/// <para><b>Plate id mapping:</b> <c>.rot</c> moving/fixed ids are parsed as integers (leading zeros
/// stripped) and matched to the app's integer plate ids. Unmapped app plates get identity rotation.</para>
/// <para>Deterministic: no wall-clock, no unseeded random.</para>
/// </remarks>
internal sealed class ImportedRotationProvider : IPlateRotationProvider
{
    private readonly long _onsetTick;
    private readonly Dictionary<int, List<Keyframe>> _keyframesByPlate;
    private readonly Dictionary<int, Quaternion> _onsetInverseByPlate;

    public ImportedRotationProvider(string sourceName, string rotText, long onsetTick)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(rotText);

        _onsetTick = onsetTick;

        RotImportResult result;
        var parser = new RotParser();
        using (var reader = new StringReader(rotText))
            result = parser.Parse(sourceName, reader);

        if (result.Issues.Count > 0)
        {
            throw new ArgumentException(
                $"Imported rotation source '{sourceName}' has {result.Issues.Count} parse issue(s): "
                + string.Join("; ", result.Issues.Select(i => $"line {i.LineNumber} [{i.Code}]: {i.Message}")),
                nameof(rotText));
        }

        if (result.Rotations.Count == 0)
            throw new ArgumentException(
                $"Imported rotation source '{sourceName}' contained no rotations.", nameof(rotText));

        _keyframesByPlate = ResolveAbsoluteRotations(result.Rotations);

        // Precompute R_abs(onsetMa)^-1 per plate. onsetMa = 0 (present).
        _onsetInverseByPlate = new Dictionary<int, Quaternion>(_keyframesByPlate.Count);
        foreach (var (plateId, keyframes) in _keyframesByPlate)
            _onsetInverseByPlate[plateId] = InterpolateAbsolute(keyframes, timeMa: 0.0).Inverse();
    }

    /// <summary>Plate ids the imported model can serve (parsed from <c>.rot</c> moving ids).</summary>
    public IReadOnlyCollection<int> ServedPlateIds => _keyframesByPlate.Keys;

    public Quaternion RotationFromOnsetTo(int plateId, long tick)
    {
        if (!_keyframesByPlate.TryGetValue(plateId, out var keyframes))
            return Quaternion.Identity;

        double timeMa = UnitConverter.TickDeltaToMegaAnnum(tick - _onsetTick);
        var absolute = InterpolateAbsolute(keyframes, timeMa);
        return absolute * _onsetInverseByPlate[plateId];
    }

    private static Quaternion InterpolateAbsolute(List<Keyframe> keyframes, double timeMa)
    {
        if (keyframes.Count == 0)
            return Quaternion.Identity;
        if (keyframes.Count == 1)
            return keyframes[0].Rotation;
        if (timeMa <= keyframes[0].TimeMa)
            return keyframes[0].Rotation;
        if (timeMa >= keyframes[^1].TimeMa)
            return keyframes[^1].Rotation;

        int lo = 0, hi = keyframes.Count - 1;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) / 2;
            if (keyframes[mid].TimeMa <= timeMa) lo = mid; else hi = mid;
        }

        var (t1, r1) = keyframes[lo];
        var (t2, r2) = keyframes[hi];
        double span = t2 - t1;
        if (span <= 0.0)
            return r2;
        double t = (timeMa - t1) / span;
        return Quaternion.Slerp(r1, r2, t);
    }

    /// <summary>
    /// Resolves absolute (anchor-frame) total rotations per plate per time, then sorts keyframes
    /// chronologically. Last-wins on duplicate (plate, time) entries.
    /// </summary>
    private static Dictionary<int, List<Keyframe>> ResolveAbsoluteRotations(
        IReadOnlyList<PlateRotationPayload> rotations)
    {
        // timeMa -> (movingPlateId -> (fixedPlateId, relative rotation))
        var byTime = new SortedDictionary<double, Dictionary<int, (int FixedId, Quaternion Rel)>>();
        foreach (var r in rotations)
        {
            if (!TryParsePlateId(r.MovingPlateId, out int movingId))
                continue;
            if (!TryParsePlateId(r.FixedPlateId, out int fixedId))
                continue;

            if (!byTime.TryGetValue(r.TimeMa, out var frame))
            {
                frame = new Dictionary<int, (int, Quaternion)>();
                byTime[r.TimeMa] = frame;
            }
            frame[movingId] = (fixedId, PayloadToQuaternion(r));
        }

        var result = new Dictionary<int, List<Keyframe>>();
        foreach (var (timeMa, frame) in byTime)
        {
            foreach (var movingId in frame.Keys)
            {
                var abs = ResolveChain(movingId, frame, new HashSet<int>());
                if (!result.TryGetValue(movingId, out var list))
                {
                    list = new List<Keyframe>();
                    result[movingId] = list;
                }
                list.Add(new Keyframe(timeMa, abs));
            }
        }

        return result;
    }

    private static Quaternion ResolveChain(
        int plateId,
        Dictionary<int, (int FixedId, Quaternion Rel)> frame,
        HashSet<int> visited)
    {
        if (!frame.TryGetValue(plateId, out var entry))
            return Quaternion.Identity; // anchor plate: no entry of its own
        if (!visited.Add(plateId))
            throw new InvalidOperationException(
                $"Cyclic plate rotation chain detected at plate {plateId} while resolving imported rotations.");

        var parentAbsolute = ResolveChain(entry.FixedId, frame, visited);
        return parentAbsolute * entry.Rel;
    }

    private static bool TryParsePlateId(string id, out int parsed)
    {
        string s = id.Trim();
        if (s.Length == 0)
        {
            parsed = 0;
            return false;
        }

        // Strip leading zeros but keep at least one digit ("000" -> "0").
        string stripped = s.TrimStart('0');
        if (stripped.Length == 0)
        {
            parsed = 0;
            return true;
        }

        return int.TryParse(stripped, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }

    private static Quaternion PayloadToQuaternion(PlateRotationPayload r)
    {
        double latRad = r.PoleLatDeg * Math.PI / 180.0;
        double lonRad = r.PoleLonDeg * Math.PI / 180.0;
        double angleRad = r.AngleDeg * Math.PI / 180.0;
        // lat/lon -> unit axis: (cos(lat)cos(lon), cos(lat)sin(lon), sin(lat)). See PlateRotationPayload docs.
        double cl = Math.Cos(latRad);
        var axis = new Vector3D(cl * Math.Cos(lonRad), cl * Math.Sin(lonRad), Math.Sin(latRad));
        double len = Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y + axis.Z * axis.Z);
        if (len < 1e-12)
            return Quaternion.Identity;
        return Quaternion.FromAxisAngle(new Vector3D(axis.X / len, axis.Y / len, axis.Z / len), angleRad);
    }

    private readonly record struct Keyframe(double TimeMa, Quaternion Rotation);
}
