using System;
using System.Collections.Generic;

using FantaSim.App.World;   // GeoPoint

namespace FantaSim.App.World.Composition;

/// <summary>
/// Small synthetic geo helpers shared by the placeholder field producers (layer-stack step 4a-ii).
/// These are CRUDE stand-ins, NOT a geodesy library: angular distance on a unit sphere scaled by a
/// fixed Earth-like radius, just so the "-m" field units are at least order-of-magnitude honest.
/// They disappear when the package-backed Geosphere.Crust producer replaces SyntheticCrustLayer.
/// </summary>
internal static class GeosphereFieldMath
{
    /// <summary>Synthetic Earth-like mean radius -- only to give the "-m" fields a plausible scale.</summary>
    public const double PlanetRadiusMeters = 6_371_000.0;

    /// <summary>Geodetic (lat/lon degrees) -> unit vector on the sphere.</summary>
    public static (double X, double Y, double Z) ToUnit(GeoPoint p)
    {
        var lat = p.LatitudeDegrees * Math.PI / 180.0;
        var lon = p.LongitudeDegrees * Math.PI / 180.0;
        var cosLat = Math.Cos(lat);
        return (cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), Math.Sin(lat));
    }

    /// <summary>Great-circle distance in metres between two geodetic points.</summary>
    public static double GreatCircleMeters(GeoPoint a, GeoPoint b)
    {
        var ua = ToUnit(a);
        var ub = ToUnit(b);
        var dot = Math.Clamp(ua.X * ub.X + ua.Y * ub.Y + ua.Z * ub.Z, -1.0, 1.0);
        return Math.Acos(dot) * PlanetRadiusMeters;
    }

    /// <summary>Spherical midpoint of a segment (average the unit vectors, renormalize).</summary>
    public static GeoPoint Midpoint(GeoPoint a, GeoPoint b)
    {
        var ua = ToUnit(a);
        var ub = ToUnit(b);
        return FromUnit((ua.X + ub.X) * 0.5, (ua.Y + ub.Y) * 0.5, (ua.Z + ub.Z) * 0.5, fallback: a);
    }

    /// <summary>Spherical centroid of a ring (average the unit vectors, renormalize).</summary>
    public static GeoPoint Centroid(IReadOnlyList<GeoPoint> ring)
    {
        double sx = 0, sy = 0, sz = 0;
        foreach (var p in ring)
        {
            var u = ToUnit(p);
            sx += u.X; sy += u.Y; sz += u.Z;
        }
        return FromUnit(sx, sy, sz, fallback: ring.Count > 0 ? ring[0] : new GeoPoint(0, 0));
    }

    private static GeoPoint FromUnit(double x, double y, double z, GeoPoint fallback)
    {
        var len = Math.Sqrt(x * x + y * y + z * z);
        if (len < 1e-12)
            return fallback;   // antipodal/degenerate average -> no meaningful direction
        x /= len; y /= len; z /= len;
        var lat = Math.Asin(Math.Clamp(z, -1.0, 1.0)) * 180.0 / Math.PI;
        var lon = Math.Atan2(y, x) * 180.0 / Math.PI;
        return new GeoPoint(lat, lon);
    }
}
