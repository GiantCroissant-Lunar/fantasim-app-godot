using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Ecs.Cells;
using FantaSim.App.Ecs.Systems;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Persistence;
using FantaSim.App.World.Topography;
using FantaSim.Geosphere.Crust;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.World.Fields;
using FantaSim.World.TruthStream;
using Microsoft.Extensions.Logging;
using UnifyCell;

namespace FantaSim.App.World.Crust;

internal sealed record WorldCrustMaterialization(
    WorldCrustRunSpec Spec,
    GeodesicSphereTessellation Tessellation,
    PlateTopology Topology,
    CrustEvolutionResult Result)
{
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

            int n = Result.Tessellation.CellCount;
            var elevations = new double[n];
            var features = new CellCrustFeature[n];
            Result.FeaturesByTick.TryGetValue(tick, out var featureMap);

            var boundaryContributions = BoundaryProfileContribution.Build(
                globeAtOnset,
                arcsAtOnset,
                state,
                featureMap,
                Spec.BoundaryProfiles);

            for (int cell = 0; cell < n; cell++)
            {
                if (state.TryGetValue(cell, out var s))
                {
                    var sample = new CrustSample(
                        s.ContinentalFraction, s.OrogenicPressure, s.VolcanicActivity, s.CrustAgeTicks);
                    elevations[cell] = CellElevationSystem.Derive(sample, Spec.HydrosphereMode) + boundaryContributions[cell];
                }

                if (featureMap is not null && featureMap.TryGetValue(cell, out var f))
                    features[cell] = CrustFeatureContractMapper.ToCellFeature(f);
            }

            return (elevations, features);
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
            return BoundarySectionBuilder.BuildRepresentativeSections(
                globeAtOnset,
                arcsAtOnset,
                state,
                featureMap,
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
        var result = await CrustPipeline.RunAsync(
            tessellation,
            spec.Plates,
            spec.Recipe,
            startTick: spec.StartTick,
            endTick: spec.EndTick,
            snapshotTicks: spec.SnapshotTicks,
            rates: spec.Rates,
            rotationReferenceTick: spec.RotationReferenceTick,
            patchRecipe: spec.PatchRecipe,
            ct: cancellationToken).ConfigureAwait(false);

        return new WorldCrustMaterialization(spec, tessellation, topology, result);
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
            features);
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
