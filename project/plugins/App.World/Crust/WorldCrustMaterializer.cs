using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Ecs.Cells;
using FantaSim.App.Ecs.Systems;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Persistence;
using FantaSim.App.World.Topography;
using FantaSim.Cartography.Globe;
using FantaSim.Geosphere.Crust;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.World.Fields;
using FantaSim.World.TruthStream;
using Microsoft.Extensions.Logging;
using UnifyCell;
using UnifyGeometry.Spherical;
using UnifyMaths;

namespace FantaSim.App.World.Crust;

internal sealed record WorldCrustMaterialization(
    WorldCrustRunSpec Spec,
    GeodesicSphereTessellation Tessellation,
    PlateTopology Topology,
    CrustEvolutionResult Result)
{
    /// <summary>
    /// Sole app-side construction seam for the compact canonical crust volume consumed by both
    /// presentation projections. Inputs are already sampled into the current plate frame by the
    /// service; this method binds them to the materialization and delegates contract validation and
    /// deterministic identity to <see cref="CrustVolumeState"/>.
    /// </summary>
    public CrustVolumeState BuildVolumeState(
        WorldGlobeSnapshot globe,
        IReadOnlyList<PlateBoundaryArc> boundaryArcs,
        long tick,
        int seed,
        int graphRevision,
        double verticalExaggeration,
        BoundaryProfileParameters profiles,
        IReadOnlyList<double> outerElevationsMetresByCell,
        IReadOnlyList<double> crustThicknessMetresByCell,
        IReadOnlyList<CellCrustFeature> featuresByCell,
        IReadOnlyDictionary<int, double> continentalFractionByCell)
    {
        if (tick < Spec.RotationReferenceTick)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tick),
                tick,
                "A solid crust volume cannot precede the materialization rotation reference tick.");
        }

        ArgumentNullException.ThrowIfNull(profiles);
        if (!double.IsFinite(verticalExaggeration) || verticalExaggeration <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(verticalExaggeration));
        ValidateVisualParameters(profiles);

        var topology = new GlobePlateSurfaces(
            globe,
            noise: new NoiseParams(Amplitude: 0.0));
        var cornerMetres = topology.BuildSharedCornerMetres(outerElevationsMetresByCell);
        double referenceThickness = MedianPositive(crustThicknessMetresByCell);
        var outerCandidates = new GlobeVec3[globe.CellCount * 3];
        var innerCandidates = new GlobeVec3[globe.CellCount * 3];
        for (int face = 0; face < globe.Cells.Count; face++)
        {
            var cell = globe.Cells[face];
            double thicknessRatio = referenceThickness <= 0.0
                ? 1.0
                : Math.Clamp(
                    crustThicknessMetresByCell[cell.CellId] / referenceThickness,
                    0.65,
                    1.50);
            double visualThickness =
                profiles.VisualCrustThicknessUnitRadius * thicknessRatio;
            for (int corner = 0; corner < 3; corner++)
            {
                int index = (cell.CellId * 3) + corner;
                var baseCorner = corner switch
                {
                    0 => cell.C0,
                    1 => cell.C1,
                    _ => cell.C2,
                };
                var controlSample = CellBoundaryField.SampleDirection(
                    baseCorner,
                    cell.PlateId,
                    boundaryArcs);
                Vector3D unit = ToVector(baseCorner).Normalize();
                double radius = 1.0 + (cornerMetres[index] * verticalExaggeration);
                Vector3D outer = unit * radius;
                Vector3D inner = outer - (unit * visualThickness);
                ApplyConvergentDeformation(
                    controlSample,
                    profiles,
                    unit,
                    ref outer,
                    ref inner);
                outerCandidates[index] = ToGlobe(outer);
                innerCandidates[index] = ToGlobe(inner);
            }
        }

        var welded = topology.WeldPlateCorners(outerCandidates, innerCandidates);
        string parameterDigest =
            ComputeVolumeParameterDigest(verticalExaggeration, profiles);
        return CrustVolumeState.Create(
            tick,
            seed,
            graphRevision,
            topology.TopologyDigest,
            parameterDigest,
            globe,
            boundaryArcs,
            welded.Outer,
            welded.Inner,
            outerElevationsMetresByCell,
            crustThicknessMetresByCell,
            featuresByCell,
            continentalFractionByCell);
    }

    private static void ApplyConvergentDeformation(
        in CellBoundarySample sample,
        in BoundaryProfileParameters profiles,
        Vector3D unit,
        ref Vector3D outer,
        ref Vector3D inner)
    {
        if (!sample.Found
            || sample.Kind != PlateBoundaryKind.Convergent
            || sample.IsCollision
            || sample.SubductingPlateId is not int subductingPlateId)
        {
            return;
        }

        double bendHalfWidth = Math.Max(
            profiles.ConvergentTrenchHalfWidthRad * 2.0,
            profiles.ConvergentArcSetbackRad + profiles.ConvergentArcHalfWidthRad);
        double distance = Math.Abs(sample.SignedDistanceRad);
        if (distance > bendHalfWidth)
            return;
        double normalizedDistance = Math.Clamp(distance / bendHalfWidth, 0.0, 1.0);
        if (sample.CellPlateId == subductingPlateId)
        {
            double bend = Math.Sin(Math.PI * SmoothStep(normalizedDistance));
            Vector3D intoOwningPlate = ToVector(sample.AcrossBoundaryDirection);
            Vector3D towardOverridingPlate = intoOwningPlate * -1.0;
            Vector3D shift =
                (towardOverridingPlate
                    * profiles.ConvergentSlabUnderlapLengthUnitRadius
                    * bend)
              - (unit * profiles.ConvergentSlabDepthUnitRadius * bend * bend);
            outer += shift;
            inner += shift;
            return;
        }

        double root = SmoothStep(1.0 - normalizedDistance);
        inner -= unit * profiles.ConvergentOverridingRootDepthUnitRadius * root;
    }

    private static double SmoothStep(double value)
    {
        double t = Math.Clamp(value, 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    private static double MedianPositive(IReadOnlyList<double> values)
    {
        var positive = values
            .Where(value => double.IsFinite(value) && value > 0.0)
            .OrderBy(value => value)
            .ToArray();
        if (positive.Length == 0)
            return 0.0;
        int middle = positive.Length / 2;
        return positive.Length % 2 == 1
            ? positive[middle]
            : (positive[middle - 1] + positive[middle]) * 0.5;
    }

    private static void ValidateVisualParameters(BoundaryProfileParameters p)
    {
        double[] values =
        {
            p.VisualCrustThicknessUnitRadius,
            p.ConvergentSlabUnderlapLengthUnitRadius,
            p.ConvergentSlabDepthUnitRadius,
            p.ConvergentOverridingRootDepthUnitRadius,
            p.ConvergentVolcanoConeHeight,
            p.ConvergentVolcanoPeriodPoints,
            p.ConvergentVolcanoSharpness,
        };
        if (values.Any(value => !double.IsFinite(value) || value < 0.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(p),
                "Visual deformation parameters must be finite and non-negative.");
        }
    }

    private static string ComputeVolumeParameterDigest(
        double verticalExaggeration,
        BoundaryProfileParameters p)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(verticalExaggeration);
            writer.Write(p.ConvergentTrenchDepth);
            writer.Write(p.ConvergentTrenchHalfWidthRad);
            writer.Write(p.ConvergentArcHeight);
            writer.Write(p.ConvergentArcSetbackRad);
            writer.Write(p.ConvergentArcHalfWidthRad);
            writer.Write(p.ConvergentCollisionHeight);
            writer.Write(p.ConvergentCollisionHalfWidthRad);
            writer.Write(p.DivergentSwellHeight);
            writer.Write(p.DivergentSwellHalfWidthRad);
            writer.Write(p.DivergentRiftNotchDepth);
            writer.Write(p.DivergentRiftHalfWidthRad);
            writer.Write(p.TransformScarpAmplitude);
            writer.Write(p.TransformHalfWidthRad);
            writer.Write(p.TransformScarpPeriodPoints);
            writer.Write(p.VisualCrustThicknessUnitRadius);
            writer.Write(p.ConvergentSlabUnderlapLengthUnitRadius);
            writer.Write(p.ConvergentSlabDepthUnitRadius);
            writer.Write(p.ConvergentOverridingRootDepthUnitRadius);
            writer.Write(p.ConvergentVolcanoConeHeight);
            writer.Write(p.ConvergentVolcanoPeriodPoints);
            writer.Write(p.ConvergentVolcanoSharpness);
        }
        return Convert.ToHexString(
                SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))))
            .ToLowerInvariant();
    }

    private static Vector3D ToVector(GlobeVec3 point)
        => new(point.X, point.Y, point.Z);

    private static GlobeVec3 ToGlobe(Vector3D point)
        => new((float)point.X, (float)point.Y, (float)point.Z);

    /// <summary>
    /// Projects the materialized crust state at <paramref name="tick"/> into presentation cell
    /// elevations and typed cell features. This is the source-of-truth implementation for the
    /// presentation surface path: <see cref="CellElevationSystem.Derive"/> plus the boundary-profile
    /// contribution from the onset-frame globe/arcs.
    /// </summary>
    /// <param name="globeAtOnset">The globe snapshot at the onset reference frame; used to align
    /// boundary-profile topography with the static mesh.</param>
    /// <param name="arcsAtOnset">Typed boundary arcs at the onset reference frame.</param>
    /// <param name="tick">The snapshot tick whose crust state is projected.</param>
    /// <param name="logger">Optional logger for projection warnings; null suppresses logging.</param>
    /// <returns>Elevations and features, or null when the tick is gated out or the pipeline
    /// produced no state.</returns>
    public (double[]? Elevations, CellCrustFeature[]? Features) BuildSurfaceData(
        WorldGlobeSnapshot globeAtOnset,
        IReadOnlyList<PlateBoundaryArc> arcsAtOnset,
        long tick,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(globeAtOnset);
        ArgumentNullException.ThrowIfNull(arcsAtOnset);

        try
        {
            if (!Result.StateByTick.TryGetValue(tick, out var state) || state.Count == 0)
                return (null, null);

            Result.FeaturesByTick.TryGetValue(tick, out var featureMap);

            var surfaceData = PlateFrameSampler.BuildSurfaceData(
                globeAtOnset,
                state,
                featureMap,
                arcsAtOnset,
                Spec.BoundaryProfiles,
                Spec.HydrosphereMode,
                birthRoughnessByCell: null);
            return (surfaceData.Elevations, surfaceData.Features);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Crust surface data unavailable at tick {Tick}; presentation falls back to untinted.", tick);
            return (null, null);
        }
    }

    /// <summary>
    /// Builds representative boundary-normal section documents for the active crust snapshot. These are
    /// distinct from the radial cutaway wedge: each section samples across one typed plate boundary.
    /// </summary>
    public IReadOnlyList<BoundarySectionDocument> BuildBoundarySections(
        WorldGlobeSnapshot globeAtOnset,
        IReadOnlyList<PlateBoundaryArc> arcsAtOnset,
        long tick,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(globeAtOnset);
        ArgumentNullException.ThrowIfNull(arcsAtOnset);

        try
        {
            if (!Result.StateByTick.TryGetValue(tick, out var state) || state.Count == 0)
                return Array.Empty<BoundarySectionDocument>();

            Result.FeaturesByTick.TryGetValue(tick, out var featureMap);
            var resolvedArcs = ConvergentPolarity.Attach(
                arcsAtOnset,
                globeAtOnset.Cells,
                featureMap,
                state);
            return BoundarySectionBuilder.BuildRepresentativeSections(
                globeAtOnset,
                resolvedArcs,
                Spec.BoundaryProfiles);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Boundary section data unavailable at tick {Tick}; presentation omits section panels.", tick);
            return Array.Empty<BoundarySectionDocument>();
        }
    }

    /// <summary>
    /// Projects the materialized crust state at <paramref name="tick"/> into app-derived crust
    /// thickness (metres). This is the source-of-truth implementation for the cutaway path:
    /// composes <see cref="GeospherePlateLayer"/> and <see cref="SyntheticCrustLayer"/> over a
    /// geodetic geometry built from <paramref name="globeAtOnset"/>, then reads
    /// <see cref="GeosphereFieldCatalog.CrustThickness"/>.
    /// </summary>
    /// <param name="globeAtOnset">The globe snapshot at the onset reference frame; used to build
    /// the field-composition geometry.</param>
    /// <param name="tick">The snapshot tick whose crust thickness is projected.</param>
    /// <param name="logger">Optional logger for projection warnings; null suppresses logging.</param>
    /// <returns>Per-cell crust thickness in metres, or null when the tick is gated out, the globe
    /// has no plates, or composition fails.</returns>
    public double[]? BuildCrustThickness(
        WorldGlobeSnapshot globeAtOnset,
        long tick,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(globeAtOnset);

        try
        {
            if (tick < Spec.RotationReferenceTick)
                return null;

            if (globeAtOnset.PlateCount == 0)
                return null;

            var geometry = WorldCrustMaterializer.BuildGlobeGeometryFromSnapshot(globeAtOnset);

            var plateLayer = new GeospherePlateLayer();
            var crustLayer = new SyntheticCrustLayer();
            var composer = new FieldComposer();
            GeosphereFieldCatalog.DeclareInto(composer);
            composer.AddLayer(plateLayer.Fields);
            composer.AddLayer(crustLayer.Fields);
            var composition = composer.Compose();
            if (!composition.IsValid)
            {
                logger?.LogWarning("Cutaway crust-thickness composition invalid: {Errors}", string.Join("; ", composition.Errors));
                return null;
            }

            var values = new FieldValueResolver().Resolve(
                composition,
                new ILayer[] { plateLayer, crustLayer },
                geometry,
                tick);

            return values.Scalars
                .First(s => s.Field == GeosphereFieldCatalog.CrustThickness)
                .Values
                .ToArray();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Crust thickness data unavailable at tick {Tick}; cutaway falls back to default.", tick);
            return null;
        }
    }

    // Snapshot (cartesian) -> geometry (geodetic) for field composition. Boundary segments are
    // left empty; the plate layer falls back to per-plate mean radius. internal so the regime
    // layer-generation node handlers share the same converter the cutaway/crust path uses
    // (P4b parity contract: one canonical snapshot->geometry path, never duplicated).
}

