using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Rendering;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Shared;
using Godot;

namespace FantaSim.App.Presentation;

/// <summary>
/// Static pure builders for plate-cap mesh inputs (vertex colors, Continents membership
/// colors/frontier, cell adjacency, adaptive-feature/detail sampling). Extracted from
/// PlanetPresentationBinder 2026-07-11
/// (vault/plans/2026-07-11-planet-presentation-binder-split-plan.md) — none of these carry binder
/// instance state, so the move is signature-preserving (verbatim bodies, visibility widened to
/// internal for cross-partial/test use). NOTE: BuildCellAppearance stayed in the binder rather
/// than moving here — see AGENT-SUMMARY.md Task 2 deviations (App.Architecture.Tests
/// ContinentProxyBanTests.ProvinceTint_UsageIsConfinedToWhitelist hard-codes an exact-path
/// allowlist that does not include this file, and that test is outside this refactor's edit
/// scope).
/// </summary>
internal static class PlateSurfaceMeshFactory
{
    internal static IReadOnlyList<double>? BuildAdaptiveFeatureWeights(
        int cellCount,
        IReadOnlyList<CellCrustFeature>? features)
    {
        if (features is null || features.Count == 0)
            return null;

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

    internal static Func<CartesianPoint3, double>? BuildTectonicDetailSampler(
        WorldGlobeSnapshot snapshot,
        IReadOnlyList<CellCrustFeature>? features,
        NoiseParams relief,
        GlobeViewMode viewMode,
        bool isTerrain)
    {
        if (!isTerrain || relief.Amplitude == 0.0 || features is null || features.Count == 0)
            return null;

        var sampler = new TectonicDetailSampler(
            snapshot,
            features,
            relief,
            PlateSurfaceReliefFabric.InteriorAmplitudeMultiplierForView(viewMode),
            PlateSurfaceReliefFabric.RidgeActiveFeaturesForView(viewMode),
            PlateSurfaceReliefFabric.ActiveAmplitudeMultiplierForView(viewMode));
        return sampler.Sample;
    }

    // Unit-sphere center per cell id: normalized corner mean of the snapshot's triangular cells.
    // Indexed by CellId (not list order) so the tint samples the direction of the cell it colors.
    internal static CartesianPoint3?[] BuildCellCenters(int cellCount, IReadOnlyList<GlobeCell> cells)
    {
        var centers = new CartesianPoint3?[cellCount];
        foreach (var cell in cells)
        {
            if (cell.CellId < 0 || cell.CellId >= cellCount)
                continue;
            double x = (cell.C0.X + cell.C1.X + cell.C2.X) / 3.0;
            double y = (cell.C0.Y + cell.C1.Y + cell.C2.Y) / 3.0;
            double z = (cell.C0.Z + cell.C1.Z + cell.C2.Z) / 3.0;
            double len = Math.Sqrt((x * x) + (y * y) + (z * z));
            if (len < 1e-9)
                continue;
            centers[cell.CellId] = new CartesianPoint3(x / len, y / len, z / len);
        }
        return centers;
    }

    // Converts the host-side per-cell Godot.Color ramp output back to the Godot-free RampColor the
    // App.World plugin envelope consumes, runs the global per-vertex colour gather, and indexes the
    // result by plate id so BuildPlateMesh can look up each cap's per-vertex colours in one read.
    internal static IReadOnlyDictionary<int, RampColor[]> BuildPerPlateVertexColors(
        GlobePlateSurfaces surfaces,
        RampColor[] perCellColor)
    {
        var perPlate = surfaces.BuildVertexColors(perCellColor);
        var byId = new Dictionary<int, RampColor[]>(perPlate.Count);
        foreach (var p in perPlate)
            byId[p.PlateId] = p.Colors;
        return byId;
    }

    internal static RampColor[] BuildContinentsCellColors(
        int cellCount,
        IReadOnlyDictionary<int, double>? continentalFractionByCell)
    {
        var colors = new RampColor[cellCount];
        for (int i = 0; i < cellCount; i++)
        {
            double fraction = continentalFractionByCell?.TryGetValue(i, out var f) == true ? f : 0.0;
            colors[i] = ContinentsPalette.ToneFor(fraction >= 0.5, isFrontier: false);
        }

        return colors;
    }

    internal static byte[] BuildFractionContourFrontier(
        WorldGlobeSnapshot snapshot,
        IReadOnlyDictionary<int, double>? continentalFractionByCell)
    {
        int cellCount = snapshot.CellCount;
        var frontier = new byte[cellCount];
        if (continentalFractionByCell is null || continentalFractionByCell.Count == 0 || snapshot.Cells.Count == 0)
            return frontier;

        IReadOnlyDictionary<int, int[]> neighbors = BuildCellNeighborsFromSharedVertices(snapshot);
        for (int cellId = 0; cellId < cellCount; cellId++)
        {
            if (!continentalFractionByCell.TryGetValue(cellId, out var f))
                continue;
            bool selfLand = f >= 0.5;
            if (!neighbors.TryGetValue(cellId, out var nbrs))
                continue;
            foreach (int n in nbrs)
            {
                double nf = continentalFractionByCell.TryGetValue(n, out var v) ? v : 0.0;
                if ((nf >= 0.5) != selfLand)
                {
                    frontier[cellId] = 1;
                    break;
                }
            }
        }

        return frontier;
    }

    // Two geodesic cells are neighbours when they share an edge (two vertices). The snapshot already
    // carries the three corners per cell, so we rebuild adjacency locally without pulling in UnifyCell.
    internal static IReadOnlyDictionary<int, int[]> BuildCellNeighborsFromSharedVertices(WorldGlobeSnapshot snapshot)
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
