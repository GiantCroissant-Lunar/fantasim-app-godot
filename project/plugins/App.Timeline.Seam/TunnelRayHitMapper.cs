using System;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Pure Godot-free 3D point for ray-hit testing (vault/plans/2026-07-12-rotating-tunnel-two-ring-
/// prototype-plan.md Task 1B). Mirrors only the subset of operations the oblique camera's
/// <c>ProjectRayOrigin</c>/<c>ProjectRayNormal</c> feed into the tunnel's mouth-plane and wall
/// selection, so the risky root selection is disproved without a viewport.
/// </summary>
public readonly record struct TunnelPoint3(double X, double Y, double Z);

public readonly record struct TunnelRay3(TunnelPoint3 Origin, TunnelPoint3 Direction);

/// <summary>
/// Oblique ray/plane/cylinder intersection for the two-ring prototype's hit testing. The mouth
/// rings are tested against the Z=<paramref name="mouthZ"/> plane (non-overlapping annular bands);
/// the corridor wall is tested against the <c>x²+y²=radius²</c> cylinder clipped to
/// <c>[throatZ, mouthZ]</c>. Near-parallel rays, behind-camera roots, reversed Z bounds, and
/// non-finite/degenerate inputs return <c>false</c> rather than garbage coordinates.
/// </summary>
public static class TunnelRayHitMapper
{
    private const double ParallelEpsilon = 1e-9;

    /// <summary>
    /// Intersects <paramref name="ray"/> with the plane Z=<paramref name="mouthZ"/>. Returns the
    /// forward (t&gt;=0) hit point, or <c>false</c> for a near-parallel ray, a behind-camera
    /// intersection, or non-finite input.
    /// </summary>
    public static bool TryIntersectMouthPlane(TunnelRay3 ray, double mouthZ, out TunnelPoint3 point)
    {
        point = default;
        if (!IsFiniteRay(ray) || !double.IsFinite(mouthZ))
            return false;

        if (Math.Abs(ray.Direction.Z) < ParallelEpsilon)
            return false;

        var t = (mouthZ - ray.Origin.Z) / ray.Direction.Z;
        if (t < 0.0)
            return false;

        point = new TunnelPoint3(
            ray.Origin.X + t * ray.Direction.X,
            ray.Origin.Y + t * ray.Direction.Y,
            mouthZ);
        return true;
    }

    /// <summary>
    /// Intersects <paramref name="ray"/> with the cylinder <c>x²+y²=radius²</c> clipped to
    /// <c>[throatZ, mouthZ]</c> (throatZ &lt; mouthZ). Solves <c>a*t²+b*t+c=0</c>, sorts the two
    /// real roots, and returns the nearest non-negative root whose computed Z is inclusively within
    /// the Z range; if the near root is outside the Z range it tests the far root before returning
    /// false. Rejects non-positive/non-finite radius, non-finite vectors, reversed Z bounds, and
    /// rays parallel to the cylinder axis.
    /// </summary>
    public static bool TryIntersectCylinder(
        TunnelRay3 ray,
        double radius,
        double throatZ,
        double mouthZ,
        out TunnelPoint3 point)
    {
        point = default;
        if (!IsFiniteRay(ray))
            return false;
        if (!(radius > 0.0) || !double.IsFinite(radius))
            return false;
        if (!(throatZ < mouthZ))
            return false;

        var ox = ray.Origin.X;
        var oy = ray.Origin.Y;
        var dx = ray.Direction.X;
        var dy = ray.Direction.Y;

        var a = dx * dx + dy * dy;
        if (a < ParallelEpsilon)
            return false; // Ray parallel to the cylinder axis.

        var b = 2.0 * (ox * dx + oy * dy);
        var c = ox * ox + oy * oy - radius * radius;
        var discriminant = b * b - 4.0 * a * c;
        if (discriminant < 0.0)
            return false; // No real roots: ray misses the infinite cylinder.

        var sqrtD = Math.Sqrt(discriminant);
        var inv2a = 1.0 / (2.0 * a);
        var tNear = (-b - sqrtD) * inv2a;
        var tFar = (-b + sqrtD) * inv2a;

        if (tNear > tFar)
            (tNear, tFar) = (tFar, tNear);

        if (TryAcceptRoot(ray, tNear, throatZ, mouthZ, out point))
            return true;

        if (TryAcceptRoot(ray, tFar, throatZ, mouthZ, out point))
            return true;

        return false;
    }

    private static bool TryAcceptRoot(
        TunnelRay3 ray, double t, double throatZ, double mouthZ, out TunnelPoint3 point)
    {
        point = default;
        if (t < 0.0)
            return false;

        var z = ray.Origin.Z + t * ray.Direction.Z;
        if (z < throatZ || z > mouthZ)
            return false;

        point = new TunnelPoint3(
            ray.Origin.X + t * ray.Direction.X,
            ray.Origin.Y + t * ray.Direction.Y,
            z);
        return true;
    }

    private static bool IsFiniteRay(TunnelRay3 ray)
        => IsFinitePoint(ray.Origin) && IsFinitePoint(ray.Direction);

    private static bool IsFinitePoint(TunnelPoint3 p)
        => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
}
