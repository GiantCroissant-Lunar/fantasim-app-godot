using System;
using System.Threading.Tasks;
using FantaSim.App.World;
using FantaSim.Geosphere.Asthenosphere.Convection;
using UnifyMaths;

namespace FantaSim.App.World.Composition;

// Mantle x-ray view (M-A), task 2: shell-grid sampling of the engine's VOLUMETRIC anomaly field.
//
// v2 (method-lock): the engine's MantleAnomalyField is a true T'(direction, radius, tick) — its
// EvaluateAt takes a position with TRUE RADIUS in [CMB, 1.0] planet radii and returns a signed
// anomaly (negative = cold slabs, positive = hot blanket/plumes/ridge curtains) with real radial
// structure (slab ribbons swept with dip, basal blanket, mushroom plumes). This sampler therefore
// just evaluates the field on a grid — NO app-side radial profile is layered on (that was the v1
// approach, rewritten away when the engine field became volumetric).
//
// Grid choice (documented decision): a CARTESIAN cube [-outer, outer]^3 clipped to the shell, not a
// lat/lon/radius lattice. Marching cubes assumes a rectilinear lattice; a spherical lattice's cells
// are curvilinear (vertices must be bent through the coordinate transform), the longitude seam needs
// explicit stitching, and the poles degenerate. The Cartesian grid gives uniform cells, a watertight
// mesh with zero seam handling, and the field is a stateless pure function that is equally cheap to
// evaluate anywhere — the only cost is the ~57% of lattice points outside the shell, which are
// zero-filled without a field call.

/// <summary>
/// Samples the engine's volumetric <see cref="MantleAnomalyField"/> on a Cartesian grid clipped to
/// the mantle shell, producing ONE signed scalar grid (negative = cold, positive = warm) for
/// four-threshold isosurface extraction. Pure, deterministic (parallel over disjoint slices — the
/// per-point value is independent of scheduling), Godot-free.
/// </summary>
public static class MantleFieldSampler
{
    /// <summary>
    /// Sample the signed anomaly on the configured grid. Values taper to 0 over
    /// <see cref="MantleViewConfig.ShellFadeWidth"/> at both shell boundaries so marching cubes
    /// closes every isosurface inside the shell (no open cuts at the sampling boundary).
    /// </summary>
    public static MantleScalarField Sample(MantleAnomalyField field, MantleViewConfig config)
    {
        ArgumentNullException.ThrowIfNull(field);
        var n = config.GridResolution;
        if (n < 2)
            throw new ArgumentException("GridResolution must be >= 2.", nameof(config));

        var inner = config.InnerRadius;
        var outer = config.OuterRadius;
        if (!(inner > 0.0) || !(outer > inner))
            throw new ArgumentException("MantleViewConfig requires 0 < InnerRadius < OuterRadius.", nameof(config));

        long count = (long)n * n * n;
        var anomaly = new float[count];
        double span = 2.0 * outer;

        // The taper must span more than one lattice cell, or the discrete field jumps from a nearly
        // unmasked value straight to the zero-filled exterior — marching cubes would then place
        // boundary vertices up to a cell OUTSIDE the shell and the cut would look chopped. Widen the
        // configured fade to at least 1.5 cells so the taper is always resolved by the grid.
        double cellSize = span / (n - 1);
        double fade = Math.Max(Math.Max(1e-6, config.ShellFadeWidth), 1.5 * cellSize);

        // Parallel over z-slices: each slice writes a disjoint index range and every value is a pure
        // function of its own lattice point, so the result is bit-identical regardless of scheduling.
        Parallel.For(0, n, zi =>
        {
            double z = -outer + (zi * span / (n - 1));
            int sliceBase = zi * n * n;
            for (int yi = 0; yi < n; yi++)
            {
                double y = -outer + (yi * span / (n - 1));
                int rowBase = sliceBase + yi * n;
                for (int xi = 0; xi < n; xi++)
                {
                    double x = -outer + (xi * span / (n - 1));
                    double r = Math.Sqrt(x * x + y * y + z * z);
                    if (r <= inner || r >= outer)
                        continue; // outside the shell: stays 0 (no field call)

                    double value = field.EvaluateAt(new Vector3D(x, y, z));

                    // Smooth taper to 0 at both shell boundaries.
                    double mask = SmoothStep((r - inner) / fade) * SmoothStep((outer - r) / fade);
                    anomaly[rowBase + xi] = (float)(value * mask);
                }
            }
        });

        return new MantleScalarField(n, inner, outer, anomaly);
    }

    private static double SmoothStep(double x)
    {
        if (x <= 0.0) return 0.0;
        if (x >= 1.0) return 1.0;
        return x * x * (3.0 - 2.0 * x);
    }
}

/// <summary>
/// The signed anomaly scalar grid sampled on a Cartesian-in-shell lattice, plus the geometry to map
/// marching-cubes vertices (grid-index space) back to unit-sphere coordinates. Pure data.
/// </summary>
public readonly record struct MantleScalarField(
    int N,
    double InnerRadius,
    double OuterRadius,
    float[] Anomaly)
{
    /// <summary>Maps a grid-index vertex (in [0, N-1] per axis) to a unit-sphere-space coordinate.</summary>
    public (double X, double Y, double Z) GridIndexToWorld(float gx, float gy, float gz)
    {
        double span = 2.0 * OuterRadius;
        double x = -OuterRadius + (gx * span / (N - 1));
        double y = -OuterRadius + (gy * span / (N - 1));
        double z = -OuterRadius + (gz * span / (N - 1));
        return (x, y, z);
    }
}
