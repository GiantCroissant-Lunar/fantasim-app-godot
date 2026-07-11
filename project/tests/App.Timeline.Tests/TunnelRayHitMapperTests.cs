using System;
using FantaSim.App.Timeline.Seam;
using Xunit;

namespace App.Timeline.Tests;

/// <summary>
/// Headless coverage for the two-ring prototype's oblique ray/plane/cylinder hit math
/// (vault/plans/2026-07-12-rotating-tunnel-two-ring-prototype-plan.md Task 1B). Pure Godot-free
/// doubles (<see cref="TunnelPoint3"/>/<see cref="TunnelRay3"/>) so the risky root selection under
/// the oblique camera is disproved without a viewport.
/// </summary>
public sealed class TunnelRayHitMapperTests
{
    // ---- TryIntersectMouthPlane ----

    [Fact]
    public void TryIntersectMouthPlane_ForwardHit_ReturnsPointAtMouthZ()
    {
        var ray = new TunnelRay3(new TunnelPoint3(0, 0, 10), new TunnelPoint3(0, 0, -1));

        var hit = TunnelRayHitMapper.TryIntersectMouthPlane(ray, mouthZ: 0.0, out var point);

        Assert.True(hit);
        Assert.Equal(0.0, point.X, precision: 9);
        Assert.Equal(0.0, point.Y, precision: 9);
        Assert.Equal(0.0, point.Z, precision: 9);
    }

    [Fact]
    public void TryIntersectMouthPlane_BehindCamera_ReturnsFalse()
    {
        // Origin above the mouth, direction pointing away (+Z, away from mouth at Z=0).
        var ray = new TunnelRay3(new TunnelPoint3(0, 0, 10), new TunnelPoint3(0, 0, 1));

        var hit = TunnelRayHitMapper.TryIntersectMouthPlane(ray, mouthZ: 0.0, out var point);

        Assert.False(hit);
        Assert.Equal(default, point);
    }

    [Fact]
    public void TryIntersectMouthPlane_ParallelRay_ReturnsFalse()
    {
        // Direction has no Z component: the ray never crosses the Z=0 plane.
        var ray = new TunnelRay3(new TunnelPoint3(5, 5, 7), new TunnelPoint3(1, 0, 0));

        var hit = TunnelRayHitMapper.TryIntersectMouthPlane(ray, mouthZ: 0.0, out var point);

        Assert.False(hit);
        Assert.Equal(default, point);
    }

    [Fact]
    public void TryIntersectMouthPlane_NonFiniteDirection_ReturnsFalse()
    {
        var ray = new TunnelRay3(new TunnelPoint3(0, 0, 10), new TunnelPoint3(0, 0, double.NaN));

        var hit = TunnelRayHitMapper.TryIntersectMouthPlane(ray, mouthZ: 0.0, out _);

        Assert.False(hit);
    }

    // ---- TryIntersectCylinder ----

    [Fact]
    public void TryIntersectCylinder_NearestForwardRoot_ReturnsEntryPoint()
    {
        // Ray straight down the axis from above the mouth hits the cylinder wall only if off-axis.
        // Here an off-axis ray traveling -Z grazes the radius-8 wall at the near side.
        var ray = new TunnelRay3(new TunnelPoint3(12, 0, 0), new TunnelPoint3(-1, 0, 0));

        var hit = TunnelRayHitMapper.TryIntersectCylinder(ray, radius: 8.0, throatZ: -14.0, mouthZ: 0.0, out var point);

        Assert.True(hit);
        Assert.Equal(8.0, point.X, precision: 6);
        Assert.Equal(0.0, point.Y, precision: 6);
        Assert.Equal(0.0, point.Z, precision: 6);
    }

    [Fact]
    public void TryIntersectCylinder_NegativeRoot_Rejected()
    {
        // Origin outside the cylinder, direction away from it: both roots negative -> miss.
        var ray = new TunnelRay3(new TunnelPoint3(20, 0, -7), new TunnelPoint3(1, 0, 0));

        var hit = TunnelRayHitMapper.TryIntersectCylinder(ray, radius: 8.0, throatZ: -14.0, mouthZ: 0.0, out _);

        Assert.False(hit);
    }

