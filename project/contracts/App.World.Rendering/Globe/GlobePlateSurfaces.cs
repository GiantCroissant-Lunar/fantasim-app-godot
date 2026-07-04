using System;
using System.Collections.Generic;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Rendering;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Globe.Core;
using FantaSim.Cartography.Shared;

namespace FantaSim.App.World.Globe;

/// <summary>
/// T3 (Godot-free) un-shattering step: turns a <see cref="WorldGlobeSnapshot"/> (tessellation cells
/// with their owning plate and corner positions) plus a per-cell elevation field into ONE
/// <b>watertight</b> <see cref="GlobeSurface"/> cap per plate, using the cartography
/// <see cref="GlobeSurfaceBuilder"/>.
///
/// <para>
/// The old globe mesh drew one triangle per cell with three UNSHARED corners, each pushed out by that
/// cell's own elevation; neighbouring cells at different heights left gaps and the ball cracked. Here,
/// within a plate, cells that meet at a corner SHARE that corner (one local vertex id), and the shared
/// corner sits at a SINGLE height — the mean of the cells touching it
/// (<see cref="GlobeSurfaceBuilder.GatherVertexHeights"/>) — so adjacent triangles meet exactly and the
/// cap is watertight by construction.
/// </para>
///
/// <para>
/// <b>Topology is tick-invariant.</b> Cells never change plate and the tessellation is fixed, so the
/// per-plate shared-vertex topology (unique corner positions + the local index buffer + the parallel
/// cell-id list) is built ONCE in the constructor and cached. Only the per-vertex heights — and hence
/// the displaced positions/normals — change per tick, recomputed in <see cref="BuildSurfaces"/>.
/// </para>
///
/// <para>
/// <b>Relief = envelope (sim truth) + seeded peaks (renderer).</b> The per-cell elevation field is the
/// low-frequency tectonic ENVELOPE (continents/oceans); on its own every facet sits near its neighbours
/// → a smooth ball. So a high-frequency seeded PEAKS layer (cartography <see cref="NoiseRelief"/>) is
/// added per vertex to make the surface rugged/faceted. The peaks are sampled on each cap's BASE
/// (tick-0) unit vertex positions and cached (metres), so they are tick-invariant, reproducible (never
/// stored), ride the plate as it rotates, and — being a pure function of the shared base position — give
/// two caps' coincident boundary vertices the SAME bump, keeping the surface watertight. Tune via
/// <see cref="DefaultPeaks"/>. The peaks change ONLY the surface FORM; colour (per-cell elevation) is
/// untouched.
/// </para>
///
/// <para>
/// <b>How shared vertices are obtained.</b> <c>UnifyCell.GeodesicSphereTessellation</c> builds a shared
/// vertex/face index buffer internally but does NOT expose it publicly (only per-face corner POSITIONS
/// via <c>GetBoundary</c>, which is what the snapshot carries as <see cref="GlobeCell.C0"/>/C1/C2). So
/// this class recovers the shared topology by DEDUPING the per-face corner positions within a tight
/// epsilon: corners that quantize to the same unit-sphere grid cell are the same icosphere vertex and
/// get one shared local vertex id. At the tessellation frequencies used here distinct vertices are
/// separated by far more than the epsilon, so the dedupe is exact.
/// </para>
/// </summary>
public sealed class GlobePlateSurfaces
{
    // Quantization step for position dedupe. Corners are unit-length (|v| ~= 1) and come from a lat/lon
    // round-trip, so the SAME icosphere vertex can differ by a few ulps between faces; distinct vertices
    // (even at frequency 5: 20480 cells) are separated by >> this. 1e-5 collapses the jitter without ever
    // merging two genuinely different corners.
    private const double DedupeEpsilon = 1e-5;
    private const double DedupeScale = 1.0 / DedupeEpsilon;

