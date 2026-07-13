using FantaSim.App.Presentation.Tunnel;
using UnifyMaths;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

public sealed class TunnelBoreSplineTests
{
    private const double MaxDepth = 15.0;

    private static TunnelBoreSpline Create(long seed = 1234)
        => TunnelBoreSpline.Create(
            seed,
            TunnelBoreContract.StraightRadius,
            TunnelBoreContract.CurvatureCapRadPerUnit,
            MaxDepth);

    [Theory]
    [InlineData(0.0)]
    [InlineData(3.0)]
    [InlineData(7.5)]
    public void Near_field_is_exactly_straight(double depth)
    {
        var frame = Create().Evaluate(depth);

        Assert.Equal(0.0, frame.Position.X, 12);
        Assert.Equal(0.0, frame.Position.Y, 12);
        Assert.Equal(-depth, frame.Position.Z, 12);
        Assert.Equal(-1.0, frame.Forward.Z, 12);
        Assert.Equal(1.0, frame.Right.X, 12);
        Assert.Equal(1.0, frame.Up.Y, 12);
    }

    [Fact]
    public void Same_seed_is_deterministic_across_instances()
    {
        var a = Create(seed: 77);
        var b = Create(seed: 77);
        for (double d = 0.0; d <= MaxDepth; d += 0.5)
        {
            var fa = a.Evaluate(d);
            var fb = b.Evaluate(d);
            Assert.Equal(fa.Position.X, fb.Position.X, 12);
            Assert.Equal(fa.Position.Y, fb.Position.Y, 12);
            Assert.Equal(fa.Position.Z, fb.Position.Z, 12);
            Assert.Equal(fa.Forward.X, fb.Forward.X, 12);
            Assert.Equal(fa.Forward.Y, fb.Forward.Y, 12);
            Assert.Equal(fa.Forward.Z, fb.Forward.Z, 12);
        }
    }

    [Fact]
    public void Different_seeds_diverge_beyond_the_straight_window()
    {
        var a = Create(seed: 1).Evaluate(MaxDepth);
        var b = Create(seed: 2).Evaluate(MaxDepth);
        var separation =
            System.Math.Abs(a.Position.X - b.Position.X)
            + System.Math.Abs(a.Position.Y - b.Position.Y);
        Assert.True(separation > 1e-3, $"expected lateral divergence, got {separation}");
    }

    [Fact]
    public void Curvature_cap_is_honored()
    {
        var spline = Create();
        const double h = 0.25;
        for (double d = h; d <= MaxDepth; d += h)
        {
            var f0 = spline.Evaluate(d - h).Forward;
            var f1 = spline.Evaluate(d).Forward;
            var dot = System.Math.Clamp(
                (f0.X * f1.X) + (f0.Y * f1.Y) + (f0.Z * f1.Z), -1.0, 1.0);
            var anglePerUnit = System.Math.Acos(dot) / h;
            Assert.True(
                anglePerUnit <= TunnelBoreContract.CurvatureCapRadPerUnit + 1e-6,
                $"turn {anglePerUnit} rad/unit at depth {d} exceeds cap");
        }
    }

    [Fact]
    public void Frames_stay_orthonormal_and_unrolled()
    {
        var spline = Create();
        for (double d = 0.0; d <= MaxDepth; d += 0.5)
        {
            var f = spline.Evaluate(d);
            Assert.Equal(1.0, Length(f.Forward), 9);
            Assert.Equal(1.0, Length(f.Right), 9);
            Assert.Equal(1.0, Length(f.Up), 9);
            Assert.Equal(0.0, Dot(f.Forward, f.Right), 9);
            Assert.Equal(0.0, Dot(f.Forward, f.Up), 9);
            Assert.Equal(0.0, Dot(f.Right, f.Up), 9);
            // Parallel transport with a bounded cap cannot flip the vertical.
            Assert.True(f.Up.Y > 0.5, $"up vector rolled at depth {d}: {f.Up.Y}");
        }
    }

    [Fact]
    public void Depth_advances_monotonically_along_the_axis()
    {
        var spline = Create();
        var previousZ = double.PositiveInfinity;
        for (double d = 0.0; d <= MaxDepth; d += 0.25)
        {
            var z = spline.Evaluate(d).Position.Z;
            Assert.True(z < previousZ, $"Z not strictly decreasing at depth {d}");
            previousZ = z;
        }
    }

    [Fact]
    public void Transition_at_the_straight_boundary_is_c1_continuous()
    {
        var spline = Create();
        const double s = 7.5;
        const double eps = 0.05;
        var before = spline.Evaluate(s - eps);
        var after = spline.Evaluate(s + eps);
        var positionJump = Length(new Vector3D(
            after.Position.X - before.Position.X,
            after.Position.Y - before.Position.Y,
            after.Position.Z - before.Position.Z));
        Assert.InRange(positionJump, 0.0, (2 * eps) + 1e-3);

        var dot = System.Math.Clamp(Dot(before.Forward, after.Forward), -1.0, 1.0);
        var headingJump = System.Math.Acos(dot);
        Assert.True(
            headingJump <= TunnelBoreContract.CurvatureCapRadPerUnit * 2 * eps + 1e-6,
            $"heading jump {headingJump} at the boundary");
    }

    [Fact]
    public void Interactive_window_is_inside_the_straight_window()
    {
        // Wall picking must never see bent geometry: the pick clip plane sits exactly at the
        // straight radius, between the throat and the current plane.
        var clip = TunnelBoreContract.InteractiveThroatZ(currentPlaneZ: -5.0f);
        Assert.Equal(-12.5f, clip, 6);
        Assert.True(clip > -20.0f);  // shallower than ThroatZ
        Assert.True(clip < -5.0f);   // deeper than the current plane
    }

    private static double Length(Vector3D v)
        => System.Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));

    private static double Dot(Vector3D a, Vector3D b)
        => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
}