    [Fact]
    public void TryIntersectCylinder_NearRootOutsideZRange_FarRootInsideSelected()
    {
        // Origin (9,0,6) aimed (-1,0,-0.5): the near root lands at Z=5.5 (above mouth=0, outside the
        // range); the far root lands at Z=-2.5 (inside [throat=-14, mouth=0]). The far root must win.
        var ray = new TunnelRay3(new TunnelPoint3(9, 0, 6), new TunnelPoint3(-1, 0, -0.5));

        var hit = TunnelRayHitMapper.TryIntersectCylinder(ray, radius: 8.0, throatZ: -14.0, mouthZ: 0.0, out var point);

        Assert.True(hit);
        Assert.True(point.Z <= 0.0 + 1e-6, $"accepted root Z {point.Z} should be <= mouth");
        Assert.True(point.Z >= -14.0 - 1e-6, $"accepted root Z {point.Z} should be >= throat");
        Assert.Equal(-2.5, point.Z, precision: 4);
    }

    [Fact]
    public void TryIntersectCylinder_BothRootsOutsideZRange_ReturnsFalse()
    {
        // Ray crosses the infinite cylinder but both roots are above the mouth (Z > 0).
        var ray = new TunnelRay3(new TunnelPoint3(9, 0, 50), new TunnelPoint3(-1, 0, 0));

        var hit = TunnelRayHitMapper.TryIntersectCylinder(ray, radius: 8.0, throatZ: -14.0, mouthZ: 0.0, out _);

        Assert.False(hit);
    }

    [Fact]
    public void TryIntersectCylinder_TangentHit_ReturnsPoint()
    {
        // Origin (10,0,-7) aimed (-3,4,0): b²=4ac exactly (discriminant 0), a grazing tangent at
        // (6.4, 4.8, -7) on the radius-8 cylinder.
        var ray = new TunnelRay3(new TunnelPoint3(10, 0, -7), new TunnelPoint3(-3, 4, 0));

        var hit = TunnelRayHitMapper.TryIntersectCylinder(ray, radius: 8.0, throatZ: -14.0, mouthZ: 0.0, out var point);

        Assert.True(hit);
        var r = Math.Sqrt(point.X * point.X + point.Y * point.Y);
        Assert.Equal(8.0, r, precision: 4);
        Assert.Equal(-7.0, point.Z, precision: 4);
    }

    [Fact]
    public void TryIntersectCylinder_RayParallelToAxis_ReturnsFalse()
    {
        // Direction is pure -Z (no XY component): a=0, the ray is parallel to the cylinder axis and
        // either never crosses the wall (off-axis) or is always inside (never a wall hit).
        var ray = new TunnelRay3(new TunnelPoint3(3, 3, 5), new TunnelPoint3(0, 0, -1));

        var hit = TunnelRayHitMapper.TryIntersectCylinder(ray, radius: 8.0, throatZ: -14.0, mouthZ: 0.0, out _);

        Assert.False(hit);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void TryIntersectCylinder_NonPositiveRadius_ReturnsFalse(double radius)
    {
        var ray = new TunnelRay3(new TunnelPoint3(12, 0, 0), new TunnelPoint3(-1, 0, 0));

        var hit = TunnelRayHitMapper.TryIntersectCylinder(ray, radius, throatZ: -14.0, mouthZ: 0.0, out _);

        Assert.False(hit);
    }

    [Fact]
    public void TryIntersectCylinder_NonFiniteVector_ReturnsFalse()
    {
        var ray = new TunnelRay3(new TunnelPoint3(double.NaN, 0, 0), new TunnelPoint3(-1, 0, 0));

        var hit = TunnelRayHitMapper.TryIntersectCylinder(ray, radius: 8.0, throatZ: -14.0, mouthZ: 0.0, out _);

        Assert.False(hit);
    }

    [Fact]
    public void TryIntersectCylinder_ReversedZBounds_ReturnsFalse()
    {
        var ray = new TunnelRay3(new TunnelPoint3(12, 0, 0), new TunnelPoint3(-1, 0, 0));

        var hit = TunnelRayHitMapper.TryIntersectCylinder(ray, radius: 8.0, throatZ: 0.0, mouthZ: -14.0, out _);

        Assert.False(hit);
    }
}
