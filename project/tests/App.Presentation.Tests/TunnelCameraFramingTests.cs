using FantaSim.App.Presentation.Tunnel;
using System.Numerics;
using Xunit;

namespace App.Presentation.Tests;

/// <summary>
/// Pins the INTERIOR occupant framing of design §4a (2026-07-12 amendment, user eye verdict):
/// near-axial camera at the mouth, the globe on the current-tick plane reading large at screen
/// center, both dial rings fully inside the frame. The geometry literals mirror the binder's
/// private tunables (GlobePlaneZ = -5, globe visual radius ~2 with relief/atmosphere, outer dial
/// radius 2.9 on RingPlaneZ = -3) — update them together with TunnelPresentationBinder.
/// </summary>
public sealed class TunnelCameraFramingTests
{
    private const double ExportAspect = 3840.0 / 1914.0;
    private const float GlobePlaneZ = -5.0f;
    private const float GlobeVisualRadius = 2.0f;
    private const float OuterDialRadius = 2.9f;
    private const float RingPlaneZ = -3.0f;

    [Fact]
    public void Default_framing_is_interior_axial_with_planet_large_at_center()
    {
        var position = TunnelCameraFraming.LocalPosition;
        var target = TunnelCameraFraming.LocalTarget;
        var forward = Vector3.Normalize(target - position);
        var obliquityDegrees = Math.Acos(Math.Abs(forward.Z)) * 180.0 / Math.PI;
        var axisOffset = Math.Sqrt(position.X * position.X + position.Y * position.Y);

        // Occupant, not spectator: on/near the tunnel axis at the mouth, looking down -Z.
        Assert.True(obliquityDegrees <= 10.0,
            $"Interior view must be near-axial; obliquity was {obliquityDegrees:F1} degrees.");
        Assert.True(axisOffset <= 1.5,
            $"Camera must sit on/near the tunnel axis; radial offset was {axisOffset:F2}.");
        Assert.True(position.Z <= 3.0,
            $"Camera must stand at the mouth, not outside as a spectator; Z was {position.Z:F1}.");

        // Planet large at the center: the globe disc is centered and spans a big share of the
        // frame height.
        var globeCenter = Project(position, target, new Vector3(0.0f, 0.0f, GlobePlaneZ));
        Assert.InRange(globeCenter.X, 0.40, 0.60);
        Assert.InRange(globeCenter.Y, 0.40, 0.60);

        var globeTop = Project(position, target, new Vector3(0.0f, GlobeVisualRadius, GlobePlaneZ));
        var globeBottom = Project(position, target, new Vector3(0.0f, -GlobeVisualRadius, GlobePlaneZ));
        var globeHeight = Math.Abs(globeBottom.Y - globeTop.Y);
        Assert.True(globeHeight >= 0.35,
            $"The planet must read LARGE at the center; projected height fraction was {globeHeight:F3}.");

        // Both dial rings render as full, nearly circular circles inside the frame.
        var bounds = ProjectRingBounds(position, target, OuterDialRadius, RingPlaneZ);
        Assert.True(bounds.MinY >= 0.0 && bounds.MaxY <= 1.0 && bounds.MinX >= 0.0 && bounds.MaxX <= 1.0,
            $"The outer dial must fit inside the frame; bounds were X [{bounds.MinX:F3}, {bounds.MaxX:F3}] Y [{bounds.MinY:F3}, {bounds.MaxY:F3}].");
        var projectedAspect = (bounds.MaxX - bounds.MinX) * ExportAspect
            / (bounds.MaxY - bounds.MinY);
        Assert.InRange(projectedAspect, 0.95, 1.05);
    }

    private static (double MinX, double MaxX, double MinY, double MaxY) ProjectRingBounds(
        Vector3 cameraPosition,
        Vector3 cameraTarget,
        float radius,
        float z)
    {
        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var minY = double.PositiveInfinity;
        var maxY = double.NegativeInfinity;

        for (var degree = 0; degree < 360; degree++)
        {
            var radians = degree * Math.PI / 180.0;
            var point = new Vector3((float)Math.Cos(radians) * radius, (float)Math.Sin(radians) * radius, z);
            var projected = Project(cameraPosition, cameraTarget, point);
            minX = Math.Min(minX, projected.X);
            maxX = Math.Max(maxX, projected.X);
            minY = Math.Min(minY, projected.Y);
            maxY = Math.Max(maxY, projected.Y);
        }

        return (minX, maxX, minY, maxY);
    }

    private static Vector2 Project(Vector3 cameraPosition, Vector3 cameraTarget, Vector3 point)
    {
        var forward = Vector3.Normalize(cameraTarget - cameraPosition);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        var offset = point - cameraPosition;
        var depth = Vector3.Dot(offset, forward);
        var verticalScale = Math.Tan(TunnelCameraFraming.FieldOfViewDegrees * Math.PI / 360.0);

        return new Vector2(
            (float)(0.5 + Vector3.Dot(offset, right) / (2.0 * depth * verticalScale * ExportAspect)),
            (float)(0.5 - Vector3.Dot(offset, up) / (2.0 * depth * verticalScale)));
    }
}
