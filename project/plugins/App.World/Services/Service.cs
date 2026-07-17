using Akka.Actor;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FantaSim.App.Common.Storage;
using FantaSim.App.Ecs.Cells;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Crust;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Persistence;
using FantaSim.App.World.Topography;
using FantaSim.Geosphere.Crust;
using FantaSim.Geosphere.Asthenosphere.Convection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceArchi.Contracts;
using FantaSim.World.Contracts.Units;
using UnifyMaths;
using UnifyStorage.Abstractions;
using BoundarySectionDocument = FantaSim.App.World.Composition.BoundarySectionDocument;
using MantleHistoryAdapter = FantaSim.App.World.Composition.MantleHistoryAdapter;
using MantleIsosurfaceExtractor = FantaSim.App.World.Composition.MantleIsosurfaceExtractor;
using OnsetRoster = FantaSim.App.World.Composition.OnsetRoster;
using SphereRegimeSchedule = FantaSim.App.World.Composition.SphereRegimeSchedule;
using SphereRegimeScheduleDefaults = FantaSim.App.World.Composition.SphereRegimeScheduleDefaults;
using TopoPlate = FantaSim.Geosphere.Plate.Topology.Plate;

namespace FantaSim.App.World.Services;

/// <summary>
/// T3 orchestrator for the App.World service. Implements <see cref="IService"/> by composing
/// the sibling fantasim-world field/catalog/reducer stack at construction and mapping its pure
/// types onto the app-side DTOs defined in <c>FantaSim.App.World.Dto</c>.
/// </summary>
/// <remarks>
/// World-lib types (FantaSim.World.Fields.*, World.TruthStream.*, World.Parameters.*) stay
/// inside this T3 assembly and never leak through the T1 contract surface. The composition is
/// always compiled (USE_PROJECT_REFERENCES only selects project vs package references in the
/// csproj, never behavioral branches here).
/// </remarks>
public sealed class Service : IService, IDisposable
{
    private const string GenerationGraphSource = "world-generation.graph";

    private readonly IRegistry _registry;
    private readonly IWorldHistoryCoordinator _history;
    private readonly ILogger _logger;
    private readonly WorldTruthEventStoreHandle _truthStoreHandle;
    private readonly ITruthEventWriter _truthWriter;
    private readonly List<Action<WorldGenerationChangedEvent>> _subscribers = new();
    private readonly object _subscribersGate = new();
    private readonly object _generationProductsGate = new();
    private WorldGenerationProductsView _generationProducts =
        new(0, Array.Empty<string>(), 0L);
    private IReadOnlyList<long> _cachedCrustSnapshotTicks = Array.Empty<long>();
    private readonly object _crustProductCacheGate = new();
    private readonly Dictionary<CrustProductCacheKey, CrustTickProducts> _crustProductCache = new();

    // Cross-session crust-product cache (2026-07-11 persistence slice 1). Resolved once at
    // construction time and reused via the instance field -- NOT re-resolved per call -- because
    // Service itself is bundle-scoped and reconstructed fresh on every bundle reload, so this is
    // already "resolve at the point where the bundle-side object is (re)built", not a stale
    // reference held across a reload (pin-class 5, cross-alc-rules.md). Null when persistence is
    // disabled by config or the resident store failed to construct; every persistence call below
    // degrades to a no-op in that case (in-memory-only, same behavior as before this slice).
    private readonly IDocumentStore? _documentStore;
    private readonly object _pendingCrustPersistGate = new();
    private readonly List<Task> _pendingCrustPersistTasks = new();

    /// <summary>
    /// Counts calls into <see cref="WorldCrustMaterializer.MaterializeAsync"/> from
    /// <see cref="GetOrBuildCrustTickProducts"/> -- i.e. actual crust-pipeline runs, as opposed to
    /// cache hits (in-memory or persisted-store restores). Test-only observability for the
    /// probe-before-build contract (App.World.Tests, InternalsVisibleTo below); production code
    /// never reads it.
    /// </summary>
    internal int CrustPipelineBuildCountForTests { get; private set; }

    private Exception? _lastSubscriberError;
    private bool _disposed;

    public Service(IRegistry registry, ActorSystem? actorSystem = null)
        : this(registry, actorSystem, WorldHistoryCoordinatorFactory.Create)
    {
    }

    internal Service(
        IRegistry registry,
        ActorSystem? actorSystem,
        Func<IRegistry, FantaSim.World.TruthStream.ITruthEventReader, ITruthEventWriter, IWorldHistoryCoordinator> historyFactory)
    {
        ArgumentNullException.ThrowIfNull(historyFactory);
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        var config = registry.TryGet<CrosscutFoundation.Config.IService>();
        var truthStoreOptions = WorldTruthStoreOptions.FromConfig(config);
        if (truthStoreOptions.Backend == WorldTruthStoreBackend.SurrealDb && actorSystem is null)
        {
            throw new InvalidOperationException(
                "SurrealDB world truth store requires an ActorSystem so writes go through the single writer actor.");
        }

        var truthStoreHandle = WorldTruthEventStoreFactory.Create(truthStoreOptions);
        ITruthEventWriter? truthWriter = null;
        try
        {
            truthWriter = truthStoreOptions.Backend == WorldTruthStoreBackend.SurrealDb
                ? ActorTruthEventWriter.Start(
                    actorSystem!,
                    truthStoreHandle.EventStore,
                    actorName: NewTruthWriterActorName())
                : new DirectTruthEventWriter(truthStoreHandle.EventStore);
            _history = historyFactory(
                registry,
                truthStoreHandle.EventStore,
                truthWriter);
            _truthWriter = truthWriter;
            _truthStoreHandle = truthStoreHandle;
            truthWriter = null;
        }
        catch
        {
            truthWriter?.Dispose();
            truthStoreHandle.Dispose();
            throw;
        }
        var loggerFactory = registry.TryGet<ILoggerFactory>();
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<Service>();

        // Resolved through IRegistry, not constructed here: the resident store (App.Common,
        // Bootstrap.cs) is a process-lifetime singleton every collectible consumer reaches the same
        // way -- TryGet returns null (never throws) when persistence is disabled or the resident
        // construction failed, and every crust-cache call below degrades to in-memory-only.
        _documentStore = registry.TryGet<IDocumentStore>();
    }

    /// <summary>
    /// Last exception thrown by a subscriber during <see cref="EmitGenerationChanged"/>, or null
    /// when no subscriber has faulted since the last successful emit. Populated only when no
    /// ILoggerFactory is registered in the registry (the registry-resolved logger path logs the
    /// fault directly, making this field unnecessary in that case).
    /// </summary>
    public Exception? LastSubscriberError => _lastSubscriberError;

    public WorldOverview GetOverviewAsync()
        => _history.GetOverview();

    public WorldFieldValues GetFieldValuesAsync(WorldFieldValuesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _history.GetFieldValues(request);
    }

    public WorldScalarFieldValues GetScalarFieldValuesAsync(WorldScalarFieldValuesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _history.GetScalarFieldValues(request);
    }

    public WorldRenderSnapshot GetRenderSnapshotAsync()
        => _history.GetRenderSnapshot();

    public WorldGenerationProductsView GetGenerationProductsAsync()
    {
        lock (_generationProductsGate)
            return _generationProducts;
    }

    /// <summary>
    /// Raised at the start of every presentation fetch. The host binder fetches the presentation
    /// right after it registers its <c>ITimelineController</c>, so this is the earliest
    /// registration-order-tolerant hook for late-arming (see WorldPlugin's crust trigger).
    /// </summary>
    public event Action? PresentationRequested;

    public PlanetPresentationDocument GetPlanetPresentationAsync()
        => GetPlanetPresentationAsync(SphereRegimeScheduleDefaults.PlateOnsetTick);

    public PlanetPresentationDocument GetPlanetPresentationAsync(long referenceTick)
        => GetPlanetPresentationAsyncCore(referenceTick, ResolvePlanetRenderOptions(WorldGenerationGraphDefaults.BuildFamily()));