internal static class WorldCrustMaterializer
{
    public static async Task<WorldCrustMaterialization> MaterializeAsync(
        WorldCrustRunSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var tessellation = spec.CreateTessellation();
        var topology = PlateTopologyBuilder.Build(tessellation, spec.Plates);
        Func<long, IReadOnlyDictionary<(int PlateA, int PlateB), BoundaryType>>? boundaryTypesAtTick =
            spec.RotationProvider is null
                ? null
                : tick => ClassifyBoundaryTypesAt(
                    tessellation,
                    topology,
                    spec.Plates,
                    spec.RotationProvider,
                    tick);
        var result = await RunPipelineAsync(
            spec,
            tessellation,
            boundaryTypesAtTick,
            cancellationToken).ConfigureAwait(false);

        return new WorldCrustMaterialization(spec, tessellation, topology, result);
    }

    // One isolated engine-version seam: generated motion preserves the historical RunAsync ABI;
    // an imported authority supplies explicit boundary semantics through the named provider API.
    private static Task<CrustEvolutionResult> RunPipelineAsync(
        WorldCrustRunSpec spec,
        GeodesicSphereTessellation tessellation,
        Func<long, IReadOnlyDictionary<(int PlateA, int PlateB), BoundaryType>>? boundaryTypesAtTick,
        CancellationToken cancellationToken)
    {
        if (boundaryTypesAtTick is null)
        {
            return CrustPipeline.RunAsync(
                tessellation,
                spec.Plates,
                spec.Recipe,
                startTick: spec.StartTick,
                endTick: spec.EndTick,
                snapshotTicks: spec.SnapshotTicks,
                rates: spec.Rates,
                rotationReferenceTick: spec.RotationReferenceTick,
                patchRecipe: spec.PatchRecipe,
                ct: cancellationToken);
        }

        return CrustPipeline.RunWithBoundaryTypesAsync(
            tessellation,
            spec.Plates,
            spec.Recipe,
            startTick: spec.StartTick,
            endTick: spec.EndTick,
            boundaryTypesAtTick,
            snapshotTicks: spec.SnapshotTicks,
            rates: spec.Rates,
            rotationReferenceTick: spec.RotationReferenceTick,
            patchRecipe: spec.PatchRecipe,
            ct: cancellationToken);
    }

