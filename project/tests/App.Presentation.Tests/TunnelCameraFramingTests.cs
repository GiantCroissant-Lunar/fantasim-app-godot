using FantaSim.App.Presentation.Tunnel;
using System.Numerics;
using Xunit;

namespace App.Presentation.Tests;

public sealed class TunnelCameraFramingTests
{
    private const double ExportAspect = 3840.0 / 1914.0;

    [Fact]
    public void Default_framing_is_oblique_and_keeps_the_mouth_above_the_timeline_hud()
    {
        var position = TunnelCameraFraming.LocalPosition;
        var target = TunnelCameraFraming.LocalTarget;
        var forward = Vector3.Normalize(target - position);
        var obliquityDegrees = Math.Acos(Math.Abs(forward.Z)) * 180.0 / Math.PI;

        var bounds = ProjectRingBounds(position, target, radius: 10.0f, z: 0.0f);
        var projectedAspect = (bounds.MaxX - bounds.MinX) * ExportAspect
            / (bounds.MaxY - bounds.MinY);
        var mouthCenter = Project(position, target, Vector3.Zero);
        var throatCenter = Project(position, target, new Vector3(0.0f, 0.0f, -14.0f));
        var depthSeparation = Vector2.Distance(mouthCenter, throatCenter);

        Assert.InRange(obliquityDegrees, 20.0, 30.0);
        Assert.InRange(projectedAspect, 0.90, 0.97);
        Assert.True(bounds.MaxY <= 0.62,
            $"The mouth bottom must remain above the HUD evidence band; projected Y was {bounds.MaxY:F3}.");
        Assert.True(depthSeparation >= 0.055,
            $"Mouth and throat centers must visibly separate; projected distance was {depthSeparation:F3}.");
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