    // ───────────────────────── seeded "peaks" relief (TUNE HERE) ──────────────────────────────────
    //
    // High-frequency noise added to each vertex's envelope height so every facet jumps to its own
    // height → rugged, faceted (Astroneer-style) surface instead of a smooth ball. This is a RENDERER
    // concern, never sim truth: it is a pure, deterministic function of the vertex's BASE (tick-0) unit
    // position + seed (see NoiseRelief), so it is reproducible, never stored, rides the plate as it
    // rotates, and gives two caps' coincident boundary vertices the SAME bump (watertight preserved).
    //
    // Units: Amplitude is in METRES, same as the envelope (continents +500 m / oceans −500 m). The final
    // per-vertex height fed to the cartography builder is (envelope_m + noise_m) × exaggeration, where
    // exaggeration (≈0.00012, in GlobeView) maps metres → unit-sphere radius.
    //
    //   Amplitude      ≈250–400 m makes adjacent cells clearly differ in height without pure static.
    //                  300 m ≈ 0.6× the ±500 m envelope: every facet bumps, continents/oceans still read.
    //   BaseFrequency  5–9 at freq-3 (1280 cells): bumps a few cells wide. 7 ≈ one bump per ~few cells.
    //   Octaves        4: a base swell + three finer harmonics → natural, non-uniform ruggedness.
    //
    // The human tunes these by eye; they are the single knob and intentionally easy to find.
    public static readonly NoiseParams DefaultPeaks = new(
        Seed: 1337,
        BaseFrequency: 16.0,
        Octaves: 4,
        Lacunarity: 2.0,
        Gain: 0.5,
        Amplitude: 300.0,    // metres
        Ridged: false);

    private readonly IGlobeSurfaceBuilder _builder;
    private readonly IAdaptiveGlobeSurfaceBuilder _adaptiveBuilder;
    private readonly IReadOnlyList<PlateTopology> _plates;
    private readonly Func<CartesianPoint3, double> _detailSampler;

    // Global shared-vertex topology across ALL plates (built once, tick-invariant). A corner on the
    // boundary between plate A and plate B is ONE global vertex; its envelope height is the mean over
    // EVERY incident cell (both plates), so both plates displace that corner to the same radius and
    // the seam stays watertight under non-uniform elevation. Per-plate topologies reference this map
    // to read off their local vertex heights (see BuildSurfaces).
    private readonly CartesianPoint3[] _globalVertices;
    private readonly int[] _globalTriangles;
    private readonly int[] _globalCellIds;

    /// <summary>
    /// Builds and caches the watertight per-plate topology from <paramref name="snapshot"/>. Plates with
    /// no cells are omitted. Plate caps are returned in ascending <see cref="GlobeCell.PlateId"/> order.
    /// </summary>
    /// <param name="snapshot">The tessellation + per-cell plate ownership + corner positions.</param>
    /// <param name="builder">Surface builder (defaults to the cartography <see cref="GlobeSurfaceBuilder"/>).</param>
    /// <param name="noise">
    /// Seeded peaks-relief parameters (defaults to <see cref="DefaultPeaks"/>). Pass
    /// <c>new NoiseParams(Amplitude: 0)</c> to disable the relief (pure tectonic envelope).
    /// </param>
    /// <param name="detailSampler">
    /// Optional pure relief sampler in metres. When supplied, it supersedes <paramref name="noise"/>
    /// for original vertices and adaptive subdivision midpoints; identical base positions must return
    /// identical values to preserve cross-plate watertightness.
    /// </param>
    public GlobePlateSurfaces(
        WorldGlobeSnapshot snapshot,
        IGlobeSurfaceBuilder? builder = null,
        NoiseParams? noise = null,
        Func<CartesianPoint3, double>? detailSampler = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _builder = builder ?? new GlobeSurfaceBuilder();
        _adaptiveBuilder = _builder as IAdaptiveGlobeSurfaceBuilder ?? new AdaptiveGlobeSurfaceBuilder(_builder);
        var peaks = noise ?? DefaultPeaks;
        _detailSampler = detailSampler ?? (pos => NoiseRelief.Sample(pos, peaks));
        (_globalVertices, _globalTriangles, _globalCellIds) = BuildGlobalTopology(snapshot);
        _plates = BuildPlateTopologies(snapshot, _detailSampler, _globalVertices);
    }

    /// <summary>The plate ids that have at least one cell (ascending), in cap order.</summary>
    public IReadOnlyList<int> PlateIds
    {
        get
        {
            var ids = new int[_plates.Count];
            for (int i = 0; i < _plates.Count; i++) ids[i] = _plates[i].PlateId;
            return ids;
        }
    }

