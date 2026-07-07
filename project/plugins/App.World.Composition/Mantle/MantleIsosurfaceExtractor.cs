using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FantaSim.App.World;
using FantaSim.Geosphere.Asthenosphere.Convection;
using UnifyMaths;

namespace FantaSim.App.World.Composition;

// Mantle x-ray view (M-A), task 3: the full pipeline from presentation boundary arcs to the pure
// four-surface isosurface DTOs the resident presentation seam consumes.
//
// Pipeline (all pure, deterministic, Godot-free):
//   arcs -> MantleHistoryAdapter -> PlateBoundaryHistory
//        -> new MantleAnomalyField(config, history, tick)   [engine: volumetric T'(dir, radius, tick)]
//        -> MantleFieldSampler -> ONE signed scalar grid on the Cartesian shell grid
//        -> MarchingCubes at four isovalues (cold outer/inner on the NEGATED field, warm outer/inner
//           on the field itself — two thresholds per polarity per the method-lock)
//        -> vertices mapped to unit-sphere coordinates; normals from MantleAnomalyField.GradientAt
//           (outward-facing per polarity), NOT from triangle geometry
//        -> MantleIsosurfaceSet
// The engine types (MantleAnomalyField, MantleFieldConfig, PlateBoundaryHistory) live entirely
// inside this class; the return type is the engine-free MantleIsosurfaceSet DTO (C1 stays intact).

/// <summary>
/// Builds the mantle x-ray isosurface set for one tick from the engine's volumetric
/// <see cref="MantleAnomalyField"/>. Engine-free on the OUTSIDE (returns <see cref="MantleIsosurfaceSet"/>);
/// consumes the engine convection package internally.
/// </summary>
public static class MantleIsosurfaceExtractor
{
    /// <summary>Fixed seed for the engine field's deterministic PRNG (plume seeding, fBm hashing).
    /// A named constant — the same world always shows the same mantle.</summary>
    public const long FieldSeed = 0x4D414E544C45; // "MANTLE"

    /// <summary>
    /// Extract the four isosurfaces at <paramref name="tick"/> from the presentation boundary arcs.
    /// </summary>
    /// <param name="arcs">Typed plate-boundary arcs at the bound tick (null/empty -> blanket + plumes only).</param>
    /// <param name="tick">The simulation tick the field is conditioned on.</param>
    /// <param name="plateOnsetTick">Plate onset tick (adapter uses it as every segment's ActiveSinceTick).</param>
    /// <param name="config">Sampling + threshold configuration (null -> <see cref="MantleViewConfig.Default"/>).</param>
    public static MantleIsosurfaceSet Extract(
        IReadOnlyList<PlateBoundaryArc>? arcs,
        long tick,
        long plateOnsetTick,
        MantleViewConfig? config = null)
    {
        var cfg = config ?? MantleViewConfig.Default;
        var history = MantleHistoryAdapter.Build(arcs, plateOnsetTick);
        return ExtractFromHistory(history, tick, cfg);
    }

    /// <summary>
    /// Extract the four isosurfaces at <paramref name="tick"/> from a prebuilt boundary history.
    /// An EMPTY history is still a valid world: the engine field renders the basal blanket and
    /// deterministic plumes with no slabs/curtains (the pre-onset mantle).
    /// </summary>
    public static MantleIsosurfaceSet ExtractFromHistory(
        PlateBoundaryHistory history,
        long tick,
        MantleViewConfig? config = null)
    {
        var cfg = config ?? MantleViewConfig.Default;
        var fieldConfig = new MantleFieldConfig
        {
            Seed = FieldSeed,
            CmbRadius = cfg.InnerRadius,
            // Look-loop (north-star eye gate, criterion 1): slabs must read as thin HANGING
            // CURTAINS, not rounded lobes. Thinner Gaussian sheets + denser polyline sampling
            // keep the along-trench continuity while flattening the cross-section.
            SlabSheetThickness = 0.03,
            SlabPolylineSamples = 20,
        };
        var field = new MantleAnomalyField(fieldConfig, history, tick);

        var grid = MantleFieldSampler.Sample(field, cfg);

        // Cold surfaces come from the NEGATED signed field (cold = negative anomaly), shared by
        // both cold thresholds so the array is built once.
        var negated = new float[grid.Anomaly.Length];
        for (int i = 0; i < negated.Length; i++)
            negated[i] = -grid.Anomaly[i];

        var coldOuter = ExtractChannel(negated, grid, field, (float)cfg.ColdOuterThreshold, coldPolarity: true);
        var coldInner = ExtractChannel(negated, grid, field, (float)cfg.ColdInnerThreshold, coldPolarity: true);
        var warmOuter = ExtractChannel(grid.Anomaly, grid, field, (float)cfg.WarmOuterThreshold, coldPolarity: false);
        var warmInner = ExtractChannel(grid.Anomaly, grid, field, (float)cfg.WarmInnerThreshold, coldPolarity: false);

        return new MantleIsosurfaceSet(tick, coldOuter, coldInner, warmOuter, warmInner);
    }

    private static MantleIsosurfaceMesh ExtractChannel(
        float[] scalars,
        MantleScalarField grid,
        MantleAnomalyField field,
        float isovalue,
        bool coldPolarity)
    {
        MarchingCubes.Extract(scalars, grid.N, grid.N, grid.N, isovalue, out var verts, out var tris);
        if (tris.Count == 0)
            return MantleIsosurfaceMesh.Empty;

        // Map grid-index vertices to unit-sphere coordinates, then normals from the field gradient.
        // Outward normal of the enclosed region: warm volume is {f > t} whose outside is downhill in
        // f (normal = -grad f); cold volume is {f < -t} whose outside is uphill (normal = +grad f).
        int vertexCount = verts.Count / 3;
        var world = new float[verts.Count];
        var normals = new float[verts.Count];
        double sign = coldPolarity ? 1.0 : -1.0;

        Parallel.For(0, vertexCount, i =>
        {
            var (x, y, z) = grid.GridIndexToWorld(verts[3 * i], verts[3 * i + 1], verts[3 * i + 2]);
            world[3 * i] = (float)x;
            world[3 * i + 1] = (float)y;
            world[3 * i + 2] = (float)z;

            var p = new Vector3D(x, y, z);
            var g = field.GradientAt(p);
            double len = g.Length();
            Vector3D nrm;
            if (len > 1e-12)
            {
                nrm = new Vector3D(sign * g.X / len, sign * g.Y / len, sign * g.Z / len);
            }
            else
            {
                // Degenerate gradient (flat spot): fall back to the radial direction.
                double r = Math.Sqrt(x * x + y * y + z * z);
                nrm = r > 1e-12 ? new Vector3D(x / r, y / r, z / r) : new Vector3D(0, 0, 1);
            }

            normals[3 * i] = (float)nrm.X;
            normals[3 * i + 1] = (float)nrm.Y;
            normals[3 * i + 2] = (float)nrm.Z;
        });

        return new MantleIsosurfaceMesh(world, normals, tris.ToArray());
    }
}
