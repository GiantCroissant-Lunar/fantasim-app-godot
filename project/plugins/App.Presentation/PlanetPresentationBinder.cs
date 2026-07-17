using System.Runtime.CompilerServices;
using FantaSim.App.Resource;
using FantaSim.App.Resource.Bundle;
using FantaSim.App.Ui;
using FantaSim.App.Ui.NodeGraph;
using FantaSim.App.Ui.Providers;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Rendering;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Shared;
using Godot;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;
using ResourceService = FantaSim.App.Resource.IService;
using WorldService = FantaSim.App.World.IService;

namespace FantaSim.App.Presentation;

internal sealed partial class PlanetPresentationBinder : IPlanetPresentation
{
    private const string StageBundleId = "stage";
    private const string WorldBundleId = "world";
    private static readonly NodePath PlanetLayerMountPath = new("Environment/PlanetMount/Planet/LayerMounts");
    // Keep the complete assembled globe inside the stage's unobstructed upper viewport while the
    // timeline remains available below. The old right-only offset left half the globe behind the HUD.
    private static readonly Vector3 PlanetBodyPreviewOffset = new(0.0f, 1.55f, 0.0f);

    // World-view height lens (look-dev 2026-07-03, knobbly-limb references): sign(h)*|h|^0.5 * scale.
    // The elevation field is ~±500..1,400 m interiors under 21,000+ m orogenic extremes — a ratio no
    // LINEAR lens can render (interiors invisible or peaks become spears). The sqrt profile compresses
    // ~48:1 to ~7:1. The assembled-reference pass raises the existing labeled lens so ordinary
    // generated boundary relief reads at roughly 3-5% of radius instead of disappearing at 1-2%;
    // rare 20 km-class extremes remain proportionate rather than becoming linear spikes. The S2
    // indicator names the profile (VerticalScaleLabel profile overload) — the lens is labeled, never
    // hidden. The crust DIAGNOSTIC view stays strictly linear on document.VerticalExaggeration:
    // diagnostics must not bend the scale. PROBE pending user sign-off — S1 doctrine amendment
    // (non-linear labeled lens) is a design decision, not a default.
    private const double WorldHeightExponent = 0.5;
    private const double WorldHeightScale = 0.0008;

    private readonly IRegistry _registry;
    private readonly ResourceService _resource;
    private readonly IBundleSceneRegistry _sceneRegistry;
    private readonly ILogger _log;
    private readonly PlanetTimelineController _timeline;
    private readonly IDisposable _timelineRegistration;
    private IDisposable? _generationSubscription;
    private Node3D? _activeRoot;
    private Node3D? _plateSurfaceRoot;

    /// <summary>
    /// Tunnel slice-1 shared-globe spike seam (vault/plans/2026-07-11-tunnel-slice1-plan.md
    /// Task 2): read-only access to the SAME planet node tree Stage's own camera already renders,
    /// so a second Camera3D/SubViewport can view it from a different angle without standing up an
    /// independent second binder. No other member of this class is exposed this way -- this is
    /// the ONE new seam the spike adds.
    /// </summary>
    internal Node3D? ActiveRoot => _activeRoot;

    // Resolved at execution time: the tunnel mount aligns to this node's global position without
    // parenting under the replaceable root, so a planet rebind cannot free the tunnel.
    internal Node3D? PlanetBody => _activeRoot?.GetNodeOrNull<Node3D>("PlanetBody");
    private PlateBoundaryFocusRenderer? _boundaryRenderer;
    private BoundarySectionRenderer? _boundarySectionRenderer;
    private PlanetGenerationGraphSource? _graphSource;
    private NodeGraphViewSource? _graphView;
    private PlanetGenerationGraphSource.PlanetGenerationTimelineGraphBinding? _graphBinding;
    private IDisposable? _graphViewRegistration;
    private bool _graphViewMounted;
    private readonly bool _showWorldGraph;
    private int? _subscribedWorldHash;
    private readonly PlanetPresentationReloadGate _worldBundleReload = new();
    private int _mountGeneration;
    private string? _boundRegimeId;
    private PlanetSurfaceBindStamp? _boundSurfaceStamp;
    private bool _regimeRefreshPending;
    private int? _pendingFrequencyOverride;
    private bool _applyingRefreshedDocument;
    private readonly ScrubRefreshCoordinator _scrubRefresh;
    private long? _boundCrustSnapshotTick;
    private IReadOnlyList<long> _boundCrustSnapshotTicks = Array.Empty<long>();
    private PlanetPresentationDocument? _currentDocument;
    private GlobeViewMode _currentViewMode;
    // D5 stacked-layer composition state. _currentComposition carries the full resolved decision
    // (derived mode + mantle mount + surface coloring). _currentViewMode is the DERIVED composition
    // mode (drives lighting, boundary, cutaway, status gates). _currentSurfaceViewMode is the surface
    // APPEARANCE mode (the coloring owner mapped back to a GlobeViewMode) that the plate-surface
    // build/bind and the separated-slab-top cache follow. The two view-mode fields decouple a combo's
    // lighting/gate plumbing (MantleInterior) from its slab-top coloring (e.g. terrain under mantle).
    private LayerCompositionDecision _currentComposition = new(
        DerivedViewMode: GlobeViewMode.Inactive,
        MountMantleInterior: false,
        SurfaceColoring: SurfaceColoringKind.World,
        TerrainRelief: false);
    private GlobeViewMode _currentSurfaceViewMode;

