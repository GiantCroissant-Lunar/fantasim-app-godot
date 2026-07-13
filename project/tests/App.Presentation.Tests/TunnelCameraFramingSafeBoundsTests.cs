using System;
using System.Numerics;
using FantaSim.App.Presentation.Tunnel;
using FantaSim.App.Timeline.Seam;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

/// <summary>
/// Headless coverage for the tunnel camera framing contract beyond the four ring points. The
/// readout stack and corridor header lane must project inside a conservative viewport safe area
/// at both 16:9 and 16:10 so live captures never clip text outside the viewport.
/// </summary>
public sealed class TunnelCameraFramingSafeBoundsTests
{
    private const double SafeMin = 0.06;
    private const double SafeMax = 0.94;

    [Theory]
    [InlineData(16.0 / 9.0)]
    [InlineData(16.0 / 10.0)]
    public void ReadoutStack_FitsInsideSafeArea(double aspect)
    {
        // Mirror the planned readout positions: outer at +Y, inner at -Y, status beneath inner.
        var outer = TunnelCameraFraming.Project(
            TunnelCameraFraming.InstrumentLocalAnchor
            + new Vector3(0f, TunnelInstrumentContract.OuterReadoutYOffset, 0f),
            aspect);
        var inner = TunnelCameraFraming.Project(
            TunnelCameraFraming.InstrumentLocalAnchor
            + new Vector3(0f, TunnelInstrumentContract.InnerReadoutYOffset, 0f),
            aspect);
        var status = TunnelCameraFraming.Project(
            TunnelCameraFraming.InstrumentLocalAnchor
            + new Vector3(0f, TunnelInstrumentContract.StatusReadoutYOffset, 0f),
            aspect);

        Assert.True(TunnelCameraFraming.IsInSafeViewport(outer, SafeMin, SafeMax),
            $"outer readout at {outer.X:F3},{outer.Y:F3} escapes safe area at aspect {aspect:F2}");
        Assert.True(TunnelCameraFraming.IsInSafeViewport(inner, SafeMin, SafeMax),
            $"inner readout at {inner.X:F3},{inner.Y:F3} escapes safe area at aspect {aspect:F2}");
        Assert.True(TunnelCameraFraming.IsInSafeViewport(status, SafeMin, SafeMax),
            $"status readout at {status.X:F3},{status.Y:F3} escapes safe area at aspect {aspect:F2}");
    }

    [Theory]
    [InlineData(16.0 / 9.0)]
    [InlineData(16.0 / 10.0)]
    public void HeaderLane_FitsInsideSafeArea(double aspect)
    {
        // Headers sit in the foreground lane at the current-plane Z, radially near the tunnel wall.
        var headerPos = new Vector3(
            TunnelCameraFraming.TunnelRadius - 0.3f,
            0f,
            TunnelCameraFraming.CurrentPlaneZ);
        var projected = TunnelCameraFraming.Project(headerPos, aspect);

        Assert.True(TunnelCameraFraming.IsInSafeViewport(projected, SafeMin, SafeMax),
            $"header lane at {projected.X:F3},{projected.Y:F3} escapes safe area at aspect {aspect:F2}");
    }

    [Theory]
    [InlineData(16.0 / 10.0)]
    public void RingBounds_FitInsideViewport(double aspect)
    {
        var outerBounds = TunnelCameraFraming.ProjectInstrumentRingBounds(
            TunnelCameraFraming.OuterRingOuterRadius, aspect);

        Assert.InRange(outerBounds.MinX, 0.0, 1.0);
        Assert.InRange(outerBounds.MaxX, 0.0, 1.0);
        Assert.InRange(outerBounds.MinY, 0.0, 1.0);
        Assert.InRange(outerBounds.MaxY, 0.0, 1.0);
    }

