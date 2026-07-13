using System;
using System.Collections.Generic;

namespace FantaSim.App.Presentation.Tunnel;

internal readonly record struct TunnelBoreSegment(
    double MidDepth,
    double HalfLength,
    TunnelBoreFrame Frame);

/// <summary>
/// Chops a depth band into rigid chord segments placed on the bore spline. The straight
/// portion (depth <= the spline's straight radius) is emitted as one exact segment; the curved
/// remainder is subdivided so each chord is at most maxSegmentLength long, which keeps the
/// polyline approximation visually smooth at the capped curvature.
/// </summary>
internal static class TunnelBoreSegments
{
    internal static IReadOnlyList<TunnelBoreSegment> Plan(
        TunnelBoreSpline spline,
        double nearDepth,
        double farDepth,
        double maxSegmentLength)
    {
        ArgumentNullException.ThrowIfNull(spline);
        if (!double.IsFinite(nearDepth) || !double.IsFinite(farDepth)
            || !double.IsFinite(maxSegmentLength)
            || maxSegmentLength <= 0.0
            || farDepth <= nearDepth)
        {
            return Array.Empty<TunnelBoreSegment>();
        }

        var result = new List<TunnelBoreSegment>();
        var straight = TunnelBoreContract.StraightRadius;

        if (nearDepth < straight)
        {
            var straightFar = Math.Min(farDepth, straight);
            var mid = (nearDepth + straightFar) / 2.0;
            result.Add(new TunnelBoreSegment(mid, (straightFar - nearDepth) / 2.0, spline.Evaluate(mid)));
            nearDepth = straightFar;
        }

        if (farDepth <= nearDepth)
            return result;

        var span = farDepth - nearDepth;
        var count = (int)Math.Ceiling(span / maxSegmentLength);
        var length = span / count;
        for (var i = 0; i < count; i++)
        {
            var mid = nearDepth + (length * (i + 0.5));
            result.Add(new TunnelBoreSegment(mid, length / 2.0, spline.Evaluate(mid)));
        }

        return result;
    }
}