    /// <summary>
    /// Per tick: build one watertight <see cref="GlobeSurface"/> cap per (non-empty) plate from the
    /// per-cell <paramref name="elevationsByCell"/> (indexed by cell id). For each plate the face
    /// elevations are gathered to per-vertex means (in metres, watertight across plates), the seeded
    /// peaks noise is added, and the total goes through the height lens:
    /// <c>displacement = sign(m) * |m|^heightExponent * exaggeration</c>.
    /// </summary>
    /// <param name="elevationsByCell">Per-cell elevation (metres), indexed by cell id.</param>
    /// <param name="exaggeration">Displacement scale applied after the profile.</param>
    /// <param name="heightExponent">
    /// Height-profile exponent (scale rule S1's non-linear lens). 1.0 (default) is the historical
    /// linear lens. Below 1.0 compresses the extreme-to-typical relief ratio — e.g. 0.5 turns a
    /// 100:1 peak-to-plains field into 10:1 — so interior relief reads at planet scale without
    /// orogenic peaks becoming spears. The profile applies to the FINAL vertex metres (envelope
    /// mean + noise), keeping the lens watertight: shared corners see identical inputs on every
    /// incident plate, so they map to identical radii.
    /// </param>
    public IReadOnlyList<PlateCap> BuildSurfaces(
        IReadOnlyList<double> elevationsByCell,
        double exaggeration,
        double heightExponent = 1.0,
        double baseRadius = GlobeSurfaceBuilder.DefaultRadius,
        double maxDisplacementUnitRadius = double.PositiveInfinity)
    {
        if (maxDisplacementUnitRadius < 0.0 || double.IsNaN(maxDisplacementUnitRadius))
            throw new ArgumentOutOfRangeException(nameof(maxDisplacementUnitRadius), "Max displacement must be non-negative and not NaN.");

        var plateVertexHeights = BuildPlateVertexHeights(elevationsByCell, exaggeration, heightExponent, maxDisplacementUnitRadius);
        var caps = new PlateCap[_plates.Count];
        for (int p = 0; p < _plates.Count; p++)
        {
            var plate = _plates[p];

            var surface = _builder.Build(
                plate.LocalVertices, plate.LocalTriangles, plateVertexHeights[p], baseRadius);

            caps[p] = new PlateCap(plate.PlateId, plate.CellIds, surface, VertexProvenance: null);
        }
        return caps;
    }

    public IReadOnlyList<PlateCap> BuildAdaptiveSurfaces(
        IReadOnlyList<double> elevationsByCell,
        double exaggeration,
        AdaptiveSubdivisionOptions options,
        double heightExponent = 1.0,
        IReadOnlyList<double>? featureWeightsByCell = null,
        double baseRadius = GlobeSurfaceBuilder.DefaultRadius,
        double maxDisplacementUnitRadius = double.PositiveInfinity)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (heightExponent <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(heightExponent), "Height exponent must be positive.");
        if (maxDisplacementUnitRadius < 0.0 || double.IsNaN(maxDisplacementUnitRadius))
            throw new ArgumentOutOfRangeException(nameof(maxDisplacementUnitRadius), "Max displacement must be non-negative and not NaN.");

        // PRE-LENS per-vertex metres: global envelope mean + plate.VertexNoiseMetres. The lens is
        // applied INSIDE the adaptive builder via HeightFinalizer so the non-linear profile acts on
        // the resampled raw metres (originals AND midpoints), preserving the lens's tuned semantics.
        var plateVertexMetres = BuildPlateVertexMetres(elevationsByCell);
        var plateVertexFeatureWeights = featureWeightsByCell is null
            ? null
            : BuildPlateVertexFeatureWeights(featureWeightsByCell);
        bool linear = heightExponent == 1.0;
        double Finalizer(double m)
        {
            double lensed = linear
                ? m * exaggeration
                : Math.Sign(m) * Math.Pow(Math.Abs(m), heightExponent) * exaggeration;
            return ClampDisplacement(lensed, maxDisplacementUnitRadius);
        }
        double Sampler(CartesianPoint3 pos) => _detailSampler(pos);

