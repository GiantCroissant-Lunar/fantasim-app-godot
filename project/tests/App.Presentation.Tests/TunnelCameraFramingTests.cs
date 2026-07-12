using FantaSim.App.Presentation.Tunnel;
using System.Numerics;
using Xunit;

namespace App.Presentation.Tests;

/// <summary>
/// Pure projection contract for the asymmetric cockpit framing in design §3 and plan Task 6.
/// The projection oracle mirrors Godot 4.7 Camera3D perspective KEEP_HEIGHT semantics: vertical
/// FOV remains fixed while landscape aspect changes horizontal FOV.
/// </summary>
public sealed class TunnelCameraFramingTests
{
    private const float CorridorSurfaceRadius = TunnelCameraFraming.TunnelRadius - 0.06f;
    private static readonly Vector3 PlanetCenter = new(0.0f, 0.0f, TunnelCameraFraming.CurrentPlaneZ);
    private static readonly Vector3 FocusedCurrentAnchor = new(
        -CorridorSurfaceRadius,
        0.0f,
        TunnelCameraFraming.CurrentPlaneZ);

    [Fact]
    public void Camera_is_inside_shell_near_axial_and_clear_of_planet()
    {
        var position = TunnelCameraFraming.LocalPosition;
        var forward = Vector3.Normalize(TunnelCameraFraming.LocalTarget - position);
        var obliquityDegrees = Math.Acos(Vector3.Dot(forward, -Vector3.UnitZ)) * 180.0 / Math.PI;
        var radialDistance = Math.Sqrt(position.X * position.X + position.Y * position.Y);
        var planetDistance = Vector3.Distance(position, PlanetCenter);

        Assert.True(position.Z > TunnelCameraFraming.CurrentPlaneZ);
        Assert.True(position.Z <= TunnelCameraFraming.MouthZ);
        Assert.True(radialDistance < TunnelCameraFraming.TunnelRadius - TunnelCameraFraming.RadialClearance,
            $"Camera radial distance {radialDistance:F3} must remain inside the shell clearance.");
        Assert.True(planetDistance > TunnelCameraFraming.PlanetVisualRadius
            + TunnelCameraFraming.NearClip
            + TunnelCameraFraming.PlanetClearance,
            $"Camera-to-planet distance {planetDistance:F3} violates planet/near clearance.");
        Assert.InRange(obliquityDegrees, 0.0, 10.0);
    }

    [Theory]
    [InlineData(16.0 / 9.0)]
    [InlineData(16.0 / 10.0)]
    public void Widescreen_projection_keeps_left_focus_right_planet_and_two_rings_readable(double aspect)
    {
        var planet = TunnelCameraFraming.Project(PlanetCenter, aspect);
        var planetBounds = TunnelCameraFraming.ProjectSphereBounds(
            PlanetCenter,
            TunnelCameraFraming.PlanetVisualRadius,
            aspect);
        var focusedAnchor = TunnelCameraFraming.Project(FocusedCurrentAnchor, aspect);
        var instrument = TunnelCameraFraming.ProjectInstrumentCenter(aspect);

        Assert.InRange(planet.X, 0.62, 0.82);
        Assert.InRange(planet.Y, 0.35, 0.65);
        Assert.InRange(planetBounds.Height, 0.80, 1.20);
        Assert.True(planetBounds.MinY <= 0.05 || planetBounds.MaxY >= 0.95,
            $"Planet must intentionally touch/crop a vertical edge; bounds were [{planetBounds.MinY:F3}, {planetBounds.MaxY:F3}].");

        Assert.InRange(focusedAnchor.X, 0.12, 0.35);
        Assert.InRange(focusedAnchor.Y, 0.35, 0.65);
        Assert.InRange(instrument.X, 0.12, 0.35);
        Assert.InRange(instrument.Y, 0.35, 0.65);
        Assert.True(focusedAnchor.X < planetBounds.MinX,
            $"Focused current anchor X={focusedAnchor.X:F3} overlaps planet start X={planetBounds.MinX:F3}.");

        var innerInner = TunnelCameraFraming.ProjectInstrumentRingBounds(
            TunnelCameraFraming.InnerRingInnerRadius,
            aspect);
        var innerOuter = TunnelCameraFraming.ProjectInstrumentRingBounds(
            TunnelCameraFraming.InnerRingOuterRadius,
            aspect);
        var outerInner = TunnelCameraFraming.ProjectInstrumentRingBounds(
            TunnelCameraFraming.OuterRingInnerRadius,
            aspect);
        var outerOuter = TunnelCameraFraming.ProjectInstrumentRingBounds(
            TunnelCameraFraming.OuterRingOuterRadius,
            aspect);

        AssertInsideViewport(innerOuter);
        AssertInsideViewport(outerOuter);
        Assert.True(innerInner.MinX > innerOuter.MinX && innerInner.MaxX < innerOuter.MaxX);
        Assert.True(outerInner.MinX > outerOuter.MinX && outerInner.MaxX < outerOuter.MaxX);
        Assert.True(innerOuter.MaxX < outerInner.MaxX && innerOuter.MinX > outerInner.MinX,
            "The camera-local annuli need a visible radial gap; their mesh bands must not overlap.");

        var middleDepthCue = TunnelCameraFraming.Project(
            new Vector3(
                -CorridorSurfaceRadius,
                0.0f,
                (TunnelCameraFraming.CurrentPlaneZ + TunnelCameraFraming.ThroatZ) / 2.0f),
            aspect);
        var farDepthCue = TunnelCameraFraming.Project(
            new Vector3(-CorridorSurfaceRadius, 0.0f, TunnelCameraFraming.ThroatZ + 1.0f),
            aspect);

        AssertInsideViewport(middleDepthCue);
        AssertInsideViewport(farDepthCue);
        Assert.True(Math.Abs(middleDepthCue.X - farDepthCue.X) >= 0.04,
            "Separated axial cues must retain visible perspective separation.");
    }

