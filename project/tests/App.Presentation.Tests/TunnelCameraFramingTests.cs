using FantaSim.App.Presentation.Tunnel;
using FantaSim.App.Timeline;
using System.Linq;
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

    [Fact]
    public void Camera_is_pulled_back_axial_and_clear_of_planet()
    {
        // Part B (slice 1) pulls the camera back through the mouth (+Z) so the full ring pair frames
        // the throat, superseding the §4a inside-the-mouth interior framing.
        var position = TunnelCameraFraming.LocalPosition;
        var forward = Vector3.Normalize(TunnelCameraFraming.LocalTarget - position);
        var obliquityDegrees = Math.Acos(Vector3.Dot(forward, -Vector3.UnitZ)) * 180.0 / Math.PI;
        var planetDistance = Vector3.Distance(position, PlanetCenter);

        Assert.True(position.Z > TunnelCameraFraming.MouthZ,
            $"Camera Z={position.Z:F3} must be pulled back outside the mouth for the full ring pair.");
        Assert.True(planetDistance > TunnelCameraFraming.PlanetVisualRadius
            + TunnelCameraFraming.NearClip
            + TunnelCameraFraming.PlanetClearance,
            $"Camera-to-planet distance {planetDistance:F3} violates planet/near clearance.");
        Assert.InRange(obliquityDegrees, 0.0, 20.0);
    }

    [Theory]
    [InlineData(16.0 / 9.0)]
    [InlineData(16.0 / 10.0)]
    public void Widescreen_projection_frames_both_rings_encircling_the_planet(double aspect)
    {
        var planet = TunnelCameraFraming.Project(PlanetCenter, aspect);
        var planetBounds = TunnelCameraFraming.ProjectSphereBounds(
            PlanetCenter,
            TunnelCameraFraming.PlanetVisualRadius,
            aspect);

        // The instrument is now mount-parented world geometry (not a camera-local dial), so the
        // rings are projected as real points on the anchor plane via the same oracle the planet
        // uses. Every extremal point of the outer ring must land in-frame (no crop) at each aspect.
        var outer = ProjectRingExtents(TunnelCameraFraming.OuterRingOuterRadius, aspect);
        var innerHole = ProjectRingExtents(TunnelCameraFraming.InnerRingInnerRadius, aspect);
        Assert.All(outer, AssertInsideViewport);

        // The rings encircle the throat: the planet projects strictly inside the inner ring hole.
        var innerMinX = innerHole.Min(p => p.X);
        var innerMaxX = innerHole.Max(p => p.X);
        var innerMinY = innerHole.Min(p => p.Y);
        var innerMaxY = innerHole.Max(p => p.Y);
        Assert.True(
            planetBounds.MinX > innerMinX && planetBounds.MaxX < innerMaxX
            && planetBounds.MinY > innerMinY && planetBounds.MaxY < innerMaxY,
            $"Planet bounds [{planetBounds.MinX:F3},{planetBounds.MaxX:F3}]x"
            + $"[{planetBounds.MinY:F3},{planetBounds.MaxY:F3}] must sit inside the inner ring hole "
            + $"[{innerMinX:F3},{innerMaxX:F3}]x[{innerMinY:F3},{innerMaxY:F3}].");

        // Planet reads near center now (no longer the right-third cockpit bias).
        Assert.InRange(planet.X, 0.30, 0.70);
        Assert.InRange(planet.Y, 0.30, 0.70);

        // Axial corridor cues still recede with visible perspective separation.
        var middleDepthCue = TunnelCameraFraming.Project(
            new Vector3(
                -CorridorSurfaceRadius,
                0.0f,
                (TunnelCameraFraming.CurrentPlaneZ + TunnelCameraFraming.ThroatZ) / 2.0f),
            aspect);
        var farDepthCue = TunnelCameraFraming.Project(
            new Vector3(-CorridorSurfaceRadius, 0.0f, TunnelCameraFraming.ThroatZ + 1.0f),
            aspect);
        Assert.True(middleDepthCue.Depth > 0.0 && farDepthCue.Depth > 0.0);
        Assert.True(Math.Abs(middleDepthCue.X - farDepthCue.X) >= 0.02,
            "Separated axial cues must retain visible perspective separation.");
    }

    [Theory]
    [InlineData(16.0 / 9.0)]
    [InlineData(16.0 / 10.0)]
    public void Near_interior_lip_cues_project_in_front_on_the_open_side(double aspect)
    {
        // With the camera pulled back the lip cues are shell-edge markers rather than an inside-mouth
        // device; they must still project in front of the camera and stay on the open (-X) side.
        var projected = Enumerable.Range(0, TunnelCameraFraming.NearInteriorLipCueCount)
            .Select(index => TunnelCameraFraming.Project(
                TunnelCameraFraming.NearInteriorLipCuePoint(index),
                aspect))
            .ToArray();

        Assert.All(projected, point => Assert.True(point.Depth > 0.0,
            "Lip cue must project in front of the camera."));
        Assert.All(projected, point => Assert.True(point.X < 0.55,
            $"Near-interior lip cue must stay on the open left side; X={point.X:F3}."));
    }

    [Fact]
    public void TryTickToZ_maps_the_real_kb_period_without_stretching_short_ranges()
    {
        var kb = TimelineModel.GetLadderRungs().Single(rung => rung.Symbol == "kb");
        var kbUnitTicks = TimelineModel.SpanTicksForRung(kb, units: 1);
        Assert.Equal(100_000_000L, kbUnitTicks);

        AssertTickToZ(
            requestedTick: 10_000_000L,
            currentTick: 10_000_000L,
            kbUnitTicks,
            expectedZ: TunnelCameraFraming.CurrentPlaneZ);
        AssertTickToZ(
            requestedTick: 60_000_000L,
            currentTick: 10_000_000L,
            kbUnitTicks,
            expectedZ: -12.5f);
        AssertTickToZ(
            requestedTick: 110_000_000L,
            currentTick: 10_000_000L,
            kbUnitTicks,
            expectedZ: TunnelCameraFraming.ThroatZ);

        // If MaxTick is 60M, its half-kb endpoint above remains -12.5, strictly ahead of the
        // throat. The clipped caller stops there and leaves the unused far segment empty.
        Assert.True(-12.5f > TunnelCameraFraming.ThroatZ);
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

    // The rings sit on the mount-local anchor plane centered on the tunnel axis; project their four
    // extremal points as real world points (same oracle as the planet) rather than the obsolete
    // camera-local ProjectInstrumentRingBounds.
    private static TunnelProjectedPoint[] ProjectRingExtents(float radius, double aspect)
    {
        var z = TunnelCameraFraming.InstrumentLocalAnchor.Z;
        return new[]
        {
            TunnelCameraFraming.Project(new Vector3(radius, 0f, z), aspect),
            TunnelCameraFraming.Project(new Vector3(-radius, 0f, z), aspect),
            TunnelCameraFraming.Project(new Vector3(0f, radius, z), aspect),
            TunnelCameraFraming.Project(new Vector3(0f, -radius, z), aspect),
        };
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
