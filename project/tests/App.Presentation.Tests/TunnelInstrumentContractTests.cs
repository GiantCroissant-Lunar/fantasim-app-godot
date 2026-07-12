using FantaSim.App.Presentation.Tunnel;
using Godot;
using Xunit;

namespace App.Presentation.Tests;

public sealed class TunnelInstrumentContractTests
{
    [Fact]
    public void Node_plan_places_rotating_roots_and_stationary_readouts_under_camera_local_instrument()
    {
        var plan = TunnelInstrumentContract.NodePlan;

        Assert.Collection(
            plan,
            node => AssertNode(node, "InstrumentRoot", "TunnelCamera"),
            node => AssertNode(node, "OuterRotationRoot", "InstrumentRoot"),
            node => AssertNode(node, "InnerRotationRoot", "InstrumentRoot"),
            node => AssertNode(node, "ReadoutRoot", "InstrumentRoot"),
            node => AssertNode(node, "OuterReadout", "ReadoutRoot"),
            node => AssertNode(node, "InnerReadout", "ReadoutRoot"),
            node => AssertNode(node, "StatusReadout", "ReadoutRoot"),
            node => AssertNode(node, "InspectionLensRoot", "ReadoutRoot"));

        Assert.Equal(0f, TunnelInstrumentContract.GeometryPlaneZ);
        Assert.Equal(TunnelCameraFraming.InstrumentLocalAnchor, TunnelInstrumentContract.LocalAnchor);
        Assert.Equal(TunnelCameraFraming.InnerRingInnerRadius, TunnelInstrumentContract.InnerRingInnerRadius);
        Assert.Equal(TunnelCameraFraming.InnerRingOuterRadius, TunnelInstrumentContract.InnerRingOuterRadius);
        Assert.Equal(TunnelCameraFraming.OuterRingInnerRadius, TunnelInstrumentContract.OuterRingInnerRadius);
        Assert.Equal(TunnelCameraFraming.OuterRingOuterRadius, TunnelInstrumentContract.OuterRingOuterRadius);
        Assert.True(TunnelInstrumentContract.IsExpectedParent("OuterReadout", "ReadoutRoot"));
        Assert.False(TunnelInstrumentContract.IsExpectedParent("OuterReadout", "OuterRotationRoot"));
        Assert.False(TunnelInstrumentContract.IsExpectedParent("InspectionLensRoot", "InnerRotationRoot"));
    }

    [Fact]
    public void Camera_settings_are_the_single_framing_contract_with_vertical_fov_lock()
    {
        var settings = TunnelCameraRuntimeContract.Settings;

        Assert.Equal(TunnelCameraFraming.LocalPosition, settings.LocalPosition);
        Assert.Equal(TunnelCameraFraming.LocalTarget, settings.LocalTarget);
        Assert.Equal(TunnelCameraFraming.FieldOfViewDegrees, settings.FieldOfViewDegrees);
        Assert.Equal(TunnelCameraFraming.NearClip, settings.NearClip);
        Assert.Equal(Camera3D.KeepAspectEnum.Height, settings.KeepAspect);
    }

    [Theory]
    [InlineData(0L, 0f)]
    [InlineData(250L, -90f)]
    [InlineData(1_000L, 0f)]
    [InlineData(2_250L, -90f)]
    public void Outer_rotation_is_derived_from_canonical_tick(long tick, float expectedDegrees)
    {
        Assert.Equal(
            expectedDegrees,
            TunnelInstrumentContract.CanonicalOuterRotationDegrees(tick, unitTicks: 1_000L),
            precision: 5);
    }

    [Theory]
    [InlineData(false, false, "No track")]
    [InlineData(true, false, "inactive at current time")]
    [InlineData(true, true, "active at current time")]
    public void Status_readout_distinguishes_empty_inactive_and_active(
        bool hasTrack,
        bool isActive,
        string expected)
    {
        Assert.Equal(expected, TunnelInstrumentContract.StatusText(hasTrack, isActive));
    }

    [Fact]
    public void Local_ray_intersects_the_same_zero_plane_used_by_instrument_geometry()
    {
        var success = TunnelInstrumentHitPolicy.TryIntersectPlane(
            new TunnelInstrumentPoint3(0.7, -0.1, -2.0),
            new TunnelInstrumentPoint3(0.7, -0.1, 2.0),
            out var point);

        Assert.True(success);
        Assert.Equal(0.7, point.X, precision: 8);
        Assert.Equal(-0.1, point.Y, precision: 8);
        Assert.Equal(TunnelInstrumentContract.GeometryPlaneZ, point.Z, precision: 8);
        Assert.True(TunnelInstrumentHitPolicy.IsInBand(
            point,
            TunnelInstrumentContract.OuterRingInnerRadius,
            TunnelInstrumentContract.OuterRingOuterRadius));
        Assert.False(TunnelInstrumentHitPolicy.IsInBand(
            point,
            TunnelInstrumentContract.InnerRingInnerRadius,
            TunnelInstrumentContract.InnerRingOuterRadius));
    }

    [Theory]
    [InlineData(0.0, 0.0, -1.0, 1.0, 0.0, -1.0)]
    [InlineData(0.0, 0.0, 1.0, 0.0, 0.0, 2.0)]
    public void Local_ray_rejects_parallel_and_behind_intersections(
        double originX,
        double originY,
        double originZ,
        double endX,
        double endY,
        double endZ)
    {
        Assert.False(TunnelInstrumentHitPolicy.TryIntersectPlane(
            new TunnelInstrumentPoint3(originX, originY, originZ),
            new TunnelInstrumentPoint3(endX, endY, endZ),
            out _));
    }

    private static void AssertNode(TunnelInstrumentNodePlan node, string name, string parentName)
    {
        Assert.Equal(name, node.Name);
        Assert.Equal(parentName, node.ParentName);
    }
}