    [Fact]
    public void TryTickToZ_maps_current_half_and_one_kb_without_stretching_short_ranges()
    {
        AssertTickToZ(
            requestedTick: 100,
            currentTick: 100,
            kbUnitTicks: 1_000,
            expectedZ: TunnelCameraFraming.CurrentPlaneZ);
        AssertTickToZ(requestedTick: 600, currentTick: 100, kbUnitTicks: 1_000, -12.5f);
        AssertTickToZ(
            requestedTick: 1_100,
            currentTick: 100,
            kbUnitTicks: 1_000,
            expectedZ: TunnelCameraFraming.ThroatZ);

        // If MaxTick is 600, its half-kb endpoint remains at -12.5. Callers leave the unused far
        // segment empty; they do not renormalize the shortened range to the throat.
        AssertTickToZ(requestedTick: 600, currentTick: 100, kbUnitTicks: 1_000, -12.5f);
    }

    [Theory]
    [InlineData(99, 100, 1_000)]
    [InlineData(1_101, 100, 1_000)]
    [InlineData(100, 100, 0)]
    [InlineData(100, 100, -1)]
    public void TryTickToZ_rejects_past_out_of_kb_and_invalid_units(
        long requestedTick,
        long currentTick,
        long kbUnitTicks)
    {
        var success = TunnelCameraFraming.TryTickToZ(
            requestedTick,
            currentTick,
            kbUnitTicks,
            out var z);

        Assert.False(success);
        Assert.Equal(TunnelCameraFraming.CurrentPlaneZ, z, precision: 5);
    }

    private static void AssertTickToZ(long requestedTick, long currentTick, long kbUnitTicks, float expectedZ)
    {
        var success = TunnelCameraFraming.TryTickToZ(
            requestedTick,
            currentTick,
            kbUnitTicks,
            out var z);

        Assert.True(success);
        Assert.Equal(expectedZ, z, precision: 5);
    }

    private static void AssertInsideViewport(TunnelProjectedPoint point)
    {
        Assert.InRange(point.X, 0.0, 1.0);
        Assert.InRange(point.Y, 0.0, 1.0);
        Assert.True(point.Depth > 0.0);
    }

    private static void AssertInsideViewport(TunnelProjectedBounds bounds)
    {
        Assert.InRange(bounds.MinX, 0.0, 1.0);
        Assert.InRange(bounds.MaxX, 0.0, 1.0);
        Assert.InRange(bounds.MinY, 0.0, 1.0);
        Assert.InRange(bounds.MaxY, 0.0, 1.0);
    }
}
