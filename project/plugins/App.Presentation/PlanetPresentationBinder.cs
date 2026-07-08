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

internal sealed class PlanetPresentationBinder : IPlanetPresentation
{
    private const string StageBundleId = "stage";
    private const string WorldBundleId = "world";
    private static readonly NodePath PlanetLayerMountPath = new("Environment/PlanetMount/Planet/LayerMounts");
    private static readonly Vector3 PlanetBodyPreviewOffset = new(0.8f, 0.0f, 0.0f);

    // World-view height lens (look-dev 2026-07-03, knobbly-limb references): sign(h)*|h|^0.5 * scale.
    // The elevation field is ~±500..1,400 m interiors under 21,000+ m orogenic extremes — a ratio no
    // LINEAR lens can render (interiors invisible or peaks become spears). The sqrt profile compresses
    // ~48:1 to ~7:1: interiors ~1.9% of radius (knobbly limb), peaks ~7.5% (proportionate). The S2
    // indicator names the profile (VerticalScaleLabel profile overload) — the lens is labeled, never
    // hidden. The crust DIAGNOSTIC view stays strictly linear on document.VerticalExaggeration:
    // diagnostics must not bend the scale. PROBE pending user sign-off — S1 doctrine amendment
    // (non-linear labeled lens) is a design decision, not a default.
    private const double WorldHeightExponent = 0.5;
    private const double WorldHeightScale = 0.0005;

    private readonly IRegistry _registry;
    private readonly ResourceService _resource;
    private readonly IBundleSceneRegistry _sceneRegistry;
    private readonly ILogger _log;
    private readonly PlanetTimelineController _timeline;
    private readonly IDisposable _timelineRegistration;
    private IDisposable? _generationSubscription;
    private Node3D? _activeRoot;
    private Node3D? _plateSurfaceRoot;
    private PlateBoundaryFocusRenderer? _boundaryRenderer;
    private BoundarySectionRenderer? _boundarySectionRenderer;
    private MeshInstance3D? _mantle;
    private MeshInstance3D? _atmosphereRim;
    private ShaderMaterial? _atmosphereRimMaterial;
    private DirectionalLight3D? _sunLight;
    private WorldEnvironment? _planetEnvironment;
    private Label3D? _statusLabel;
    private GlobePlateSurfaces? _plateSurfaces;
    private PlanetGenerationGraphSource? _graphSource;
    private NodeGraphViewSource? _graphView;
    private PlanetGenerationGraphSource.PlanetGenerationTimelineGraphBinding? _graphBinding;
    private IDisposable? _graphViewRegistration;
    private bool _graphViewMounted;
    private readonly bool _showWorldGraph;
    private int? _subscribedWorldHash;
    private readonly PlanetPresentationReloadGate _worldRuntimeReload = new();
    private string? _boundRegimeId;
    private bool _regimeRefreshPending;
    private readonly ScrubApplyScheduler _scrubApplyScheduler = new(restDelayMs: 300L);
    private CancellationTokenSource? _scrubRefreshDelay;
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

    // W3a cutaway wedge state (inactive by default; width 0 = zero render change).
    private CutawayWedge _cutawayWedge = new(new UnifyMaths.Vector3D(0, 0, 1), 0, 0);
    private double _cutawayAzimuthDeg;
    private double _cutawayWidthDeg;
    private Node3D? _cutawayFaceRoot;
    private ShaderMaterial? _hypsoPlateMaterialOverride;

    // M-A mantle x-ray state (inactive by default). _mantleXrayRoot holds the dark core sphere plus
    // the four isosurface MeshInstance3Ds (cold/warm x outer/inner) sampled from the engine's
    // volumetric MantleAnomalyField at the current tick.
    private Node3D? _mantleXrayRoot;
    private bool _mantleXrayActive;

    // D1 mantle-interior LAYER view state (inactive by default). _mantleLayerRoot holds the composed
    // tree from MantleInteriorViewComposer: core sphere + four isosurfaces + separated crust slabs
    // (NO ghost shell — the slabs are the reference frame). Driven by viewMode == MantleInterior,
    // reconciled in ApplyTimelineTick; entering/leaving the layer builds/frees the root.
    private Node3D? _mantleLayerRoot;
    private bool _mantleLayerActive;

    private bool _disposed;
    private readonly string? _plateViewOverride;
    private long? _boundContinentsTick; // last tick whose membership the Continents caps show

    // M-B exploded solid-crust state (inactive until render.exploded is invoked at least once).
    private bool _explodedActive;
    private double _explodedFactor;
    private Node3D? _explodedCrustRoot;

    // D3 radial section profile: the single declared source of truth for crust thickness and mantle
    // depth scaling. Defaults cover the canonical Earth-like planet; a future look-dev knob or a
    // document-carried profile can override. Thickness exaggeration here feeds PlateSolidBuilder;
    // the core-sphere radius (CMB × mantle scale) feeds the mantle interior backdrop.
    private RadialSectionProfile _radialProfile = RadialSectionProfile.Default;