        var caps = new PlateCap[_plates.Count];
        for (int p = 0; p < _plates.Count; p++)
        {
            var plate = _plates[p];
            var adaptive = _adaptiveBuilder.BuildAdaptive(
                plate.LocalVertices,
                plate.LocalTriangles,
                plateVertexMetres[p],
                options with
                {
                    Radius = baseRadius,
                    VertexFeatureWeights = plateVertexFeatureWeights?[p],
                    HeightFinalizer = Finalizer,
                    DetailSampler = Sampler,
                });
            var cellIds = MapSourceTriangleIdsToCellIds(adaptive.SourceTriangleIds, plate.CellIds);
            caps[p] = new PlateCap(plate.PlateId, cellIds, adaptive.Surface, adaptive.VertexProvenance);
        }

        return caps;
    }

    /// <summary>
    /// Per tick (or per regime): the colour analogue of <see cref="BuildSurfaces"/>. Turns a per-cell
    /// ramp colour field into ONE per-vertex colour array per plate, where each shared vertex gets
    /// the component-wise MEAN of the ramp colours of every cell incident to it — GLOBALLY across all
    /// plates. A boundary corner shared by plates A and B sees both plates' cells in the gather, so
    /// both caps read the same colour at that corner and the cross-plate seam shows no colour step:
    /// the colour envelope is watertight the same way the elevation envelope is.
    /// </summary>
    /// <para>
    /// Reuses the SAME cached global topology (shared-vertex array, global triangle/cell-id buffers,
    /// per-plate <c>LocalToGlobal</c> maps) the constructor built for the elevation gather — no
    /// parallel topology, no second dedupe. Only the per-face values change between calls.
    /// </para>
    /// <param name="perCellColor">
    /// Per-cell <see cref="RampColor"/> (the ramp output for that cell's elevation), indexed by cell
    /// id. Cells outside the array's range contribute black, matching the elevation fallback to 0.0.
    /// </param>
    /// <returns>
    /// One <see cref="PlateVertexColors"/> per non-empty plate (ascending <see cref="GlobeCell.PlateId"/>
    /// order, same order as <see cref="BuildSurfaces"/>). Each <see cref="PlateVertexColors.Colors"/>
    /// array is parallel to that plate's local shared-vertex array — index it by the same local
    /// vertex id used for <c>Surface.Positions</c>.
    /// </returns>
    public IReadOnlyList<PlateVertexColors> BuildVertexColors(IReadOnlyList<RampColor> perCellColor)
    {
        ArgumentNullException.ThrowIfNull(perCellColor);

        // Global per-FACE colors in global face order, then gather to per-VERTEX means across ALL
        // incident cells of ALL plates — the exact shape of BuildSurfaces' elevation gather. A
        // boundary corner shared by plates A and B sees both plates' cells here, so its mean is
        // identical regardless of which plate reads it back through LocalToGlobal.
        var globalFaceColors = new RampColor[_globalCellIds.Length];
        for (int f = 0; f < _globalCellIds.Length; f++)
        {
            int cellId = _globalCellIds[f];
            globalFaceColors[f] = (cellId >= 0 && cellId < perCellColor.Count)
                ? perCellColor[cellId]
                : default;
        }

        var globalVertexColors = VertexColorEnvelope.GatherVertexColors(
            _globalVertices.Length, _globalTriangles, globalFaceColors);

        var result = new PlateVertexColors[_plates.Count];
        for (int p = 0; p < _plates.Count; p++)
        {
            var plate = _plates[p];
            var local = new RampColor[plate.LocalVertices.Length];
            for (int v = 0; v < local.Length; v++)
                local[v] = globalVertexColors[plate.LocalToGlobal[v]];
            result[p] = new PlateVertexColors(plate.PlateId, local);
        }
        return result;
    }

    private double[][] BuildPlateVertexHeights(
        IReadOnlyList<double> elevationsByCell,
        double exaggeration,
        double heightExponent,
        double maxDisplacementUnitRadius = double.PositiveInfinity)
    {
        ArgumentNullException.ThrowIfNull(elevationsByCell);
        if (heightExponent <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(heightExponent), "Height exponent must be positive.");

        var plateVertexMetres = BuildPlateVertexMetres(elevationsByCell);
        bool linear = heightExponent == 1.0;
        var result = new double[_plates.Count][];
        for (int p = 0; p < _plates.Count; p++)
        {
            var plate = _plates[p];
            var perVertexHeights = new double[plate.LocalVertices.Length];
            for (int v = 0; v < perVertexHeights.Length; v++)
            {
                double metres = plateVertexMetres[p][v];
                double lensed = linear
                    ? metres * exaggeration
                    : Math.Sign(metres) * Math.Pow(Math.Abs(metres), heightExponent) * exaggeration;
                perVertexHeights[v] = ClampDisplacement(lensed, maxDisplacementUnitRadius);
            }
            result[p] = perVertexHeights;
        }

        return result;
    }

    // Silhouette budget (north-star spec §1): pure clamp on the FINALIZED displacement. Sign is
    // preserved so basins stay depressions; finite caps leave the limb a circle; +inf is a no-op.
    // Pure function of the finalized height -> shared corners clamp identically -> seams stay
    // watertight.
    private static double ClampDisplacement(double displacement, double maxDisplacementUnitRadius)
        => double.IsInfinity(maxDisplacementUnitRadius)
            ? displacement
            : Math.Sign(displacement) * Math.Min(Math.Abs(displacement), maxDisplacementUnitRadius);

    // Pre-lens per-vertex metres: global envelope mean (across ALL incident cells of ALL plates) +
    // plate.VertexNoiseMetres. The lens (linear or non-linear) is applied by the caller, so this
    // returns the RAW metres the adaptive builder needs for midpoint detail resampling.
    private double[][] BuildPlateVertexMetres(IReadOnlyList<double> elevationsByCell)
    {
        ArgumentNullException.ThrowIfNull(elevationsByCell);

        // Global per-FACE elevations (METRES — the lens applies at the end) in global face order,
        // then gather to per-VERTEX means across ALL incident cells of ALL plates. A boundary corner
        // shared by plates A and B sees both plates' cells here, so its mean (and hence radius) is
        // identical regardless of which plate builds it.
        var globalFaceMetres = new double[_globalCellIds.Length];
        for (int f = 0; f < _globalCellIds.Length; f++)
        {
            int cellId = _globalCellIds[f];
            globalFaceMetres[f] = (cellId >= 0 && cellId < elevationsByCell.Count) ? elevationsByCell[cellId] : 0.0;
        }

        var globalVertexMetres = GlobeSurfaceBuilder.GatherVertexHeights(
            _globalVertices.Length, _globalTriangles, globalFaceMetres);

        var result = new double[_plates.Count][];
        for (int p = 0; p < _plates.Count; p++)
        {
            var plate = _plates[p];
            var perVertexMetres = new double[plate.LocalVertices.Length];
            for (int v = 0; v < perVertexMetres.Length; v++)
                perVertexMetres[v] = globalVertexMetres[plate.LocalToGlobal[v]] + plate.VertexNoiseMetres[v];
            result[p] = perVertexMetres;
        }

        return result;
    }

    private double[][] BuildPlateVertexFeatureWeights(IReadOnlyList<double> featureWeightsByCell)
    {
        ArgumentNullException.ThrowIfNull(featureWeightsByCell);

        var globalFaceWeights = new double[_globalCellIds.Length];
        for (int f = 0; f < _globalCellIds.Length; f++)
        {
            int cellId = _globalCellIds[f];
            globalFaceWeights[f] = (cellId >= 0 && cellId < featureWeightsByCell.Count)
                ? featureWeightsByCell[cellId]
                : 0.0;
        }

        var globalVertexWeights = GlobeSurfaceBuilder.GatherVertexHeights(
            _globalVertices.Length,
            _globalTriangles,
            globalFaceWeights);

        var result = new double[_plates.Count][];
        for (int p = 0; p < _plates.Count; p++)
        {
            var plate = _plates[p];
            var local = new double[plate.LocalVertices.Length];
            for (int v = 0; v < local.Length; v++)
                local[v] = globalVertexWeights[plate.LocalToGlobal[v]];
            result[p] = local;
        }

        return result;
    }

    private static int[] MapSourceTriangleIdsToCellIds(int[] sourceTriangleIds, int[] cellIds)
    {
        var mapped = new int[sourceTriangleIds.Length];
        for (int i = 0; i < sourceTriangleIds.Length; i++)
        {
            int source = sourceTriangleIds[i];
            mapped[i] = source >= 0 && source < cellIds.Length ? cellIds[source] : -1;
        }
        return mapped;
    }

    // --- cached per-plate topology (built once) ----------------------------------------------------

    // Build a single global shared-vertex topology over the whole snapshot: dedupes every cell's
    // three corners across ALL plates, so a corner on a plate boundary is ONE global vertex. The
    // global triangle/cell-id arrays follow global face order (cells in snapshot order). Per-plate
    // topologies then carry a LocalToGlobal map so BuildSurfaces can read the global envelope mean.
    private static (CartesianPoint3[] Vertices, int[] Triangles, int[] CellIds) BuildGlobalTopology(WorldGlobeSnapshot snapshot)
    {
        var vertexIndex = new Dictionary<(long, long, long), int>();
        var vertices = new List<CartesianPoint3>();
        var triangles = new int[snapshot.CellCount * 3];
        var cellIds = new int[snapshot.CellCount];

        var cells = snapshot.Cells;
        for (int f = 0; f < cells.Count; f++)
        {
            var cell = cells[f];
            cellIds[f] = cell.CellId;
            triangles[(f * 3) + 0] = GetOrAddVertex(cell.C0, vertexIndex, vertices);
            triangles[(f * 3) + 1] = GetOrAddVertex(cell.C1, vertexIndex, vertices);
            triangles[(f * 3) + 2] = GetOrAddVertex(cell.C2, vertexIndex, vertices);
        }

        return (vertices.ToArray(), triangles, cellIds);
    }

    private static IReadOnlyList<PlateTopology> BuildPlateTopologies(
        WorldGlobeSnapshot snapshot,
        Func<CartesianPoint3, double> detailSampler,
        CartesianPoint3[] globalVertices)
    {
        // Reverse index: global vertex id -> its quantized base position (already the dedupe key).
        var globalKeyById = new (long, long, long)[globalVertices.Length];
        for (int g = 0; g < globalVertices.Length; g++)
        {
            var v = globalVertices[g];
            globalKeyById[g] = (
                (long)Math.Round(v.X * DedupeScale),
                (long)Math.Round(v.Y * DedupeScale),
                (long)Math.Round(v.Z * DedupeScale));
        }

        // Group faces by plate, preserving cell-id order within each plate (deterministic, tick-stable).
        var byPlate = new SortedDictionary<int, List<GlobeCell>>();
        foreach (var cell in snapshot.Cells)
        {
            if (!byPlate.TryGetValue(cell.PlateId, out var list))
            {
                list = new List<GlobeCell>();
                byPlate[cell.PlateId] = list;
            }
            list.Add(cell);
        }

        var result = new List<PlateTopology>(byPlate.Count);
        foreach (var kvp in byPlate)
            result.Add(BuildOnePlate(kvp.Key, kvp.Value, detailSampler, globalKeyById));
        return result;
    }

    private static PlateTopology BuildOnePlate(
        int plateId,
        List<GlobeCell> cells,
        Func<CartesianPoint3, double> detailSampler,
        (long, long, long)[] globalKeyById)
    {
        // Dedupe the three corners of every face within the plate into shared local vertex ids, and
        // record the global vertex each local vertex corresponds to (same quantized base position).
        var localIndex = new Dictionary<(long, long, long), int>();
        var localVertices = new List<CartesianPoint3>();
        var localToGlobal = new List<int>();
        var localTriangles = new int[cells.Count * 3];
        var cellIds = new int[cells.Count];

        for (int f = 0; f < cells.Count; f++)
        {
            var cell = cells[f];
            cellIds[f] = cell.CellId;
            localTriangles[(f * 3) + 0] = GetOrAddVertex(cell.C0, localIndex, localVertices, localToGlobal, globalKeyById);
            localTriangles[(f * 3) + 1] = GetOrAddVertex(cell.C1, localIndex, localVertices, localToGlobal, globalKeyById);
            localTriangles[(f * 3) + 2] = GetOrAddVertex(cell.C2, localIndex, localVertices, localToGlobal, globalKeyById);
        }

        var vertices = localVertices.ToArray();

        // Tick-invariant seeded peaks: sample the noise ONCE per shared vertex on its BASE (tick-0) unit
        // position and cache it (metres). This is what makes the bumps reproducible, ride the plate, and
        // stay watertight — a corner shared with another plate has the same base position → same noise.
        var vertexNoiseMetres = new double[vertices.Length];
        for (int v = 0; v < vertices.Length; v++)
            vertexNoiseMetres[v] = detailSampler(vertices[v]);

        return new PlateTopology(plateId, vertices, localTriangles, cellIds, vertexNoiseMetres, localToGlobal.ToArray());
    }

    private static int GetOrAddVertex(
        GlobeVec3 corner,
        Dictionary<(long, long, long), int> vertexIndex,
        List<CartesianPoint3> localVertices)
    {
        var key = Quantize(corner);
        if (vertexIndex.TryGetValue(key, out var existing)) return existing;

        int id = localVertices.Count;
        localVertices.Add(new CartesianPoint3(corner.X, corner.Y, corner.Z));
        vertexIndex[key] = id;
        return id;
    }

    private static int GetOrAddVertex(
        GlobeVec3 corner,
        Dictionary<(long, long, long), int> localIndex,
        List<CartesianPoint3> localVertices,
        List<int> localToGlobal,
        (long, long, long)[] globalKeyById)
    {
        var key = Quantize(corner);
        if (localIndex.TryGetValue(key, out var existing)) return existing;

        int localId = localVertices.Count;
        localVertices.Add(new CartesianPoint3(corner.X, corner.Y, corner.Z));
        localIndex[key] = localId;

        // Find the global vertex with the same base-position key. The global topology already deduped
        // across all plates, so exactly one global id matches this corner.
        int globalId = Array.IndexOf(globalKeyById, key);
        localToGlobal.Add(globalId);
        return localId;
    }

    // Quantize a unit-sphere corner to an integer grid so corners that are the same icosphere vertex
    // (bar a few ulps from the lat/lon round-trip) map to the same key. Math.Round to avoid the −0.0
    // vs +0.0 and truncation-toward-zero asymmetry a plain cast would introduce near axis planes.
    private static (long, long, long) Quantize(GlobeVec3 v) => (
        (long)Math.Round(v.X * DedupeScale),
        (long)Math.Round(v.Y * DedupeScale),
        (long)Math.Round(v.Z * DedupeScale));

    /// <summary>Cached, tick-invariant shared-vertex topology of one plate's cap.</summary>
    private sealed record PlateTopology(
        int PlateId,
        CartesianPoint3[] LocalVertices,  // unique corners (shared-vertex array)
        int[] LocalTriangles,             // 3 indices per face, into LocalVertices
        int[] CellIds,                    // parallel to faces: the source cell id of each face
        double[] VertexNoiseMetres,      // parallel to LocalVertices: seeded peaks (metres), tick-invariant
        int[] LocalToGlobal);            // parallel to LocalVertices: index into the global shared-vertex array
}

