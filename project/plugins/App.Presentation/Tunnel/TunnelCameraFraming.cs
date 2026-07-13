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
    // Slice-1 Part B pulls the camera back from the mouth so the axis-concentric rings read as a
    // full pair encircling the tunnel (vs the prior inside-the-mouth planet-large framing). Widened
    // FOV frames both rings; the exact pull-back / FOV are the user's eye-tune.
    internal const float FieldOfViewDegrees = 74.0f;
    internal const float RadialClearance = 0.5f;
    internal const float PlanetClearance = 0.25f;
    internal const float NearClip = 0.05f;
    internal const float PlanetVisualRadius = 2.06f;

    // The physical MouthZ plane is necessarily behind every near-axial interior camera and cannot
    // enter a 60-degree widescreen frustum. These three disconnected points form an honest shell-
    // attached near-interior lip instead: no annulus, hit region, or claim that Z=-4.5 is MouthZ.
    internal const float NearInteriorLipZ = -4.5f;
    internal const int NearInteriorLipCueCount = 3;

    // Part B framing (round 2, user eye-tune): rings shrunk ~25% and the camera pulled part-way
    // back in so the planet and corridors read larger while the ring pair still fully encircles the
    // throat. Slight -X asymmetry retained from the cockpit look. Candidates for further tuning:
    // pull-in Z, FOV, ring radii.
    internal static readonly Vector3 LocalPosition = new(-0.25f, 0.25f, 2.9f);
    internal static readonly Vector3 LocalTarget = new(0.0f, 0.0f, -6.0f);

    // The instrument is parented to the mount (not the camera) and centered on the tunnel axis, so
    // the rings encircle the throat instead of sitting as a corner dial. Anchor Z sits in front of
    // the camera; radii ring the planet (visual radius ~2.06) — the ring plane is nearer the camera
    // than the planet, so it projects larger even at a smaller raw radius.
    internal static readonly Vector3 InstrumentLocalAnchor = new(0.0f, 0.0f, -1.0f);
    internal const float InnerRingInnerRadius = 1.70f;
    internal const float InnerRingOuterRadius = 1.90f;
    internal const float OuterRingInnerRadius = 2.20f;
    internal const float OuterRingOuterRadius = 2.45f;

    internal static Vector3 NearInteriorLipCuePoint(int index)
    {
        var angleDegrees = index switch
        {
            0 => 160.0f,
            1 => 180.0f,
            2 => 200.0f,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
        var angle = angleDegrees * MathF.PI / 180.0f;
        return new Vector3(
            MathF.Cos(angle) * TunnelRadius,
            MathF.Sin(angle) * TunnelRadius,
            NearInteriorLipZ);
    }

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
        return Project(InstrumentLocalAnchor, aspect);
    }

    internal static TunnelProjectedBounds ProjectInstrumentRingBounds(float radius, double aspect)
    {
        ValidateAspect(aspect);
        if (radius <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(radius));

        var anchor = InstrumentLocalAnchor;
        var tl = Project(new Vector3(anchor.X - radius, anchor.Y + radius, anchor.Z), aspect);
        var tr = Project(new Vector3(anchor.X + radius, anchor.Y + radius, anchor.Z), aspect);
        var bl = Project(new Vector3(anchor.X - radius, anchor.Y - radius, anchor.Z), aspect);
        var br = Project(new Vector3(anchor.X + radius, anchor.Y - radius, anchor.Z), aspect);
        return new TunnelProjectedBounds(
            Math.Min(Math.Min(tl.X, tr.X), Math.Min(bl.X, br.X)),
            Math.Max(Math.Max(tl.X, tr.X), Math.Max(bl.X, br.X)),
            Math.Min(Math.Min(tl.Y, bl.Y), Math.Min(tr.Y, br.Y)),
            Math.Max(Math.Max(tl.Y, bl.Y), Math.Max(tr.Y, br.Y)));
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

    /// <summary>
    /// Returns true when the projected point is inside the inclusive [min,max] safe viewport
    /// rectangle. Used by layout tests to prove readout/header positions stay in-bounds at common
    /// aspects without relying on a live Godot viewport.
    /// </summary>
    internal static bool IsInSafeViewport(TunnelProjectedPoint point, double min, double max)
        => double.IsFinite(point.X)
            && double.IsFinite(point.Y)
            && point.X >= min
            && point.X <= max
            && point.Y >= min
            && point.Y <= max;

    private static void ValidateAspect(double aspect)
    {
        if (!double.IsFinite(aspect) || aspect <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(aspect));
    }
}