    // M-B cached build inputs captured at plate-surface bind time so UpdateExploded can rebuild
    // byte-identical TOP DTOs (same Continents/terrain colors) without recomputing the heavy surface
    // path. The solid THICKNESS exaggeration is decoupled from the surface relief exaggeration (D3):
    // it comes from _radialProfile.CrustThicknessExaggeration, not _lastExaggeration.
    private IReadOnlyList<PlateCap>? _lastCaps;
    private double _lastExaggeration;
    private IReadOnlyList<PlateSolidCentroid>? _lastCentroids;
    private GlobeViewMode _lastViewMode;
    private bool _lastIsTerrain;
    private IReadOnlyDictionary<int, RampColor[]>? _lastPerPlateVertexColors;
    private IReadOnlyList<RampColor>? _lastPerCellColor;
    private IReadOnlyList<float>? _lastPerCellEmission;
    private VertexTintJitter? _lastJitter;
    private PlateCapMeshColorMode _lastColorMode;
    private PlateCapMeshNormalMode _lastNormalMode;
    private IReadOnlyList<RampColor>? _lastContinentsCellColors;
    private byte[]? _lastContinentsFrontier;

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
        Callable.From(() => BindDocument(document)).CallDeferred();
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
            if (_currentDocument is not null)
                Callable.From(ScheduleRegimeRefresh).CallDeferred();
            else
                Callable.From(Rebind).CallDeferred();
        });
    }

    private void BindDocument(PlanetPresentationDocument document)
    {
        if (_disposed)
            return;

        var mount = _sceneRegistry.GetNodeOrNull(StageBundleId, PlanetLayerMountPath) as Node3D;
        if (mount is null)
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
        // M-A: ditto for the mantle x-ray isosurfaces — re-sample at the playhead tick after rebind.
        RebuildMantleXray();
        ApplyTimelineTick(_timeline.Tick);

        _log.LogInformation(
            "Planet presentation mounted under stage Environment: planet={PlanetId}, plates={PlateCount}, cells={CellCount}, productLayers={LayerCount}, revision={Revision}.",
            document.PlanetId,
            document.GlobeSnapshot?.PlateCount ?? 0,
            document.GlobeSnapshot?.CellCount ?? 0,
            document.Layers.Count,
            document.Revision);
        _worldRuntimeReload.MarkMounted();
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

        HandleScrubAwareHeavyRefresh(tick, origin, heavyRefreshRequested);

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
        if (decision.SurfaceColoring == SurfaceColoringKind.Continents)
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

        // M-A look-loop: GeometryInstance3D.Transparency proved unreliable against the custom
        // hypso/continents shaders in the exported app (surface stayed opaque — windowed gate
        // 2026-07-07). Do what the reference imagery does instead: HIDE the terrain surface in
        // mantle view; the x-ray root carries its own translucent ghost shell + the boundary
        // wireframe stays visible as the locator. D1: the mantle-interior LAYER view likewise
        // hides the regular surface (the separated slabs are the reference frame instead).
        if (_plateSurfaceRoot is not null && GodotObject.IsInstanceValid(_plateSurfaceRoot))
            _plateSurfaceRoot.Visible = showsPlateFeatures && !_mantleXrayActive && !_mantleLayerActive;

        bool mantleLocatorActive = _mantleXrayActive || _mantleLayerActive;
        if (_boundaryRenderer is not null && GodotObject.IsInstanceValid(_boundaryRenderer))
        {
            _boundaryRenderer.Visible = showBoundaries || mantleLocatorActive;
            // M-A / D1: restyle the boundary arcs (thin desaturated filaments) whenever a mantle
            // view is active. Idempotent, and the rebind path reconstructs this renderer fresh so
            // the style re-applies here on the first ApplyTimelineTick after mount.
            _boundaryRenderer.ApplyMantleViewStyle(mantleLocatorActive);
        }

        if (_boundarySectionRenderer is not null && GodotObject.IsInstanceValid(_boundarySectionRenderer))
            _boundarySectionRenderer.Visible = BoundarySectionVisibility.ShouldShow(showsPlateFeatures, viewMode);

        if (_mantle is not null && GodotObject.IsInstanceValid(_mantle))
        {
            _mantle.MaterialOverride = ResolveMantleMaterial(RegimeSurfaceResolver.Resolve(regimeId));
            // M-A: the opaque interior mantle sphere would occlude the x-ray isosurfaces; the x-ray
            // mounts its own dark core sphere at the CMB radius instead. D1: same for the layer view.
            _mantle.Visible = !mantleLocatorActive && MantleSurfaceGate.IsVisible(
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

    private static string BuildVerticalScaleIndicator(PlanetPresentationDocument document, GlobeViewMode viewMode)
    {
        if (viewMode == GlobeViewMode.World)
            return VerticalScaleLabel.BuildIndicatorSuffix(WorldHeightScale, WorldHeightExponent);

        var projection = LayerProjectionProfileResolver.ResolveForView(
            document,
            viewMode,
            worldMetresToUnitRadius: WorldHeightScale,
            worldHeightExponent: WorldHeightExponent);
        return VerticalScaleLabel.BuildIndicatorSuffix(
            projection.MetresToUnitRadius,
            projection.HeightExponent,
            projection.TrueScaleMetresToUnitRadius);
    }

    // W3a: entry from render.cutaway. Width 0 = inactive: clears the wedge, disables the shader
    // discard, frees the cut-face root — zero render change vs. today.
    public void UpdateCutaway(double azimuthDeg, double widthDeg)
    {
        if (_disposed)
            return;

        _cutawayAzimuthDeg = azimuthDeg;
        _cutawayWidthDeg = widthDeg;
        _cutawayWedge = new CutawayWedge(new UnifyMaths.Vector3D(0, 0, 1), azimuthDeg, widthDeg);

        UpdateCutawayPlateShader();
        RebuildCutawayFaces();
        ApplyTimelineTick(_timeline.Tick);
    }

    // M-B: entry from render.exploded. Factor 0 = assembled solid crust (solids in place, thickness
    // and side walls visible at the silhouette); factor in (0,1] radially translates each plate along
    // its area-weighted centroid direction. Activation hides the single-surface plate root and shows
    // the per-plate solid slabs instead; there is no deactivate path for M-B (spec: activation only).
    public void UpdateExploded(double factor)
    {
        if (_disposed)
            return;

        _explodedActive = true;
        _explodedFactor = factor;
        RebuildExplodedCrust();
        ApplyTimelineTick(_timeline.Tick);
    }

    private void UpdateCutawayPlateShader()
    {
        var mat = HypsoPlateMaterialOverride;
        mat.SetShaderParameter("u_wedge_active", !_cutawayWedge.IsInactive);
        if (_cutawayWedge.IsInactive)
            return;

        var axis = _cutawayWedge.Axis;
        var reference = _cutawayWedge.Reference;
        var referenceCross = new UnifyMaths.Vector3D(
            axis.Y * reference.Z - axis.Z * reference.Y,
            axis.Z * reference.X - axis.X * reference.Z,
            axis.X * reference.Y - axis.Y * reference.X);

        mat.SetShaderParameter("u_wedge_axis", new Vector3((float)axis.X, (float)axis.Y, (float)axis.Z));
        mat.SetShaderParameter("u_wedge_reference", new Vector3((float)reference.X, (float)reference.Y, (float)reference.Z));
        mat.SetShaderParameter("u_wedge_reference_cross", new Vector3((float)referenceCross.X, (float)referenceCross.Y, (float)referenceCross.Z));
        mat.SetShaderParameter("u_wedge_start_rad", (float)(_cutawayWedge.NormalizedStart * Math.PI / 180.0));
        mat.SetShaderParameter("u_wedge_width_rad", (float)(_cutawayWedge.WidthDeg * Math.PI / 180.0));
    }

    private void RebuildCutawayFaces()
    {
        if (_cutawayFaceRoot is not null && GodotObject.IsInstanceValid(_cutawayFaceRoot))
        {
            _cutawayFaceRoot.GetParent()?.RemoveChild(_cutawayFaceRoot);
            _cutawayFaceRoot.QueueFree();
        }
        _cutawayFaceRoot = null;

        if (_cutawayWedge.IsInactive)
            return;

        if (_activeRoot is null || !GodotObject.IsInstanceValid(_activeRoot))
            return;

        var body = _activeRoot.GetNodeOrNull<Node3D>("PlanetBody");
        if (body is null)
            return;

        _cutawayFaceRoot = BuildCutawayFaces();
        body.AddChild(_cutawayFaceRoot);
    }

    // M-B: free the old exploded crust root, then if active build a new one and parent it under
    // PlanetBody. Mirrors RebuildCutawayFaces. When exploded is active the single-surface plate
    // root is hidden so only the per-plate solid slabs render.
    private void RebuildExplodedCrust()
    {
        if (_explodedCrustRoot is not null && GodotObject.IsInstanceValid(_explodedCrustRoot))
        {
            _explodedCrustRoot.GetParent()?.RemoveChild(_explodedCrustRoot);
            _explodedCrustRoot.QueueFree();
        }
        _explodedCrustRoot = null;

        if (_plateSurfaceRoot is not null && GodotObject.IsInstanceValid(_plateSurfaceRoot))
            _plateSurfaceRoot.Visible = !_explodedActive;

        if (!_explodedActive)
            return;

        if (_activeRoot is null || !GodotObject.IsInstanceValid(_activeRoot))
            return;

        var explodedBody = _activeRoot.GetNodeOrNull<Node3D>("PlanetBody");
        if (explodedBody is null)
            return;

        _explodedCrustRoot = BuildExplodedSolidCrust();
        explodedBody.AddChild(_explodedCrustRoot);
    }

    // M-A: entry from render.mantle. enabled=true samples the conditioned convection field at the
    // playhead tick and mounts cold/warm isosurface meshes; enabled=false clears them and restores the
    // plate surface. The ghosted crust + boundary wireframe visibility is applied through the standard
    // ApplyTimelineTick path (which checks _mantleXrayActive), mirroring how cutaway re-applies.
    public void UpdateMantle(bool enabled)
    {
        if (_disposed)
            return;

        _mantleXrayActive = enabled;
        RebuildMantleXray();
        ApplyTimelineTick(_timeline.Tick);
    }

    // M-B: per-plate SOLID crust. Two MeshInstance3Ds per plate under a single root: the TOP (the
    // attributed cap surface DTO — same Continents/terrain colors as the hidden single-surface —
    // with the plate's explode offset baked into positions) and the BOTTOM+WALLS (the
    // PlateSolidBuilder output, dark unlit material). Both Scale = Vector3.One * 2.0f to match
    // PlateSurfaceRenderer and BuildCutawayFaceSector. No per-plate GPU rotation exists in this
    // path, so a baked position offset is exactly correct.
    private Node3D BuildExplodedSolidCrust(double? factorOverride = null)
    {
        var root = new Node3D { Name = "ExplodedCrust" };

        var document = _currentDocument;
        var snapshot = document?.GlobeSnapshot;
        if (document is null || snapshot is null)
            return root;

        var caps = _lastCaps;
        var centroids = _lastCentroids;
        if (caps is null || centroids is null)
            return root;

        // D1: the mantle-interior layer passes MantleLayerExplodeFactor so the slabs detach at a
        // modest, profile-declared fraction of DefaultMaxOffset. render.exploded passes null so
        // the agent look-dev knob (_explodedFactor) stays in control of that path.
        double factor = factorOverride ?? _explodedFactor;

        var thickness = ResolveCrustThicknessMetres(document);
        // D3: the slab thickness exaggeration is EXPLICIT and distinct from the surface relief
        // exaggeration (_lastExaggeration). The profile exposes the metres-to-unit-radius scale
        // PlateSolidBuilder expects (CrustThicknessExaggeration / PlanetRadiusMetres), so 30 km of
        // crust reads as ~0.038R slab walls — independent of the ~3e-5 surface relief lens.
        var solids = PlateSolidBuilder.Build(caps, thickness, _radialProfile.ThicknessDepthScale());
        var exploded = PlateSolidBuilder.ApplyExplodedFactor(solids, centroids, factor);

        var byPlate = new Dictionary<int, PlateSolidCentroid>(centroids.Count);
        foreach (var c in centroids)
            byPlate[c.PlateId] = c;

        var offsetMag = factor * PlateSolidBuilder.DefaultMaxOffset;

        for (int i = 0; i < caps.Count; i++)
        {
            var cap = caps[i];
            if (!byPlate.TryGetValue(cap.PlateId, out var centroid))
                continue;

            var solid = exploded[i];

            var topDto = BuildExplodedTopDto(cap, centroid, offsetMag);
            root.AddChild(BuildExplodedMeshInstance($"Plate{cap.PlateId}_Top", topDto, HypsoPlateMaterialOverride));

            var solidDto = BuildExplodedSolidDto(cap, solid);
            root.AddChild(BuildExplodedSolidMeshInstance($"Plate{cap.PlateId}_Solid", solidDto, ExplodedCrustDarkMaterial));
        }

        return root;
    }

    // M-B: same PlateCapMeshBuilder.Build* branch the surface uses (cached inputs), with the plate's
    // explode offset baked into the DTO positions (uniform per-plate translation — correct because
    // there is NO per-plate GPU rotation in this path).
    private PlateCapMeshDto BuildExplodedTopDto(PlateCap cap, PlateSolidCentroid centroid, double offsetMag)
    {
        var dx = centroid.CentroidDirection.X * offsetMag;
        var dy = centroid.CentroidDirection.Y * offsetMag;
        var dz = centroid.CentroidDirection.Z * offsetMag;

        PlateCapMeshDto dto = _lastIsTerrain
            ? PlateCapMeshBuilder.BuildTerrain(
                cap,
                _lastPerPlateVertexColors!,
                _lastPerCellEmission!,
                _lastJitter,
                _lastColorMode,
                _lastPerCellColor,
                _lastNormalMode)
            : _lastViewMode == GlobeViewMode.Continents
                ? PlateCapMeshBuilder.BuildContinents(
                    cap,
                    _lastContinentsCellColors!,
                    _lastContinentsFrontier!)
                : PlateCapMeshBuilder.BuildPlateIdentity(cap);

        if (offsetMag == 0.0)
            return dto;

        var positions = dto.Positions;
        for (int v = 0; v < positions.Length; v += 3)
        {
            positions[v + 0] = (float)(positions[v + 0] + dx);
            positions[v + 1] = (float)(positions[v + 1] + dy);
            positions[v + 2] = (float)(positions[v + 2] + dz);
        }
        return dto;
    }

    // M-B: BOTTOM + SIDE WALLS from the exploded solid. The solid's Triangles = [top | bottom | walls]
    // concatenated; top+bottom each have cap.Surface.Triangles.Length indices; walls follow. We deref
    // the bottom+wall triangle range into a non-indexed Vector3 list (unlit dark material needs no
    // normals/UV).
    private static PlateCapMeshDto BuildExplodedSolidDto(PlateCap cap, PlateSolid solid)
    {
        int topIndexCount = cap.Surface.Triangles.Length;
        int bottomWallIndexCount = solid.Triangles.Length - topIndexCount;

        var positions = new float[bottomWallIndexCount * 3];
        int w = 0;
        for (int t = topIndexCount; t < solid.Triangles.Length; t++)
        {
            int idx = solid.Triangles[t];
            var p = solid.Positions[idx];
            positions[w++] = (float)p.X;
            positions[w++] = (float)p.Y;
            positions[w++] = (float)p.Z;
        }

        return new PlateCapMeshDto(
            PlateId: cap.PlateId,
            NormalMode: PlateCapMeshNormalMode.Flat,
            VertexCount: bottomWallIndexCount,
            TriangleCount: bottomWallIndexCount / 3,
            Positions: positions,
            Normals: Array.Empty<float>(),
            Colors: Array.Empty<float>(),
            Uv2: Array.Empty<float>());
    }

    private static MeshInstance3D BuildExplodedMeshInstance(string name, PlateCapMeshDto dto, Material material)
    {
        var vertices = new Vector3[dto.VertexCount];
        var normals = new Vector3[dto.VertexCount];
        var colors = new Color[dto.VertexCount];
        var uv2 = new Vector2[dto.VertexCount];

        for (int i = 0; i < dto.VertexCount; i++)
        {
            int v3 = i * 3;
            vertices[i] = new Vector3(dto.Positions[v3 + 0], dto.Positions[v3 + 1], dto.Positions[v3 + 2]);
            normals[i] = new Vector3(dto.Normals[v3 + 0], dto.Normals[v3 + 1], dto.Normals[v3 + 2]);
            colors[i] = new Color(dto.Colors[v3 + 0], dto.Colors[v3 + 1], dto.Colors[v3 + 2]);
            int uv = i * 2;
            uv2[i] = new Vector2(dto.Uv2[uv + 0], dto.Uv2[uv + 1]);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        return new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            Scale = Vector3.One * 2.0f,
            MaterialOverride = material,
        };
    }

    // M-A: free the old mantle x-ray root; if active, sample the volumetric field at the playhead
    // tick and mount the four isosurface meshes + dark core sphere under PlanetBody.
    private void RebuildMantleXray()
    {
        if (_mantleXrayRoot is not null && GodotObject.IsInstanceValid(_mantleXrayRoot))
        {
            _mantleXrayRoot.GetParent()?.RemoveChild(_mantleXrayRoot);
            _mantleXrayRoot.QueueFree();
        }
        _mantleXrayRoot = null;

        if (!_mantleXrayActive)
            return;

        if (_activeRoot is null || !GodotObject.IsInstanceValid(_activeRoot))
            return;

        var body = _activeRoot.GetNodeOrNull<Node3D>("PlanetBody");
        if (body is null)
            return;

        var world = _registry.TryGet<WorldService>();
        if (world is null)
        {
            _log.LogWarning("Mantle x-ray skipped: world service is not registered.");
            return;
        }

        FantaSim.App.World.MantleIsosurfaceSet set;
        try
        {
            set = world.GetMantleIsosurfacesAsync(_timeline.Tick);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Mantle x-ray sampling failed at t={Tick}: {Message}", _timeline.Tick, ex.Message);
            return;
        }

        _mantleXrayRoot = BuildMantleXrayRoot(set);
        body.AddChild(_mantleXrayRoot);
        _log.LogInformation(
            "Mantle x-ray mounted at t={Tick}: cold outer/inner={ColdOuter}/{ColdInner} verts, warm outer/inner={WarmOuter}/{WarmInner} verts.",
            _timeline.Tick,
            set.ColdOuter.Vertices.Length / 3, set.ColdInner.Vertices.Length / 3,
            set.WarmOuter.Vertices.Length / 3, set.WarmInner.Vertices.Length / 3);
    }

    // D1: free the old mantle-interior layer root; if active, sample the volumetric field at the
    // playhead tick and mount the composed tree (core sphere + four isosurfaces + separated crust
    // slabs, NO ghost shell) via MantleInteriorViewComposer. Mirrors RebuildMantleXray's lifecycle
    // but composes the slabs instead of ghosting the surface. Called on view-mode transition into
    // MantleInterior (reconciled in ApplyTimelineTick).
    private void RebuildMantleLayer()
    {
        if (_mantleLayerRoot is not null && GodotObject.IsInstanceValid(_mantleLayerRoot))
        {
            _mantleLayerRoot.GetParent()?.RemoveChild(_mantleLayerRoot);
            _mantleLayerRoot.QueueFree();
        }
        _mantleLayerRoot = null;

        if (!_mantleLayerActive)
            return;

        if (_activeRoot is null || !GodotObject.IsInstanceValid(_activeRoot))
            return;

        var body = _activeRoot.GetNodeOrNull<Node3D>("PlanetBody");
        if (body is null)
            return;

        var world = _registry.TryGet<WorldService>();
        if (world is null)
        {
            _log.LogWarning("Mantle layer view skipped: world service is not registered.");
            return;
        }

        FantaSim.App.World.MantleIsosurfaceSet set;
        try
        {
            set = world.GetMantleIsosurfacesAsync(_timeline.Tick);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Mantle layer sampling failed at t={Tick}: {Message}", _timeline.Tick, ex.Message);
            return;
        }

        // Separated crust slabs at the profile's declared layer explode factor (0.4 of max offset).
        Node3D slabRoot = BuildExplodedSolidCrust(RadialSectionProfile.MantleLayerExplodeFactor);
        // SCALE COMPENSATION (windowed gate 2026-07-08): BuildExplodedSolidCrust's child mesh
        // instances each carry the house x2 scale (they were built for the standalone
        // render.exploded path, parented directly under PlanetBody). The composer root applies
        // the house x2 as well — without compensation the slabs render x4 while the isosurfaces
        // (unit-built children) render x2, and giant slab shells englobe the interior. Halving
        // the slab root nets the slabs back to x2. Follow-up: unify the scaling convention so
        // piece builders emit unit-scale nodes and ONLY composition roots scale.
        slabRoot.Scale = Vector3.One * 0.5f;

        // Four isosurface entries (opaque inner cores first, translucent outer halos last) — the
        // same material singletons and BuildIsosurfaceNode the x-ray path uses.
        var entries = new List<MantleInteriorViewComposer.IsosurfaceEntry>(4);
        if (!set.ColdInner.IsEmpty)
            entries.Add(new(BuildIsosurfaceNode("ColdInner", set.ColdInner, ColdInnerMaterial), RenderPriority: 0));
        if (!set.WarmInner.IsEmpty)
            entries.Add(new(BuildIsosurfaceNode("WarmInner", set.WarmInner, WarmInnerMaterial), RenderPriority: 0));
        if (!set.ColdOuter.IsEmpty)
            entries.Add(new(BuildIsosurfaceNode("ColdOuter", set.ColdOuter, ColdOuterMaterial), RenderPriority: 2));
        if (!set.WarmOuter.IsEmpty)
            entries.Add(new(BuildIsosurfaceNode("WarmOuter", set.WarmOuter, WarmOuterMaterial), RenderPriority: 2));

        _mantleLayerRoot = MantleInteriorViewComposer.Compose(
            coreSphere: BuildCoreSphere(),
            isosurfaces: entries,
            separatedSlabRoot: slabRoot);
        body.AddChild(_mantleLayerRoot);
        _log.LogInformation(
            "Mantle interior layer mounted at t={Tick}: cold outer/inner={ColdOuter}/{ColdInner} verts, warm outer/inner={WarmOuter}/{WarmInner} verts, slabs={SlabChildren}.",
            _timeline.Tick,
            set.ColdOuter.Vertices.Length / 3, set.ColdInner.Vertices.Length / 3,
            set.WarmOuter.Vertices.Length / 3, set.WarmInner.Vertices.Length / 3,
            slabRoot.GetChildCount());
    }

    // M-A: ghost/unghost every plate-surface GeometryInstance3D. Transparency (0=opaque, 1=gone) is
    // per-instance and leaves the material untouched — no permanent transparent-pipeline switch.
    private void ApplyCrustGhost(float transparency)
    {
        if (_plateSurfaceRoot is null || !GodotObject.IsInstanceValid(_plateSurfaceRoot))
            return;
        ApplyTransparencyRecursive(_plateSurfaceRoot, transparency);
    }

    private static void ApplyTransparencyRecursive(Node node, float transparency)
    {
        if (node is GeometryInstance3D geometry)
            geometry.Transparency = transparency;
        foreach (var child in node.GetChildren())
            ApplyTransparencyRecursive(child, transparency);
    }

    // The four method-lock surfaces plus stage dressing (spec ingredient 6): opaque INNER cores
    // (deep blue slab hearts / red-orange plume hearts, drawn in the opaque pass), translucent
    // OUTER halos (drawn in the transparent pass with explicit render priority AFTER opaques),
    // and the dark core sphere at the CMB radius. All geometry is unit-sphere; the root applies
    // the house globe scale (x2, matching the plate surface and cutaway nodes).
    private Node3D BuildMantleXrayRoot(FantaSim.App.World.MantleIsosurfaceSet set)
    {
        var root = new Node3D { Name = "MantleXray", Scale = Vector3.One * 2.0f };
        root.AddChild(BuildCoreSphere());
        if (!set.ColdInner.IsEmpty)
            root.AddChild(BuildIsosurfaceNode("ColdInner", set.ColdInner, ColdInnerMaterial));
        if (!set.WarmInner.IsEmpty)
            root.AddChild(BuildIsosurfaceNode("WarmInner", set.WarmInner, WarmInnerMaterial));
        if (!set.ColdOuter.IsEmpty)
            root.AddChild(BuildIsosurfaceNode("ColdOuter", set.ColdOuter, ColdOuterMaterial));
        if (!set.WarmOuter.IsEmpty)
            root.AddChild(BuildIsosurfaceNode("WarmOuter", set.WarmOuter, WarmOuterMaterial));
        root.AddChild(BuildGhostShell());
        return root;
    }

    // Translucent ghost shell at the surface radius — the reference-image framing device: the
    // viewer sees the interior THROUGH a faint skin that still reads as "the planet". Drawn last
    // in the transparent pass (priority above the outer isosurfaces), back faces culled so the
    // far side doesn't double the haze.
    private static MeshInstance3D BuildGhostShell() => new()
    {
        Name = "GhostShell",
        Mesh = new SphereMesh { Radius = 1.0f, Height = 2.0f, RadialSegments = 96, Rings = 48 },
        MaterialOverride = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.75f, 0.82f, 0.9f, 0.10f),
            Roughness = 0.9f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Back,
            RenderPriority = 3,
        },
    };

    // Dark core sphere at the CMB radius — the backdrop the anomaly volumes read against. D3: the
    // geometric radius comes from _radialProfile (CMB × mantle depth scale), not a 0.55 literal.
    private MeshInstance3D BuildCoreSphere()
    {
        double r = _radialProfile.DisplayedCoreSphereRadius();
        return new MeshInstance3D
        {
            Name = "MantleCore",
            Mesh = new SphereMesh { Radius = (float)r, Height = (float)(2.0 * r), RadialSegments = 48, Rings = 24 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.05f, 0.055f, 0.07f),
                Roughness = 1.0f,
                Metallic = 0.0f,
            },
        };
    }

    private static MeshInstance3D BuildIsosurfaceNode(
        string name,
        FantaSim.App.World.MantleIsosurfaceMesh mesh,
        Material material)
    {
        int vertexCount = mesh.Vertices.Length / 3;
        var vertices = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = new Vector3(mesh.Vertices[3 * i], mesh.Vertices[3 * i + 1], mesh.Vertices[3 * i + 2]);
            normals[i] = new Vector3(mesh.Normals[3 * i], mesh.Normals[3 * i + 1], mesh.Normals[3 * i + 2]);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = mesh.Triangles;

        var arrayMesh = new ArrayMesh();
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        return new MeshInstance3D
        {
            Name = name,
            Mesh = arrayMesh,
            MaterialOverride = material,
        };
    }

    // M-B: BOTTOM+WALLS instance — unlit dark material needs only positions (no normals/UV). Matches
    // BuildCutawayFaceSector's ArrayMesh shape exactly.
    private static MeshInstance3D BuildExplodedSolidMeshInstance(string name, PlateCapMeshDto dto, Material material)
    {
        var vertices = new Vector3[dto.VertexCount];
        for (int i = 0; i < dto.VertexCount; i++)
        {
            int v3 = i * 3;
            vertices[i] = new Vector3(dto.Positions[v3 + 0], dto.Positions[v3 + 1], dto.Positions[v3 + 2]);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        return new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            Scale = Vector3.One * 2.0f,
            MaterialOverride = material,
        };
    }

    // M-B: per-cell crust thickness in metres, with the SAME null/mean fallback BuildCutawayFaces
    // uses so the solid reads when the document has no materialized thickness yet.
    private IReadOnlyList<double> ResolveCrustThicknessMetres(PlanetPresentationDocument document)
    {
        var crustThickness = document.CellCrustThickness;
        if (crustThickness is { Count: > 0 })
            return crustThickness;

        double meanCrust = CutawayStratumProfile.DefaultCrustThicknessMetres;
        var snapshot = document.GlobeSnapshot;
        int cellCount = snapshot is not null ? snapshot.CellCount : 0;
        if (cellCount <= 0)
            return new[] { meanCrust };
        var fallback = new double[cellCount];
        Array.Fill(fallback, meanCrust);
        return fallback;
    }
    private ShaderMaterial? _coldInnerMaterial;
    private ShaderMaterial? _coldOuterMaterial;
    private ShaderMaterial? _warmInnerMaterial;
    private ShaderMaterial? _warmOuterMaterial;

    // Opaque inner cores. Cold = deep blue with a faint glow so the slab hearts stay legible inside
    // the dark shell; warm = red-orange with a stronger emissive (the plumes are the focal point).
    private ShaderMaterial ColdInnerMaterial => _coldInnerMaterial ??= BuildIsosurfaceMaterial(
        new Color(0.10f, 0.22f, 0.75f), emission: 0.5f, alpha: 1.0f, priority: 0);

    private ShaderMaterial WarmInnerMaterial => _warmInnerMaterial ??= BuildIsosurfaceMaterial(
        new Color(0.95f, 0.30f, 0.08f), emission: 1.6f, alpha: 1.0f, priority: 0);

    // Translucent outer halos, drawn AFTER the opaques with explicit render priority (spec
    // ingredient 4: layered translucency is what reads as volumetric). Cold outer is tuned to the
    // same visual weight as warm outer: blue reads darker than orange against the dark core and the
    // cold field is dimmer (slab peak ~0.55 vs plume ~1.0), so the halo needs a brighter tint, a
    // higher emission (0.25 -> 0.55, matching warm), and a touch more alpha (0.22 -> 0.28) to read
    // as a distinct translucent envelope rather than a single flat surface.
    private ShaderMaterial ColdOuterMaterial => _coldOuterMaterial ??= BuildIsosurfaceMaterial(
        new Color(0.35f, 0.60f, 0.98f), emission: 0.55f, alpha: 0.28f, priority: 1);

    private ShaderMaterial WarmOuterMaterial => _warmOuterMaterial ??= BuildIsosurfaceMaterial(
        new Color(1.0f, 0.55f, 0.15f), emission: 0.6f, alpha: 0.22f, priority: 2);

    private static ShaderMaterial BuildIsosurfaceMaterial(Color tint, float emission, float alpha, int priority)
    {
        var material = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = alpha >= 1.0f ? MantleIsosurfaceOpaqueShaderCode : MantleIsosurfaceTranslucentShaderCode,
            },
            RenderPriority = priority,
        };
        material.SetShaderParameter("u_tint", tint);
        material.SetShaderParameter("u_emission_energy", emission);
        if (alpha < 1.0f)
            material.SetShaderParameter("u_alpha", alpha);
        return material;
    }

    // Both isosurface shaders render double-sided (cull_disabled) so the volumes read from any
    // angle; normals come from the anomaly-field gradient baked into the mesh. The translucent
    // variant is a separate shader because any static ALPHA write moves a material to the
    // transparent pipeline — the opaque cores must stay in the opaque pass (spec ingredient 4).
    private const string MantleIsosurfaceOpaqueShaderCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform vec4 u_tint : source_color = vec4(0.2, 0.45, 0.9, 1.0);
