using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Rendering;
using FantaSim.App.World;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Shared;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// VISIBLE adaptive LOD — falsifiable acceptance for directive 4, slice 1
/// (vault/plans/2026-07-16-visible-adaptive-lod-slice-plan.md).
///
/// These tests END the "machinery exists but renders uniform" failure. The gate FAILS on uniform
/// output by construction: it requires boundary-band density >= 3x interior, total triangles <=
/// declared budget, and interiors COARSER than today's uniform baseline (pinned first).
///
/// TDD order (locked by the plan):
/// 1. Characterization test pinning today's uniform baseline densities.
/// 2. Density ratio test (initially RED — uniform output cannot pass).
/// 3. Determinism test: two builds -> identical vertex/index buffers.
/// 4. Criterion implementation via <see cref="VisibleLodProfile"/> makes density + determinism GREEN.
/// </summary>
public sealed class VisibleAdaptiveLodTests
{
    private static readonly NoiseParams NoNoise = new(Amplitude: 0.0);

    // A mobile-plate tick: the legacy parameterless GlobeReconstructor always ShowsPlateFeatures
    // (no regime gating), so tick 0 is a valid mobile-plate tick. ClassifyCellsAt(0) produces real
    // boundary classifications from the default 4-plate arrangement.
    private const long MobilePlateTick = 0L;

    // ─── Step 1: Characterization — pin today's UNIFORM baseline densities ─────────────────

    /// <summary>
    /// CHARACTERIZATION: pins the triangle density distribution of today's production adaptive
    /// configuration (FeatureWeightDeltaThreshold: 0.25, EdgeHeightDeltaThreshold: 0.02, MaxDepth: 2).
    /// The production config splits on height deltas which are nearly universal (adjacent cells
    /// almost always differ in elevation), producing effectively UNIFORM tessellation. This test
    /// records that fact so the nonuniform slice can prove interiors got COARSER.
    ///
    /// This test MUST PASS before any changes — it pins the baseline. If it fails, the baseline
    /// shifted and the density-ratio test's "coarser than baseline" assertion needs recalibration.
    /// </summary>
    [Fact]
    public void Characterization_TodayProductionConfigProducesEffectivelyUniformDensities()
    {
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);
        var elevations = BuildRealisticElevations(snapshot.CellCount);
        var features = BuildRealisticFeatures(snapshot);

        // TODAY's production config: height threshold 0.02 (splits almost everywhere), MaxDepth 2.
        var productionOptions = new AdaptiveSubdivisionOptions(
            MaxDepth: 2,
            EdgeHeightDeltaThreshold: 0.02,
            FeatureWeightDeltaThreshold: 0.25);

        var featureWeights = PlateSurfaceMeshFactoryLikeWeights(snapshot.CellCount, features);

        var caps = surfaces.BuildAdaptiveSurfaces(
            elevations,
            exaggeration: 0.0005,
            options: productionOptions,
            featureWeightsByCell: featureWeights);

        var totalTriangles = caps.Sum(c => c.Surface.TriangleCount);
        var baselineDensity = ComputeDensityHistogram(snapshot, caps);

        // Pin the baseline: the production config produces some number of triangles. The exact
        // value is not the point — the point is that the density RATIO (boundary/interior) is
        // close to 1.0 (uniform), NOT >= 3.0.
        Assert.True(totalTriangles > 0, "baseline must produce triangles");

        var boundaryDensity = baselineDensity.BoundaryTrianglesPerCell;
        var interiorDensity = baselineDensity.InteriorTrianglesPerCell;
        var baselineRatio = boundaryDensity / Math.Max(interiorDensity, 1e-10);

