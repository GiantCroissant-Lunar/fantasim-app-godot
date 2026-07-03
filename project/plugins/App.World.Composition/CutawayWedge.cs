using System;
using UnifyMaths;

namespace FantaSim.App.World.Composition;

/// <summary>
/// A dihedral wedge through the planet center for the cutaway interaction (§5c, W3a). Defined by an
/// axis through the planet center (the dihedral hinge), a start azimuth, and an angular width measured
/// in the plane perpendicular to the axis. A unit direction is "contained" if its azimuth (the signed
/// angle from a reference direction in that perpendicular plane) falls within the wedge's angular span.
///
/// <para>This is the pure, Godot-free model that drives BOTH:</para>
/// <list type="bullet">
/// <item>The surface clip (the plate shader discards fragments whose position-in-direction is inside
/// the wedge — the planet reads as a solid with a wedge cut out, like a cut cake).</item>
/// <item>The cut faces (the two flat half-disc faces of the wedge, rendered as strata).</item>
/// </list>
///
/// <para>The containment test works as follows: given a unit direction <c>d</c> and the wedge axis
/// <c>axis</c>, project <c>d</c> onto the plane perpendicular to <c>axis</c>, then measure the signed
/// azimuth of that projection from a reference direction (chosen from a non-degenerate basis). The
/// direction is contained iff its azimuth falls in the half-open interval
/// <c>[start, start + width)</c> (mod 360). Points along the axis itself (projection near zero) are
/// never contained — there is no azimuth to test. Width 0 = inactive (never contains). Width 360 =
/// full disc minus the axis. Wrap-around (start + width > 360) is handled by testing both
/// <c>[start, 360)</c> and <c>[0, (start+width) mod 360)</c>.</para>
///
/// <para>All angles are in degrees; azimuths are measured clockwise from the reference direction when
/// looking along the negative axis (right-handed convention, matching the shader's discard test).</para>
/// </summary>
public readonly struct CutawayWedge
{
    private const double TwoPi = 2.0 * Math.PI;
    private const double DegToRad = Math.PI / 180.0;

    private readonly Vector3D _axis;          // unit-length
    private readonly Vector3D _reference;      // unit-length, perpendicular to axis
    private readonly Vector3D _referenceCross; // axis x reference (the third basis vector)
    private readonly double _startRad;
    private readonly double _widthRad;

    /// <summary>
    /// Constructs a dihedral wedge. The axis is normalized; width is clamped to [0, 360].
    /// Negative width is treated as 0 (inactive). The reference direction is derived from the axis
    /// via a stable basis selection (so the azimuth origin is deterministic, not arbitrary).
    /// </summary>
    public CutawayWedge(Vector3D axis, double startAzimuthDeg, double widthDeg)
    {
        _axis = NormalizeOrZero(axis);

        if (widthDeg < 0.0) widthDeg = 0.0;
        if (widthDeg > 360.0) widthDeg = 360.0;

        _startRad = NormalizeAngle(startAzimuthDeg * DegToRad);
        _widthRad = widthDeg * DegToRad;

        // Build a stable orthonormal basis: pick a reference direction perpendicular to the axis.
        // If |z| < 0.9, cross with the z-basis; else cross with the y-basis. This mirrors the
        // FallbackPoleAxis pattern in OnsetRoster — deterministic and non-degenerate.
        // Right-handed basis: reference = seed × axis (so for axis=+Z, seed=(0,1,0), reference=+X).
        var seed = Math.Abs(_axis.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(0, 1, 0);
        _reference = NormalizeOrZero(Cross(seed, _axis));
        _referenceCross = Cross(_axis, _reference);

        NormalizedStart = NormalizeAngle(startAzimuthDeg * DegToRad) / DegToRad;
        EndAzimuthDeg = (_startRad + _widthRad) / DegToRad;
    }

    /// <summary>The normalized (unit) wedge axis through the planet center.</summary>
    public Vector3D Axis => _axis;

    /// <summary>The unit reference direction from which azimuths are measured (perpendicular to axis).</summary>
    public Vector3D Reference => _reference;

    /// <summary>Start azimuth in degrees, normalized to [0, 360).</summary>
    public double NormalizedStart { get; }

    /// <summary>End azimuth in degrees (= start + width, in [0, 720); not wrapped so wrap-around is detectable).</summary>
    public double EndAzimuthDeg { get; }

    /// <summary>Angular width in degrees, clamped to [0, 360].</summary>
    public double WidthDeg => _widthRad / DegToRad;

    /// <summary>True when the wedge is inactive (width 0) — no direction is ever contained.</summary>
    public bool IsInactive => _widthRad <= 0.0;

    /// <summary>
    /// Tests whether a unit direction falls inside the wedge. Non-unit directions are projected
    /// onto the perpendicular plane; the projection's azimuth determines containment. Directions
    /// along the axis (projection near zero) are never contained.
    /// </summary>
    public bool Contains(Vector3D direction)
    {
        if (IsInactive)
            return false;

        // Project onto the perpendicular plane: subtract the axial component.
        var proj = ProjectToPerpendicularPlane(direction);
        var projLen = Length(proj);
        if (projLen < 1e-12)
            return false; // along axis — no azimuth

        var unit = new Vector3D(proj.X / projLen, proj.Y / projLen, proj.Z / projLen);

        // Azimuth = atan2(unit . referenceCross, unit . reference). atan2(y, x) gives the signed
        // angle from the +x axis (reference) toward +y (referenceCross) in [0, 2π).
        var x = Dot(unit, _reference);
        var y = Dot(unit, _referenceCross);
        var azimuth = Math.Atan2(y, x);
        if (azimuth < 0.0)
            azimuth += TwoPi; // normalize to [0, 2π)

        return AzimuthInRange(azimuth);
    }

    /// <summary>
    /// True iff <paramref name="azimuth"/> (radians, [0, 2π)) falls in [start, start+width) modulo 2π.
    /// Half-open: the start is inclusive, the end is exclusive (so adjacent wedges tile without overlap).
    /// </summary>
    private bool AzimuthInRange(double azimuth)
    {
        var end = _startRad + _widthRad;

        if (end <= TwoPi)
        {
            // Simple (non-wrapping) interval.
            return azimuth >= _startRad && azimuth < end;
        }

        // Wraps past 2π: [start, 2π) ∪ [0, end - 2π).
        var wrappedEnd = end - TwoPi;
        return azimuth >= _startRad || azimuth < wrappedEnd;
    }

    private Vector3D ProjectToPerpendicularPlane(Vector3D v)
    {
        // v - (v.axis) * axis, leaving only the perpendicular component.
        var dot = Dot(v, _axis);
        return new Vector3D(
            v.X - dot * _axis.X,
            v.Y - dot * _axis.Y,
            v.Z - dot * _axis.Z);
    }

    private static double NormalizeAngle(double rad)
    {
        rad %= TwoPi;
        if (rad < 0.0) rad += TwoPi;
        return rad;
    }

    private static Vector3D NormalizeOrZero(Vector3D v)
    {
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return len < 1e-15 ? new Vector3D(0, 0, 0) : new Vector3D(v.X / len, v.Y / len, v.Z / len);
    }

    private static Vector3D Cross(Vector3D a, Vector3D b)
        => new(
            (a.Y * b.Z) - (a.Z * b.Y),
            (a.Z * b.X) - (a.X * b.Z),
            (a.X * b.Y) - (a.Y * b.X));

    private static double Dot(Vector3D a, Vector3D b)
        => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static double Length(Vector3D v)
        => Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
}