    private static IReadOnlyDictionary<(int PlateA, int PlateB), BoundaryType> ClassifyBoundaryTypesAt(
        GeodesicSphereTessellation tessellation,
        PlateTopology topology,
        IReadOnlyList<Plate> plates,
        IPlateRotationProvider rotationProvider,
        long tick)
    {
        var positions = new Dictionary<int, UnifyMaths.Vector3D>(tessellation.CellCount);
        for (int cell = 0; cell < tessellation.CellCount; cell++)
        {
            var center = tessellation
                .GetCenter(new GeodesicCoord(cell, tessellation.Frequency))
                .ToVector3D();
            int plateId = topology.Assignment[cell];
            positions[cell] = rotationProvider.RotationFromOnsetTo(plateId, tick).Rotate(center);
        }

        var poles = plates.ToDictionary(
            plate => plate.PlateId,
            plate => rotationProvider.InstantaneousPoleAt(plate.PlateId, tick));
        return PlateTopologyBuilder
            .ClassifyBoundariesFromKinematics(tessellation, topology, positions, poles)
            .ToDictionary(
                boundary => (boundary.PlateA, boundary.PlateB),
                boundary => boundary.Type);
    }

    /// <summary>
    /// Projects a freshly-built <see cref="WorldCrustMaterialization"/>'s crust-evolution fold AT
    /// <paramref name="snapshotTick"/> into the persisted payload shape (2026-07-11 persistence
    /// slice 1). Called on a cache MISS, after <see cref="MaterializeAsync"/> — the persist path
    /// never re-runs the pipeline, it only re-shapes state that pipeline run already produced.
    /// </summary>
    internal static CrustProductCacheRecord ToPersistedRecord(
        WorldCrustMaterialization materialization,
        int seed,
        int frequency,
        double spinRateRadiansPerMegaAnnum,
        int graphRevision,
        string rotationAuthorityDigest,
        long snapshotTick)
    {
        ArgumentNullException.ThrowIfNull(materialization);

        var cellStates = materialization.Result.StateByTick.TryGetValue(snapshotTick, out var stateAtTick)
            ? stateAtTick.Values
                .OrderBy(s => s.CellId)
                .Select(s => new CellCrustStateRecord(s.CellId, s.ContinentalFraction, s.OrogenicPressure, s.VolcanicActivity, s.CrustAgeTicks))
                .ToArray()
            : Array.Empty<CellCrustStateRecord>();

        var features = materialization.Result.FeaturesByTick.TryGetValue(snapshotTick, out var featuresAtTick)
            ? featuresAtTick.Values
                .OrderBy(f => f.CellId)
                .Select(f => new CrustFeatureRecord(
                    f.CellId,
                    CrustFeatureContractMapper.ToContractKind(f.Kind).ToWireByte(),
                    f.Magnitude))
                .ToArray()
            : Array.Empty<CrustFeatureRecord>();

        return new CrustProductCacheRecord(
            seed,
            frequency,
            spinRateRadiansPerMegaAnnum,
            graphRevision,
            snapshotTick,
            CrustProductCacheSchema.SchemaVersion,
            CrustProductCacheSchema.CurrentAppVersionStamp,
            cellStates,
            features,
            rotationAuthorityDigest);
    }