uniform float u_emission_energy : hint_range(0.0, 8.0) = 1.0;

void fragment() {
    ALBEDO = u_tint.rgb;
    EMISSION = u_tint.rgb * u_emission_energy;
    ROUGHNESS = 0.85;
    METALLIC = 0.0;
}
";

    private const string MantleIsosurfaceTranslucentShaderCode = @"
shader_type spatial;
render_mode cull_disabled, depth_draw_never;

uniform vec4 u_tint : source_color = vec4(0.2, 0.45, 0.9, 1.0);
uniform float u_emission_energy : hint_range(0.0, 8.0) = 1.0;
uniform float u_alpha : hint_range(0.0, 1.0) = 0.25;

void fragment() {
    ALBEDO = u_tint.rgb;
    EMISSION = u_tint.rgb * u_emission_energy;
    ALPHA = u_alpha;
    ROUGHNESS = 0.85;
    METALLIC = 0.0;
}
";

    // W3a: two flat half-disc cut faces (one per wedge boundary azimuth), per-vertex COLOR encodes
    // stratum bands. Crust thickness from CellCrustThickness when available (mean), else default.
    private Node3D BuildCutawayFaces()
    {
        var root = new Node3D { Name = "CutawayFaces" };

        var document = _currentDocument;
        var exaggeration = document?.CutawayExaggeration ?? 1.0;
        var planetRadiusMetres = ResolvePlanetRadiusMetres(document);

        var crustThickness = document?.CellCrustThickness;
        double meanCrust = CutawayStratumProfile.DefaultCrustThicknessMetres;
        if (crustThickness is { Count: > 0 })
        {
            double sum = 0;
            int n = 0;
            foreach (var t in crustThickness)
            {
                if (t > 0) { sum += t; n++; }
            }
            if (n > 0)
                meanCrust = sum / n;
        }

        var bands = CutawayStratumProfile.ComputeBands(
            meanCrust,
            CutawayStratumProfile.DefaultLithosphereLidThicknessMetres,
            exaggeration,
            planetRadiusMetres);

        var axis = _cutawayWedge.Axis;
        var reference = _cutawayWedge.Reference;
        var referenceCross = new UnifyMaths.Vector3D(
            axis.Y * reference.Z - axis.Z * reference.Y,
            axis.Z * reference.X - axis.X * reference.Z,
            axis.X * reference.Y - axis.Y * reference.X);

        var startDeg = _cutawayWedge.NormalizedStart;
        var endDeg = startDeg + _cutawayWedge.WidthDeg;

        root.AddChild(BuildCutawayFaceSector("CutFaceStart", startDeg, axis, reference, referenceCross, bands));
        root.AddChild(BuildCutawayFaceSector("CutFaceEnd", endDeg, axis, reference, referenceCross, bands));

        return root;
    }

    private static double ResolvePlanetRadiusMetres(PlanetPresentationDocument? document)
    {
        var profile = document?.LayerProjectionProfiles.FirstOrDefault(p =>
            string.Equals(p.LayerId, PlanetLayerProjectionProfile.CrustLayerId, StringComparison.Ordinal)
            && p.ProjectionKind == PlanetLayerProjectionKind.GlobeSurface);
        return profile is { PlanetRadiusMetres: > 0.0 }
            ? profile.PlanetRadiusMetres
            : PlanetLayerProjectionProfile.EarthLikePlanetRadiusMetres;
    }

    // Half-disc in plane(boundaryDir, axis): point = r*(cos(theta)*boundaryDir + sin(theta)*axis),
    // theta in [-pi/2, pi/2], r in [0,1]. Strata are concentric rings colored per band.
    private MeshInstance3D BuildCutawayFaceSector(
        string name,
        double azimuthDeg,
        UnifyMaths.Vector3D axis,
        UnifyMaths.Vector3D reference,
        UnifyMaths.Vector3D referenceCross,
        IReadOnlyList<StratumBand> bands)
    {
        const int angularSegments = 32;

        var boundaryDir = new UnifyMaths.Vector3D(
            reference.X * Math.Cos(azimuthDeg * Math.PI / 180.0) + referenceCross.X * Math.Sin(azimuthDeg * Math.PI / 180.0),
            reference.Y * Math.Cos(azimuthDeg * Math.PI / 180.0) + referenceCross.Y * Math.Sin(azimuthDeg * Math.PI / 180.0),
            reference.Z * Math.Cos(azimuthDeg * Math.PI / 180.0) + referenceCross.Z * Math.Sin(azimuthDeg * Math.PI / 180.0));

        var vertices = new List<Vector3>();
        var colors = new List<Color>();

        for (int b = 0; b < bands.Count; b++)
        {
            var band = bands[b];
            var outerR = Math.Max(0.0, band.OuterRadius);
            var innerR = Math.Max(0.0, band.InnerRadius);
            if (outerR <= innerR)
                continue;

            var bandColor = new Color(
                (float)band.Color.R,
                (float)band.Color.G,
                (float)band.Color.B);

            for (int s = 0; s < angularSegments; s++)
            {
                double t0 = -Math.PI / 2 + (s * Math.PI / angularSegments);
                double t1 = -Math.PI / 2 + ((s + 1) * Math.PI / angularSegments);

                var p0_outer = PolarToCartesian(outerR, t0, boundaryDir, axis);
                var p1_outer = PolarToCartesian(outerR, t1, boundaryDir, axis);
                var p0_inner = PolarToCartesian(innerR, t0, boundaryDir, axis);
                var p1_inner = PolarToCartesian(innerR, t1, boundaryDir, axis);

                vertices.Add(p0_outer); colors.Add(bandColor);
                vertices.Add(p1_outer); colors.Add(bandColor);
                vertices.Add(p0_inner); colors.Add(bandColor);

                vertices.Add(p1_outer); colors.Add(bandColor);
                vertices.Add(p1_inner); colors.Add(bandColor);
                vertices.Add(p0_inner); colors.Add(bandColor);
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        return new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            Scale = Vector3.One * 2.0f,
            MaterialOverride = HypsoPlateMaterialOverride,
        };
    }

    private static Vector3 PolarToCartesian(
        double radius,
        double theta,
        UnifyMaths.Vector3D boundaryDir,
        UnifyMaths.Vector3D axis)
    {
        var cosT = Math.Cos(theta);
        var sinT = Math.Sin(theta);
        return new Vector3(
            (float)(radius * (cosT * boundaryDir.X + sinT * axis.X)),
            (float)(radius * (cosT * boundaryDir.Y + sinT * axis.Y)),
            (float)(radius * (cosT * boundaryDir.Z + sinT * axis.Z)));
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

    private void ApplySurfaceAppearance(GlobeViewMode surfaceViewMode)
    {
        if (!PlateSurfaceViewModeTransition.ShouldRebuild(_currentSurfaceViewMode, surfaceViewMode))
        {
            _currentSurfaceViewMode = surfaceViewMode;
            return;
        }

        _currentSurfaceViewMode = surfaceViewMode;
        RebuildPlateSurface();
    }

    private void RebuildPlateSurface()
    {
        if (_currentDocument is null || _currentDocument.GlobeSnapshot is null)
            return;
        if (_activeRoot is null || !GodotObject.IsInstanceValid(_activeRoot))
            return;

        var body = _activeRoot.GetNodeOrNull<Node3D>("PlanetBody");
        if (body is null)
            return;

        if (_plateSurfaceRoot is PlateSurfaceRenderer renderer && GodotObject.IsInstanceValid(renderer))
        {
            BindPlateSurface(renderer, _currentDocument, _currentSurfaceViewMode);
            if (_explodedActive)
                RebuildExplodedCrust();
            return;
        }

        if (_plateSurfaceRoot is not null && GodotObject.IsInstanceValid(_plateSurfaceRoot))
        {
            ReleasePlateSurfaceRenderer();
            body.RemoveChild(_plateSurfaceRoot);
            _plateSurfaceRoot.QueueFree();
        }
        _plateSurfaceRoot = null;
        _plateSurfaces = null;

        _plateSurfaceRoot = BuildPlateSurface(_currentDocument, _currentSurfaceViewMode);
        body.AddChild(_plateSurfaceRoot);
        if (_explodedActive)
            RebuildExplodedCrust();
    }

    // M0 light refresh (spec §3.2): swap ONLY the globe snapshot to the playhead's reassigned
    // membership and rebuild the plate caps in place. No document re-fetch, no crust
    // materialization — GetGlobeSnapshotAt rides the service's cached reconstructor (~ms at
    // freq 4, see MotionGateTests), so continents glide during scrub and Play. P3: also re-sample
    // the per-cell continental fraction at the seek tick from cached sampler state so the
    // Continents coloring drifts smoothly between 5 M-tick snapshots instead of stepping.
    private void RefreshContinentsMembership(long tick)
    {
        // PERMANENT diagnostic: this seam-critical light path was inert in the exported app for two
        // arcs because every early return is silent. The entry log + per-guard logs let a windowed
        // run distinguish "never called" (the viewMode gate / seek wiring) from "called but guard
        // fired" (document null / already bound / world service unresolved). Debug level so Play does
        // not spam at info.
        _log.LogInformation(
            "RefreshContinentsMembership(tick={Tick}) entered: hasDocument={HasDocument}, boundContinentsTick={BoundTick}.",
            tick, _currentDocument is not null, _boundContinentsTick);

        if (_currentDocument is null || _boundContinentsTick == tick)
        {
            _log.LogInformation(
                "RefreshContinentsMembership(tick={Tick}) early-return: document null or tick already bound (hasDocument={HasDocument}, boundContinentsTick={BoundTick}).",
                tick, _currentDocument is not null, _boundContinentsTick);
            return;
        }

        var world = _registry.TryGet<WorldService>();
        if (world is null)
        {
            _log.LogInformation(
                "RefreshContinentsMembership(tick={Tick}) early-return: WorldService not registered (cross-ALC type-identity split or bundle not yet loaded).",
                tick);
            return;
        }

        WorldGlobeSnapshot snapshot;
        IReadOnlyDictionary<int, double> fractions;
        try
        {
            snapshot = world.GetGlobeSnapshotAt(tick);
            fractions = world.GetContinentalFractionByCellAt(tick);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Continents membership refresh failed at t={Tick}: {Message}", tick, ex.Message);
            return;
        }

        _currentDocument = _currentDocument with
        {
            GlobeSnapshot = snapshot,
            GlobeReferenceTick = tick,
            ContinentalFractionByCell = fractions,
        };
        _boundContinentsTick = tick;
        RebuildPlateSurface();
        _log.LogInformation(
            "RefreshContinentsMembership(tick={Tick}) refreshed: snapshotCells={SnapshotCells}, fractionCells={FractionCells}.",
            tick, snapshot.CellCount, fractions.Count);
    }

    private void ScheduleRegimeRefresh()
    {
        if (_regimeRefreshPending)
            return;
        _regimeRefreshPending = true;
        Callable.From(RefreshPresentationForRegime).CallDeferred();
    }

    private void HandleScrubAwareHeavyRefresh(long tick, TimelineTickOrigin origin, bool heavyRefreshRequested)
    {
        switch (origin)
        {
            case TimelineTickOrigin.ScrubPreview:
                ScheduleScrubRestRefresh(tick, heavyRefreshRequested);
                break;
            case TimelineTickOrigin.ScrubCommit:
                FlushScrubRestRefresh(tick);
                if (heavyRefreshRequested)
                    ScheduleRegimeRefresh();
                break;
            default:
                CancelScrubRestRefresh();
                if (heavyRefreshRequested)
                    ScheduleRegimeRefresh();
                break;
        }
    }

    private void ScheduleScrubRestRefresh(long tick, bool heavyRefreshRequested)
    {
        var schedule = _scrubApplyScheduler.RecordPreview(tick, heavyRefreshRequested, System.Environment.TickCount64);
        if (schedule is null)
            return;

        _scrubRefreshDelay?.Cancel();
        _scrubRefreshDelay?.Dispose();
        var cts = new CancellationTokenSource();
        _scrubRefreshDelay = cts;
        var dueInMs = Math.Max(0L, schedule.Value.DueAtMs - System.Environment.TickCount64);
        _ = DelayThenFlushScrubRefreshAsync(schedule.Value.Generation, dueInMs, cts.Token);
    }

    private async Task DelayThenFlushScrubRefreshAsync(int generation, long delayMs, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested || _disposed)
            return;

        Callable.From(() => FlushScrubRestRefreshIfDue(generation)).CallDeferred();
    }

    private void FlushScrubRestRefreshIfDue(int generation)
    {
        if (_disposed)
            return;

        if (_scrubApplyScheduler.ConsumeDue(generation, System.Environment.TickCount64) is null)
            return;

        ScheduleRegimeRefresh();
    }

    private void FlushScrubRestRefresh(long tick)
    {
        if (_scrubApplyScheduler.ConsumeCommit(tick) is null)
            return;

        _scrubRefreshDelay?.Cancel();
        _scrubRefreshDelay?.Dispose();
        _scrubRefreshDelay = null;
        ScheduleRegimeRefresh();
    }

    private void CancelScrubRestRefresh()
    {
        _scrubApplyScheduler.Clear();
        _scrubRefreshDelay?.Cancel();
        _scrubRefreshDelay?.Dispose();
        _scrubRefreshDelay = null;
    }

    private void RefreshPresentationForRegime()
    {
        if (_disposed)
        {
            _regimeRefreshPending = false;
            return;
        }

        var world = _registry.TryGet<WorldService>();
        if (world is null)
        {
            _regimeRefreshPending = false;
            _log.LogWarning("Planet presentation regime refresh skipped: world service is not registered.");
            return;
        }

        PlanetPresentationDocument document;
        try
        {
            document = world.GetPlanetPresentationAsync(_timeline.Tick);
        }
        catch (Exception ex)
        {
            _regimeRefreshPending = false;
            _log.LogError(ex, "Planet presentation document failed during regime refresh at tick {Tick}: {Message}", _timeline.Tick, ex.Message);
            return;
        }

        _timeline.UpdateFrom(document);
        EnsureNodeGraphView(document);
         BindDocument(document);
         _regimeRefreshPending = false;
     }

    private void AddLightingAndCamera(Node3D root, GlobeViewMode viewMode)
    {
        var tuning = PlanetLightingTuning.ForView(viewMode);
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = tuning.SunLightEnergy,
            LightColor = tuning.SunColor,
            ShadowEnabled = false,
        };
        root.AddChild(sun);
        _sunLight = sun;
        sun.Position = new Vector3(5.2f, 2.2f, 4.3f);
        sun.LookAt(Vector3.Zero, Vector3.Up);

        var environment = new WorldEnvironment
        {
            Name = "PlanetEnvironment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.015f, 0.018f, 0.022f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = tuning.AmbientColor,
                AmbientLightEnergy = tuning.AmbientLightEnergy,
            }
        };
        root.AddChild(environment);
        _planetEnvironment = environment;

        if (_registry.TryGet<FantaSim.App.Camera.IService>() is not null)
        {
            _log.LogDebug("Planet fallback camera skipped; App.Camera IService is registered.");
            return;
        }

        var camera = new Camera3D
        {
            Name = "PlanetCamera",
            Current = true,
            Position = new Vector3(0.0f, 1.3f, 6.3f),
        };
        root.AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up);
    }

    private void ApplyLightingForView(GlobeViewMode viewMode)
    {
        if (_sunLight is null || !GodotObject.IsInstanceValid(_sunLight))
            return;
        if (_planetEnvironment is null || !GodotObject.IsInstanceValid(_planetEnvironment))
            return;

        PlanetLightingTuning.ForView(viewMode).ApplyTo(_sunLight, _planetEnvironment);
    }

    private MeshInstance3D BuildMantle(PlanetPresentationDocument document)
    {
        var mesh = new SphereMesh
        {
            Radius = document.GlobeSnapshot is null ? 1.0f : 0.96f,
            Height = document.GlobeSnapshot is null ? 2.0f : 1.92f,
            RadialSegments = 64,
            Rings = 32,
        };

        return new MeshInstance3D
        {
            Name = "BaseSphere",
            Mesh = mesh,
            Scale = Vector3.One * 2.0f,
            MaterialOverride = ResolveMantleMaterial(RegimeSurfaceKind.Default),
        };
    }

    private MeshInstance3D BuildAtmosphereRim()
    {
        var mesh = new SphereMesh
        {
            Radius = 1.03f,
            Height = 2.06f,
            RadialSegments = 64,
            Rings = 32,
        };

        _atmosphereRimMaterial = new ShaderMaterial
        {
            Shader = AtmosphereRimShader,
            RenderPriority = 2,
        };

        return new MeshInstance3D
        {
            Name = "AtmosphereRim",
            Mesh = mesh,
            Scale = Vector3.One * 2.0f,
            Visible = false,
            MaterialOverride = _atmosphereRimMaterial,
        };
    }

    private void UpdateAtmosphereRim(long tick)
    {
        if (_atmosphereRim is null
            || !GodotObject.IsInstanceValid(_atmosphereRim)
            || _atmosphereRimMaterial is null)
            return;

        var state = AtmosphereRimStateMapper.Map(_timeline.AtmosphereSchedule, tick);
        bool visible = state.Exists && WorldViewContentGate.IsActive(_timeline.SelectedLayer);
        _atmosphereRim.Visible = visible;

        if (!visible)
            return;

        _atmosphereRimMaterial.SetShaderParameter("u_intensity", (float)state.Intensity);
        _atmosphereRimMaterial.SetShaderParameter("u_tint",
            new Color((float)state.Tint.R, (float)state.Tint.G, (float)state.Tint.B));
    }

    private PlateSurfaceRenderer BuildPlateSurface(PlanetPresentationDocument document, GlobeViewMode viewMode)
    {
        var renderer = new PlateSurfaceRenderer();
        BindPlateSurface(renderer, document, viewMode);
        return renderer;
    }

    private void BindPlateSurface(PlateSurfaceRenderer renderer, PlanetPresentationDocument document, GlobeViewMode viewMode)
    {
        var snapshot = document.GlobeSnapshot!;
        // P1 + W1: view mode selects cap appearance — World (composed product) and HypsometricTerrain
        // (crust diagnostic) both displace by elevation; PlateIdentity is flat. World uses a tuned
        // noise amplitude (sub-cell detail that buries the cell grid) + the WorldTerrainRamp + per-
        // vertex tint jitter; HypsometricTerrain uses the diagnostic crust palette.
        bool isTerrain = viewMode is GlobeViewMode.World or GlobeViewMode.HypsometricTerrain;
        bool isWorld = viewMode == GlobeViewMode.World;

        var relief = PlateSurfaceReliefFabric.ForView(viewMode);
        _plateSurfaces = new GlobePlateSurfaces(
            snapshot,
            noise: relief,
            detailSampler: BuildTectonicDetailSampler(snapshot, document.CellFeatures, relief, viewMode, isTerrain));

        IReadOnlyList<double> elevations = isTerrain
            ? (document.CellElevations is { } cellElevations && cellElevations.Count == snapshot.CellCount
                ? cellElevations
                : new double[snapshot.CellCount])
            : new double[snapshot.CellCount];

        var projection = LayerProjectionProfileResolver.ResolveForView(
            document,
            viewMode,
            worldMetresToUnitRadius: WorldHeightScale,
            worldHeightExponent: WorldHeightExponent);
        bool useAdaptiveSurface = projection.UseAdaptiveSurface;
        var featureWeights = useAdaptiveSurface
            ? BuildAdaptiveFeatureWeights(snapshot.CellCount, document.CellFeatures)
            : null;
        // Silhouette budget (north-star spec §1): planet views clamp the finalized radial
        // displacement to the profile's declared cap so the limb stays a circle. Diagnostic views
        // that do not declare a cap keep the +inf default (legacy unclamped behaviour).
        var maxDisp = projection.MaxDisplacementUnitRadius;
        var caps = useAdaptiveSurface
            ? _plateSurfaces.BuildAdaptiveSurfaces(
                elevations,
                exaggeration: projection.MetresToUnitRadius,
                options: new AdaptiveSubdivisionOptions(
                    MaxDepth: projection.AdaptiveSubdivisionMaxDepth,
                    EdgeHeightDeltaThreshold: projection.AdaptiveSubdivisionEdgeHeightDelta,
                    FeatureWeightDeltaThreshold: projection.AdaptiveSubdivisionFeatureWeightDelta),
                heightExponent: projection.HeightExponent,
                featureWeightsByCell: featureWeights,
                baseRadius: projection.BaseRadius,
                maxDisplacementUnitRadius: maxDisp)
            : _plateSurfaces.BuildSurfaces(
                elevations,
                exaggeration: projection.MetresToUnitRadius,
                heightExponent: projection.HeightExponent,
                baseRadius: projection.BaseRadius,
                maxDisplacementUnitRadius: maxDisp);

        var (perCellColor, perCellEmission) = isTerrain
            ? BuildCellAppearance(snapshot.CellCount, document, viewMode, isWorld ? snapshot.Cells : null)
            : (Array.Empty<RampColor>(), Array.Empty<float>());

        var colorMode = PlateSurfaceColorModePolicy.ForView(viewMode);
        var normalMode = PlateSurfaceNormalModePolicy.ForView(viewMode);
        // Per-vertex color envelope (world terrain): smooth per-cell ramp colours across cell AND
        // plate boundaries so terrain reads as Gouraud-shaded gradients instead of chunky per-cell
        // triangles. The crust diagnostic intentionally bypasses this smoothing and uses source-cell
        // facet colours so the dry crust stays readable from the front face, not only on the limb.
        var perPlateVertexColors = isTerrain && colorMode == PlateCapMeshColorMode.VertexEnvelope
            ? BuildPerPlateVertexColors(_plateSurfaces!, perCellColor)
            : new Dictionary<int, RampColor[]>();

        var jitter = PlateSurfaceTintFabric.ForView(viewMode);
        var meshes = new List<PlateCapMeshDto>(caps.Count);

        // P2A Continents (spec §3.1/D4): color by per-cell continental fraction sampled in the
        // moving plate frame; coastline frontier tint is derived from the fraction contour itself,
        // not from plate membership, so the seam tracks land/ocean transitions as plates drift.
        var continentsCellColors = viewMode == GlobeViewMode.Continents
            ? BuildContinentsCellColors(snapshot.CellCount, document.ContinentalFractionByCell)
            : Array.Empty<RampColor>();

        byte[]? continentsFrontier = viewMode == GlobeViewMode.Continents
            ? BuildFractionContourFrontier(snapshot, document.ContinentalFractionByCell)
            : null;

        foreach (var cap in caps.OrderBy(c => c.PlateId))
        {
            var mesh = isTerrain
                ? PlateCapMeshBuilder.BuildTerrain(
                    cap,
                    perPlateVertexColors!,
                    perCellEmission,
                    jitter,
                    colorMode,
                    perCellColor,
                    normalMode)
                : viewMode == GlobeViewMode.Continents
                    ? PlateCapMeshBuilder.BuildContinents(
                        cap,
                        continentsCellColors,
                        continentsFrontier!)
                    : PlateCapMeshBuilder.BuildPlateIdentity(cap);
            meshes.Add(mesh);
        }

        var material = HypsoPlateMaterialOverride;
        PlateSurfaceMaterialTuning.ForView(viewMode).ApplyTo(material);
        renderer.SetMeshes(meshes, material);

        // M-B: cache the build inputs so UpdateExploded can rebuild byte-identical TOP DTOs (same
        // Continents/terrain colors + emission) and the solid thickness exaggeration matches the
        // surface relief exaggeration exactly. Centroids come from the BASE unit-sphere corners
        // (tick/relief-invariant) so the explode direction stays stable across ticks.
        _lastCaps = caps;
        _lastExaggeration = projection.MetresToUnitRadius;
        _lastCentroids = PlateSolidBuilder.ComputeCentroids(snapshot);
        _lastViewMode = viewMode;
        _lastIsTerrain = isTerrain;
        _lastPerPlateVertexColors = perPlateVertexColors;
        _lastPerCellColor = perCellColor;
        _lastPerCellEmission = perCellEmission;
        _lastJitter = jitter;
        _lastColorMode = colorMode;
        _lastNormalMode = normalMode;
        _lastContinentsCellColors = continentsCellColors;
        _lastContinentsFrontier = continentsFrontier;

        _log.LogInformation(
            "Planet plate surface bound: view={ViewMode}, subdivision={Subdivision}, plates={PlateCount}, triangles={TriangleCount}, meshVertices={VertexCount}, scale={Scale}, trueScale={TrueScale}, amplification={Amplification}x.",
            viewMode,
            useAdaptiveSurface ? "adaptive" : "fixed",
            caps.Count,
            caps.Sum(cap => cap.Surface.TriangleCount),
            meshes.Sum(mesh => mesh.VertexCount),
            projection.MetresToUnitRadius,
            projection.TrueScaleMetresToUnitRadius,
            projection.ReliefAmplification);
    }

    private static IReadOnlyList<double>? BuildAdaptiveFeatureWeights(
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

    private static Func<CartesianPoint3, double>? BuildTectonicDetailSampler(
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

    // Computes per-cell color (world or crust ramp with trench/ridge accent baked in) and
    // per-cell volcanic emission intensity, from the document's crust elevation + feature data. The
    // world view uses WorldTerrainRamp (bare-rock product palette) modulated by the continental
    // ProvinceTint (cells indexed by CellId supply the sample direction); the crust diagnostic uses
    // HypsometricTint, un-tinted. Falls back to a neutral mid-ramp tint when crust data is absent.
    private static (RampColor[] Colors, float[] Emission) BuildCellAppearance(
        int cellCount,
        PlanetPresentationDocument document,
        GlobeViewMode viewMode,
        IReadOnlyList<GlobeCell>? cells)
    {
        var colors = new RampColor[cellCount];
        var emission = new float[cellCount];
        bool isWorld = viewMode == GlobeViewMode.World;
        bool showsVolcanicGlow = PlateSurfaceEmissionPolicy.ShowsVolcanicGlow(viewMode);

        var elevations = document.CellElevations;
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
        var cellCenters = provinceTint is not null ? BuildCellCenters(cellCount, cells!) : null;

        var features = document.CellFeatures;
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

    // Unit-sphere center per cell id: normalized corner mean of the snapshot's triangular cells.
    // Indexed by CellId (not list order) so the tint samples the direction of the cell it colors.
    private static CartesianPoint3?[] BuildCellCenters(int cellCount, IReadOnlyList<GlobeCell> cells)
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

    private static Color ToColor(RampColor c) => new((float)c.R, (float)c.G, (float)c.B);

    // Converts the host-side per-cell Godot.Color ramp output back to the Godot-free RampColor the
    // App.World plugin envelope consumes, runs the global per-vertex colour gather, and indexes the
    // result by plate id so BuildPlateMesh can look up each cap's per-vertex colours in one read.
    private static IReadOnlyDictionary<int, RampColor[]> BuildPerPlateVertexColors(
        GlobePlateSurfaces surfaces,
        RampColor[] perCellColor)
    {
        var perPlate = surfaces.BuildVertexColors(perCellColor);
        var byId = new Dictionary<int, RampColor[]>(perPlate.Count);
        foreach (var p in perPlate)
            byId[p.PlateId] = p.Colors;
        return byId;
    }

    private static Vector3 ToV3(CartesianPoint3 p) => new((float)p.X, (float)p.Y, (float)p.Z);

    private static RampColor[] BuildContinentsCellColors(
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

    private static byte[] BuildFractionContourFrontier(
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

    private static Node3D BuildProductLayerRoot(PlanetPresentationDocument document)
    {
        var root = new Node3D { Name = "ProductLayers" };
        for (var i = 0; i < document.Layers.Count; i++)
        {
            var layer = document.Layers[i];
            var node = new Node3D { Name = SafeNodeName($"{i:D2}_{layer.LayerId}") };
            node.SetMeta("layerId", layer.LayerId);
            node.SetMeta("regimeId", layer.RegimeId);
            node.SetMeta("variant", layer.Variant);
            node.SetMeta("branch", layer.Branch);
            node.SetMeta("productDomain", layer.ProductDomain);
            node.SetMeta("productName", layer.ProductName);
            node.SetMeta("productTick", layer.ProductTick);
            node.SetMeta("productAddress", layer.ProductAddress);
            root.AddChild(node);
        }

        return root;
    }

    private static Label3D BuildStatusLabel(PlanetPresentationDocument document)
        => new()
        {
            Name = "PlanetStatus",
            Text = document.PlanetId,
            Position = new Vector3(-2.6f, -2.1f, 0.0f),
            FontSize = 22,
            PixelSize = 0.008f,
            Modulate = new Color(0.86f, 0.90f, 0.95f, 0.88f),
        };

    // magma-ocean mantle: emissive molten lava with a slowly drifting fBm churn. lava-hot tracks the
    // sibling GlobeView.MagmaAlbedoForTemperature lava endpoint so both render paths read as the same lava.
    private const string MagmaShaderCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform vec4 u_lava_hot  : source_color = vec4(1.00, 0.46, 0.10, 1.0);
uniform vec4 u_lava_cool : source_color = vec4(0.16, 0.04, 0.03, 1.0);
uniform float u_emission_energy : hint_range(0.0, 8.0) = 1.6;
uniform float u_noise_scale = 2.6;
uniform float u_drift_speed = 0.05;

varying vec3 v_obj_pos;

vec3 hash3(vec3 p) {
    p = vec3(dot(p, vec3(127.1, 311.7, 74.7)),
             dot(p, vec3(269.5, 183.3, 246.1)),
             dot(p, vec3(113.5, 271.9, 124.6)));
    return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

float noise3(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(mix(dot(hash3(i + vec3(0.0, 0.0, 0.0)), f - vec3(0.0, 0.0, 0.0)),
                dot(hash3(i + vec3(1.0, 0.0, 0.0)), f - vec3(1.0, 0.0, 0.0)), u.x),
            mix(dot(hash3(i + vec3(0.0, 1.0, 0.0)), f - vec3(0.0, 1.0, 0.0)),
                dot(hash3(i + vec3(1.0, 1.0, 0.0)), f - vec3(1.0, 1.0, 0.0)), u.x), u.y),
        mix(mix(dot(hash3(i + vec3(0.0, 0.0, 1.0)), f - vec3(0.0, 0.0, 1.0)),
                dot(hash3(i + vec3(1.0, 0.0, 1.0)), f - vec3(1.0, 0.0, 1.0)), u.x),
            mix(dot(hash3(i + vec3(0.0, 1.0, 1.0)), f - vec3(0.0, 1.0, 1.0)),
                dot(hash3(i + vec3(1.0, 1.0, 1.0)), f - vec3(1.0, 1.0, 1.0)), u.x), u.y),
        u.z);
}

float fbm(vec3 p) {
    float n  = noise3(p *  5.0) * 0.5000;
          n += noise3(p * 10.0) * 0.2500;
          n += noise3(p * 20.0) * 0.1250;
          n += noise3(p * 40.0) * 0.0625;
    return n;
}

void vertex() {
    v_obj_pos = VERTEX;
}

void fragment() {
    vec3 q = v_obj_pos * u_noise_scale + vec3(0.0, TIME * u_drift_speed, 0.0);
    float n = fbm(q);
    float t = smoothstep(-0.05, 0.45, n);
    vec3 col = mix(u_lava_cool.rgb, u_lava_hot.rgb, t);
    float vein = smoothstep(0.55, 0.80, n);
    col += u_lava_hot.rgb * vein * 0.60;

    ALBEDO = col;
    EMISSION = col * u_emission_energy + u_lava_hot.rgb * vein * u_emission_energy;
    ROUGHNESS = 0.62;
    METALLIC = 0.0;
}
";

    // stagnant-lid mantle: dark basaltic cooled crust, subtle noise-modulated albedo/roughness, and a
    // thin faintly-emissive crack band where the fBm crosses a threshold (cheap: one smoothstep pair).
    private const string StagnantShaderCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform vec4 u_basalt_dark  : source_color = vec4(0.05, 0.05, 0.06, 1.0);
uniform vec4 u_basalt_light : source_color = vec4(0.20, 0.19, 0.21, 1.0);
uniform float u_crack_glow : hint_range(0.0, 2.0) = 0.16;
uniform float u_noise_scale = 3.2;

varying vec3 v_obj_pos;

vec3 hash3(vec3 p) {
    p = vec3(dot(p, vec3(127.1, 311.7, 74.7)),
             dot(p, vec3(269.5, 183.3, 246.1)),
             dot(p, vec3(113.5, 271.9, 124.6)));
    return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

float noise3(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(mix(dot(hash3(i + vec3(0.0, 0.0, 0.0)), f - vec3(0.0, 0.0, 0.0)),
                dot(hash3(i + vec3(1.0, 0.0, 0.0)), f - vec3(1.0, 0.0, 0.0)), u.x),
            mix(dot(hash3(i + vec3(0.0, 1.0, 0.0)), f - vec3(0.0, 1.0, 0.0)),
                dot(hash3(i + vec3(1.0, 1.0, 0.0)), f - vec3(1.0, 1.0, 0.0)), u.x), u.y),
        mix(mix(dot(hash3(i + vec3(0.0, 0.0, 1.0)), f - vec3(0.0, 0.0, 1.0)),
                dot(hash3(i + vec3(1.0, 0.0, 1.0)), f - vec3(1.0, 0.0, 1.0)), u.x),
            mix(dot(hash3(i + vec3(0.0, 1.0, 1.0)), f - vec3(0.0, 1.0, 1.0)),
                dot(hash3(i + vec3(1.0, 1.0, 1.0)), f - vec3(1.0, 1.0, 1.0)), u.x), u.y),
        u.z);
}

float fbm(vec3 p) {
    float n  = noise3(p *  5.0) * 0.5000;
          n += noise3(p * 10.0) * 0.2500;
          n += noise3(p * 20.0) * 0.1250;
          n += noise3(p * 40.0) * 0.0625;
    return n;
}

void vertex() {
    v_obj_pos = VERTEX;
}

void fragment() {
    vec3 q = v_obj_pos * u_noise_scale;
    float n = fbm(q);
    float t = smoothstep(-0.10, 0.40, n);
    vec3 col = mix(u_basalt_dark.rgb, u_basalt_light.rgb, t);
    float crack = smoothstep(0.45, 0.55, n) - smoothstep(0.55, 0.70, n);
    col += u_basalt_light.rgb * crack * 0.40;

    ALBEDO = col;
    EMISSION = u_basalt_light.rgb * crack * u_crack_glow;
    ROUGHNESS = mix(0.88, 0.62, t);
    METALLIC = 0.0;
}
";

    // Hypsometric plate-cap shader (A2 + W3a cutaway discard): per-vertex COLOR carries the bare-crust
    // tint (dark basalt → rock brown → light rock, computed on the CPU with percentile normalization —
    // no water imagery; the hydrosphere lane owns that when it exists, per the no-sphere-costume rule);
    // UV2.x carries the volcanic-vent emission intensity (0 = none). Trench darkening and ridge
    // brightening are baked into the vertex COLOR on the CPU (CrustAccentMapper.Apply) so the shader
    // only needs albedo + a gated emission pass. Half-Lambert light keeps displaced relief readable.
    // W3a: the wedge discard (u_wedge_active) drops fragments whose object-space position direction
    // falls inside the dihedral wedge — the planet reads as a solid with a wedge cut out. Inactive
    // (u_wedge_active=false, width 0) = zero discard = today's render unchanged. The discard test
    // mirrors CutawayWedge.Contains (pure, unit-tested): project onto the perpendicular plane, measure
    // azimuth via atan2 against a basis derived the same way as the C# model.
    // Godot 4 docs: COLOR (vec4, auto-populated from ArrayType.Color, no flag on ShaderMaterial) and
    // UV2 (vec2, auto-populated from ArrayType.TexUV2); EMISSION is out-vec3 in fragment().
    private const string HypsoPlateShaderCode = @"
shader_type spatial;
render_mode cull_disabled;

uniform vec4 u_volcanic_glow : source_color = vec4(1.0, 0.42, 0.10, 1.0);
uniform float u_volcanic_energy : hint_range(0.0, 8.0) = 1.4;
uniform float u_albedo_gain : hint_range(0.5, 2.0) = 1.0;
uniform float u_albedo_ceiling : hint_range(0.1, 1.0) = 1.0;
uniform float u_light_floor : hint_range(0.0, 1.0) = 0.08;
uniform float u_wrap_strength : hint_range(0.0, 1.0) = 1.0;
uniform float u_light_contrast : hint_range(0.5, 2.0) = 1.0;
uniform vec3 u_color_balance = vec3(1.0, 1.0, 1.0);

// W3a cutaway wedge (inactive by default; zero discard when u_wedge_active is false).
uniform bool u_wedge_active = false;
uniform vec3 u_wedge_axis = vec3(0.0, 0.0, 1.0);
uniform vec3 u_wedge_reference = vec3(1.0, 0.0, 0.0);
uniform vec3 u_wedge_reference_cross = vec3(0.0, 1.0, 0.0);
uniform float u_wedge_start_rad = 0.0;
uniform float u_wedge_width_rad = 0.0;

const float TWO_PI = 6.28318530718;

// Wedge test needs the MODEL-space direction: in fragment() VERTEX is VIEW-space, so testing it
// there would make the wedge camera-relative (it would swing with the camera instead of cutting
// the planet). Capture object-space VERTEX in vertex() — where it IS model space — via a varying.
varying vec3 v_wedge_obj;

void vertex() {
    v_wedge_obj = VERTEX;
}

float wedge_azimuth(vec3 dir) {
    vec3 proj = dir - dot(dir, u_wedge_axis) * u_wedge_axis;
    float pl = length(proj);
    if (pl < 1e-7) return -1.0;
    vec3 unit = proj / pl;
    float x = dot(unit, u_wedge_reference);
    float y = dot(unit, u_wedge_reference_cross);
    float a = atan(y, x);
    if (a < 0.0) a += TWO_PI;
    return a;
}

bool wedge_contains(float azimuth) {
    if (azimuth < 0.0) return false;
    float end = u_wedge_start_rad + u_wedge_width_rad;
    if (end <= TWO_PI) {
        return azimuth >= u_wedge_start_rad && azimuth < end;
    }
    return azimuth >= u_wedge_start_rad || azimuth < (end - TWO_PI);
}

void fragment() {
    if (u_wedge_active) {
        vec3 dir = normalize(v_wedge_obj);
        float az = wedge_azimuth(dir);
        if (wedge_contains(az)) {
            discard;
        }
    }
    ALBEDO = clamp(COLOR.rgb * u_color_balance * u_albedo_gain, vec3(0.0), vec3(u_albedo_ceiling));
    float vent = UV2.x;
    if (vent > 0.001) {
        EMISSION = u_volcanic_glow.rgb * vent * u_volcanic_energy;
    }
    ROUGHNESS = 0.92;
    METALLIC = 0.0;
}

void light() {
    float ndotl = dot(normalize(NORMAL), normalize(LIGHT));
    float lambert = max(ndotl, 0.0);
    float wrapped = ndotl * 0.5 + 0.5;
    wrapped *= wrapped;
    float lit = mix(lambert, wrapped, u_wrap_strength);
    lit = pow(max(lit, u_light_floor), u_light_contrast);
    DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR * ATTENUATION * lit;
}
";

    // Atmosphere limb-glow shader (W2): a fresnel rim on a shell slightly larger than the surface.
    // The rim glows only at grazing angles (the limb) and vanishes face-on, so it never occludes the
    // surface or the label. Godot 4 docs grounding:
    //   - render_mode blend_add: additive blend (source added to destination; the rim only ADDS light,
    //     never darkens/occludes). Spatial Shader reference -> render_mode blend options.
    //   - depth_draw_never: the shell writes no depth, so it cannot hide the surface behind it.
    //   - unshaded: skip lighting; ALBEDO is the direct output color (the rim is pure glow, not lit).
    //   - cull_disabled: render both faces (house idiom; the near-hemisphere carries the fresnel).
    //   - NORMAL (view-space surface normal) and VIEW (fragment->camera direction, view space) are
    //     fragment() built-ins; dot(NORMAL, VIEW) peaks face-on, so (1 - dot) peaks at the limb.
    //   - source_color / hint_range uniform hints match the sibling shaders. RenderPriority (set on
    //     the ShaderMaterial in BuildAtmosphereRim) draws this after the opaque surface.
    private const string AtmosphereRimShaderCode = @"
shader_type spatial;
render_mode cull_disabled, blend_add, depth_draw_never, unshaded;

uniform vec4 u_tint : source_color = vec4(0.46, 0.68, 1.0, 1.0);
uniform float u_intensity : hint_range(0.0, 1.0) = 0.5;
// Falloff exponent: how tightly the glow hugs the limb. 3.0 washed an additive tint over most of
// the disk (2026-07-03 world-view finding: the whole planet read navy); 6.0 confines it to a rim.
uniform float u_falloff : hint_range(1.0, 12.0) = 6.0;

void fragment() {
    float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), u_falloff);
    ALBEDO = u_tint.rgb * (fresnel * u_intensity);
}
";

    private static Shader? _magmaShader;
    private static Shader? _stagnantShader;
    private static Shader? _hypsoPlateShader;
    private static Shader? _atmosphereRimShader;

    private Material? _magmaMantleMaterial;
    private Material? _stagnantMantleMaterial;
    private Material? _baseMantleMaterial;
    private static Material? _hypsoPlateMaterial;

    // M-B: darker unlit material for the solid-crust BOTTOM + SIDE WALLS — distinct from the
    // attributed surface so the slab silhouette reads as thickness, not as more surface. Unlit +
    // cull_disabled matches the cutaway render_mode; lit + real normals is a future refinement.
    private static readonly Material ExplodedCrustDarkMaterial = new StandardMaterial3D
    {
        AlbedoColor = new Color(0.12f, 0.10f, 0.09f),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static Shader MagmaShader => _magmaShader ??= new Shader { Code = MagmaShaderCode };
    private static Shader StagnantShader => _stagnantShader ??= new Shader { Code = StagnantShaderCode };
    private static Shader HypsoPlateShader => _hypsoPlateShader ??= new Shader { Code = HypsoPlateShaderCode };
    private static Shader AtmosphereRimShader => _atmosphereRimShader ??= new Shader { Code = AtmosphereRimShaderCode };

    private static Material HypsoPlateMaterial => _hypsoPlateMaterial ??= new ShaderMaterial { Shader = HypsoPlateShader };

    // W3a: per-instance plate material so the cutaway wedge uniforms are binder-scoped (a static
    // singleton would let one binder's cutaway leak into another). Lazily built; the wedge uniforms
    // are updated by UpdateCutaway. Falls back to the shared static when the cutaway is inactive
    // (zero-cost default: same material reference as before, so inactive = truly zero render change).
    private ShaderMaterial HypsoPlateMaterialOverride => _hypsoPlateMaterialOverride ??= new ShaderMaterial
    {
        Shader = HypsoPlateShader,
    };

    private Material ResolveMantleMaterial(RegimeSurfaceKind kind) =>
        kind switch
        {
            RegimeSurfaceKind.MagmaOcean => _magmaMantleMaterial ??= BuildMagmaMantleMaterial(),
            RegimeSurfaceKind.StagnantLid => _stagnantMantleMaterial ??= BuildStagnantMantleMaterial(),
            _ => _baseMantleMaterial ??= BuildBaseMantleMaterial(),
        };

    private static ShaderMaterial BuildMagmaMantleMaterial() => new() { Shader = MagmaShader };

    private static ShaderMaterial BuildStagnantMantleMaterial() => new() { Shader = StagnantShader };

    private static StandardMaterial3D BuildBaseMantleMaterial() =>
        new()
        {
            AlbedoColor = new Color(0.02f, 0.20f, 0.28f),
            Roughness = 0.82f,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

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
        if (!string.Equals(args.BundleId, WorldBundleId, StringComparison.OrdinalIgnoreCase))
            return;

        _subscribedWorldHash = null;
        _generationSubscription?.Dispose();
        _generationSubscription = null;
        _worldRuntimeReload.MarkRuntimeChanging();
        ResetRegimeTracking();
        Callable.From(() =>
        {
            ClearActiveRoot();
            ReleaseNodeGraphView();
        }).CallDeferred();
        _log.LogInformation("Planet presentation released before resource {Operation}: {BundleId}", args.Operation, args.BundleId);
    }

    private void OnResourceRuntimeChanged(object? sender, EventArgs args)
    {
        if (_disposed || !_worldRuntimeReload.TryScheduleDeferredAttempt())
            return;

        Callable.From(TryRebindAfterWorldRuntimeChange).CallDeferred();
    }

    private void TryRebindAfterWorldRuntimeChange()
    {
        _worldRuntimeReload.CompleteDeferredAttempt();
        if (_disposed || !_worldRuntimeReload.IsPending || !_resource.IsLoaded(WorldBundleId))
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

        if (_mantleXrayRoot is not null && GodotObject.IsInstanceValid(_mantleXrayRoot))
        {
            _mantleXrayRoot.GetParent()?.RemoveChild(_mantleXrayRoot);
            _mantleXrayRoot.QueueFree();
        }
        _mantleXrayRoot = null;

        if (_mantleLayerRoot is not null && GodotObject.IsInstanceValid(_mantleLayerRoot))
        {
            _mantleLayerRoot.GetParent()?.RemoveChild(_mantleLayerRoot);
            _mantleLayerRoot.QueueFree();
        }
        _mantleLayerRoot = null;
        _mantleLayerActive = false;
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
        _timeline.LayerSelectionChanged -= OnLayerSelectionChanged;
        _resource.RuntimeChanging -= OnResourceRuntimeChanging;
        _resource.RuntimeChanged -= OnResourceRuntimeChanged;
        _generationSubscription?.Dispose();
        _generationSubscription = null;
        CancelScrubRestRefresh();
        ReleaseNodeGraphView();
        _timelineRegistration.Dispose();
        ClearActiveRoot();
    }
}

internal sealed class PlanetTimelineController : ITimelineController
{
    private readonly Action<long, TimelineTickOrigin> _applyTick;
    private long _tick;
    private long _maxTick = 1;
    // D5: stacked active set (pure helper); SelectedLayer is the primary (first or null).
    private readonly LayerActiveSet _activeLayers = new();
    private Action? _onPlay;
    private Action? _onPause;
    private Action<long>? _onSeek;
    private Func<bool>? _checkPlaying;

    public PlanetTimelineController(Action<long, TimelineTickOrigin> applyTick)
    {
        _applyTick = applyTick ?? throw new ArgumentNullException(nameof(applyTick));
        GeosphereSchedule = EmptySchedule("geosphere");
        AtmosphereSchedule = EmptySchedule("atmosphere");
    }

    public long Tick => _tick;

    public long MaxTick => _maxTick;

    public bool IsPlaying => _checkPlaying?.Invoke() ?? false;

    public SphereRegimeSchedule GeosphereSchedule { get; private set; }

    public SphereRegimeSchedule AtmosphereSchedule { get; private set; }

    public TimelineLayerSelection? SelectedLayer => _activeLayers.Primary;

    public IReadOnlyList<TimelineLayerSelection> ActiveLayers => _activeLayers.Layers;

    public event Action<long>? TickChanged;
    public event Action<TimelineLayerSelection?>? LayerSelectionChanged;

    public void UpdateFrom(PlanetPresentationDocument document)
    {
        GeosphereSchedule = document.GeosphereSchedule ?? EmptySchedule("geosphere");
        AtmosphereSchedule = document.AtmosphereSchedule ?? EmptySchedule("atmosphere");
        _maxTick = Math.Max(1L, document.MaxTick);
        PushTick(Math.Clamp(_tick, 0L, _maxTick));
    }

    public void Play() => _onPlay?.Invoke();

    public void Pause() => _onPause?.Invoke();

    public void SeekTo(long tick) => _onSeek?.Invoke(Math.Clamp(tick, 0L, _maxTick));
    public void SeekTo(long tick, TimelineTickOrigin origin) => SeekTo(tick);

    // D5 back-compat: SelectLayer makes the active set EXACTLY {layer}.
    public void SelectLayer(string sphereId, string layerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sphereId);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);

        if (_activeLayers.SetExclusive(new TimelineLayerSelection(sphereId, layerId)))
            LayerSelectionChanged?.Invoke(_activeLayers.Primary);
    }

    // D5: toggle membership; LayerSelectionChanged always fires (stacked-set consumers react to
    // every toggle even when the primary is unchanged).
    public void ToggleLayer(string sphereId, string layerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sphereId);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);

        _activeLayers.Toggle(new TimelineLayerSelection(sphereId, layerId));
        LayerSelectionChanged?.Invoke(_activeLayers.Primary);
    }

    public void PushTick(long tick)
        => PushTick(tick, TimelineTickOrigin.Standard);

    public void PushTick(long tick, TimelineTickOrigin origin)
    {
        _tick = Math.Clamp(tick, 0L, _maxTick);
        _applyTick(_tick, origin);
        if (origin != TimelineTickOrigin.ScrubPreview)
            TickChanged?.Invoke(_tick);
    }

    public void RegisterPlayback(Action onPlay, Action onPause, Action<long> onSeek, Func<bool> checkPlaying)
    {
        _onPlay = onPlay;
        _onPause = onPause;
        _onSeek = onSeek;
        _checkPlaying = checkPlaying;
    }

    public void UnregisterPlayback()
    {
        _onPlay = null;
        _onPause = null;
        _onSeek = null;
        _checkPlaying = null;
    }

    private static SphereRegimeSchedule EmptySchedule(string sphereId)
        => new(new SphereId(sphereId), Array.Empty<SphereRegime>());
}
