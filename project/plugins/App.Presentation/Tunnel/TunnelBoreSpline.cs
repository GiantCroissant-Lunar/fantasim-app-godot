using System;
using UnifyMaths;

namespace FantaSim.App.Presentation.Tunnel;

/// <summary>
/// Shared constants for the bent bore. StraightRadius covers the interactive near-field (the
/// first two corridor depth bands: 2 x TimelineDepth/4 = 7.5 units), so every shipped input
/// path operates on exactly the straight geometry it was written against.
/// </summary>
internal static class TunnelBoreContract
{
    internal const double StraightRadius = 7.5;
    internal const double CurvatureCapRadPerUnit = 0.12;
    internal const double RampLength = 1.5;
    internal const double MaxSegmentLength = 1.25;

    internal static float InteractiveThroatZ(float currentPlaneZ)
        => currentPlaneZ - (float)StraightRadius;
}

internal readonly record struct TunnelBoreFrame(
    Vector3D Position,
    Vector3D Forward,
    Vector3D Right,
    Vector3D Up);

/// <summary>
/// Deterministic bent-bore centerline. Depth is measured from the current-tick plane (0) toward
/// the throat; positions are relative to the current plane (straight bore: (0,0,-depth)). The
/// near-field is exactly straight through StraightRadius; beyond it, curvature ramps in
/// C1-continuously (smoothstep over RampLength), is capped, and is integrated with
/// parallel-transported frames so no roll accumulates. Everything is a pure function of the
/// seed — no runtime randomness, no wall-clock.
/// </summary>
internal sealed class TunnelBoreSpline
{
    private const double Step = 0.05;
    private readonly double _straightRadius;
    private readonly double _maxDepth;
    private readonly TunnelBoreFrame[] _samples; // index i = depth straightRadius + i*Step

    private TunnelBoreSpline(double straightRadius, double maxDepth, TunnelBoreFrame[] samples)
    {
        _straightRadius = straightRadius;
        _maxDepth = maxDepth;
        _samples = samples;
    }

    internal static TunnelBoreSpline Create(
        long seed,
        double straightRadius,
        double curvatureCapRadPerUnit,
        double maxDepth)
    {
        if (!double.IsFinite(straightRadius) || straightRadius < 0.0)
            throw new ArgumentOutOfRangeException(nameof(straightRadius));
        if (!double.IsFinite(curvatureCapRadPerUnit) || curvatureCapRadPerUnit < 0.0)
            throw new ArgumentOutOfRangeException(nameof(curvatureCapRadPerUnit));
        if (!double.IsFinite(maxDepth) || maxDepth <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(maxDepth));

        // SplitMix64: deterministic phase/frequency derivation from the seed.
        ulong state = unchecked((ulong)seed);
        double NextUnit()
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) * (1.0 / (1UL << 53));
        }

        double yawFreq = 0.03 + (0.03 * NextUnit());   // cycles per depth unit
        double pitchFreq = 0.03 + (0.03 * NextUnit());
        double yawPhase = NextUnit();
        double pitchPhase = NextUnit();

        var curvedSpan = Math.Max(0.0, maxDepth - straightRadius);
        var count = (int)Math.Ceiling(curvedSpan / Step) + 1;
        var samples = new TunnelBoreFrame[Math.Max(count, 1)];

        var position = new Vector3D(0.0, 0.0, -straightRadius);
        var forward = new Vector3D(0.0, 0.0, -1.0);
        var right = new Vector3D(1.0, 0.0, 0.0);
        var up = new Vector3D(0.0, 1.0, 0.0);
        samples[0] = new TunnelBoreFrame(position, forward, right, up);

        // The two sinusoids share the cap budget: |(yawRate, pitchRate)| <= cap.
        var amplitude = curvatureCapRadPerUnit / Math.Sqrt(2.0);

        for (var i = 1; i < samples.Length; i++)
        {
            var depth = straightRadius + ((i - 1) * Step);
            var t = Math.Clamp((depth - straightRadius) / TunnelBoreContract.RampLength, 0.0, 1.0);
            var ramp = t * t * (3.0 - (2.0 * t)); // smoothstep => C1 at the boundary

            var yawRate = amplitude * ramp
                * Math.Sin(2.0 * Math.PI * ((yawFreq * depth) + yawPhase));
            var pitchRate = amplitude * ramp
                * Math.Sin(2.0 * Math.PI * ((pitchFreq * depth) + pitchPhase));

            // Parallel transport: rotate the WHOLE frame by the incremental turn. Rotating
            // right/up with the same rotations that turn forward injects zero roll.
            var yaw = Quaternion.FromAxisAngle(up, yawRate * Step);
            var pitch = Quaternion.FromAxisAngle(right, pitchRate * Step);

            forward = pitch.Rotate(yaw.Rotate(forward)).Normalize();
            right = pitch.Rotate(yaw.Rotate(right)).Normalize();
            up = pitch.Rotate(yaw.Rotate(up)).Normalize();

            position = new Vector3D(
                position.X + (forward.X * Step),
                position.Y + (forward.Y * Step),
                position.Z + (forward.Z * Step));

            samples[i] = new TunnelBoreFrame(position, forward, right, up);
        }

        return new TunnelBoreSpline(straightRadius, maxDepth, samples);
    }

    internal TunnelBoreFrame Evaluate(double depth)
    {
        if (!double.IsFinite(depth))
            depth = 0.0;
        depth = Math.Clamp(depth, 0.0, _maxDepth);

        if (depth <= _straightRadius)
        {
            return new TunnelBoreFrame(
                new Vector3D(0.0, 0.0, -depth),
                new Vector3D(0.0, 0.0, -1.0),
                new Vector3D(1.0, 0.0, 0.0),
                new Vector3D(0.0, 1.0, 0.0));
        }

        var offset = (depth - _straightRadius) / Step;
        var index = (int)Math.Floor(offset);
        if (index >= _samples.Length - 1)
            return _samples[^1];

        var f = offset - index;
        var a = _samples[index];
        var b = _samples[index + 1];
        return new TunnelBoreFrame(
            Lerp(a.Position, b.Position, f),
            NLerp(a.Forward, b.Forward, f),
            NLerp(a.Right, b.Right, f),
            NLerp(a.Up, b.Up, f));
    }

    private static Vector3D Lerp(Vector3D a, Vector3D b, double t)
        => new(
            a.X + ((b.X - a.X) * t),
            a.Y + ((b.Y - a.Y) * t),
            a.Z + ((b.Z - a.Z) * t));

    private static Vector3D NLerp(Vector3D a, Vector3D b, double t)
        => Lerp(a, b, t).Normalize();
}
