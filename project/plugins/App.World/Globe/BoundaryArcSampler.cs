using System;
using System.Collections.Generic;
using FantaSim.App.World.Dto;
using FantaSim.Geosphere.Plate.Topology;
using UnifyGeometry.Spherical;
using UnifyMaths;

namespace FantaSim.App.World.Globe;

/// <summary>
/// Godot-free helpers that turn plate-topology truth into render-ready boundary data:
/// great-circle subdivision of an ordered sample pair, the topology→contract kind mapping, and
/// boundary-set diffing over ticks. All sphere math uses Unify primitives (Quaternion/Vector3D);
/// no hand-rolled slerp.
/// </summary>
public static class BoundaryArcSampler
{
    /// <summary>
    /// Subdivides the short great-circle arc between <paramref name="a"/> and <paramref name="b"/>
    /// into <c>subdiv + 1</c> ordered unit-sphere points (endpoints inclusive). <paramref name="subdiv"/>
    /// is clamped to at least 1. Collinear pairs (parallel/antipodal, where the rotation axis is
    /// undefined) fall back to rotation about a deterministic perpendicular axis so the result stays
    /// unit-length and finite.
    /// </summary>
    public static IReadOnlyList<GlobeVec3> SubdivideGreatCircle(SphericalPoint a, SphericalPoint b, int subdiv)
    {
        int steps = Math.Max(1, subdiv);
        var u0 = a.ToVector3D();
        var u1 = b.ToVector3D();

        var result = new GlobeVec3[steps + 1];
        result[0] = ToGlobeVec(u0);

        if (steps == 1)
        {
            result[1] = ToGlobeVec(u1);
            return result;
        }

        var axis = Vector3D.Cross(u0, u1);
        double axisLen = axis.Length();
        double dot = Math.Clamp(Vector3D.Dot(u0, u1), -1.0, 1.0);
        double angle = Math.Acos(dot);

        // Collinear (parallel or antipodal): the natural cross-product axis is undefined. Pick a
        // deterministic axis perpendicular to u0 so the arc traces a real great circle instead of
        // collapsing (linear interp would zero out at the midpoint of antipodal points).
        var rotationAxis = axisLen < 1e-12 ? PerpendicularAxis(u0) : axis * (1.0 / axisLen);

        for (int i = 1; i < steps; i++)
        {
            double t = (double)i / steps;
            var q = Quaternion.FromAxisAngle(rotationAxis, angle * t);
            result[i] = ToGlobeVec(NormalizeOrZero(q.Rotate(u0)));
        }

        result[steps] = ToGlobeVec(u1);
        return result;
    }

    /// <summary>Maps the topology engine's <see cref="BoundaryType"/> onto the contract-side kind.</summary>
    public static PlateBoundaryKind MapKind(BoundaryType type) => type switch
    {
        BoundaryType.Convergent => PlateBoundaryKind.Convergent,
        BoundaryType.Divergent => PlateBoundaryKind.Divergent,
        BoundaryType.Transform => PlateBoundaryKind.Transform,
        _ => PlateBoundaryKind.Inactive,
    };

    /// <summary>
    /// Diffs two boundary-arc sets (keyed by the unordered plate pair) into added / retired / retyped.
    /// Used to describe how boundaries appear, disappear, and change type as the playhead advances.
    /// </summary>
    public static BoundarySetDiff DiffBoundaries(
        IReadOnlyList<PlateBoundaryArc> previous,
        IReadOnlyList<PlateBoundaryArc> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var prevByKey = new Dictionary<(int A, int B), PlateBoundaryArc>(previous.Count);
        foreach (var arc in previous) prevByKey[KeyOf(arc)] = arc;

        var curByKey = new Dictionary<(int A, int B), PlateBoundaryArc>(current.Count);
        foreach (var arc in current) curByKey[KeyOf(arc)] = arc;

        var added = new List<PlateBoundaryArc>();
        var retired = new List<PlateBoundaryArc>();
        var retyped = new List<BoundaryTypeChange>();

        foreach (var (key, curArc) in curByKey)
        {
            if (!prevByKey.TryGetValue(key, out var prevArc))
                added.Add(curArc);
            else if (prevArc.Kind != curArc.Kind)
                retyped.Add(new BoundaryTypeChange(key.A, key.B, prevArc.Kind, curArc.Kind));
        }

        foreach (var (key, prevArc) in prevByKey)
        {
            if (!curByKey.ContainsKey(key))
                retired.Add(prevArc);
        }

        return new BoundarySetDiff(added, retired, retyped);
    }

    private static (int A, int B) KeyOf(PlateBoundaryArc arc)
        => arc.PlateA <= arc.PlateB ? (arc.PlateA, arc.PlateB) : (arc.PlateB, arc.PlateA);

    private static GlobeVec3 ToGlobeVec(Vector3D v)
        => new((float)v.X, (float)v.Y, (float)v.Z);

    private static Vector3D NormalizeOrZero(Vector3D v)
    {
        double len = v.Length();
        return len < 1e-15 ? new Vector3D(0, 0, 0) : v * (1.0 / len);
    }

    private static Vector3D PerpendicularAxis(Vector3D v)
    {
        var reference = Math.Abs(v.X) < 0.9 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
        var perp = Vector3D.Cross(v, reference);
        double len = perp.Length();
        return len < 1e-15 ? new Vector3D(0, 0, 1) : perp * (1.0 / len);
    }
}

/// <summary>Result of diffing two boundary-arc sets over a tick transition.</summary>
public sealed record BoundarySetDiff(
    IReadOnlyList<PlateBoundaryArc> Added,
    IReadOnlyList<PlateBoundaryArc> Retired,
    IReadOnlyList<BoundaryTypeChange> Retyped);

/// <summary>One boundary that changed kind between two ticks (plate pair unchanged).</summary>
public sealed record BoundaryTypeChange(
    int PlateA,
    int PlateB,
    PlateBoundaryKind OldKind,
    PlateBoundaryKind NewKind);