    [Fact]
    public void Ring_Widths_Are_Locked_To_008_And_010()
    {
        Assert.Equal(0.08f,
            TunnelCameraFraming.InnerRingOuterRadius - TunnelCameraFraming.InnerRingInnerRadius,
            precision: 5);
        Assert.Equal(0.10f,
            TunnelCameraFraming.OuterRingOuterRadius - TunnelCameraFraming.OuterRingInnerRadius,
            precision: 5);
    }

    [Theory]
    [InlineData(4.0 / 3.0)]
    [InlineData(16.0 / 9.0)]
    [InlineData(21.0 / 9.0)]
    public void Default_Planet_Exceeds_Inner_Aperture_By_At_Least_5_Percent(double aspect)
    {
        var planetCenter = new Vector3(0f, 0f, TunnelCameraFraming.CurrentPlaneZ);
        var planetRadius = TunnelCameraFraming.PlanetVisualRadius * TunnelPlanetZoom.DefaultScale;

        var planetBounds = TunnelCameraFraming.ProjectSphereBounds(planetCenter, planetRadius, aspect);

        var apertureBounds = TunnelCameraFraming.ProjectInstrumentRingBounds(
            TunnelCameraFraming.InnerRingInnerRadius, aspect);

        Assert.True(
            planetBounds.Height >= 1.05 * apertureBounds.Height,
            $"planet projected HEIGHT {planetBounds.Height:F4} must exceed inner aperture {apertureBounds.Height:F4} by >= 5% at aspect {aspect:F2}");

        Assert.True(
            planetBounds.MaxX - planetBounds.MinX >= 1.05 * (apertureBounds.MaxX - apertureBounds.MinX),
            $"planet projected WIDTH {planetBounds.MaxX - planetBounds.MinX:F4} must exceed inner aperture {apertureBounds.MaxX - apertureBounds.MinX:F4} by >= 5% at aspect {aspect:F2}");
    }

    [Theory]
    [InlineData(4.0 / 3.0)]
    [InlineData(16.0 / 9.0)]
    [InlineData(21.0 / 9.0)]
    public void Readouts_And_Rings_Stay_In_Safe_Viewport(double aspect)
    {
        var outer = TunnelCameraFraming.Project(
            TunnelCameraFraming.InstrumentLocalAnchor
            + new Vector3(0f, TunnelInstrumentContract.OuterReadoutYOffset, 0f),
            aspect);
        var inner = TunnelCameraFraming.Project(
            TunnelCameraFraming.InstrumentLocalAnchor
            + new Vector3(0f, TunnelInstrumentContract.InnerReadoutYOffset, 0f),
            aspect);
        var status = TunnelCameraFraming.Project(
            TunnelCameraFraming.InstrumentLocalAnchor
            + new Vector3(0f, TunnelInstrumentContract.StatusReadoutYOffset, 0f),
            aspect);

        Assert.True(TunnelCameraFraming.IsInSafeViewport(outer, SafeMin, SafeMax),
            $"outer readout at {outer.X:F3},{outer.Y:F3} escapes safe area at aspect {aspect:F2}");
        Assert.True(TunnelCameraFraming.IsInSafeViewport(inner, SafeMin, SafeMax),
            $"inner readout at {inner.X:F3},{inner.Y:F3} escapes safe area at aspect {aspect:F2}");
        Assert.True(TunnelCameraFraming.IsInSafeViewport(status, SafeMin, SafeMax),
            $"status readout at {status.X:F3},{status.Y:F3} escapes safe area at aspect {aspect:F2}");

        var ringBounds = TunnelCameraFraming.ProjectInstrumentRingBounds(
            TunnelCameraFraming.OuterRingOuterRadius, aspect);
        Assert.InRange(ringBounds.MinX, SafeMin, SafeMax);
        Assert.InRange(ringBounds.MaxX, SafeMin, SafeMax);
        Assert.InRange(ringBounds.MinY, SafeMin, SafeMax);
        Assert.InRange(ringBounds.MaxY, SafeMin, SafeMax);
    }
}
