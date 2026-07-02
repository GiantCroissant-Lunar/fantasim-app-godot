using System;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.Geosphere.Plate.Topology;
using UnifyGeometry.Spherical;
using UnifyMaths;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class BoundaryArcSamplerTests
{
    [Fact]
    public void SubdivideGreatCircle_midpoint_is_the_great_circle_midpoint()
    {
        // slerp(a, b, 0.5) == normalize(a + b) for unit vectors on a great circle.
        var a = SphericalPoint.FromDegrees(0, 0);
        var b = SphericalPoint.FromDegrees(0, 90);

        var pts = BoundaryArcSampler.SubdivideGreatCircle(a, b, subdiv: 2);

        Assert.Equal(3, pts.Count);
        var expected = Normalize(a.ToVector3D() + b.ToVector3D());
        AssertApproxEqual(expected, pts[1], 1e-5);
    }

    [Fact]
    public void SubdivideGreatCircle_points_are_unit_length()
    {
        var a = SphericalPoint.FromDegrees(10, 20);
        var b = SphericalPoint.FromDegrees(50, 80);

        var pts = BoundaryArcSampler.SubdivideGreatCircle(a, b, subdiv: 8);

        Assert.Equal(9, pts.Count);
        foreach (var p in pts)
            AssertUnitLength(p);
    }

    [Fact]
    public void SubdivideGreatCircle_equal_endpoints_return_repeated_point_without_nan()
    {
        var a = SphericalPoint.FromDegrees(30, 45);

        var pts = BoundaryArcSampler.SubdivideGreatCircle(a, a, subdiv: 4);

        Assert.Equal(5, pts.Count);
        foreach (var p in pts)
        {
            Assert.False(float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z));
            AssertApproxEqual(a.ToVector3D(), p, 1e-5);
        }
    }

    [Fact]
    public void SubdivideGreatCircle_antipodal_endpoints_do_not_throw_and_stay_unit_length()
    {
        var a = SphericalPoint.FromDegrees(0, 0);
        var b = SphericalPoint.FromDegrees(0, 180); // antipodal on the equator

        var pts = BoundaryArcSampler.SubdivideGreatCircle(a, b, subdiv: 6);

        Assert.Equal(7, pts.Count);
        foreach (var p in pts)
        {
            Assert.False(float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z));
            AssertUnitLength(p);
        }
    }

    [Fact]
    public void SubdivideGreatCircle_clamps_subdiv_below_one_to_endpoints()
    {
        var a = SphericalPoint.FromDegrees(0, 0);
        var b = SphericalPoint.FromDegrees(0, 45);

        var ptsZero = BoundaryArcSampler.SubdivideGreatCircle(a, b, subdiv: 0);
        var ptsOne = BoundaryArcSampler.SubdivideGreatCircle(a, b, subdiv: 1);

        Assert.Equal(2, ptsZero.Count);
        Assert.Equal(2, ptsOne.Count);
        AssertApproxEqual(a.ToVector3D(), ptsZero[0], 1e-5);
        AssertApproxEqual(b.ToVector3D(), ptsZero[1], 1e-5);
    }

    [Theory]
    [InlineData(BoundaryType.Convergent, PlateBoundaryKind.Convergent)]
    [InlineData(BoundaryType.Divergent, PlateBoundaryKind.Divergent)]
    [InlineData(BoundaryType.Transform, PlateBoundaryKind.Transform)]
    [InlineData(BoundaryType.Inactive, PlateBoundaryKind.Inactive)]
    public void MapKind_maps_each_topology_type(BoundaryType type, PlateBoundaryKind expected)
    {
        Assert.Equal(expected, BoundaryArcSampler.MapKind(type));
    }

    [Fact]
    public void DiffBoundaries_reports_added_retired_and_retyped_by_plate_pair()
    {
        var dummyPoints = new[] { new GlobeVec3(1, 0, 0), new GlobeVec3(0, 1, 0) };
        var previous = new[]
        {
            new PlateBoundaryArc(0, 2, PlateBoundaryKind.Convergent, dummyPoints), // retyped below
            new PlateBoundaryArc(2, 3, PlateBoundaryKind.Divergent, dummyPoints),  // retired
        };
        var current = new[]
        {
            new PlateBoundaryArc(0, 2, PlateBoundaryKind.Transform, dummyPoints),  // retyped
            new PlateBoundaryArc(0, 1, PlateBoundaryKind.Divergent, dummyPoints),  // added
        };

        var diff = BoundaryArcSampler.DiffBoundaries(previous, current);

        var added = Assert.Single(diff.Added);
        Assert.Equal((0, 1), (added.PlateA, added.PlateB));

        var retired = Assert.Single(diff.Retired);
        Assert.Equal((2, 3), (retired.PlateA, retired.PlateB));

        var change = Assert.Single(diff.Retyped);
        Assert.Equal((0, 2), (change.PlateA, change.PlateB));
        Assert.Equal(PlateBoundaryKind.Convergent, change.OldKind);
        Assert.Equal(PlateBoundaryKind.Transform, change.NewKind);
    }

    private static Vector3D Normalize(Vector3D v)
    {
        double len = v.Length();
        return len < 1e-15 ? new Vector3D(0, 0, 0) : v * (1.0 / len);
    }

    private static void AssertUnitLength(GlobeVec3 v)
    {
        double len = Math.Sqrt((double)v.X * v.X + (double)v.Y * v.Y + (double)v.Z * v.Z);
        Assert.InRange(len, 1.0 - 1e-5, 1.0 + 1e-5);
    }

    private static void AssertApproxEqual(Vector3D expected, GlobeVec3 actual, double tol)
    {
        Assert.True(Math.Abs(expected.X - actual.X) < tol, $"X mismatch: {expected.X} vs {actual.X}");
        Assert.True(Math.Abs(expected.Y - actual.Y) < tol, $"Y mismatch: {expected.Y} vs {actual.Y}");
        Assert.True(Math.Abs(expected.Z - actual.Z) < tol, $"Z mismatch: {expected.Z} vs {actual.Z}");
    }
}
