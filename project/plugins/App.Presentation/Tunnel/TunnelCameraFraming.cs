using System;
using System.Numerics;

namespace FantaSim.App.Presentation.Tunnel;

internal readonly record struct TunnelProjectedPoint(double X, double Y, double Depth);

internal readonly record struct TunnelProjectedBounds(
    double MinX,
    double MaxX,
    double MinY,
    double MaxY)
{
    internal double Height => MaxY - MinY;
}

/// <summary>
/// Godot-free spatial contract for the asymmetric cockpit tunnel. Projection mirrors a perspective
/// Camera3D with KeepAspect = KeepHeight: FOV is vertical and landscape width grows with aspect.
/// Source:
/// https://docs.godotengine.org/en/4.7/tutorials/rendering/multiple_resolutions.html#field-of-view-scaling
/// </summary>
internal static class TunnelCameraFraming
{
    internal const float TunnelRadius = 5.0f;
    internal const float MouthZ = 0.0f;
    internal const float CurrentPlaneZ = -5.0f;
    internal const float ThroatZ = -20.0f;
    internal const float TimelineDepth = CurrentPlaneZ - ThroatZ;
    internal const float FieldOfViewDegrees = 60.0f;
    internal const float RadialClearance = 0.5f;
    internal const float PlanetClearance = 0.25f;
    internal const float NearClip = 0.05f;
    internal const float PlanetVisualRadius = 2.06f;

    // The approved -1.8 X seed put the real radius-4.94 focus anchor just outside X=0.12 at 16:10.
    // Moving camera and target together to -2.0 preserves the near-axial view and all clearances.
    internal static readonly Vector3 LocalPosition = new(-2.0f, 0.6f, -0.8f);
    internal static readonly Vector3 LocalTarget = new(-2.0f, 0.0f, -7.0f);

    internal static readonly Vector3 InstrumentLocalAnchor = new(-2.2f, 0.0f, -4.0f);
    internal const float InnerRingInnerRadius = 0.38f;
    internal const float InnerRingOuterRadius = 0.52f;
    internal const float OuterRingInnerRadius = 0.64f;
    internal const float OuterRingOuterRadius = 0.82f;

    internal static bool TryTickToZ(
        long requestedTick,
        long currentTick,
        long kbUnitTicks,
        out float z)
    {
        z = CurrentPlaneZ;
        if (kbUnitTicks <= 0 || requestedTick < currentTick)
            return false;

        var fraction = (requestedTick - (double)currentTick) / kbUnitTicks;
        if (fraction < 0.0 || fraction > 1.0)
            return false;

        z = CurrentPlaneZ - (float)(fraction * TimelineDepth);
        return true;
    }

    /// <summary>
    /// World-to-normalized-viewport projection equivalent to Camera3D.UnprojectPosition for the
    /// fixed perspective framing. Camera3D.ProjectPosition/ProjectRay* provide the inverse path.
    /// Sources:
    /// https://docs.godotengine.org/en/4.7/classes/class_camera3d.html#class-camera3d-method-unproject-position
    /// https://docs.godotengine.org/en/4.7/classes/class_projection.html#class-projection-method-create-perspective
    /// </summary>
    internal static TunnelProjectedPoint Project(Vector3 point, double aspect)
    {
        ValidateAspect(aspect);
        var (forward, right, up) = CameraBasis();
        var relative = point - LocalPosition;
        var depth = Vector3.Dot(relative, forward);
        if (depth <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(point), "Point must be in front of the tunnel camera.");

        var tanHalf = Math.Tan(FieldOfViewDegrees * Math.PI / 360.0);
        var x = 0.5 + Vector3.Dot(relative, right) / (2.0 * depth * tanHalf * aspect);
        var y = 0.5 - Vector3.Dot(relative, up) / (2.0 * depth * tanHalf);
        return new TunnelProjectedPoint(x, y, depth);
    }

    internal static TunnelProjectedPoint ProjectInstrumentCenter(double aspect)
    {
        ValidateAspect(aspect);
        var depth = -InstrumentLocalAnchor.Z;
        var tanHalf = Math.Tan(FieldOfViewDegrees * Math.PI / 360.0);
        return new TunnelProjectedPoint(
            0.5 + InstrumentLocalAnchor.X / (2.0 * depth * tanHalf * aspect),
            0.5 - InstrumentLocalAnchor.Y / (2.0 * depth * tanHalf),
            depth);
    }

    internal static TunnelProjectedBounds ProjectInstrumentRingBounds(float radius, double aspect)
    {
        ValidateAspect(aspect);
        if (radius <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(radius));

        var depth = -InstrumentLocalAnchor.Z;
        var tanHalf = Math.Tan(FieldOfViewDegrees * Math.PI / 360.0);
        return new TunnelProjectedBounds(
            0.5 + (InstrumentLocalAnchor.X - radius) / (2.0 * depth * tanHalf * aspect),
            0.5 + (InstrumentLocalAnchor.X + radius) / (2.0 * depth * tanHalf * aspect),
            0.5 - (InstrumentLocalAnchor.Y + radius) / (2.0 * depth * tanHalf),
            0.5 - (InstrumentLocalAnchor.Y - radius) / (2.0 * depth * tanHalf));
    }

    internal static TunnelProjectedBounds ProjectSphereBounds(
        Vector3 center,
        double radius,
        double aspect)
    {
        ValidateAspect(aspect);
        var (forward, right, up) = CameraBasis();
        var relative = center - LocalPosition;
        var depth = Vector3.Dot(relative, forward);
        var cameraX = Vector3.Dot(relative, right);
        var cameraY = Vector3.Dot(relative, up);
        var (minHorizontal, maxHorizontal) = TangentSlopes(cameraX, depth, radius);
        var (minVertical, maxVertical) = TangentSlopes(cameraY, depth, radius);
        var tanHalf = Math.Tan(FieldOfViewDegrees * Math.PI / 360.0);
        return new TunnelProjectedBounds(
            0.5 + minHorizontal / (2.0 * tanHalf * aspect),
            0.5 + maxHorizontal / (2.0 * tanHalf * aspect),
            0.5 - maxVertical / (2.0 * tanHalf),
            0.5 - minVertical / (2.0 * tanHalf));
    }

    private static (Vector3 Forward, Vector3 Right, Vector3 Up) CameraBasis()
    {
        var forward = Vector3.Normalize(LocalTarget - LocalPosition);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        return (forward, right, up);
    }

    private static (double Min, double Max) TangentSlopes(
        double axisOffset,
        double depth,
        double radius)
    {
        if (radius <= 0.0 || depth <= radius)
            throw new ArgumentOutOfRangeException(nameof(radius));

        var root = radius * Math.Sqrt(axisOffset * axisOffset + depth * depth - radius * radius);
        var denominator = depth * depth - radius * radius;
        return (
            (axisOffset * depth - root) / denominator,
            (axisOffset * depth + root) / denominator);
    }

    private static void ValidateAspect(double aspect)
    {
        if (!double.IsFinite(aspect) || aspect <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(aspect));
    }
}