    /// <summary>
    /// Reconstructs a <see cref="WorldCrustMaterialization"/> from a persisted record WITHOUT
    /// re-running <see cref="CrustPipeline.RunAsync"/> — the one genuinely expensive step this cache
    /// exists to skip on a warm boot. Safe to skip because everything else
    /// <see cref="MaterializeAsync"/> would otherwise (re)compute is a cheap, PURE function of
    /// <paramref name="spec"/> alone: <c>Tessellation = new GeodesicSphereTessellation(Frequency)</c>
    /// and <c>Topology = PlateTopologyBuilder.Build(tessellation, spec.Plates)</c> — the exact two
    /// lines <see cref="MaterializeAsync"/> runs before the pipeline call, reproduced here
    /// identically. <see cref="CrustEvolutionResult"/>'s remaining fields (Store/Stream/Reduced) are
    /// truth-stream plumbing internal to the pipeline run that produced them; no App.World consumer
    /// reads them off a materialization afterwards (verified by grep: zero ".Result.Store"/
    /// ".Result.Stream"/".Result.Reduced" call sites in Services/Service.cs), so a restored result
    /// carries a <see cref="NoOpTruthEventStore"/> shell instead of a live one — see
    /// CrustProductCacheRecord's doc comment for the full reasoning.
    /// </summary>
    internal static WorldCrustMaterialization FromPersistedRecord(WorldCrustRunSpec spec, CrustProductCacheRecord record)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(record);