    /// <summary>
    /// D8b progressive-resolution scrub (vault/plans/2026-07-11-d8b-progressive-resolution-slice1-plan.md):
    /// clamps the frequency to [2, configured default] and threads it through the same path as the
    /// 1-arg overload so both caches (crust product + reconstructor) stay frequency-keyed.
    /// </summary>
    public PlanetPresentationDocument GetPlanetPresentationAsync(long referenceTick, int tessellationFrequency)
    {
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var renderOptions = ResolvePlanetRenderOptions(family);
        var clamped = Math.Clamp(tessellationFrequency, 2, renderOptions.TessellationFrequency);
        return GetPlanetPresentationAsyncCore(referenceTick, renderOptions with { TessellationFrequency = clamped });
    }

    internal PlanetPresentationDocument GetPlanetPresentationAsyncCore(long referenceTick, WorldGenerationRenderOptions renderOptions)
    {
        PresentationRequested?.Invoke();
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        // A presentation is one read transaction over rotation truth. Selection may change on a
        // different thread while the relatively expensive globe/crust build is running, but every
        // phase of this document must retain the authority captured at the query boundary.
        var rotationProjection = _history.GetActiveRotationProjection(onsetTick);
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var overview = _history.GetOverview();
        var renderSnapshot = _history.GetRenderSnapshot();
        var runtime = BuildPlanetPresentationRuntime(
            family,
            renderOptions,
            referenceTick,
            rotationProjection);
        WorldGenerationProductsView products;
        IReadOnlyList<long> crustSnapshotTicks;
        lock (_generationProductsGate)
        {
            products = _generationProducts;
            crustSnapshotTicks = _cachedCrustSnapshotTicks;
        }

        var selectedCrustTick = CrustSnapshotTickSeries.ForRegime(
            runtime.GeosphereSchedule.RegimeAt(referenceTick) ?? runtime.GeosphereSchedule.Regimes[^1],
            CrustSnapshotTickSeries.DefaultSpacingTicks,
            runtime.MaxTick).SelectSnapshotForPlayhead(referenceTick);

        var layers = products.Products
            .Select(address => ToPlanetLayer(address, family, selectedCrustTick))
            .Where(layer => layer is not null)
            .Cast<PlanetPresentationLayer>()
            .ToArray();

        var snapshotTickStates = BuildCrustSnapshotTickStates(runtime.GeosphereSchedule, crustSnapshotTicks, runtime.MaxTick);

        return new PlanetPresentationDocument(
            PlanetId: overview.WorldId,
            SourceWorldId: overview.WorldId,
            ReferenceTick: products.ReferenceTick,
            Revision: products.GraphRevision,
            Layers: layers,
            RenderEntities: renderSnapshot.Entities ?? Array.Empty<RenderEntityDto>())
        {
            GlobeSnapshot = runtime.GlobeSnapshot,
            GlobeReferenceTick = runtime.GlobeReferenceTick,
            GeosphereSchedule = runtime.GeosphereSchedule,
            AtmosphereSchedule = runtime.AtmosphereSchedule,
            MaxTick = runtime.MaxTick,
            GenerationGraphFamily = family,
            LayerProjectionProfiles = BuildLayerProjectionProfiles(runtime),
            CrustVolume = runtime.CrustVolume,
            BoundarySections = runtime.BoundarySections,
            CrustSnapshotTicks = snapshotTickStates,
            VerticalExaggeration = runtime.VerticalExaggeration,
            SurfaceSubdivision = runtime.SurfaceSubdivision,
            AdaptiveSubdivisionMaxDepth = runtime.AdaptiveSubdivisionMaxDepth,
            AdaptiveSubdivisionEdgeHeightDelta = runtime.AdaptiveSubdivisionEdgeHeightDelta,
            AdaptiveSubdivisionFeatureWeightDelta = runtime.AdaptiveSubdivisionFeatureWeightDelta,
#pragma warning disable CS0618
            ContinentalPlateIds = WorldCrustRunSpec.ContinentalPlateIdsForPresentation(
                renderOptions, SphereRegimeScheduleDefaults.PlateOnsetTick),
#pragma warning restore CS0618
        };
    }

    private static IReadOnlyList<PlanetLayerProjectionProfile> BuildLayerProjectionProfiles(
        PlanetPresentationRuntime runtime)
        => new[]
        {
            PlanetLayerProjectionProfile.Crust(
                runtime.VerticalExaggeration,
                runtime.SurfaceSubdivision,
                runtime.AdaptiveSubdivisionMaxDepth,
                runtime.AdaptiveSubdivisionEdgeHeightDelta,
                runtime.AdaptiveSubdivisionFeatureWeightDelta)
        };

