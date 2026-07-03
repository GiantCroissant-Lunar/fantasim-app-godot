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

    // World-view seeded peaks (W1, §5c "sub-cell detail"): tuned to bury the 5120-cell grid faceting.
    // Lower amplitude than the diagnostic DefaultPeaks (1000 m) so the tectonic envelope still reads,
    // higher base frequency so bumps are a few cells wide, 5 octaves for enough finer grain. The noise
    // is cross-plate watertight-safe (sampled on shared base positions — see GlobePlateSurfaces).
    private static readonly NoiseParams WorldPeaks = new(
        Seed: 1337,
        // Base fabric, not garnish (look-dev 2026-07-03, user's everywhere-relief reference): an old
        // waterless world is rough at every point — impact history, pre-onset orogenies, erosion —
        // none of which the crust pipeline simulates yet. This noise is the DECLARED stand-in for
        // that unsimulated history: base freq 8 gives continental-scale lumps, 6 octaves add crag.
        // NoiseRelief's fBm output is heavily normalized (measured std ≈ 0.15 × Amplitude, extremes
        // ≈ ±0.45 ×), so the nominal figure is NOT metres of relief: 17,000 delivers a ~2,500 m-std
        // fabric — ~2.5% of radius on the silhouette through the sqrt lens, extremes ~4.5%. The
        // tectonic envelope keeps reserved contrast on top (ranges ~8%, trenches ~-5%). Known
        // limitation: the fabric is sphere-fixed (sampled on shared base positions), so it does not
        // drift with plates — the truth-side replacement (roughness from crust age / impact fields)
        // is the A4-adjacent roadmap item that will.
        BaseFrequency: 8.0,
        Octaves: 6,
        Lacunarity: 2.0,
        Gain: 0.5,
        Amplitude: 17_000.0,
        Ridged: false);

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
    private readonly IDisposable _watch;
    private IDisposable? _generationSubscription;
    private Node3D? _activeRoot;
    private Node3D? _plateSurfaceRoot;
    private PlateBoundaryFocusRenderer? _boundaryRenderer;
    private MeshInstance3D? _mantle;
    private MeshInstance3D? _atmosphereRim;
    private ShaderMaterial? _atmosphereRimMaterial;
    private Label3D? _statusLabel;
    private GlobePlateSurfaces? _plateSurfaces;
    private PlanetGenerationGraphSource? _graphSource;
    private NodeGraphViewSource? _graphView;
    private PlanetGenerationGraphSource.PlanetGenerationTimelineGraphBinding? _graphBinding;
    private IDisposable? _graphViewRegistration;
    private bool _graphViewMounted;
    private int? _subscribedWorldHash;
    private bool _worldRuntimeChangePending;
    private string? _boundRegimeId;
    private bool _regimeRefreshPending;
    private long? _boundCrustSnapshotTick;
    private IReadOnlyList<long> _boundCrustSnapshotTicks = Array.Empty<long>();
    private PlanetPresentationDocument? _currentDocument;
    private GlobeViewMode _currentViewMode;

    // W3a cutaway wedge state (inactive by default; width 0 = zero render change).
    private CutawayWedge _cutawayWedge = new(new UnifyMaths.Vector3D(0, 0, 1), 0, 0);
    private double _cutawayAzimuthDeg;
    private double _cutawayWidthDeg;
    private Node3D? _cutawayFaceRoot;
    private ShaderMaterial? _hypsoPlateMaterialOverride;
    private bool _disposed;

    public PlanetPresentationBinder(
        IRegistry registry,
        ResourceService resource,
        IBundleSceneRegistry sceneRegistry,
        ILoggerFactory loggerFactory)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _resource = resource ?? throw new ArgumentNullException(nameof(resource));
        _sceneRegistry = sceneRegistry ?? throw new ArgumentNullException(nameof(sceneRegistry));
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));

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
        _watch = _resource.WatchResource(WorldBundleId);
    }

    private void ResetRegimeTracking()
    {
        _boundRegimeId = null;
        _regimeRefreshPending = false;
        _boundCrustSnapshotTick = null;
        _boundCrustSnapshotTicks = Array.Empty<long>();
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
            document = world.GetPlanetPresentationAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Planet presentation document failed.");
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

        AddLightingAndCamera(root);

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
        _boundRegimeId = _timeline.GeosphereSchedule.RegimeAt(_timeline.Tick)?.RegimeId;
        _currentViewMode = GlobeViewModeResolver.Resolve(_boundRegimeId, _timeline.SelectedLayer);

        if (document.GlobeSnapshot is not null)
        {
            _plateSurfaceRoot = BuildPlateSurface(document, _currentViewMode);
            body.AddChild(_plateSurfaceRoot);

            _boundaryRenderer = new PlateBoundaryFocusRenderer(
                document.BoundaryArcs ?? Array.Empty<PlateBoundaryArc>());
            body.AddChild(_boundaryRenderer);
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
        ApplyTimelineTick(_timeline.Tick);

        _log.LogInformation(
            "Planet presentation mounted under stage Environment: planet={PlanetId}, plates={PlateCount}, cells={CellCount}, productLayers={LayerCount}, revision={Revision}.",
            document.PlanetId,
            document.GlobeSnapshot?.PlateCount ?? 0,
            document.GlobeSnapshot?.CellCount ?? 0,
            document.Layers.Count,
            document.Revision);
    }

    private void ApplyTimelineTick(long tick)
    {
        var regime = _timeline.GeosphereSchedule.RegimeAt(tick);
        var regimeId = regime?.RegimeId;
        var showsPlateFeatures = regime?.ShowsPlateFeatures ?? true;

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
            ScheduleRegimeRefresh();
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
                ScheduleRegimeRefresh();
            }
        }

        var viewMode = GlobeViewModeResolver.Resolve(regimeId, _timeline.SelectedLayer);
        bool showBoundaries = viewMode == GlobeViewMode.PlateIdentity;

        if (_plateSurfaceRoot is not null && GodotObject.IsInstanceValid(_plateSurfaceRoot))
            _plateSurfaceRoot.Visible = showsPlateFeatures;

        if (_boundaryRenderer is not null && GodotObject.IsInstanceValid(_boundaryRenderer))
            _boundaryRenderer.Visible = showBoundaries;

        if (_mantle is not null && GodotObject.IsInstanceValid(_mantle))
        {
            _mantle.MaterialOverride = ResolveMantleMaterial(RegimeSurfaceResolver.Resolve(regimeId));
            _mantle.Visible = MantleSurfaceGate.IsVisible(
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
                label += viewMode == GlobeViewMode.World
                    ? VerticalScaleLabel.BuildIndicatorSuffix(WorldHeightScale, WorldHeightExponent)
                    : VerticalScaleLabel.BuildIndicatorSuffix(_currentDocument.VerticalExaggeration);
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

    // W3a: two flat half-disc cut faces (one per wedge boundary azimuth), per-vertex COLOR encodes
    // stratum bands. Crust thickness from CellCrustThickness when available (mean), else default.
    private Node3D BuildCutawayFaces()
    {
        var root = new Node3D { Name = "CutawayFaces" };

        var document = _currentDocument;
        var exaggeration = document?.CutawayExaggeration ?? 1.0;
        // INTERIM spatial anchor: the S3 world-radius parameter is roadmap (see the terminology
        // doctrine note) — until it lands, Earth's mean radius is the declared default converting
        // stratum metres to unit-globe fractions. Upgrade path: replace with the document's
        // world-radius parameter alongside VerticalScaleLabel's honest xN switch.
        const double planetRadiusMetres = 6_371_000.0;

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

        // P1: layer focus swaps the cap appearance. Rebuild just the plate surface (free old caps,
        // build new ones) without re-fetching — no node leaks, no full rebind.
        var regimeId = _timeline.GeosphereSchedule.RegimeAt(_timeline.Tick)?.RegimeId;
        var newViewMode = GlobeViewModeResolver.Resolve(regimeId, selection);
        if (newViewMode != _currentViewMode
            && newViewMode != GlobeViewMode.Inactive
            && _currentViewMode != GlobeViewMode.Inactive)
        {
            _currentViewMode = newViewMode;
            RebuildPlateSurface();
        }

        ApplyTimelineTick(_timeline.Tick);
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

        if (_plateSurfaceRoot is not null && GodotObject.IsInstanceValid(_plateSurfaceRoot))
        {
            body.RemoveChild(_plateSurfaceRoot);
            _plateSurfaceRoot.QueueFree();
        }
        _plateSurfaceRoot = null;
        _plateSurfaces = null;

        _plateSurfaceRoot = BuildPlateSurface(_currentDocument, _currentViewMode);
        body.AddChild(_plateSurfaceRoot);
    }

    private void ScheduleRegimeRefresh()
    {
        if (_regimeRefreshPending)
            return;
        _regimeRefreshPending = true;
        Callable.From(RefreshPresentationForRegime).CallDeferred();
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
            _log.LogError(ex, "Planet presentation document failed during regime refresh at tick {Tick}.", _timeline.Tick);
            return;
        }

        _timeline.UpdateFrom(document);
        EnsureNodeGraphView(document);
         BindDocument(document);
         _regimeRefreshPending = false;
     }

    private static void AddLightingAndCamera(Node3D root)
    {
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            // Warm key light (§5c): a slightly warm white replaces the neutral default so bare rock
            // reads as sunlit terrain, not re-costumed as ocean. Diagnostic views share this light;
            // their palettes assume neutral-warm illumination.
            LightEnergy = 1.8f,
            LightColor = new Color(1.02f, 0.96f, 0.88f),
            ShadowEnabled = false,
        };
        root.AddChild(sun);
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
                // Warm/neutral ambient (§5c): the blue-grey (0.34,0.36,0.40) was re-costuming bare
                // rock as ocean. This is a global scene change — diagnostic views are affected too
                // (intended; their palettes assume neutral-warm light).
                AmbientLightColor = new Color(0.38f, 0.34f, 0.30f),
                AmbientLightEnergy = 0.42f,
            }
        };
        root.AddChild(environment);

        var camera = new Camera3D
        {
            Name = "PlanetCamera",
            Current = true,
            Position = new Vector3(0.0f, 1.3f, 6.3f),
        };
        root.AddChild(camera);
        camera.LookAt(Vector3.Zero, Vector3.Up);
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

    private Node3D BuildPlateSurface(PlanetPresentationDocument document, GlobeViewMode viewMode)
    {
        var snapshot = document.GlobeSnapshot!;
        var root = new Node3D
        {
            Name = "PlateSurface",
            Scale = Vector3.One * 2.0f,
        };

        // P1 + W1: view mode selects cap appearance — World (composed product) and HypsometricTerrain
        // (crust diagnostic) both displace by elevation; PlateIdentity is flat. World uses a tuned
        // noise amplitude (sub-cell detail that buries the cell grid) + the WorldTerrainRamp + per-
        // vertex tint jitter; HypsometricTerrain uses the diagnostic crust palette.
        bool isTerrain = viewMode is GlobeViewMode.World or GlobeViewMode.HypsometricTerrain;
        bool isWorld = viewMode == GlobeViewMode.World;

        _plateSurfaces = isWorld
            ? new GlobePlateSurfaces(snapshot, noise: WorldPeaks)
            : new GlobePlateSurfaces(snapshot);

        IReadOnlyList<double> elevations = isTerrain
            ? (document.CellElevations is { } cellElevations && cellElevations.Count == snapshot.CellCount
                ? cellElevations
                : new double[snapshot.CellCount])
            : new double[snapshot.CellCount];

        var caps = isWorld
            ? _plateSurfaces.BuildSurfaces(elevations, exaggeration: WorldHeightScale, heightExponent: WorldHeightExponent)
            : _plateSurfaces.BuildSurfaces(elevations, exaggeration: document.VerticalExaggeration);

        var (perCellColor, perCellEmission) = isTerrain
            ? BuildCellAppearance(snapshot.CellCount, document, isWorld, isWorld ? snapshot.Cells : null)
            : (Array.Empty<Color>(), Array.Empty<float>());

        // Per-vertex color envelope (terrain views): smooth per-cell ramp colours across cell AND
        // plate boundaries so terrain reads as Gouraud-shaded gradients instead of chunky per-cell
        // triangles. Mirrors the elevation envelope in GlobePlateSurfaces — same global shared-vertex
        // topology, component-wise mean — so cross-plate seams show no colour step either.
        var perPlateVertexColors = isTerrain
            ? BuildPerPlateVertexColors(_plateSurfaces!, perCellColor)
            : null;

        var jitter = isWorld ? new VertexTintJitter(seed: 1337, amplitude: 0.06) : null;

        foreach (var cap in caps.OrderBy(c => c.PlateId))
        {
            var plate = isTerrain
                ? BuildPlateMesh(cap, perPlateVertexColors!, perCellEmission, jitter, HypsoPlateMaterialOverride)
                : BuildPlateIdentityMesh(cap, HypsoPlateMaterialOverride);
            root.AddChild(plate);
        }

        return root;
    }

    // Computes per-cell Godot.Color (world or crust ramp with trench/ridge accent baked in) and
    // per-cell volcanic emission intensity, from the document's crust elevation + feature data. The
    // world view uses WorldTerrainRamp (bare-rock product palette) modulated by the continental
    // ProvinceTint (cells indexed by CellId supply the sample direction); the crust diagnostic uses
    // HypsometricTint, un-tinted. Falls back to a neutral mid-ramp tint when crust data is absent.
    private static (Color[] Colors, float[] Emission) BuildCellAppearance(
        int cellCount,
        PlanetPresentationDocument document,
        bool isWorld,
        IReadOnlyList<GlobeCell>? cells)
    {
        var colors = new Color[cellCount];
        var emission = new float[cellCount];

        var elevations = document.CellElevations;
        if (elevations is null || elevations.Count != cellCount)
        {
            var fallbackRamp = isWorld
                ? WorldTerrainRamp.ComputeColors(new double[] { 0.0 })[0]
                : HypsometricTint.ComputeColors(new double[] { 0.0 })[0];
            var fallback = ToColor(fallbackRamp);
            for (int c = 0; c < cellCount; c++) colors[c] = fallback;
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
            colors[c] = ToColor(CrustAccentMapper.Apply(tint, accent));
            emission[c] = (float)accent.VolcanicEmission;
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
        Color[] perCellColor)
    {
        var ramp = new RampColor[perCellColor.Length];
        for (int c = 0; c < perCellColor.Length; c++)
            ramp[c] = new RampColor(perCellColor[c].R, perCellColor[c].G, perCellColor[c].B);

        var perPlate = surfaces.BuildVertexColors(ramp);
        var byId = new Dictionary<int, RampColor[]>(perPlate.Count);
        foreach (var p in perPlate)
            byId[p.PlateId] = p.Colors;
        return byId;
    }

    private static MeshInstance3D BuildPlateMesh(
        PlateCap cap,
        IReadOnlyDictionary<int, RampColor[]> perPlateVertexColors,
        float[] perCellEmission,
        VertexTintJitter? jitter,
        Material plateMaterial)
    {
        var surface = cap.Surface;
        int triCount = surface.TriangleCount;
        int vertCount = triCount * 3;

        // Watertight + smooth: positions come from the shared-vertex GlobeSurface.Positions, so
        // adjacent triangles meet EXACTLY at shared corners (no black cracks), while per-vertex
        // SmoothNormals keep the silhouette round instead of faceted.
        var vertices = new Vector3[vertCount];
        var normals = new Vector3[vertCount];

        for (int t = 0; t < triCount; t++)
        {
            int i0 = surface.Triangles[(t * 3) + 0];
            int i1 = surface.Triangles[(t * 3) + 1];
            int i2 = surface.Triangles[(t * 3) + 2];

            int b = t * 3;
            vertices[b + 0] = ToV3(surface.Positions[i0]);
            vertices[b + 1] = ToV3(surface.Positions[i1]);
            vertices[b + 2] = ToV3(surface.Positions[i2]);
            normals[b + 0] = ToV3(surface.SmoothNormals[i0]);
            normals[b + 1] = ToV3(surface.SmoothNormals[i1]);
            normals[b + 2] = ToV3(surface.SmoothNormals[i2]);
        }

        // Per-vertex smoothed colour envelope: each corner takes its global-vertex mean ramp colour
        // (averaged across all incident cells of all plates), so cell and plate boundaries read as
        // Gouraud gradients instead of hard per-triangle steps. Volcanic emission (UV2.x) stays
        // per-cell — a vent is a property of the cell, not the shared corner. VertexTintJitter is
        // applied on top of the smoothed colour exactly as before, so the cell-grid anti-banding
        // noise still rides the smoothed base.
        var vertexColors = perPlateVertexColors[cap.PlateId];
        var colors = new Color[vertCount];
        var uv2 = new Vector2[vertCount];
        for (int t = 0; t < triCount; t++)
        {
            int cellId = cap.CellIds[t];
            float emis = cellId >= 0 && cellId < perCellEmission.Length
                ? perCellEmission[cellId]
                : 0f;

            int b = t * 3;
            for (int v = 0; v < 3; v++)
            {
                int idx = b + v;
                int surfIdx = surface.Triangles[(t * 3) + v];
                var baseColor = surfIdx >= 0 && surfIdx < vertexColors.Length
                    ? ToColor(vertexColors[surfIdx])
                    : new Color(0.3f, 0.35f, 0.28f);
                colors[idx] = jitter is not null
                    ? ToColor(jitter.Apply(surface.Positions[surfIdx], ToRampColor(baseColor)))
                    : baseColor;
                uv2[idx] = new Vector2(emis, 0f);
            }
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
            Name = $"Plate_{cap.PlateId}",
            Mesh = mesh,
            MaterialOverride = plateMaterial,
        };
    }

    private static RampColor ToRampColor(Color c) => new(c.R, c.G, c.B);

    // P1: plate-identity cap — every vertex in this cap gets the plate's identity color, flat-zero
    // displacement (positions are already at unit-sphere radius from BuildSurfaces with zero
    // elevations), no volcanic emission. Reuses HypsoPlateMaterial: the shader reads COLOR.rgb for
    // albedo and only emits when UV2.x > 0, which is absent here.
    private static MeshInstance3D BuildPlateIdentityMesh(PlateCap cap, Material plateMaterial)
    {
        var surface = cap.Surface;
        int triCount = surface.TriangleCount;
        int vertCount = triCount * 3;

        var vertices = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var colors = new Color[vertCount];

        var plateColor = ToColor(PlateIdentityPalette.ColorFor(cap.PlateId));

        for (int t = 0; t < triCount; t++)
        {
            int i0 = surface.Triangles[(t * 3) + 0];
            int i1 = surface.Triangles[(t * 3) + 1];
            int i2 = surface.Triangles[(t * 3) + 2];

            int b = t * 3;
            vertices[b + 0] = ToV3(surface.Positions[i0]);
            vertices[b + 1] = ToV3(surface.Positions[i1]);
            vertices[b + 2] = ToV3(surface.Positions[i2]);
            normals[b + 0] = ToV3(surface.SmoothNormals[i0]);
            normals[b + 1] = ToV3(surface.SmoothNormals[i1]);
            normals[b + 2] = ToV3(surface.SmoothNormals[i2]);
            colors[b + 0] = plateColor;
            colors[b + 1] = plateColor;
            colors[b + 2] = plateColor;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        return new MeshInstance3D
        {
            Name = $"Plate_{cap.PlateId}",
            Mesh = mesh,
            MaterialOverride = plateMaterial,
        };
    }

    private static Vector3 ToV3(CartesianPoint3 p) => new((float)p.X, (float)p.Y, (float)p.Z);

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
    ALBEDO = COLOR.rgb;
    float vent = UV2.x;
    if (vent > 0.001) {
        EMISSION = u_volcanic_glow.rgb * vent * u_volcanic_energy;
    }
    ROUGHNESS = 0.92;
    METALLIC = 0.0;
}

void light() {
    float ndotl = dot(normalize(NORMAL), normalize(LIGHT));
    float wrap = ndotl * 0.5 + 0.5;
    wrap *= wrap;
    DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR * ATTENUATION * wrap;
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
        _worldRuntimeChangePending = true;
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
        if (_disposed || !_worldRuntimeChangePending || !_resource.IsLoaded(WorldBundleId))
            return;

        _worldRuntimeChangePending = false;
        Callable.From(Rebind).CallDeferred();
    }

    private void ClearActiveRoot()
    {
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
        _statusLabel = null;
        _currentDocument = null;
        _currentViewMode = GlobeViewMode.Inactive;

        if (_cutawayFaceRoot is not null && GodotObject.IsInstanceValid(_cutawayFaceRoot))
        {
            _cutawayFaceRoot.GetParent()?.RemoveChild(_cutawayFaceRoot);
            _cutawayFaceRoot.QueueFree();
        }
        _cutawayFaceRoot = null;
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
        _watch.Dispose();
        _generationSubscription?.Dispose();
        _generationSubscription = null;
        ReleaseNodeGraphView();
        _timelineRegistration.Dispose();
        ClearActiveRoot();
    }
}

internal sealed class PlanetTimelineController : ITimelineController
{
    private readonly Action<long> _applyTick;
    private long _tick;
    private long _maxTick = 1;
    private TimelineLayerSelection? _selectedLayer;
    private Action? _onPlay;
    private Action? _onPause;
    private Action<long>? _onSeek;
    private Func<bool>? _checkPlaying;

    public PlanetTimelineController(Action<long> applyTick)
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

    public TimelineLayerSelection? SelectedLayer => _selectedLayer;

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

    public void SelectLayer(string sphereId, string layerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sphereId);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);

        var next = new TimelineLayerSelection(sphereId, layerId);
        if (Equals(_selectedLayer, next))
            return;

        _selectedLayer = next;
        LayerSelectionChanged?.Invoke(_selectedLayer);
    }

    public void PushTick(long tick)
    {
        _tick = Math.Clamp(tick, 0L, _maxTick);
        _applyTick(_tick);
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
