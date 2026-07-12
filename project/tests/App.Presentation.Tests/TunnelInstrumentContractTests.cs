using FantaSim.App.Presentation.Tunnel;
using Godot;
using System.IO;
using System.Runtime.CompilerServices;
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

    [Fact]
    public void Inspection_lens_is_a_distinct_stationary_sphere_beside_the_dials()
    {
        var lens = TunnelInspectionLensContract.Settings;

        Assert.Equal("inspection", lens.Label);
        Assert.InRange(lens.Radius, 0.40f, 0.60f);
        Assert.True(lens.LocalPosition.X > TunnelInstrumentContract.OuterRingOuterRadius);
        Assert.Equal(0f, lens.LocalPosition.Y);
        Assert.True(lens.LocalPosition.Z >= 0f);
    }

    [Fact]
    public void Production_lens_and_reset_path_consume_the_contract()
    {
        var rings = File.ReadAllText(ProjectFile(
            "project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Rings.cs"));
        var input = File.ReadAllText(ProjectFile(
            "project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Input.cs"));
        var binder = File.ReadAllText(ProjectFile(
            "project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.cs"));

        Assert.Contains("new SphereMesh", rings, StringComparison.Ordinal);
        Assert.Contains("new SnapshotSphereMaterial", rings, StringComparison.Ordinal);
        Assert.Contains("new SnapshotSphereFilmstripSink", rings, StringComparison.Ordinal);
        Assert.Contains("TunnelInspectionLensContract.Settings", rings, StringComparison.Ordinal);
        Assert.Contains("_filmstrip.CancelFineRequests();", input, StringComparison.Ordinal);
        Assert.Contains("ClearInspectionLens();", input, StringComparison.Ordinal);
        Assert.Contains("ApplyFineFrameEmphasis(inspectionActive: false);", input, StringComparison.Ordinal);
        var rebindStart = binder.IndexOf("public void Rebind()", StringComparison.Ordinal);
        Assert.True(rebindStart >= 0, "Tunnel binder Rebind entry point is missing.");
        var rebindCancel = binder.IndexOf(
            "CancelTunnelGesture(\"rebind\");",
            rebindStart,
            StringComparison.Ordinal);
        var rebindReset = binder.IndexOf(
            "ResetFinePreview(TunnelFineResetReason.BaseTimeChanged);",
            rebindStart,
            StringComparison.Ordinal);
        var controllerLookup = binder.IndexOf(
            "var controller = _registry.TryGet<ITimelineController>();",
            rebindStart,
            StringComparison.Ordinal);
        Assert.True(rebindCancel > rebindStart && rebindReset > rebindCancel && controllerLookup > rebindReset,
            "Rebind must cancel gesture ownership and released fine work before resolving lifecycle dependencies.");

        var runtimeGate = binder.IndexOf("TunnelRuntimeChangeThreadGate.Run(", StringComparison.Ordinal);
        var runtimeApply = binder.IndexOf(
            "applyOnMainThread: () => ApplyResourceRuntimeChangingOnMainThread(",
            runtimeGate,
            StringComparison.Ordinal);
        var runtimeApplyMethod = binder.IndexOf(
            "private void ApplyResourceRuntimeChangingOnMainThread(",
            runtimeApply,
            StringComparison.Ordinal);
        Assert.True(runtimeGate >= 0 && runtimeApply > runtimeGate && runtimeApplyMethod > runtimeApply,
            "Runtime-changing scene mutations must pass through the blocking main-thread gate.");

        var timelineBranch = binder.IndexOf("if (timelineChanging)", runtimeApplyMethod, StringComparison.Ordinal);
        Assert.True(timelineBranch >= 0, "Timeline runtime-changing branch is missing.");
        var timelineCancel = binder.IndexOf(
            "CancelTunnelGesture(\"timeline_reload\");",
            timelineBranch,
            StringComparison.Ordinal);
        var timelineReturn = binder.IndexOf("return;", timelineBranch, StringComparison.Ordinal);
        Assert.True(timelineCancel > timelineBranch && timelineReturn > timelineCancel,
            "Timeline reload must relinquish any owned gesture before preserving the tunnel mount.");
    }

    [Fact]
    public void Snapshot_spheres_use_true_luminance_desaturation_without_replacing_cached_textures()
    {
        var material = File.ReadAllText(ProjectFile(
            "project/plugins/App.Presentation/Tunnel/SnapshotSphereMaterial.cs"));
        var sink = File.ReadAllText(ProjectFile(
            "project/plugins/App.Presentation/Tunnel/SnapshotSphereFilmstripSink.cs"));

        Assert.Contains("uniform sampler2D albedo_texture : source_color", material, StringComparison.Ordinal);
        Assert.Contains("dot(color, vec3(0.2126, 0.7152, 0.0722))", material, StringComparison.Ordinal);
        Assert.Contains("mix(vec3(luminance), color, saturation)", material, StringComparison.Ordinal);
        Assert.Contains("SetTexture(frame.Texture)", sink, StringComparison.Ordinal);
        Assert.DoesNotContain("AlbedoColor", sink, StringComparison.Ordinal);
    }

    [Fact]
    public void Fine_lens_sink_rechecks_live_binder_and_graph_state_at_apply_time()
    {
        var input = File.ReadAllText(ProjectFile(
            "project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Input.cs"));

        Assert.Contains("private sealed class GuardedFineInspectionSink", input, StringComparison.Ordinal);
        Assert.Contains("TunnelFineApplyPolicy.CanApply", input, StringComparison.Ordinal);
        Assert.Contains("CurrentGraphRevision: currentGraphRevision.Value", input, StringComparison.Ordinal);
        Assert.Contains("new GuardedFineInspectionSink", input, StringComparison.Ordinal);
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

    private static string ProjectFile(
        string relativePath,
        [CallerFilePath] string testSourcePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testSourcePath)
            ?? throw new InvalidOperationException("Test source directory unavailable.");
        return Path.GetFullPath(Path.Combine(testDirectory, "..", "..", "..", relativePath));
    }
}