    public WorldGlobeSnapshot GetGlobeSnapshotAt(long tick)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var renderOptions = ResolvePlanetRenderOptions(family);
        var onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var rotationProjection = _history.GetActiveRotationProjection(onsetTick);
        var reconstructor = GetCachedGlobeReconstructor(renderOptions, onsetTick, rotationProjection);
        return reconstructor.BuildGlobeAt(tick);
    }

    public byte[] GetGlobeBoundaryCellsAt(long tick)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var renderOptions = ResolvePlanetRenderOptions(family);
        var onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var rotationProjection = _history.GetActiveRotationProjection(onsetTick);
        var reconstructor = GetCachedGlobeReconstructor(renderOptions, onsetTick, rotationProjection);
        return reconstructor.ClassifyCellsAt(tick);
    }

    /// <summary>
    /// Mobile-plate presentation window after onset. Widened from the original 20 Ma proving
    /// window to 1 Gy (1000 Ma = 100,000,000 ticks at 100k ticks/Ma = 1000 ka) so long-arc
    /// plate motion is scrubbable; snapshot products materialize lazily per governing snapshot,
    /// so the wider window costs nothing until seeked.
    /// </summary>
    internal const long MobilePlateWindowTicks = 100_000_000L;

    /// <summary>
    /// Per-tick continental fraction (P3 light path): re-samples the cached crust snapshot's onset
    /// material through the <see cref="PlateFrameSampler"/> at the seek tick. The snapshot's state is
    /// keyed at <paramref name="tick"/> so the sampler's Lagrangian transport carries onset material
    /// to the query tick, producing smoothly drifting fractions between 5 M-tick snapshots.
    /// </summary>
    public IReadOnlyDictionary<int, double> GetContinentalFractionByCellAt(long tick)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));

        var family = WorldGenerationGraphDefaults.BuildFamily();
        var renderOptions = ResolvePlanetRenderOptions(family);
        var onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var rotationProjection = _history.GetActiveRotationProjection(onsetTick);

        var products = GetOrBuildCrustTickProducts(
            renderOptions,
            onsetTick,
            tick,
            family.Revision,
            rotationProjection);
        if (!products.Materialization.Result.StateByTick.TryGetValue(products.SnapshotTick, out var snapshotState)
            || snapshotState.Count == 0)
        {
            return new Dictionary<int, double>();
        }

        var rotationProvider = BuildRotationProvider(
            rotationProjection,
            products.Materialization.Result.Plates,
            onsetTick);
        var sampler = new PlateFrameSampler(
            products.Materialization.Tessellation,
            products.Materialization.Result.Plates,
            products.Materialization.Topology,
            onsetTick,
            rotationProvider);

        var reconstructor = GetCachedGlobeReconstructor(renderOptions, onsetTick, rotationProjection);
        var currentGlobe = reconstructor.BuildGlobeAt(tick);
        var plateIdByCell = currentGlobe.Cells.ToDictionary(c => c.CellId, c => c.PlateId);

        var sampledState = sampler.SampleAt(tick, StateKeyedAt(products, tick), plateIdByCell);
        return ContinentalFractionsFromState(sampledState);
    }

    public LayerFilmstripPreviewMap? GetLayerFilmstripPreview(LayerFilmstripPreviewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Tick < 0) throw new ArgumentOutOfRangeException(nameof(request.Tick));
        if (request.Width <= 0) throw new ArgumentOutOfRangeException(nameof(request.Width));
        if (request.Height <= 0) throw new ArgumentOutOfRangeException(nameof(request.Height));

        // Honored per pixel row below (contract requirement): this render runs bundle code on a
        // threadpool thread, so an uncancelled call left on the stack roots the timeline AND world
        // ALCs past the hot-reload collection probe.
        cancellationToken.ThrowIfCancellationRequested();

        return FilmstripRevisionGate.RenderIfStable(
            request.GraphRevision,
            () => GetGenerationProductsAsync().GraphRevision,
            startRevision =>
            {
                var family = WorldGenerationGraphDefaults.BuildFamily();
                var renderOptions = ResolvePlanetRenderOptions(family);
                int previewFrequency = ResolveFilmstripFrequency(request.ViewRung);
                var previewOptions = renderOptions with
                {
                    TessellationFrequency = previewFrequency,
                    SurfaceSubdivision = SurfaceSubdivisionMode.Fixed,
                    AdaptiveSubdivisionMaxDepth = 0,
                };
                long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
                var rotationProjection = _history.GetActiveRotationProjection(onsetTick);

                var map = request.LayerId switch
                {
                    "geosphere.crust" => BuildCrustFilmstripPreview(request, previewOptions, startRevision, rotationProjection, cancellationToken),
                    "geosphere.plate" => BuildPlateFilmstripPreview(request, previewOptions, startRevision, rotationProjection, cancellationToken),
                    "geosphere.magma-ocean" => BuildProceduralFilmstripPreview(request, previewFrequency, startRevision, "magma-ocean", new(225, 74, 38), new(255, 190, 72), cancellationToken),
                    "geosphere.stagnant-lid" => BuildProceduralFilmstripPreview(request, previewFrequency, startRevision, "stagnant-lid", new(54, 68, 74), new(125, 101, 82), cancellationToken),
                    "geosphere.mantle" => BuildMantleFilmstripPreview(request, previewOptions, startRevision, rotationProjection, cancellationToken),
                    _ when string.Equals(request.SphereId, "atmosphere", StringComparison.Ordinal)
                        => BuildProceduralFilmstripPreview(request, previewFrequency, startRevision, "atmosphere-placeholder", new(39, 93, 143), new(145, 203, 224), cancellationToken),
                    _ => BuildProceduralFilmstripPreview(request, previewFrequency, startRevision, "layer-placeholder", new(70, 82, 92), new(128, 146, 156), cancellationToken),
                };

                cancellationToken.ThrowIfCancellationRequested();
                return map;
            });
    }

    private LayerFilmstripPreviewMap BuildCrustFilmstripPreview(
        LayerFilmstripPreviewRequest request,
        WorldGenerationRenderOptions previewOptions,
        int graphRevision,
        RotationAuthorityProjection rotationProjection,
        CancellationToken cancellationToken)
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        if (request.Tick < onsetTick)
            return BuildProceduralFilmstripPreview(request, previewOptions.TessellationFrequency, graphRevision, "pre-crust", new(45, 49, 54), new(100, 85, 68), cancellationToken);

        var products = GetOrBuildCrustTickProducts(
            previewOptions,
            onsetTick,
            request.Tick,
            graphRevision,
            rotationProjection);
        cancellationToken.ThrowIfCancellationRequested();
        var reconstructor = GetCachedGlobeReconstructor(previewOptions, onsetTick, rotationProjection);
        var currentGlobe = reconstructor.BuildGlobeAt(request.Tick);
        cancellationToken.ThrowIfCancellationRequested();
        var currentArcs = reconstructor.BuildBoundaryArcsAt(request.Tick);
        var rotationProvider = BuildRotationProvider(
            rotationProjection,
            products.Materialization.Result.Plates,
            onsetTick);
        var sampler = new PlateFrameSampler(
            products.Materialization.Tessellation,
            products.Materialization.Result.Plates,
            products.Materialization.Topology,
            onsetTick,
            rotationProvider);
        var plateIdByCell = currentGlobe.Cells.ToDictionary(c => c.CellId, c => c.PlateId);
        var stateForSampling = StateKeyedAt(products, request.Tick);
        var sampledState = sampler.SampleAt(request.Tick, stateForSampling, plateIdByCell);
        var sampledFeatures = sampler.SampleFeaturesAt(currentGlobe, sampledState, currentArcs);
        var elevations = sampler.SampleElevationsAt(
            currentGlobe,
            sampledState,
            sampledFeatures,
            currentArcs,
            previewOptions.BoundaryProfiles,
            previewOptions.HydrosphereMode);

        var cells = BuildFilmstripCells(currentGlobe, cell =>
        {
            double fraction = sampledState.TryGetValue(cell.CellId, out var state) ? state.ContinentalFraction : 0.0;
            double elevation = cell.CellId >= 0 && cell.CellId < elevations.Length ? elevations[cell.CellId] : 0.0;
            return (fraction, elevation);
        });
        var rgba = RasterizeCells(request.Width, request.Height, cells, ColorCrustCell, cancellationToken);
        return new LayerFilmstripPreviewMap(
            request.SphereId,
            request.LayerId,
            request.Tick,
            products.SnapshotTick,
            graphRevision,
            request.ViewRung,
            previewOptions.TessellationFrequency,
            request.Width,
            request.Height,
            "crust-low-res",
            rgba);
    }

    private LayerFilmstripPreviewMap BuildPlateFilmstripPreview(
        LayerFilmstripPreviewRequest request,
        WorldGenerationRenderOptions previewOptions,
        int graphRevision,
        RotationAuthorityProjection rotationProjection,
        CancellationToken cancellationToken)
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var reconstructor = GetCachedGlobeReconstructor(previewOptions, onsetTick, rotationProjection);
        var globe = reconstructor.BuildGlobeAt(request.Tick);
        cancellationToken.ThrowIfCancellationRequested();
        var cells = BuildFilmstripCells(globe, cell => (cell.PlateId, 0.0));
        var rgba = RasterizeCells(request.Width, request.Height, cells, ColorPlateCell, cancellationToken);
        return new LayerFilmstripPreviewMap(
            request.SphereId,
            request.LayerId,
            request.Tick,
            request.Tick,
            graphRevision,
            request.ViewRung,
            previewOptions.TessellationFrequency,
            request.Width,
            request.Height,
            "plate-low-res",
            rgba);
    }

    private LayerFilmstripPreviewMap BuildMantleFilmstripPreview(
        LayerFilmstripPreviewRequest request,
        WorldGenerationRenderOptions previewOptions,
        int graphRevision,
        RotationAuthorityProjection rotationProjection,
        CancellationToken cancellationToken)
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var reconstructor = GetCachedGlobeReconstructor(previewOptions, onsetTick, rotationProjection);
        var arcs = reconstructor.BuildBoundaryArcsAt(request.Tick);
        cancellationToken.ThrowIfCancellationRequested();
        var history = MantleHistoryAdapter.Build(arcs, onsetTick);
        var mantleConfig = MantleViewConfig.Default;
        var fieldConfig = new MantleFieldConfig
        {
            Seed = MantleIsosurfaceExtractor.FieldSeed,
            CmbRadius = mantleConfig.InnerRadius,
            SlabSheetThickness = 0.03,
            SlabPolylineSamples = 20,
        };
        var field = new MantleAnomalyField(fieldConfig, history, request.Tick);

        const double shellRadius = 0.75;
        var rgba = new byte[request.Width * request.Height * 4];
        for (int y = 0; y < request.Height; y++)
        {
            // Per-row cancellation: EvaluateAt is the expensive inner call (clrstack-proven to
            // outlive the ALC collection probe when uncancelled).
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < request.Width; x++)
            {
                var direction = LayerFilmstripEquirect.PixelToDirection(x, y, request.Width, request.Height);
                double anomaly = field.EvaluateAt(new Vector3D(
                    direction.X * shellRadius,
                    direction.Y * shellRadius,
                    direction.Z * shellRadius));
                WritePixel(rgba, x, y, request.Width, ColorMantleAnomaly(anomaly));
            }
        }

        return new LayerFilmstripPreviewMap(
            request.SphereId,
            request.LayerId,
            request.Tick,
            request.Tick,
            graphRevision,
            request.ViewRung,
            previewOptions.TessellationFrequency,
            request.Width,
            request.Height,
            "mantle-shell-low-res",
            rgba);
    }

    private static LayerFilmstripPreviewMap BuildProceduralFilmstripPreview(
        LayerFilmstripPreviewRequest request,
        int sourceFrequency,
        int graphRevision,
        string sourceKind,
        Rgb low,
        Rgb high,
        CancellationToken cancellationToken)
    {
        var rgba = new byte[request.Width * request.Height * 4];
        for (int y = 0; y < request.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double v = request.Height <= 1 ? 0.0 : y / (double)(request.Height - 1);
            for (int x = 0; x < request.Width; x++)
            {
                double u = request.Width <= 1 ? 0.0 : x / (double)(request.Width - 1);
                double wave = 0.5 + (0.5 * Math.Sin((u * Math.PI * 4.0) + (request.Tick * 0.00000008)));
                double t = Math.Clamp((v * 0.65) + (wave * 0.35), 0.0, 1.0);
                WritePixel(rgba, x, y, request.Width, Lerp(low, high, t));
            }
        }

        return new LayerFilmstripPreviewMap(
            request.SphereId,
            request.LayerId,
            request.Tick,
            request.Tick,
            graphRevision,
            request.ViewRung,
            sourceFrequency,
            request.Width,
            request.Height,
            sourceKind,
            rgba);
    }

    /// <summary>
    /// The sampler resolves state by exact tick key, but playheads are rarely on a snapshot
    /// boundary. Re-key the governing snapshot's onset material at the query tick so the
    /// sampler's Lagrangian transport (delta = tick − onset) carries material to any tick.
    /// Falls back to the raw series when the snapshot state is absent (e.g. pre-onset).
    /// </summary>
    private static IReadOnlyDictionary<long, IReadOnlyDictionary<int, CellCrustState>> StateKeyedAt(
        CrustTickProducts products, long tick)
    {
        if (products.Materialization.Result.StateByTick.TryGetValue(products.SnapshotTick, out var snapshotState)
            && snapshotState.Count > 0)
        {
            return new Dictionary<long, IReadOnlyDictionary<int, CellCrustState>> { [tick] = snapshotState };
        }
        return products.Materialization.Result.StateByTick;
    }

    internal static IPlateRotationProvider BuildRotationProvider(
        RotationAuthorityProjection rotationProjection,
        IReadOnlyList<TopoPlate> plates,
        long onsetTick)
    {
        return rotationProjection.Provider
            ?? new GeneratedEulerPoleRotationProvider(plates, onsetTick);
    }

    public MantleIsosurfaceSet GetMantleIsosurfacesAsync(long tick)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));

        // Boundary arcs at the playhead tick drive the conditioned field (a regime's arcs differ from
        // the onset frame's). The plate-onset tick is the v1 ActiveSinceTick for every segment.
        var document = GetPlanetPresentationAsync(tick);
        var onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        return MantleIsosurfaceExtractor.Extract(document.BoundaryArcs, tick, onsetTick);
    }

    public WorldGenerationResult RunGenerationAsync(WorldGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsGenerationGraphRequest(request))
        {
            var recipe = ReadRotationSourceFromParameters(request.Parameters);
            if (recipe.Kind == RotationSourceKind.Imported)
            {
                _history.ImportRotationSource(
                    request.WorldId,
                    ReadRotationBranchId(request.Parameters),
                    recipe,
                    SphereRegimeScheduleDefaults.PlateOnsetTick);
            }
            else
            {
                _history.UseGeneratedRotationSource();
            }
        }

        var result = _history.RunGeneration(request);
        if (result.Success && IsGenerationGraphRequest(request))
            UpdateGenerationProducts(request);
        EmitGenerationChanged(new WorldGenerationChangedEvent(result.ResultWorldId, "generation", request.GenerationSpec));
        return result;
    }

    public IDisposable SubscribeGenerationChanged(Action<WorldGenerationChangedEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_subscribersGate)
        {
            if (_disposed)
                return Disposable.Empty;
            _subscribers.Add(callback);
        }

        return new GenerationChangedSubscription(this, callback);
    }

    private void EmitGenerationChanged(WorldGenerationChangedEvent evt)
    {
        Action<WorldGenerationChangedEvent>[] snapshot;
        lock (_subscribersGate) snapshot = _subscribers.ToArray();
        foreach (var cb in snapshot)
        {
            try
            {
                cb(evt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GenerationChanged subscriber faulted.");
                _lastSubscriberError = ex;
            }
        }
    }

    private void UpdateGenerationProducts(WorldGenerationRequest request)
    {
        lock (_generationProductsGate)
        {
            _generationProducts = ToProductsView(request, _generationProducts);
            _cachedCrustSnapshotTicks = ReadSnapshotTicks(request.Parameters);
        }
    }

    private static RotationSourceRecipe ReadRotationSourceFromParameters(
        IReadOnlyDictionary<string, object>? parameters)
    {
        if (parameters is null)
            return RotationSourceRecipe.Default;

        if (!parameters.TryGetValue("rotationSourceKind", out var kindObj) || kindObj is not string kind)
            return RotationSourceRecipe.Default;

        string normalized = kind.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
            return RotationSourceRecipe.Default;

        if (normalized is "generated" or "gen" or "default")
            return RotationSourceRecipe.Default;

        if (normalized is not ("imported" or "rot" or "gplates"))
        {
            throw new ArgumentException(
                $"rotationSourceKind '{normalized}' is not recognized. "
                + "Accepted kinds are 'generated' (or absent) and 'imported', 'rot', or 'gplates'.",
                nameof(parameters));
        }

        if (!parameters.TryGetValue("rotationSourcePayload", out var payloadObj) || payloadObj is not string payload
            || string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException(
                "Imported rotation source requires a non-empty 'rotationSourcePayload'.",
                nameof(parameters));
        }

        string name = parameters.TryGetValue("rotationSourceName", out var nameObj) && nameObj is string n
            && !string.IsNullOrWhiteSpace(n)
                ? n
                : "imported";

        return new RotationSourceRecipe(RotationSourceKind.Imported, payload, name);
    }

    private static string ReadRotationBranchId(IReadOnlyDictionary<string, object>? parameters)
    {
        if (parameters is not null
            && parameters.TryGetValue("rotationSourceBranchId", out var value)
            && value is string branchId
            && !string.IsNullOrWhiteSpace(branchId))
        {
            return branchId.Trim();
        }

        return "main";
    }

    private static IReadOnlyList<long> ReadSnapshotTicks(IReadOnlyDictionary<string, object>? parameters)
    {
        if (parameters is null)
            return Array.Empty<long>();

        if (!parameters.TryGetValue("snapshotTicks", out var value) || value is null)
            return Array.Empty<long>();

        return value switch
        {
            long[] ticks => ticks,
            int[] ints => ints.Select(i => (long)i).ToArray(),
            IEnumerable<long> ticks => ticks.ToArray(),
            IEnumerable<int> ints => ints.Select(i => (long)i).ToArray(),
            JsonElement json => ReadJsonLongArray(json),
            string text => ReadStringLongArray(text),
            _ => Array.Empty<long>(),
        };
    }

    private static IReadOnlyList<long> ReadJsonLongArray(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return Array.Empty<long>();

        var result = new List<long>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var l))
                result.Add(l);
        }

        return result;
    }

    private static IReadOnlyList<long> ReadStringLongArray(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith("[", StringComparison.Ordinal))
            return Array.Empty<long>();

        try
        {
            return JsonSerializer.Deserialize<long[]>(text) ?? Array.Empty<long>();
        }
        catch (JsonException)
        {
            return Array.Empty<long>();
        }
    }

    private static IReadOnlyList<CrustSnapshotTickState> BuildCrustSnapshotTickStates(
        SphereRegimeSchedule schedule,
        IReadOnlyList<long> availableTicks,
        long maxTick)
    {
        var mobilePlate = schedule.Regimes.FirstOrDefault(r =>
            string.Equals(r.RegimeId, "mobile-plate", StringComparison.Ordinal));

        if (mobilePlate is null)
            return Array.Empty<CrustSnapshotTickState>();

        var availableSet = new HashSet<long>(availableTicks);
        var series = CrustSnapshotTickSeries.ForRegime(mobilePlate, CrustSnapshotTickSeries.DefaultSpacingTicks, maxTick);
        return series.SnapshotTicks
            .Select(t => new CrustSnapshotTickState(t, availableSet.Contains(t)))
            .ToArray();
    }

    private static WorldGenerationProductsView ToProductsView(
        WorldGenerationRequest request,
        WorldGenerationProductsView previous)
    {
        IReadOnlyDictionary<string, object> parameters =
            request.Parameters ?? new Dictionary<string, object>(StringComparer.Ordinal);
        var products = ReadStringArray(parameters, "productAddresses");
        var graphRevision = ReadInt(parameters, "graphRevision", previous.GraphRevision);
        var referenceTick = ReadLong(
            parameters,
            "canonicalTick",
            ReadLong(parameters, "tick", 0L));

        return new WorldGenerationProductsView(graphRevision, products, referenceTick);
    }

    private static bool IsGenerationGraphRequest(WorldGenerationRequest request)
        => request.Parameters is not null
           && request.Parameters.TryGetValue("source", out var source)
           && string.Equals(source?.ToString(), GenerationGraphSource, StringComparison.Ordinal);

    private static PlanetPresentationLayer? ToPlanetLayer(
        string productAddress,
        WorldGenerationGraphFamilyDocument family,
        long? selectedProductTick = null)
    {
        if (!WorldGenerationProductAddress.TryParse(productAddress, out var address) || address is null)
            return null;

        var (regimeId, layerId) = SplitRegimeLayer(address);
        var regime = string.IsNullOrEmpty(regimeId) ? null : regimeId;
        var graphId = WorldGenerationGraphFamilyComposer.TryFindLayerBinding(family, address.Domain, layerId, regime)?.GraphId;
        var source = WorldGenerationGraphFamilyComposer.TryFindDefaultLayerSourceBinding(family, address.Domain, layerId, regime);

        // The selected crust-snapshot tick applies to the mobile-plate crust layer ONLY, and it
        // rewrites ProductTick and ProductAddress TOGETHER — a layer whose advertised tick
        // contradicts its address is corrupt metadata (2026-07-03 review fix: the tick override
        // used to apply to every layer while the address rewrite was crust-scoped).
        long productTick = address.Tick;
        if (selectedProductTick.HasValue
            && string.Equals(layerId, "geosphere.crust", StringComparison.Ordinal)
            && string.Equals(regimeId, "mobile-plate", StringComparison.Ordinal))
        {
            productTick = selectedProductTick.Value;
            address = address with { Tick = selectedProductTick.Value };
        }

        return new PlanetPresentationLayer(
            LayerId: layerId,
            RegimeId: regimeId,
            Variant: address.Variant,
            Branch: address.Branch,
            ProductDomain: address.Domain,
            ProductName: address.Product,
            ProductTick: productTick,
            ProductAddress: address.ToPath(),
            GenerationGraphId: graphId,
            SourceId: source?.SourceId,
            SourceKind: source?.SourceKind,
            SourceLabel: source?.Label,
            SourceAvailability: source?.Availability,
            RendererContract: source?.RendererContract);
    }

    private static (string RegimeId, string LayerId) SplitRegimeLayer(WorldGenerationProductAddress address)
    {
        var separator = address.Product.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || separator == address.Product.Length - 1)
            return (string.Empty, $"{address.Domain}.{address.Product}");

        return (
            RegimeId: address.Product[..separator],
            LayerId: address.Product[(separator + 1)..]);
    }

    private PlanetPresentationRuntime BuildPlanetPresentationRuntime(
        WorldGenerationGraphFamilyDocument family,
        WorldGenerationRenderOptions renderOptions,
        long arcTick,
        RotationAuthorityProjection rotationProjection)
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var reconstructor = GetCachedGlobeReconstructor(renderOptions, onsetTick, rotationProjection);
        var geosphere = SphereRegimeScheduleDefaults.GeosphereDefault;
        var atmosphere = SphereRegimeScheduleDefaults.AtmosphereFor(onsetTick);
        var currentGlobe = reconstructor.BuildGlobeAt(arcTick);
        IReadOnlyList<PlateBoundaryArc> currentArcs = reconstructor.BuildBoundaryArcsAt(arcTick);

        // PRE-ONSET (magma-ocean / stagnant-lid): there is NO crust — running the crust
        // pipeline with endTick < onset walks evolution ticks negative and throws inside
        // CanonicalTick (boot regression 2026-07-06: Rebind() fetches at the controller's
        // initial tick 0 and the failed fetch unbound the whole planet). Return the lid
        // globe with empty crust products instead.
        if (arcTick < onsetTick)
        {
            return new PlanetPresentationRuntime(
                currentGlobe,
                arcTick,
                geosphere,
                atmosphere,
                onsetTick + MobilePlateWindowTicks,
                null,
                Array.Empty<BoundarySectionDocument>(),
                renderOptions.VerticalExaggeration,
                renderOptions.SurfaceSubdivision,
                renderOptions.AdaptiveSubdivisionMaxDepth,
                renderOptions.AdaptiveSubdivisionEdgeHeightDelta,
                renderOptions.AdaptiveSubdivisionFeatureWeightDelta);
        }

        var products = GetOrBuildCrustTickProducts(
            renderOptions,
            onsetTick,
            arcTick,
            family.Revision,
            rotationProjection);
        var rotationProvider = BuildRotationProvider(
            rotationProjection,
            products.Materialization.Result.Plates,
            onsetTick);
        var sampler = new PlateFrameSampler(
            products.Materialization.Tessellation,
            products.Materialization.Result.Plates,
            products.Materialization.Topology,
            onsetTick,
            rotationProvider);

        var plateIdByCell = currentGlobe.Cells.ToDictionary(c => c.CellId, c => c.PlateId);
        // The sampler looks state up by exact tick, but the playhead is rarely on a 5 M-tick
        // snapshot boundary; key the governing snapshot's onset material at the playhead tick so
        // seeks between snapshots bind land instead of an empty (all-ocean) state.
        var stateForSampling = StateKeyedAt(products, arcTick);
        var sampledState = sampler.SampleAt(arcTick, stateForSampling, plateIdByCell);
        var sampledFractions = ContinentalFractionsFromState(sampledState);

        var sampledFeatures = sampler.SampleFeaturesAt(currentGlobe, sampledState, currentArcs);
        currentArcs = ConvergentPolarity.Attach(
            currentArcs,
            currentGlobe.Cells,
            sampledFeatures,
            sampledState);
        // Directive 3d — plate-anchored birth roughness: a derived, deterministic noise field sampled
        // in the PLATE-MATERIAL frame (rides drifting plates), amplitude-conditioned on crust age. It
        // enters the SAME elevation-side composition both the World and slab paths consume (added onto
        // CellElevations here), inside the north-star interior-fabric budget. The seed flows from the
        // world render options so each world gets its own realization.
        var birthRoughnessProfile = BirthRoughnessProfile.Default with
        {
            Noise = BirthRoughnessProfile.Default.Noise with { Seed = renderOptions.Seed },
        };
        var birthRoughness = sampler.SampleBirthRoughnessAt(arcTick, stateForSampling, birthRoughnessProfile);

        var surfaceData = sampler.SampleSurfaceDataAt(
            currentGlobe,
            sampledState,
            sampledFeatures,
            currentArcs,
            renderOptions.BoundaryProfiles,
            renderOptions.HydrosphereMode,
            birthRoughnessByCell: birthRoughness);
        var cellElevations = surfaceData.Elevations;
        var cellFeatures = surfaceData.Features;

        var cellCrustThickness = products.Materialization.BuildCrustThickness(
                products.GlobeAtSnapshot,
                arcTick,
                _logger)
            ?? Enumerable.Repeat(
                    FantaSim.App.World.Composition.CutawayStratumProfile.DefaultCrustThicknessMetres,
                    currentGlobe.CellCount)
                .ToArray();

        var boundarySections = products.Materialization.BuildBoundarySections(
            products.GlobeAtSnapshot,
            products.ArcsAtSnapshot,
            arcTick,
            _logger);

        var crustVolume = products.Materialization.BuildVolumeState(
            currentGlobe,
            currentArcs,
            arcTick,
            renderOptions.Seed,
            family.Revision,
            renderOptions.VerticalExaggeration,
            renderOptions.BoundaryProfiles,
            cellElevations,
            cellCrustThickness,
            cellFeatures,
            sampledFractions);
        bool requiresUnderlapProof = currentArcs.Any(arc =>
            arc.Kind == PlateBoundaryKind.Convergent
            && !arc.IsCollision
            && arc.SubductingPlateId is not null);
        if (requiresUnderlapProof)
        {
            if (!crustVolume.TryFindConvergentUnderlapProof(out var proof))
            {
                throw new InvalidOperationException(
                    $"Crust volume {crustVolume.Digest} has convergent polarity but no ordered "
                  + "overriding/down-going ray intervals.");
            }

            _logger.LogInformation(
                "Crust underlap proof: digest={Digest}, arc={BoundaryArc}, "
              + "overriding={OverridingPlate}, downGoing={SubductingPlate}, "
              + "downGoingCell={SubductingCell}, "
              + "overridingInterval=[{OverEnter:R},{OverExit:R}], "
              + "downGoingInterval=[{DownEnter:R},{DownExit:R}].",
                crustVolume.Digest,
                proof.BoundaryArcIndex,
                proof.OverridingPlateId,
                proof.SubductingPlateId,
                proof.SubductingCellId,
                proof.OverridingEnter,
                proof.OverridingExit,
                proof.SubductingEnter,
                proof.SubductingExit);
        }

        _logger.LogInformation(
            "Crust volume state materialized: algorithm={Algorithm}, tick={Tick}, "
          + "cells={CellCount}, arcs={ArcCount}, digest={Digest}.",
            crustVolume.AlgorithmVersion,
            crustVolume.Tick,
            crustVolume.CellCount,
            crustVolume.BoundaryArcs.Count,
            crustVolume.Digest);

        return new PlanetPresentationRuntime(
            currentGlobe,
            arcTick,
            geosphere,
            atmosphere,
            onsetTick + MobilePlateWindowTicks,
            crustVolume,
            boundarySections,
            renderOptions.VerticalExaggeration,
            renderOptions.SurfaceSubdivision,
            renderOptions.AdaptiveSubdivisionMaxDepth,
            renderOptions.AdaptiveSubdivisionEdgeHeightDelta,
            renderOptions.AdaptiveSubdivisionFeatureWeightDelta);
    }

    private static WorldGenerationRenderOptions ResolvePlanetRenderOptions(WorldGenerationGraphFamilyDocument family)
    {
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            SphereRegimeScheduleDefaults.PlateOnsetTick,
            WorldGenerationGraphDefaults.GeosphereSphereId);

        return WorldGenerationRenderOptions.Resolve(source.Graph);
    }

    private CrustTickProducts GetOrBuildCrustTickProducts(
        WorldGenerationRenderOptions renderOptions,
        long onsetTick,
        long arcTick,
        int graphRevision,
        RotationAuthorityProjection rotationProjection)
    {
        var mobilePlateRegime = GeosphereScheduleFor(onsetTick).Regimes
            .FirstOrDefault(r => string.Equals(r.RegimeId, "mobile-plate", StringComparison.Ordinal));
        if (mobilePlateRegime is null)
            throw new InvalidOperationException("No mobile-plate regime found for crust snapshot selection.");

        var series = CrustSnapshotTickSeries.ForRegime(
            mobilePlateRegime,
            CrustSnapshotTickSeries.DefaultSpacingTicks,
            onsetTick + MobilePlateWindowTicks);
        var snapshotTick = series.SelectSnapshotForPlayhead(arcTick) ?? arcTick;
        // Seed + SpinRate mirror the sibling globe-reconstructor cache key (_globeReconstructorKey
        // below); GraphRevision mirrors CrustGenerationTriggerKey (CrustGenerationTriggerPolicy.cs)
        // -- a generation-graph edit must not serve a stale crust product (2026-07-11 cache-key
        // completion, vault/specs/2026-07-11-surrealdb-persistence-slice1-design.md §1.2).
        // RotationAuthorityDigest binds the product to the exact generated/imported motion truth;
        // changing authority cannot reuse deposits or feature semantics from the previous source.
        var key = new CrustProductCacheKey(
            renderOptions.Seed,
            renderOptions.TessellationFrequency,
            renderOptions.SpinRateRadiansPerMegaAnnum,
            graphRevision,
            rotationProjection.Authority.Digest,
            snapshotTick);
        lock (_crustProductCacheGate)
        {
            if (_crustProductCache.TryGetValue(key, out var cached))
                return cached;
        }

        var spec = WorldCrustRunSpec.ForPresentation(
            renderOptions,
            onsetTick,
            snapshotTick,
            rotationProjection.Provider);

        // Cross-session probe (2026-07-11 persistence slice 1): a hit here reconstructs the
        // materialization from the persisted crust-evolution fold WITHOUT re-running
        // WorldCrustMaterializer.MaterializeAsync's pipeline call -- see
        // WorldCrustMaterializer.FromPersistedRecord for exactly what is/isn't recomputed. Never
        // blocks longer than a local LiteDB read + MessagePack decode; a miss or any failure falls
        // straight through to the normal build path below.
        var stopwatch = Stopwatch.StartNew();
        var materialization = TryRestoreCrustMaterializationFromStore(key, spec);
        if (materialization is null)
        {
            materialization = WorldCrustMaterializer.MaterializeAsync(spec).GetAwaiter().GetResult();
            CrustPipelineBuildCountForTests++;
            stopwatch.Stop();
            // Windowed warm-boot gate log line (lead-facing, vault/specs/2026-07-11-surrealdb-
            // persistence-slice1-design.md §6.3): cold boot's first seek at a given key logs this.
            _logger.LogInformation(
                "Crust product ready in {ElapsedMs} ms via fresh pipeline build (key={Key}); persisting for next session.",
                stopwatch.ElapsedMilliseconds, key);

            // Persist AFTER the fetch has what it needs, off the fetch path: PersistCrustProductAsync
            // is tracked (FlushPendingCrustPersistenceAsync) but never awaited here.
            PersistCrustMaterializationAsync(key, materialization);
        }
        else
        {
            stopwatch.Stop();
            // Windowed warm-boot gate log line: a SECOND boot's seek at the SAME key logs this
            // instead, with an ElapsedMs the lead should observe as measurably smaller than the
            // fresh-build line above (both lines share the "Crust product ready in {ms} ms" prefix
            // and the same key= suffix so they diff cleanly).
            _logger.LogInformation(
                "Crust product ready in {ElapsedMs} ms via persisted-cache restore (key={Key}).",
                stopwatch.ElapsedMilliseconds, key);
        }

        var reconstructor = GetCachedGlobeReconstructor(renderOptions, onsetTick, rotationProjection);
        var globeAtSnapshot = reconstructor.BuildGlobeAt(snapshotTick);
        var arcsAtSnapshot = reconstructor.BuildBoundaryArcsAt(snapshotTick);
        var products = new CrustTickProducts(snapshotTick, materialization, globeAtSnapshot, arcsAtSnapshot);

        lock (_crustProductCacheGate)
        {
            _crustProductCache[key] = products;
        }

        return products;
    }

    /// <summary>
    /// Reads the persisted crust-product cache for <paramref name="key"/>, returning null on a miss
    /// (no store registered, no document under the current SchemaVersion/AppVersionStamp id, or any
    /// decode failure -- all three are treated identically: rebuild, never crash). The document id
    /// folds in both invalidation stamps (spec §5), so a SchemaVersion bump or app rebuild is a
    /// structural cache miss, not a post-fetch version check.
    /// </summary>
    private WorldCrustMaterialization? TryRestoreCrustMaterializationFromStore(CrustProductCacheKey key, WorldCrustRunSpec spec)
    {
        if (_documentStore is null)
            return null;

        var id = CrustProductCacheSchema.ComposeDocumentId(
            key.Seed,
            key.Frequency,
            key.SpinRateRadiansPerMegaAnnum,
            key.GraphRevision,
            key.RotationAuthorityDigest,
            key.SnapshotTick,
            CrustProductCacheSchema.SchemaVersion,
            CrustProductCacheSchema.CurrentAppVersionStamp);

        try
        {
            var blob = _documentStore.GetAsync<DocumentBlob>(CrustProductCacheSchema.Collection, id).GetAwaiter().GetResult();
            if (blob is null || blob.Data.Length == 0)
                return null;

            var record = CrustProductCacheSchema.Decode(blob.Data);
            if (record.Seed != key.Seed
                || record.Frequency != key.Frequency
                || record.SpinRateRadiansPerMegaAnnum != key.SpinRateRadiansPerMegaAnnum
                || record.GraphRevision != key.GraphRevision
                || !string.Equals(record.RotationAuthorityDigest, key.RotationAuthorityDigest, StringComparison.Ordinal)
                || record.SnapshotTick != key.SnapshotTick)
            {
                return null;
            }
            return WorldCrustMaterializer.FromPersistedRecord(spec, record);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore persisted crust product for key {Key}; rebuilding.", key);
            return null;
        }
    }

    /// <summary>
    /// Fire-and-forget (but tracked) write of a freshly-built materialization's crust-evolution fold
    /// to the persisted store. The spawned task closes over only resident-or-primitive parameters
    /// (<see cref="PersistBlobAsync"/> is <c>static</c>) -- never <c>this</c> -- so it carries no
    /// bundle reference past this call (pin-class 7, cross-alc-rules.md / the seven-pin-class list).
    /// </summary>
    private void PersistCrustMaterializationAsync(CrustProductCacheKey key, WorldCrustMaterialization materialization)
    {
        if (_documentStore is null)
            return;

        var record = WorldCrustMaterializer.ToPersistedRecord(
            materialization, key.Seed, key.Frequency, key.SpinRateRadiansPerMegaAnnum, key.GraphRevision,
            key.RotationAuthorityDigest, key.SnapshotTick);
        var id = CrustProductCacheSchema.ComposeDocumentId(
            key.Seed, key.Frequency, key.SpinRateRadiansPerMegaAnnum, key.GraphRevision,
            key.RotationAuthorityDigest, key.SnapshotTick,
            record.SchemaVersion, record.AppVersionStamp);
        var blob = new DocumentBlob(CrustProductCacheSchema.Encode(record));

        var task = PersistBlobAsync(_documentStore, CrustProductCacheSchema.Collection, id, blob, _logger);

        lock (_pendingCrustPersistGate)
        {
            _pendingCrustPersistTasks.RemoveAll(t => t.IsCompleted);
            _pendingCrustPersistTasks.Add(task);
        }
    }

    private static async Task PersistBlobAsync(IDocumentStore store, string collection, string id, DocumentBlob blob, ILogger logger)
    {
        try
        {
            await store.UpsertAsync(collection, id, blob).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist crust product {Id} in {Collection}; continuing in-memory-only.", id, collection);
        }
    }

    /// <summary>
    /// Awaits every in-flight crust-product persistence write. Test-only observability for
    /// deterministic assertions after a build (App.World.Tests); also invoked best-effort (bounded)
    /// from <see cref="Dispose"/> so a Service torn down immediately after a build doesn't silently
    /// drop the write.
    /// </summary>
    internal Task FlushPendingCrustPersistenceAsync()
    {
        Task[] pending;
        lock (_pendingCrustPersistGate)
        {
            pending = _pendingCrustPersistTasks.ToArray();
        }

        return Task.WhenAll(pending);
    }

    private static SphereRegimeSchedule GeosphereScheduleFor(long onsetTick)
        => SphereRegimeScheduleDefaults.GeosphereFor(onsetTick);

    private static IReadOnlyDictionary<int, double> ContinentalFractionsFromState(
        IReadOnlyDictionary<int, CellCrustState> state)
    {
        var result = new Dictionary<int, double>(state.Count);
        foreach (var (cellId, s) in state)
            result[cellId] = s.ContinentalFraction;
        return result;
    }

    private static int ResolveFilmstripFrequency(string? viewRung)
    {
        if (string.Equals(viewRung, "jz", StringComparison.Ordinal)
            || string.Equals(viewRung, "ka", StringComparison.Ordinal))
        {
            return 3;
        }

        return 2;
    }

    private static IReadOnlyList<LayerFilmstripCellSample> BuildFilmstripCells(
        WorldGlobeSnapshot globe,
        Func<GlobeCell, (double Primary, double Secondary)> values)
    {
        ArgumentNullException.ThrowIfNull(globe);
        ArgumentNullException.ThrowIfNull(values);

        var cells = new List<LayerFilmstripCellSample>(globe.Cells.Count);
        foreach (var cell in globe.Cells)
        {
            var center = Normalize(new GlobeVec3(
                (cell.C0.X + cell.C1.X + cell.C2.X) / 3f,
                (cell.C0.Y + cell.C1.Y + cell.C2.Y) / 3f,
                (cell.C0.Z + cell.C1.Z + cell.C2.Z) / 3f));
            var (primary, secondary) = values(cell);
            cells.Add(new LayerFilmstripCellSample(
                cell.CellId,
                center.X,
                center.Y,
                center.Z,
                cell.PlateId,
                primary,
                secondary));
        }

        return cells;
    }

    private static byte[] RasterizeCells(
        int width,
        int height,
        IReadOnlyList<LayerFilmstripCellSample> cells,
        Func<LayerFilmstripCellSample, Rgb> colorForCell,
        CancellationToken cancellationToken)
    {
        var rgba = new byte[width * height * 4];
        if (cells.Count == 0)
            return rgba;

        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                var direction = LayerFilmstripEquirect.PixelToDirection(x, y, width, height);
                int cellIndex = LayerFilmstripEquirect.NearestCellIndex(direction, cells);
                WritePixel(rgba, x, y, width, colorForCell(cells[cellIndex]));
            }
        }

        return rgba;
    }

    private static Rgb ColorCrustCell(LayerFilmstripCellSample cell)
    {
        double land = Math.Clamp(cell.Primary, 0.0, 1.0);
        double relief = Math.Clamp((cell.Secondary + 1800.0) / 5200.0, 0.0, 1.0);
        var ocean = Lerp(new Rgb(19, 63, 99), new Rgb(56, 123, 151), relief);
        var lowland = Lerp(new Rgb(72, 112, 64), new Rgb(154, 134, 82), relief);
        var mountain = Lerp(lowland, new Rgb(224, 220, 191), Math.Clamp((relief - 0.72) / 0.28, 0.0, 1.0));
        return Lerp(ocean, mountain, land);
    }

    private static Rgb ColorPlateCell(LayerFilmstripCellSample cell)
    {
        uint h = (uint)(cell.PlateId * 1103515245 + 12345);
        byte r = (byte)(72 + (h & 0x7f));
        byte g = (byte)(72 + ((h >> 8) & 0x7f));
        byte b = (byte)(72 + ((h >> 16) & 0x7f));
        return new Rgb(r, g, b);
    }

    private static Rgb ColorMantleAnomaly(double anomaly)
    {
        var neutral = new Rgb(28, 23, 34);
        if (anomaly < 0.0)
        {
            double t = Math.Clamp(-anomaly / MantleViewConfig.Default.ColdInnerThreshold, 0.0, 1.0);
            return Lerp(neutral, new Rgb(89, 153, 250), t);
        }
        else
        {
            double t = Math.Clamp(anomaly / MantleViewConfig.Default.WarmInnerThreshold, 0.0, 1.0);
            return Lerp(neutral, new Rgb(242, 77, 20), t);
        }
    }

    private static GlobeVec3 Normalize(GlobeVec3 v)
    {
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        if (len < 1e-9)
            return new GlobeVec3(0f, 0f, 1f);

        return new GlobeVec3((float)(v.X / len), (float)(v.Y / len), (float)(v.Z / len));
    }

    private static Rgb Lerp(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return new Rgb(
            (byte)Math.Round(a.R + ((b.R - a.R) * t)),
            (byte)Math.Round(a.G + ((b.G - a.G) * t)),
            (byte)Math.Round(a.B + ((b.B - a.B) * t)));
    }

    private static void WritePixel(byte[] rgba, int x, int y, int width, Rgb color)
    {
        int i = ((y * width) + x) * 4;
        rgba[i] = color.R;
        rgba[i + 1] = color.G;
        rgba[i + 2] = color.B;
        rgba[i + 3] = 255;
    }

    private sealed record PlanetPresentationRuntime(
        WorldGlobeSnapshot GlobeSnapshot,
        long GlobeReferenceTick,
        SphereRegimeSchedule GeosphereSchedule,
        SphereRegimeSchedule AtmosphereSchedule,
        long MaxTick,
        CrustVolumeState? CrustVolume,
        IReadOnlyList<BoundarySectionDocument> BoundarySections,
        double VerticalExaggeration,
        SurfaceSubdivisionMode SurfaceSubdivision,
        int AdaptiveSubdivisionMaxDepth,
        double AdaptiveSubdivisionEdgeHeightDelta,
        double AdaptiveSubdivisionFeatureWeightDelta);

    private sealed record CrustTickProducts(
        long SnapshotTick,
        WorldCrustMaterialization Materialization,
        WorldGlobeSnapshot GlobeAtSnapshot,
        IReadOnlyList<PlateBoundaryArc> ArcsAtSnapshot);

    /// <summary>
    /// Crust-product cache key. Extended 2026-07-11 (vault/specs/2026-07-11-surrealdb-persistence-
    /// slice1-design.md §1.2) to carry every world-identity dimension the render options and
    /// generation graph expose: Seed + SpinRateRadiansPerMegaAnnum mirror the sibling
    /// <see cref="_globeReconstructorKey"/> (Seed, Frequency, SpinRate, RotationAuthorityDigest)
    /// below; GraphRevision mirrors <c>CrustGenerationTriggerKey</c>
    /// (CrustGenerationTriggerPolicy.cs). RotationAuthorityDigest distinguishes the exact selected
    /// generated/imported rotation truth. Before these fixes the key carried only (Frequency,
    /// SnapshotTick), so worlds differing by Seed/SpinRate/GraphRevision/rotation authority could
    /// alias onto the same cache slot. <c>internal</c> (not <c>private</c>) so
    /// App.World.Tests (InternalsVisibleTo, App.World.csproj) can assert on key equality directly.
    /// </summary>
    internal readonly record struct CrustProductCacheKey(
        int Seed,
        int Frequency,
        double SpinRateRadiansPerMegaAnnum,
        int GraphRevision,
        string RotationAuthorityDigest,
        long SnapshotTick);

    private readonly record struct Rgb(byte R, byte G, byte B);

    private readonly object _globeReconstructorGate = new();
    private (int Seed, int Frequency, double SpinRateRadiansPerMegaAnnum, string RotationAuthorityDigest) _globeReconstructorKey;
    private OnsetRoster? _cachedGlobeRoster;
    private GlobeReconstructor? _cachedGlobeReconstructor;

    private GlobeReconstructor GetCachedGlobeReconstructor(
        WorldGenerationRenderOptions renderOptions,
        long onsetTick,
        RotationAuthorityProjection rotationProjection)
    {
        // Spin rate is part of the key: authoring the knob must rebuild the roster, not
        // serve a reconstructor seeded at the previous rate.
        var key = (
            renderOptions.Seed,
            renderOptions.TessellationFrequency,
            renderOptions.SpinRateRadiansPerMegaAnnum,
            rotationProjection.Authority.Digest);
        lock (_globeReconstructorGate)
        {
            if (_cachedGlobeReconstructor is null || _globeReconstructorKey != key)
            {
                _cachedGlobeRoster = OnsetRoster.Build(
                    renderOptions.Seed,
                    onsetTick,
                    renderOptions.TessellationFrequency,
                    renderOptions.SpinRateRadiansPerMegaAnnum);
                var plates = _cachedGlobeRoster.SeedPlatesAt(onsetTick);
                var rotationProvider = rotationProjection.Provider
                    ?? new GeneratedEulerPoleRotationProvider(plates, onsetTick);
                _cachedGlobeReconstructor = GlobeReconstructor.FromOnsetRoster(
                    _cachedGlobeRoster,
                    onsetTick,
                    SphereRegimeScheduleDefaults.GeosphereDefault,
                    renderOptions.TessellationFrequency,
                    rotationProvider);
                _globeReconstructorKey = key;
            }

            return _cachedGlobeReconstructor;
        }
    }

    private static string NewTruthWriterActorName()
        => $"world-truth-writer-{Guid.NewGuid():N}";

    private static int ReadInt(IReadOnlyDictionary<string, object> parameters, string key, int fallback)
    {
        if (!parameters.TryGetValue(key, out var value) || !TryReadLong(value, out var parsed))
            return fallback;
        return parsed is >= int.MinValue and <= int.MaxValue ? (int)parsed : fallback;
    }

    private static long ReadLong(IReadOnlyDictionary<string, object> parameters, string key, long fallback)
        => parameters.TryGetValue(key, out var value) && TryReadLong(value, out var parsed)
            ? parsed
            : fallback;

    private static bool TryReadLong(object? value, out long result)
    {
        if (value is long longValue)
        {
            result = longValue;
            return true;
        }

        if (value is int intValue)
        {
            result = intValue;
            return true;
        }

        if (value is string text)
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

        if (value is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Number && json.TryGetInt64(out result))
                return true;
            if (json.ValueKind == JsonValueKind.String)
                return long.TryParse(json.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        result = 0L;
        return false;
    }

    private static string[] ReadStringArray(IReadOnlyDictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return Array.Empty<string>();

        return value switch
        {
            string[] values => NormalizeStrings(values),
            IEnumerable<string> values => NormalizeStrings(values),
            JsonElement json => ReadStringArray(json),
            string text => ReadStringArray(text),
            _ => Array.Empty<string>(),
        };
    }

    private static string[] ReadStringArray(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        if (TryDeserializeArray<string>(text, out var values))
            return NormalizeStrings(values);

        return new[] { text };
    }

    private static string[] ReadStringArray(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            return NormalizeStrings(value.EnumerateArray().Select(item =>
                item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString()));
        }

        return value.ValueKind == JsonValueKind.String
            ? ReadStringArray(value.GetString() ?? string.Empty)
            : Array.Empty<string>();
    }

    private static bool TryDeserializeArray<T>(string text, out T[] values)
    {
        values = Array.Empty<T>();
        if (!text.TrimStart().StartsWith("[", StringComparison.Ordinal))
            return false;

        try
        {
            values = JsonSerializer.Deserialize<T[]>(text) ?? Array.Empty<T>();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string[] NormalizeStrings(IEnumerable<string?> values)
    {
        var result = new List<string>();
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                result.Add(value);
        }

        return result.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            // Best-effort, bounded: a Service disposed immediately after a build shouldn't silently
            // drop the persist write, but disposal must never hang on a slow/stuck store either. The
            // persisted store itself is resident (outlives this call either way), so a timeout here
            // only affects whether THIS process observes the write before exiting, not correctness.
            try
            {
                FlushPendingCrustPersistenceAsync().Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Logged inside PersistBlobAsync already; Dispose must not throw.
            }

            lock (_subscribersGate)
                _subscribers.Clear();
            _history.Dispose();
        }
        finally
        {
            try
            {
                _truthWriter.Dispose();
            }
            finally
            {
                _truthStoreHandle.Dispose();
            }
        }
    }

    private void UnsubscribeGenerationChanged(Action<WorldGenerationChangedEvent> callback)
    {
        lock (_subscribersGate)
            _subscribers.Remove(callback);
    }

    private sealed class GenerationChangedSubscription : IDisposable
    {
        private Service? _owner;
        private Action<WorldGenerationChangedEvent>? _callback;

        public GenerationChangedSubscription(Service owner, Action<WorldGenerationChangedEvent> callback)
        {
            _owner = owner;
            _callback = callback;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            var callback = Interlocked.Exchange(ref _callback, null);
            if (owner is null || callback is null)
                return;

            owner.UnsubscribeGenerationChanged(callback);
        }
    }

    private sealed class Disposable : IDisposable
    {
        public static readonly IDisposable Empty = new Disposable();

        public void Dispose()
        {
        }
    }
}