    private bool _disposed;
    private readonly string? _plateViewOverride;
    private long? _boundContinentsTick; // last tick whose membership the Continents caps show

    public PlanetPresentationBinder(
        IRegistry registry,
        ResourceService resource,
        IBundleSceneRegistry sceneRegistry,
        ILoggerFactory loggerFactory,
        string? plateViewOverride = null,
        bool showWorldGraph = false)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _resource = resource ?? throw new ArgumentNullException(nameof(resource));
        _sceneRegistry = sceneRegistry ?? throw new ArgumentNullException(nameof(sceneRegistry));
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));

        // M0 (spec D1): host config knob globe:plateView — "identity" keeps the PlateIdentity
        // diagnostic on the geosphere.plate track; anything else selects the Continents view.
        _plateViewOverride = plateViewOverride;
        _showWorldGraph = showWorldGraph;

        _log = loggerFactory.CreateLogger("World.PlanetPresentation");
        _timeline = new PlanetTimelineController(ApplyTimelineTick);
        _scrubRefresh = new ScrubRefreshCoordinator(
            new ScrubApplyScheduler(restDelayMs: 300L),
            requestRefresh: freq => ScheduleRegimeRefresh(freq),
            a => Callable.From(() => a()).CallDeferred(),
            () => System.Environment.TickCount64);

        _timelineRegistration = _registry.RegisterOwned<ITimelineController>(
            _timeline,
            new ServiceRegistration
            {
                Tags = new[] { "world", "timeline", "presentation" },
                Description = "Resident contract-only planet timeline controller"
            });

        _timeline.LayerSelectionChanged += OnLayerSelectionChanged;

        _resource.RuntimeChanging += OnResourceRuntimeChanging;
        _resource.RuntimeChanged += OnResourceRuntimeChanged;
        // The world pck WATCH deliberately does NOT live here (bundle-maximalism phase 1): this
        // binder ships inside the world bundle, and a watcher owned by the bundle cancels its own
        // reload mid-flight when the unload phase disposes it (the load half never runs). The
        // resident host owns the watch (Host.SubscribeResourceRuntimeEvents); this binder only
        // unmounts on RuntimeChanging and is recreated by the new bundle's PresentationPlugin.
    }

    private void ResetRegimeTracking()
    {
        _boundRegimeId = null;
        _regimeRefreshPending = false;
        _pendingFrequencyOverride = null;
        _boundCrustSnapshotTick = null;
        _boundCrustSnapshotTicks = Array.Empty<long>();
        _boundContinentsTick = null;
    }

    public void Rebind()
    {
        if (_disposed)
            return;

        var world = _registry.TryGet<WorldService>();
        if (world is null)
        {
            _log.LogWarning("Planet presentation skipped: world service is not registered.");
            return;
        }

        SubscribeGenerationChanged(world);

        PlanetPresentationDocument document;
        try
        {
            document = world.GetPlanetPresentationAsync(_timeline.Tick);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Planet presentation document failed: {Message}", ex.Message);
            return;
        }

        // Clear the bound-regime baseline so the UpdateFrom -> PushTick -> ApplyTimelineTick path
        // below (and any intermediate tick) cannot mistake this rebind for a regime transition.
        ResetRegimeTracking();
        _timeline.UpdateFrom(document);
        EnsureNodeGraphView(document);
        var expectedGeneration = ++_mountGeneration;
        Callable.From(() => BindDocument(document, expectedGeneration)).CallDeferred();
    }

    private void EnsureNodeGraphView(PlanetPresentationDocument document)
    {
        if (!_showWorldGraph)
            return;

        if (_graphView is null)
        {
            _graphSource = BuildInitialGraphSource(document);
            _graphView = new NodeGraphViewSource(_graphSource, title: "world generation graph");
            _graphBinding = new PlanetGenerationGraphSource.PlanetGenerationTimelineGraphBinding(_timeline, _graphSource);
            _graphViewRegistration = _registry.RegisterOwned<IViewSource>(
                _graphView,
                new ServiceRegistration
                {
                    Tags = new[] { "ui", "node-graph", "world" },
                    Description = "Resident world-generation layer graph view"
                });
        }
        else if (document.GenerationGraphFamily is not null)
        {
            _graphSource!.UpdateFamily(document.GenerationGraphFamily, _timeline.Tick);
            _graphBinding?.FollowNow();
        }

        if (_graphViewMounted)
            return;

        var viewHost = _registry.TryGet<IViewHost>();
        if (viewHost is null)
        {
            _log.LogWarning("World node graph view skipped: UI view host is not registered.");
            return;
        }

        viewHost.Mount(_graphView.ViewId);
        _graphViewMounted = true;
        _log.LogInformation("World node graph view requested: {ViewId}", _graphView.ViewId);
    }

    private PlanetGenerationGraphSource BuildInitialGraphSource(PlanetPresentationDocument document)
    {
        var family = document.GenerationGraphFamily
            ?? PlanetGenerationGraphSource.BuildFallbackFamily(document);
        return new PlanetGenerationGraphSource("world-generation", family, _timeline.Tick);
    }

    private void SubscribeGenerationChanged(WorldService world)
    {
        var hash = RuntimeHelpers.GetHashCode(world);
        if (_subscribedWorldHash == hash)
            return;

        _subscribedWorldHash = hash;
        _generationSubscription?.Dispose();
        _generationSubscription = world.SubscribeGenerationChanged(_ =>
        {
            if (_disposed)
                return;
            // Once a document is bound, a completed generation must refresh AT THE PLAYHEAD.
            // Rebind's parameterless fetch defaults to PlateOnsetTick, so routing this event
            // through Rebind rebuilt the surface with onset terrain after every crust-trigger
            // completion — overwriting the playhead's terrain and (because Rebind also resets
            // snapshot tracking) leaving the crossing detector unable to recover: the
            // 105M-vs-119M identical-terrain bug. Rebind stays for the initial mount only.
            // D8b: while a scrub owns the pipeline, generation completions are the scrub's OWN
            // low-rung fetches — chasing them with a no-override (full-frequency) refresh both
            // wastes a full generation mid-drag and overwrites the pending rung stamp. The rest
            // climb re-binds at full when the hand rests.
            if (_currentDocument is not null)
            {
                if (!_scrubRefresh.IsScrubActive)
                    Callable.From(() => ScheduleRegimeRefresh()).CallDeferred();
            }
            else
                Callable.From(Rebind).CallDeferred();
        });
    }

    private void BindDocument(PlanetPresentationDocument document, int expectedGeneration)
    {
        if (_disposed || expectedGeneration != _mountGeneration)
            return;

        var mount = _sceneRegistry.GetNodeOrNull(StageBundleId, PlanetLayerMountPath) as Node3D;
        if (mount is null || !GodotObject.IsInstanceValid(mount) || !mount.IsInsideTree())
        {
            _log.LogWarning(
                "Planet presentation skipped: stage mount not found at {Path}.",
                PlanetLayerMountPath);
            return;
        }

        ClearActiveRoot();

        var root = new Node3D { Name = "PlanetPresentation" };
        root.SetMeta("planetId", document.PlanetId);
        root.SetMeta("sourceWorldId", document.SourceWorldId);
        root.SetMeta("revision", document.Revision);
        root.SetMeta("referenceTick", document.ReferenceTick);
        root.SetMeta("globeReferenceTick", document.GlobeReferenceTick);
        root.SetMeta("crustVolumeDigest", document.CrustVolume?.Digest ?? "none");
        mount.AddChild(root);
        _activeRoot = root;

        _boundRegimeId = _timeline.GeosphereSchedule.RegimeAt(_timeline.Tick)?.RegimeId;
        _currentComposition = GlobeViewModeResolver.ResolveComposition(
            _boundRegimeId, _timeline.ActiveLayers, _plateViewOverride);
        _currentViewMode = _currentComposition.DerivedViewMode;
        _currentSurfaceViewMode = _currentComposition.SurfaceColoring.ToSurfaceViewMode();
        AddLightingAndCamera(root, _currentViewMode);

        var body = new Node3D
        {
            Name = "PlanetBody",
            Position = PlanetBodyPreviewOffset,
        };
        root.AddChild(body);

        _mantle = BuildMantle(document);
        body.AddChild(_mantle);

        // Built hidden; ApplyTimelineTick drives visibility/intensity from AtmosphereRimStateMapper.
        _atmosphereRim = BuildAtmosphereRim();
        body.AddChild(_atmosphereRim);

        _currentDocument = document;
        _boundSurfaceStamp = PlanetSurfaceBindStamp.From(
            document, _boundRegimeId, _timeline.ActiveLayers, _plateViewOverride);

        if (document.GlobeSnapshot is not null)
        {
            _plateSurfaceRoot = BuildPlateSurface(document, _currentSurfaceViewMode);
            body.AddChild(_plateSurfaceRoot);

            _boundaryRenderer = new PlateBoundaryFocusRenderer(
                document.BoundaryArcs ?? Array.Empty<PlateBoundaryArc>());
            body.AddChild(_boundaryRenderer);

            if (document.BoundarySections is { Count: > 0 } sections)
            {
                var placement = BoundarySectionPlacement.Default;
                _boundarySectionRenderer = new BoundarySectionRenderer(sections)
                {
                    Position = placement.Position,
                    RotationDegrees = placement.RotationDegrees,
                    Scale = placement.Scale,
                };
                body.AddChild(_boundarySectionRenderer);
            }
        }

        body.AddChild(BuildProductLayerRoot(document));
        _statusLabel = BuildStatusLabel(document);
        body.AddChild(_statusLabel);
        _boundCrustSnapshotTicks = document.CrustSnapshotTicks.Select(state => state.Tick).ToArray();
        _boundCrustSnapshotTick = new CrustSnapshotTickSeries(_boundCrustSnapshotTicks)
            .SelectSnapshotForPlayhead(_timeline.Tick);
        // W3a: a rebind replaces the whole PlanetBody, taking any mounted cut faces with it — an
        // active wedge must survive regime/snapshot refreshes, so re-apply uniforms + faces here.
        UpdateCutawayPlateShader();
        RebuildCutawayFaces();
        // The mantle-interior layer re-mounts via ApplyTimelineTick's composition reconcile (no separate x-ray rebind).
        ApplyTimelineTick(_timeline.Tick);

        _log.LogInformation(
            "Planet presentation mounted under stage Environment: planet={PlanetId}, plates={PlateCount}, cells={CellCount}, productLayers={LayerCount}, revision={Revision}, crustVolumeDigest={CrustVolumeDigest}.",
            document.PlanetId,
            document.GlobeSnapshot?.PlateCount ?? 0,
            document.GlobeSnapshot?.CellCount ?? 0,
            document.Layers.Count,
            document.Revision,
            document.CrustVolume?.Digest ?? "none");
        _worldBundleReload.MarkMounted();
    }

    private void ApplyTimelineTick(long tick)
        => ApplyTimelineTick(tick, TimelineTickOrigin.Standard);

    private void ApplyTimelineTick(long tick, TimelineTickOrigin origin)
    {
        var regime = _timeline.GeosphereSchedule.RegimeAt(tick);
        var regimeId = regime?.RegimeId;
        var showsPlateFeatures = regime?.ShowsPlateFeatures ?? true;
        var heavyRefreshRequested = false;

        if (_boundRegimeId is not null
            && !string.Equals(_boundRegimeId, regimeId, StringComparison.Ordinal))
        {
            // Regime-gated content (boundary arcs) differs across this boundary: re-fetch the
            // presentation document so arcs built at the current tick reach the renderer. The id
            // is stamped synchronously so rapid ticks cannot stack refreshes; the pending flag
            // bounds the deferred work to one in-flight rebind.
            var previousRegimeId = _boundRegimeId;
            _boundRegimeId = regimeId;
            _log.LogInformation(
                "Planet regime transition {Previous} -> {Current} at t={Tick}: refreshing presentation.",
                previousRegimeId, regimeId ?? "<none>", tick);
            heavyRefreshRequested = true;
        }
        else if (_boundCrustSnapshotTicks.Count > 0)
        {
            // Same regime, but the playhead may have crossed a crust-snapshot boundary: the
            // presented terrain (elevation + tint) belongs to the snapshot at <= playhead, so
            // re-fetch when the selected snapshot changes. Scrubbing within one snapshot
            // interval stays free of re-fetches.
            var selectedSnapshot = new CrustSnapshotTickSeries(_boundCrustSnapshotTicks)
                .SelectSnapshotForPlayhead(tick);
            if (selectedSnapshot != _boundCrustSnapshotTick)
            {
                var previousSnapshot = _boundCrustSnapshotTick;
                _boundCrustSnapshotTick = selectedSnapshot;
                _log.LogInformation(
                    "Crust snapshot transition {Previous} -> {Current} at t={Tick}: refreshing presentation.",
                    previousSnapshot?.ToString("N0") ?? "<none>", selectedSnapshot?.ToString("N0") ?? "<none>", tick);
                heavyRefreshRequested = true;
            }
        }

        // D8b: a refresh apply's own echo (RefreshPresentationForRegime -> UpdateFrom -> PushTick
        // re-applies the SAME playhead as a Standard tick) is not a user tick — feeding it back
        // into scrub policy cancels the very rest/climb that requested the refresh, stranding the
        // planet at a low rung. Suppress the echo unless it genuinely detects new content.
        if (!_applyingRefreshedDocument || heavyRefreshRequested)
            _scrubRefresh.HandleTick(tick, origin, heavyRefreshRequested);

        var previousDecision = _currentComposition;
        var decision = GlobeViewModeResolver.ResolveComposition(regimeId, _timeline.ActiveLayers, _plateViewOverride);
        _currentComposition = decision;
        var viewMode = decision.DerivedViewMode;
        _currentViewMode = viewMode;
        ApplySurfaceAppearance(decision.SurfaceColoring.ToSurfaceViewMode());
        // M0/D8: when the active surface owner is Continents, the membership map IS the content —
        // refresh the globe snapshot at every playhead move through the light path (no crust
        // materialization). Keying off SurfaceColoring keeps stacked layer views (e.g.
        // Mantle+Plate slabs) moving even when the derived view mode is MantleInterior.
        if (decision.SurfaceColoring == SurfaceColoringKind.Continents
            && origin != TimelineTickOrigin.ScrubPreview)
            RefreshContinentsMembership(tick);
        bool showBoundaries = viewMode == GlobeViewMode.PlateIdentity;
        ApplyLightingForView(viewMode);

        // D1/D5: reconcile the mantle-interior LAYER view. The composed root is built/freed on
        // transition into/out of the active mantle membership (the field sampling is too heavy for
        // every tick). Driven by the resolved composition decision, not a single selected layer.
        // The slab tops follow the surface-coloring owner, so a coloring change while mantle stays
        // active (e.g. Mantle+Crust -> Mantle+Crust+Plate) must also rebuild the slabs.
        bool mantleLayerActive = decision.MountMantleInterior;
        bool surfaceColoringChanged = decision.SurfaceColoring != previousDecision.SurfaceColoring;
        if (mantleLayerActive != _mantleLayerActive || (mantleLayerActive && surfaceColoringChanged))
        {
            _mantleLayerActive = mantleLayerActive;
            RebuildMantleLayer();
        }

        // Crust-volume Slice B: the normal World view is the adaptive OUTER ENVELOPE. The radial
        // slab assembly is retained only as extraction scaffolding for the later cutaway/exploded
        // migration; it is no longer an assembled-world owner and buried underlap stays hidden.
        if (_worldSlabAssemblyActive)
        {
            _worldSlabAssemblyActive = false;
            RebuildWorldSlabAssembly();
        }

        // D1: the mantle-interior LAYER view hides the regular terrain surface (the separated slabs
        // are the reference frame); the boundary wireframe stays visible as the locator. The World
        // slab assembly likewise replaces the single-surface sphere while it is mounted.
        if (_plateSurfaceRoot is not null && GodotObject.IsInstanceValid(_plateSurfaceRoot))
            _plateSurfaceRoot.Visible = showsPlateFeatures && !_mantleLayerActive && !_worldSlabAssemblyActive && !_explodedActive;

        bool mantleLocatorActive = _mantleLayerActive;
        if (_boundaryRenderer is not null && GodotObject.IsInstanceValid(_boundaryRenderer))
        {
            _boundaryRenderer.Visible = showBoundaries || mantleLocatorActive;
            // D1: restyle the boundary arcs (thin desaturated filaments) whenever the mantle layer is
            // active. Idempotent, and the rebind path reconstructs this renderer fresh so the style
            // re-applies here on the first ApplyTimelineTick after mount.
            _boundaryRenderer.ApplyMantleViewStyle(mantleLocatorActive);
        }

        if (_boundarySectionRenderer is not null && GodotObject.IsInstanceValid(_boundarySectionRenderer))
            _boundarySectionRenderer.Visible = BoundarySectionVisibility.ShouldShow(showsPlateFeatures, viewMode);

        if (_mantle is not null && GodotObject.IsInstanceValid(_mantle))
        {
            _mantle.MaterialOverride = ResolveMantleMaterial(RegimeSurfaceResolver.Resolve(regimeId));
            // D1: the opaque interior mantle sphere would occlude the layer view's isosurfaces; the
            // layer mounts its own dark core sphere at the CMB radius instead. Exploded crust likewise
            // owns its interior context; keeping the ordinary mantle sphere would falsely fill the
            // separated plate volumes and hide their bottom/side geometry.
            _mantle.Visible = !mantleLocatorActive && !_explodedActive && MantleSurfaceGate.IsVisible(
                viewMode,
                platesShown: showsPlateFeatures,
                hasPlateSurface: _plateSurfaceRoot is not null && GodotObject.IsInstanceValid(_plateSurfaceRoot));
        }

        // W3a: the cutaway is a WORLD-view interaction only — diagnostic views are never clipped
        // (they share the plate material, so the wedge must gate on the resolved view mode here,
        // not just on wedge width). The cut faces follow the same gate.
        bool cutawayVisible = !_cutawayWedge.IsInactive && viewMode == GlobeViewMode.World;
        _hypsoPlateMaterialOverride?.SetShaderParameter("u_wedge_active", cutawayVisible);
        if (_cutawayFaceRoot is not null && GodotObject.IsInstanceValid(_cutawayFaceRoot))
            _cutawayFaceRoot.Visible = cutawayVisible;

        UpdateAtmosphereRim(tick);

        if (_statusLabel is not null && GodotObject.IsInstanceValid(_statusLabel))
        {
            var label = $"{regimeId ?? "world"} : t={tick:N0}";
            if (VerticalScaleLabel.ShouldShowIndicator(viewMode) && _currentDocument is not null)
            {
                // World view renders through the sqrt height lens; the indicator must name it (S2).
                label += BuildVerticalScaleIndicator(_currentDocument, viewMode);
            }
            // W3a: cutaway stratum exaggeration is a separate declared parameter (S1) — name it
            // alongside the surface lens so the two exaggerations are visually distinct (S2).
            if (!_cutawayWedge.IsInactive && _currentDocument is not null)
            {
                label += CutawayStratumProfile.FormatExaggerationIndicator(_currentDocument.CutawayExaggeration);
            }
            _statusLabel.Text = label;
        }
    }

    private void OnLayerSelectionChanged(TimelineLayerSelection? selection)
    {
        if (_disposed)
            return;

        // D5: the active set changed. ApplyTimelineTick recomputes the composition decision from
        // _timeline.ActiveLayers (regime may have changed too), rebuilds the plate surface for the
        // new surface-coloring owner, and reconciles the mantle-interior mount — the full path.
        ApplyTimelineTick(_timeline.Tick);
    }

    private void ScheduleRegimeRefresh(int? frequencyOverride = null)
    {
        // D8b last-writer-wins: a later full request (null) overwrites a pending low one; a later
        // low request overwrites a pending full. One deferred refresh either way via _regimeRefreshPending.
        _pendingFrequencyOverride = frequencyOverride;
        if (_regimeRefreshPending)
            return;
        _regimeRefreshPending = true;
        Callable.From(() => RefreshPresentationForRegime()).CallDeferred();
    }

    private void RefreshPresentationForRegime()
    {
        if (_disposed)
        {
            _regimeRefreshPending = false;
            _pendingFrequencyOverride = null;
            return;
        }

        var world = _registry.TryGet<WorldService>();
        if (world is null)
        {
            _regimeRefreshPending = false;
            _pendingFrequencyOverride = null;
            _log.LogWarning("Planet presentation regime refresh skipped: world service is not registered.");
            return;
        }

        // Capture the LATEST requested rung (last writer wins) and clear the stamp before the
        // potentially slow fetch so a concurrent ScheduleRegimeRefresh can stamp the next one.
        var frequencyOverride = _pendingFrequencyOverride;
        _pendingFrequencyOverride = null;

        PlanetPresentationDocument document;
        try
        {
            document = frequencyOverride is { } freq
                ? world.GetPlanetPresentationAsync(_timeline.Tick, freq)
                : world.GetPlanetPresentationAsync(_timeline.Tick);
        }
        catch (Exception ex)
        {
            _regimeRefreshPending = false;
            _log.LogError(ex, "Planet presentation document failed during regime refresh at tick {Tick}: {Message}", _timeline.Tick, ex.Message);
            return;
        }

        // Mark the apply so its echo tick (UpdateFrom -> PushTick at the same playhead) bypasses
        // scrub policy — see the ApplyTimelineTick gate.
        _applyingRefreshedDocument = true;
        try
        {
            _timeline.UpdateFrom(document);
            EnsureNodeGraphView(document);
            // G34 double-full-bind dedupe: a generation-completion chase re-fetches at the same
            // playhead and used to re-bind an identical surface. Timeline metadata (UpdateFrom,
            // snapshot-lane states) is applied above either way; the expensive mesh re-bind is
            // skipped only when the surface content stamp is provably unchanged, so new-content
            // completions (the 105M identical-terrain class) still bind.
            var regimeId = _timeline.GeosphereSchedule.RegimeAt(_timeline.Tick)?.RegimeId;
            var candidate = PlanetSurfaceBindStamp.From(
                document, regimeId, _timeline.ActiveLayers, _plateViewOverride);
            if (candidate == _boundSurfaceStamp
                && _activeRoot is not null
                && GodotObject.IsInstanceValid(_activeRoot))
            {
                _log.LogInformation(
                    "Planet surface re-bind skipped at t={Tick}: content stamp unchanged (generation-completion echo).",
                    _timeline.Tick);
            }
            else
            {
                BindDocument(document, _mountGeneration);
            }
        }
        finally
        {
            _applyingRefreshedDocument = false;
        }
        _regimeRefreshPending = false;
     }

        // Computes per-cell color (world or crust ramp with trench/ridge accent baked in) and
        // per-cell volcanic emission intensity from the caller's selected elevation + feature owner. The
    // world view uses WorldTerrainRamp (bare-rock product palette) modulated by the continental
    // ProvinceTint (cells indexed by CellId supply the sample direction); the crust diagnostic uses
    // HypsometricTint, un-tinted. Falls back to a neutral mid-ramp tint when crust data is absent.
    // Task 2 deviation (2026-07-11 split): stays here rather than moving to PlateSurfaceMeshFactory
    // because App.Architecture.Tests.Gates.ContinentProxyBanTests.ProvinceTint_UsageIsConfinedToWhitelist
    // hard-codes an exact-path allowlist for the ProvinceTint call site below, and that test file is
    // outside this refactor's edit scope. See AGENT-SUMMARY.md.
    private static (RampColor[] Colors, float[] Emission) BuildCellAppearance(
        int cellCount,
        IReadOnlyList<double>? elevations,
        IReadOnlyList<CellCrustFeature>? features,
        GlobeViewMode viewMode,
        IReadOnlyList<GlobeCell>? cells)
    {
        var colors = new RampColor[cellCount];
        var emission = new float[cellCount];
        bool isWorld = viewMode == GlobeViewMode.World;
        bool showsVolcanicGlow = PlateSurfaceEmissionPolicy.ShowsVolcanicGlow(viewMode);

        if (elevations is null || elevations.Count != cellCount)
        {
            var fallbackRamp = isWorld
                ? WorldTerrainRamp.ComputeColors(new double[] { 0.0 })[0]
                : HypsometricTint.ComputeColors(new double[] { 0.0 })[0];
            for (int c = 0; c < cellCount; c++) colors[c] = fallbackRamp;
            return (colors, emission);
        }

        // World view only: continental-scale albedo provinces, applied to the ramp color BEFORE the
        // typed accents so trench/ridge/volcanic signals stay legible on top of the province field.
        var provinceTint = isWorld && cells is not null
            ? new ProvinceTint(seed: 1337, amplitude: 0.12)
            : null;
        var cellCenters = provinceTint is not null ? PlateSurfaceMeshFactory.BuildCellCenters(cellCount, cells!) : null;

        var rampColors = isWorld
            ? WorldTerrainRamp.ComputeColors(elevations)
            : HypsometricTint.ComputeColors(elevations);
        for (int c = 0; c < cellCount; c++)
        {
            var tint = rampColors[c];
            if (provinceTint is not null && cellCenters![c] is { } center)
                tint = provinceTint.Apply(center, tint);
            byte kind = 0;
            double magnitude = 0.0;
            if (features is not null && c < features.Count)
            {
                kind = features[c].Kind;
                magnitude = features[c].Magnitude;
            }
            var accent = CrustAccentMapper.Map(kind, magnitude);
            colors[c] = CrustAccentMapper.Apply(tint, accent);
            emission[c] = showsVolcanicGlow ? (float)accent.VolcanicEmission : 0f;
        }
        return (colors, emission);
    }

    // ResolvePlanetRadiusMetres is shared across the CutawayExploded and PlateSurface clusters
    // (BuildCutawayFaces + BuildVerticalScaleIndicator's projection path both need it) — it stays
    // in the core file rather than moving with either partial (2026-07-11 split plan, Task 6 note).
    private static double ResolvePlanetRadiusMetres(PlanetPresentationDocument? document)
    {
        var profile = document?.LayerProjectionProfiles.FirstOrDefault(p =>
            string.Equals(p.LayerId, PlanetLayerProjectionProfile.CrustLayerId, StringComparison.Ordinal)
            && p.ProjectionKind == PlanetLayerProjectionKind.GlobeSurface);
        return profile is { PlanetRadiusMetres: > 0.0 }
            ? profile.PlanetRadiusMetres
            : PlanetLayerProjectionProfile.EarthLikePlanetRadiusMetres;
    }

    private static Vector3 ToV3(GlobeVec3 value)
        => new(value.X, value.Y, value.Z);

    private static bool TryNormalize(Vector3 value, out Vector3 normalized)
    {
        if (value.LengthSquared() < 0.000001f)
        {
            normalized = Vector3.Zero;
            return false;
        }

        normalized = value.Normalized();
        return true;
    }

    private static string SafeNodeName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray();
        var name = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(name) ? "Layer" : name;
    }

    private void OnResourceRuntimeChanging(object? sender, ResourceRuntimeChangingEventArgs args)
    {
        var worldChanging = string.Equals(args.BundleId, WorldBundleId, StringComparison.OrdinalIgnoreCase);
        var stageChanging = string.Equals(args.BundleId, StageBundleId, StringComparison.OrdinalIgnoreCase);
        if (!worldChanging && !stageChanging)
            return;

        var expectedGeneration = ++_mountGeneration;
        if (worldChanging)
        {
            _subscribedWorldHash = null;
            _generationSubscription?.Dispose();
            _generationSubscription = null;
        }
        _worldBundleReload.MarkRuntimeChanging();
        ResetRegimeTracking();
        Callable.From(() =>
        {
            if (_disposed || expectedGeneration != _mountGeneration)
                return;
            ClearActiveRoot();
            if (worldChanging)
                ReleaseNodeGraphView();
        }).CallDeferred();
        _log.LogInformation("Planet presentation released before resource {Operation}: {BundleId}", args.Operation, args.BundleId);
    }

    private void OnResourceRuntimeChanged(object? sender, EventArgs args)
    {
        if (_disposed || !_worldBundleReload.TryScheduleDeferredAttempt())
            return;

        Callable.From(TryRebindAfterWorldBundleChange).CallDeferred();
    }

    private void TryRebindAfterWorldBundleChange()
    {
        var runtimeChangeInProgress = _resource.IsRuntimeChangeInProgress(WorldBundleId)
            || _resource.IsRuntimeChangeInProgress(StageBundleId);
        if (!_worldBundleReload.CompleteDeferredAttempt(runtimeChangeInProgress)
            || _disposed
            || !_worldBundleReload.IsPending
            || !_resource.IsLoaded(WorldBundleId)
            || !_resource.IsLoaded(StageBundleId))
            return;
        var mount = _sceneRegistry.GetNodeOrNull(StageBundleId, PlanetLayerMountPath) as Node3D;
        if (mount is null || !GodotObject.IsInstanceValid(mount) || !mount.IsInsideTree())
            return;

        Rebind();
    }

    private void ClearActiveRoot()
    {
        ReleasePlateSurfaceRenderer();

        if (_activeRoot is not null && GodotObject.IsInstanceValid(_activeRoot))
        {
            _activeRoot.GetParent()?.RemoveChild(_activeRoot);
            _activeRoot.QueueFree();
        }

        _activeRoot = null;
        _boundSurfaceStamp = null;
        _plateSurfaceRoot = null;
        _plateSurfaces = null;
        if (_boundaryRenderer is not null && GodotObject.IsInstanceValid(_boundaryRenderer))
        {
            _boundaryRenderer.GetParent()?.RemoveChild(_boundaryRenderer);
            _boundaryRenderer.QueueFree();
        }
        _boundaryRenderer = null;
        _mantle = null;
        _atmosphereRim = null;
        _atmosphereRimMaterial = null;
        _sunLight = null;
        _planetEnvironment = null;
        _statusLabel = null;
        _currentDocument = null;
        _currentViewMode = GlobeViewMode.Inactive;
        _currentSurfaceViewMode = GlobeViewMode.Inactive;

        if (_cutawayFaceRoot is not null && GodotObject.IsInstanceValid(_cutawayFaceRoot))
        {
            _cutawayFaceRoot.GetParent()?.RemoveChild(_cutawayFaceRoot);
            _cutawayFaceRoot.QueueFree();
        }
        _cutawayFaceRoot = null;

        if (_mantleLayerRoot is not null && GodotObject.IsInstanceValid(_mantleLayerRoot))
        {
            _mantleLayerRoot.GetParent()?.RemoveChild(_mantleLayerRoot);
            _mantleLayerRoot.QueueFree();
        }
        _mantleLayerRoot = null;
        _mantleLayerActive = false;

        if (_worldSlabAssemblyRoot is not null && GodotObject.IsInstanceValid(_worldSlabAssemblyRoot))
        {
            _worldSlabAssemblyRoot.GetParent()?.RemoveChild(_worldSlabAssemblyRoot);
            _worldSlabAssemblyRoot.QueueFree();
        }
        _worldSlabAssemblyRoot = null;
        _worldSlabAssemblyActive = false;
    }

    private void ReleasePlateSurfaceRenderer()
    {
        if (_plateSurfaceRoot is PlateSurfaceRenderer renderer && GodotObject.IsInstanceValid(renderer))
            renderer.ReleaseRenderingResources();
    }

    private void ReleaseNodeGraphView()
    {
        if (_graphViewMounted && _graphView is not null)
        {
            var viewHost = _registry.TryGet<IViewHost>();
            if (viewHost is not null)
            {
                viewHost.UnmountNow(_graphView.ViewId);
                viewHost.Unmount(_graphView.ViewId);
            }
        }

        _graphViewMounted = false;
        _graphBinding?.Dispose();
        _graphBinding = null;
        _graphViewRegistration?.Dispose();
        _graphViewRegistration = null;
        _graphView?.Dispose();
        _graphView = null;
        _graphSource = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _mountGeneration++;
        _timeline.LayerSelectionChanged -= OnLayerSelectionChanged;
        _resource.RuntimeChanging -= OnResourceRuntimeChanging;
        _resource.RuntimeChanged -= OnResourceRuntimeChanged;
        _generationSubscription?.Dispose();
        _generationSubscription = null;
        _scrubRefresh.Dispose();
        ReleaseNodeGraphView();
        _timelineRegistration.Dispose();
        ClearActiveRoot();
    }
}