/// <summary>
/// One plate's per-vertex colour envelope: the per-local-vertex <see cref="RampColor"/> array
/// (parallel to the cap's shared-vertex array) plus the plate id. Produced by
/// <see cref="GlobePlateSurfaces.BuildVertexColors"/>; consumed by the render seam to Gouraud-shade
/// terrain across cell and plate boundaries instead of flat per-cell triangles.
/// </summary>
public sealed record PlateVertexColors(int PlateId, RampColor[] Colors);

/// <summary>
/// One plate's watertight cap for a tick: the cartography <see cref="GlobeSurface"/> plus the parallel
/// per-face <see cref="CellIds"/> (face <c>t</c> came from cell <c>CellIds[t]</c>) so the render seam can
/// tag each face with its cell (tectonic-type texture U, per-cell elevation for the biome ramp).
/// </summary>
/// <param name="VertexProvenance">
/// Per-<see cref="GlobeSurface"/>-vertex provenance from the adaptive subdivision builder: original
/// input vertex vs. midpoint of two endpoints. <c>null</c> for fixed (non-adaptive) caps; non-null
/// and parallel to <see cref="GlobeSurface.Positions"/> for adaptive caps. The render seam uses it to
/// reattach base-vertex colours (and other base-parallel attributes) to the appended midpoint
/// vertices instead of falling back to a placeholder colour.
/// </param>
public sealed record PlateCap(
    int PlateId,
    int[] CellIds,
    GlobeSurface Surface,
    VertexProvenance[]? VertexProvenance);