        // Record the baseline numbers for the summary.
        // The key characterization: the ratio is < 3.0 (effectively uniform). If this assertion
        // FAILS, today's config is already nonuniform and the slice's premise is wrong.
        Assert.True(baselineRatio < VisibleLodProfile.RequiredDensityRatio,
            $"Baseline ratio {baselineRatio:F2}x is already >= {VisibleLodProfile.RequiredDensityRatio}x. " +
            "The production config is not as uniform as the plan assumed — recalibrate the density test.");
    }

    // ─── Step 2: Density ratio test (RED until VisibleLodProfile is applied) ───────────────

    /// <summary>
    /// DENSITY GATE (falsifiable): at a mobile-plate tick, triangle density in the boundary band
    /// must be >= 3x the interior density, total triangles <= declared budget, and interiors
    /// COARSER than today's uniform baseline.
    ///
    /// This test FAILS on uniform output by construction. It was RED before VisibleLodProfile
    /// existed (production config produces ratio < 3.0) and turns GREEN once the nonuniform
    /// criterion (feature-weight-only splits) is applied.
    /// </summary>
    [Fact]
    public void Density_BoundaryBandAtLeast3xInterior_TotalWithinBudget_InteriorsCoarserThanBaseline()
    {
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var surfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);
        var elevations = BuildRealisticElevations(snapshot.CellCount);
        var features = BuildRealisticFeatures(snapshot);
        var featureWeights = PlateSurfaceMeshFactoryLikeWeights(snapshot.CellCount, features);

        // Nonuniform LOD profile: feature-weight-only splits, height disabled.
        var options = VisibleLodProfile.BuildOptions(maxDepth: 2, featureWeightDeltaThreshold: 0.12);

        var caps = surfaces.BuildAdaptiveSurfaces(
            elevations,
            exaggeration: 0.0005,
            options: options,
            featureWeightsByCell: featureWeights);

        var totalTriangles = caps.Sum(c => c.Surface.TriangleCount);
        var histogram = ComputeDensityHistogram(snapshot, caps);

        // 1. Total triangles <= declared budget.
        Assert.True(totalTriangles <= VisibleLodProfile.DeclaredBudget(3),
            $"Total triangles {totalTriangles} exceeds declared budget {VisibleLodProfile.DeclaredBudget(3)}.");

        // 2. Boundary density >= 3x interior density.
        var boundaryDensity = histogram.BoundaryTrianglesPerCell;
        var interiorDensity = histogram.InteriorTrianglesPerCell;
        var ratio = boundaryDensity / Math.Max(interiorDensity, 1e-10);

        Assert.True(ratio >= VisibleLodProfile.RequiredDensityRatio,
            $"Boundary density ratio {ratio:F2}x is below the required {VisibleLodProfile.RequiredDensityRatio}x. " +
            $"Boundary band: {histogram.BoundaryBandTriangles} triangles / {histogram.BoundaryBandCells} cells = {boundaryDensity:F4}. " +
            $"Deep interior: {histogram.DeepInteriorTriangles} triangles / {histogram.DeepInteriorCells} cells = {interiorDensity:F4}. " +
            "UNIFORM OUTPUT — the slice FAILED.");

        // 3. Interiors are COARSER than today's uniform baseline.
        // The nonuniform profile must NOT refine interiors — only boundaries. So the interior
        // triangle count per cell should be <= the baseline's interior density.
        var baselineSurfaces = new GlobePlateSurfaces(snapshot, noise: NoNoise);
        var baselineOptions = new AdaptiveSubdivisionOptions(
            MaxDepth: 2,
            EdgeHeightDeltaThreshold: 0.02,
            FeatureWeightDeltaThreshold: 0.25);
        var baselineCaps = baselineSurfaces.BuildAdaptiveSurfaces(
            elevations,
            exaggeration: 0.0005,
            options: baselineOptions,
            featureWeightsByCell: featureWeights);
        var baselineHistogram = ComputeDensityHistogram(snapshot, baselineCaps);

        Assert.True(interiorDensity <= baselineHistogram.InteriorTrianglesPerCell * 1.05,
            $"Interior density {interiorDensity:F4} is not coarser than baseline " +
            $"{baselineHistogram.InteriorTrianglesPerCell:F4}. The nonuniform profile must not refine interiors.");
    }

    // ─── Step 3: Determinism — two builds produce identical vertex/index buffers ──────────

    /// <summary>
    /// DETERMINISM: two independent builds at the same (tick, seed, params, R-budget) must produce
    /// bit-identical vertex and index buffers. The mesh is a pure function of its declared inputs
    /// — no camera dependency, no query history, no caching side effects. Terrain-diffusion
    /// adoption: conditioning on the causal bundle means identical inputs produce identical output.
    /// </summary>
    [Fact]
    public void Determinism_TwoBuildsAtSameIdentityProduceIdenticalBuffers()
    {
        var snapshot = new GlobeReconstructor(frequency: 3).BuildGlobe();
        var features = BuildRealisticFeatures(snapshot);
        var featureWeights = PlateSurfaceMeshFactoryLikeWeights(snapshot.CellCount, features);
        var elevations = BuildRealisticElevations(snapshot.CellCount);
        var options = VisibleLodProfile.BuildOptions(maxDepth: 2, featureWeightDeltaThreshold: 0.12);

        // Build 1.
        var surfaces1 = new GlobePlateSurfaces(snapshot, noise: NoNoise);
        var caps1 = surfaces1.BuildAdaptiveSurfaces(
            elevations,
            exaggeration: 0.0005,
            options: options,
            featureWeightsByCell: featureWeights)
            .OrderBy(c => c.PlateId)
            .ToArray();

        // Build 2: completely independent instance, same inputs.
        var surfaces2 = new GlobePlateSurfaces(snapshot, noise: NoNoise);
        var caps2 = surfaces2.BuildAdaptiveSurfaces(
            elevations,
            exaggeration: 0.0005,
            options: options,
            featureWeightsByCell: featureWeights)
            .OrderBy(c => c.PlateId)
            .ToArray();

        Assert.Equal(caps1.Length, caps2.Length);

        for (int p = 0; p < caps1.Length; p++)
        {
            Assert.Equal(caps1[p].PlateId, caps2[p].PlateId);

            // Vertex count must match.
            Assert.Equal(caps1[p].Surface.VertexCount, caps2[p].Surface.VertexCount);

            // Triangle count must match.
            Assert.Equal(caps1[p].Surface.TriangleCount, caps2[p].Surface.TriangleCount);

            // Index buffer must be bit-identical.
            Assert.Equal(caps1[p].Surface.Triangles, caps2[p].Surface.Triangles);

            // Vertex positions must be bit-identical (every component).
            for (int v = 0; v < caps1[p].Surface.VertexCount; v++)
            {
                Assert.Equal(caps1[p].Surface.Positions[v].X, caps2[p].Surface.Positions[v].X);
                Assert.Equal(caps1[p].Surface.Positions[v].Y, caps2[p].Surface.Positions[v].Y);
                Assert.Equal(caps1[p].Surface.Positions[v].Z, caps2[p].Surface.Positions[v].Z);
            }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────────────

    private static readonly Random DeterministicRng = new(42);

    // Realistic per-cell elevations (metres): vary per cell so the height threshold has something
    // to trigger on — this is what makes the production config effectively uniform (it splits
    // almost every edge because adjacent cells have different elevations).
    private static double[] BuildRealisticElevations(int cellCount)
    {
        var elevations = new double[cellCount];
        for (int i = 0; i < cellCount; i++)
            elevations[i] = ((i % 11) * 100.0) - 500.0;
        return elevations;
    }

    // Realistic CellCrustFeatures: classify cells at tick 0 and assign features to boundary cells.
    private static CellCrustFeature[] BuildRealisticFeatures(WorldGlobeSnapshot snapshot)
    {
        var reconstructor = new GlobeReconstructor(frequency: snapshot.Frequency);
        var classifications = reconstructor.ClassifyCellsAt(MobilePlateTick);

        var features = new CellCrustFeature[snapshot.CellCount];
        for (int i = 0; i < snapshot.CellCount; i++)
        {
            byte kind = classifications[i];
            if (kind > 0)
            {
                // Boundary cell: assign a feature with magnitude that drives the weight.
                features[i] = kind switch
                {
                    1 => new CellCrustFeature(1, 10_000.0), // Mountain
                    2 => new CellCrustFeature(2, 8_000.0),  // VolcanicArc
                    3 => new CellCrustFeature(3, 6_000.0),  // Trench
                    _ => new CellCrustFeature(4, 5_000.0),  // Ridge/Fault
                };
            }
            // Interior cells keep default (Kind=0, Magnitude=0).
        }
        return features;
    }

    // Mirrors PlateSurfaceMeshFactory.BuildAdaptiveFeatureWeights but without the internal access
    // restriction. Produces the same per-cell weight array the presentation binder feeds to the
    // adaptive builder.
    private static double[] BuildAdaptiveFeatureWeights(
        int cellCount,
        IReadOnlyList<CellCrustFeature> features)
    {
        var weights = new double[cellCount];
        int count = Math.Min(cellCount, features.Count);
        for (int i = 0; i < count; i++)
        {
            var feature = features[i];
            if (feature.Kind == 0)
                continue;

            weights[i] = Math.Clamp(
                0.35 + Math.Log10(1.0 + Math.Max(0.0, feature.Magnitude)) / 2.0,
                0.0,
                1.0);
        }
        return weights;
    }

    // Alias for clarity — this is the same computation the production binder does.
    private static double[] PlateSurfaceMeshFactoryLikeWeights(
        int cellCount,
        CellCrustFeature[] features)
        => BuildAdaptiveFeatureWeights(cellCount, features);

    // Density histogram: partitions cells by graph-distance-to-nearest-boundary, counts triangles
    // per cell in each partition. The boundary band = cells at distance 0 or 1 from a boundary
    // cell (boundary cells + their immediate neighbors). The deep interior = distance >= 2.
    // This matches the plan's "partition faces by distance-to-nearest-boundary".
    private sealed record DensityHistogram(
        int BoundaryBandCells,
        int DeepInteriorCells,
        int BoundaryBandTriangles,
        int DeepInteriorTriangles)
    {
        public double BoundaryTrianglesPerCell =>
            BoundaryBandCells > 0 ? (double)BoundaryBandTriangles / BoundaryBandCells : 0.0;
        public double InteriorTrianglesPerCell =>
            DeepInteriorCells > 0 ? (double)DeepInteriorTriangles / DeepInteriorCells : 0.0;
    }

    private static DensityHistogram ComputeDensityHistogram(
        WorldGlobeSnapshot snapshot,
        IReadOnlyList<PlateCap> caps)
    {
        var reconstructor = new GlobeReconstructor(frequency: snapshot.Frequency);
        var classifications = reconstructor.ClassifyCellsAt(MobilePlateTick);

        // Build cell adjacency from shared vertices (replicates PlateSurfaceMeshFactory's method
        // since that type is internal to App.Presentation which this test project does not reference).
        var neighbors = BuildCellNeighborsFromSharedVertices(snapshot);

        // Classify cells by graph distance to nearest boundary cell.
        // Distance 0 = boundary cell (classification > 0).
        // Distance 1 = neighbor of a boundary cell.
        // Distance 2+ = deep interior.
        var distance = new int[snapshot.CellCount];
        for (int i = 0; i < snapshot.CellCount; i++)
            distance[i] = classifications[i] > 0 ? 0 : int.MaxValue;

        // BFS: distance 1 = neighbors of distance-0 cells.
        for (int cell = 0; cell < snapshot.CellCount; cell++)
        {
            if (distance[cell] != 0)
                continue;
            if (!neighbors.TryGetValue(cell, out var nbrs))
                continue;
            foreach (int nb in nbrs)
            {
                if (distance[nb] > 1)
                    distance[nb] = 1;
            }
        }

        int boundaryBandCells = 0;
        int deepInteriorCells = 0;
        for (int i = 0; i < snapshot.CellCount; i++)
        {
            if (distance[i] <= 1)
                boundaryBandCells++;
            else
                deepInteriorCells++;
        }

        // Count triangles per cell: each face in a cap's index buffer maps to a source cell via
        // PlateCap.CellIds (parallel to faces). Sum triangles by boundary-band/deep-interior.
        int boundaryBandTriangles = 0;
        int deepInteriorTriangles = 0;

        foreach (var cap in caps)
        {
            int faceCount = cap.Surface.TriangleCount;
            for (int f = 0; f < faceCount; f++)
            {
                int cellId = f < cap.CellIds.Length ? cap.CellIds[f] : -1;
                if (cellId >= 0 && cellId < distance.Length)
                {
                    if (distance[cellId] <= 1)
                        boundaryBandTriangles++;
                    else
                        deepInteriorTriangles++;
                }
                else
                {
                    // Unknown cell id — attribute to deep interior (conservative).
                    deepInteriorTriangles++;
                }
            }
        }

        return new DensityHistogram(
            boundaryBandCells, deepInteriorCells,
            boundaryBandTriangles, deepInteriorTriangles);
    }

    private static IReadOnlyDictionary<int, int[]> BuildCellNeighborsFromSharedVertices(WorldGlobeSnapshot snapshot)
    {
        var edgeToCells = new Dictionary<(int, int), List<int>>();
        foreach (var cell in snapshot.Cells)
        {
            AddEdge(edgeToCells, cell.CellId, cell.C0, cell.C1);
            AddEdge(edgeToCells, cell.CellId, cell.C1, cell.C2);
            AddEdge(edgeToCells, cell.CellId, cell.C2, cell.C0);
        }

        var result = new Dictionary<int, int[]>(snapshot.CellCount);
        var cellSet = new HashSet<int>();
        foreach (var cell in snapshot.Cells)
        {
            cellSet.Clear();
            AddNeighbors(cellSet, edgeToCells, cell.C0, cell.C1, cell.CellId);
            AddNeighbors(cellSet, edgeToCells, cell.C1, cell.C2, cell.CellId);
            AddNeighbors(cellSet, edgeToCells, cell.C2, cell.C0, cell.CellId);
            result[cell.CellId] = cellSet.ToArray();
        }

        return result;

        static void AddEdge(Dictionary<(int, int), List<int>> map, int cellId, GlobeVec3 a, GlobeVec3 b)
        {
            var key = VertexKey(a, b);
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                map[key] = list;
            }
            list.Add(cellId);
        }

        static void AddNeighbors(HashSet<int> set, Dictionary<(int, int), List<int>> map, GlobeVec3 a, GlobeVec3 b, int self)
        {
            var key = VertexKey(a, b);
            if (map.TryGetValue(key, out var list))
            {
                foreach (var id in list)
                    if (id != self)
                        set.Add(id);
            }
        }

        static (int, int) VertexKey(GlobeVec3 a, GlobeVec3 b)
        {
            int ka = HashVertex(a);
            int kb = HashVertex(b);
            return ka < kb ? (ka, kb) : (kb, ka);
        }

        static int HashVertex(GlobeVec3 v)
            => HashCode.Combine(v.X.GetHashCode(), v.Y.GetHashCode(), v.Z.GetHashCode());
    }
}