        var tessellation = spec.CreateTessellation();
        var topology = PlateTopologyBuilder.Build(tessellation, spec.Plates);

        IReadOnlyDictionary<int, CellCrustState> cellStates = record.CellStates.ToDictionary(
            s => s.CellId,
            s => new CellCrustState(s.CellId, s.ContinentalFraction, s.OrogenicPressure, s.VolcanicActivity, s.CrustAgeTicks));
        IReadOnlyDictionary<int, CrustFeature> features = record.Features.ToDictionary(
            f => f.CellId,
            f => new CrustFeature(
                f.CellId,
                CrustFeatureContractMapper.ToEngineKind(f.Kind),
                f.Magnitude));

        var stateByTick = new Dictionary<long, IReadOnlyDictionary<int, CellCrustState>> { [record.SnapshotTick] = cellStates };
        var featuresByTick = new Dictionary<long, IReadOnlyDictionary<int, CrustFeature>> { [record.SnapshotTick] = features };

        var result = new CrustEvolutionResult(
            tessellation,
            topology,
            spec.Plates,
            NoOpTruthEventStore.Instance,
            default,
            Array.Empty<FieldValue>(),
            stateByTick,
            featuresByTick);

        return new WorldCrustMaterialization(spec, tessellation, topology, result);
    }

    /// <summary>
    /// Throwing <see cref="ITruthEventStore"/> shell for a restored <see cref="CrustEvolutionResult"/>
    /// (see <see cref="FromPersistedRecord"/>). Every method throws deliberately: it is a bug, not a
    /// graceful-degradation case, if presentation code ever reaches for the truth stream of a
    /// cache-restored crust product.
    /// </summary>
    private sealed class NoOpTruthEventStore : ITruthEventStore
    {
        public static readonly NoOpTruthEventStore Instance = new();

        private NoOpTruthEventStore()
        {
        }

        private const string Message =
            "This CrustEvolutionResult was restored from the persisted crust-product cache (2026-07-11 " +
            "persistence slice 1) and carries no live truth-event store. App.World only reads " +
            "StateByTick/FeaturesByTick/Tessellation/Topology/Plates off a restored result.";

        public Task<StreamHead> AppendAsync(TruthStreamIdentity stream, IReadOnlyList<ITruthEventDraft> drafts, CancellationToken ct = default)
            => throw new NotSupportedException(Message);

        public Task<StreamHead> AppendIfHeadAsync(TruthStreamIdentity stream, IReadOnlyList<ITruthEventDraft> drafts, StreamHead? expectedHead, CancellationToken ct = default)
            => throw new NotSupportedException(Message);

        public IAsyncEnumerable<ITruthEvent> ReadAsync(TruthStreamIdentity stream, long fromSequence = 0, CancellationToken ct = default)
            => throw new NotSupportedException(Message);

        public Task<StreamHead?> GetHeadAsync(TruthStreamIdentity stream, CancellationToken ct = default)
            => throw new NotSupportedException(Message);
    }

    internal static WorldGlobeGeometry BuildGlobeGeometryFromSnapshot(WorldGlobeSnapshot snapshot)
    {
        var plateIds = snapshot.Plates.Count > 0
            ? snapshot.Plates.Select(p => p.PlateId.ToString(CultureInfo.InvariantCulture)).ToArray()
            : new[] { "0" };

        var cells = new List<PlateCellPolygon>(snapshot.CellCount);
        foreach (var cell in snapshot.Cells)
        {
            var ring = new[]
            {
                ToGeoPoint(cell.C0),
                ToGeoPoint(cell.C1),
                ToGeoPoint(cell.C2),
            };
            var plateId = cell.PlateId >= 0 && cell.PlateId < snapshot.Plates.Count
                ? snapshot.Plates[cell.PlateId].PlateId.ToString(CultureInfo.InvariantCulture)
                : cell.PlateId.ToString(CultureInfo.InvariantCulture);
            cells.Add(new PlateCellPolygon(plateId, ring));
        }

        return new WorldGlobeGeometry(plateIds, cells, BoundarySegments: Array.Empty<BoundaryGeoSegment>());
    }

    private static GeoPoint ToGeoPoint(GlobeVec3 v)
    {
        var len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        if (len < 1e-9)
            return new GeoPoint(0, 0);
        var lat = Math.Asin(Math.Clamp(v.Z / len, -1.0, 1.0)) * 180.0 / Math.PI;
        var lon = Math.Atan2(v.Y, v.X) * 180.0 / Math.PI;
        return new GeoPoint(lat, lon);
    }
}
