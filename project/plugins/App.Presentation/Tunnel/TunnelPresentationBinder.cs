using System;
using System.Collections.Generic;
using FantaSim.App.Resource;
using FantaSim.App.Resource.Bundle;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World.Composition;
using Godot;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;
using NumericsVector3 = System.Numerics.Vector3;
using ResourceService = FantaSim.App.Resource.IService;

namespace FantaSim.App.Presentation.Tunnel;

internal sealed partial class TunnelPresentationBinder : ITunnelPresentation
{
    private const string StageBundleId = "stage";
    private const string WorldBundleId = "world";
    private const string TimelineBundleId = "timeline";
    private static readonly NodePath StageEnvironmentPath = new("Environment");

    private const float TunnelRadius = TunnelCameraFraming.TunnelRadius;
    private const float MouthZ = TunnelCameraFraming.MouthZ;
    private const float ThroatZ = TunnelCameraFraming.ThroatZ;
    private const float TunnelDepth = MouthZ - ThroatZ;
    private const float CorridorSurfaceRadius = TunnelRadius - 0.06f;
    private const double CorridorSpanDegrees = 24.0;
    private const float FineRailCenterZ = -TunnelDepth / 2.0f;
    private const float FineRailHalfLength = 2.5f;
    private readonly IRegistry _registry;
    private readonly IBundleSceneRegistry _sceneRegistry;
    private readonly ResourceService _resource;
    private readonly ILogger _log;
    private readonly FilmstripPreviewController _filmstrip;
    private readonly Func<Node3D?> _planetBodyProvider;
    private readonly PlanetPresentationReloadGate _worldRuntimeReload = new();
    private const int StagePreparationRetryLimit = 120;

    private ITimelineController? _ctl;
    private ILayerTrackRegistry? _layerTrackRegistry;
    private Node3D? _mount;
    private TunnelInputRelay? _inputRelay;
    private bool _enabled;
    private bool _builtOnce;
    private bool _tearingDown;
    private bool _disposed;
    private int _generation;
    private SceneTree? _stagePreparationRetryTree;
    private Callable? _stagePreparationRetryCallable;
    private int _stagePreparationRetryFrames;
    private bool _stagePreparationRetryExhaustedLogged;

    private int _focusIndex = -1;
    private IReadOnlyList<LayerTrackDescriptor> _sourceTracks = Array.Empty<LayerTrackDescriptor>();

    private double _lastPointerAngleDeg;
    private TunnelFineTrackBinding _fineBinding;
    private TunnelFinePreview _finePreview;
    private bool _pendingCorridorRebuild;

    // Planet zoom state is owned by the Godot-free input policy: scale is captured on effective
    // enable and restored on disable/teardown so the globe view never inherits a tunnel zoom.

    public TunnelPresentationBinder(
        IRegistry registry,
        IBundleSceneRegistry sceneRegistry,
        ILoggerFactory loggerFactory,
        Func<Node3D?> planetBodyProvider)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sceneRegistry = sceneRegistry ?? throw new ArgumentNullException(nameof(sceneRegistry));
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));
        _planetBodyProvider = planetBodyProvider ?? throw new ArgumentNullException(nameof(planetBodyProvider));

        _log = loggerFactory.CreateLogger("World.TunnelPresentation");
        _resource = _registry.Get<ResourceService>();

        _fineBinding = TunnelFinePreviewMapper.Bind(null, false, TunnelScrubMapper.ResolveOuterRung());
        _finePreview = TunnelFinePreviewMapper.Reset(_fineBinding, FineRailCenterZ, FineRailHalfLength);

        _filmstrip = new FilmstripPreviewController(
            isFaceAlive: () => _mount is not null && GodotObject.IsInstanceValid(_mount) && _mount.IsInsideTree(),
            deferToMainThread: action => Callable.From(action).CallDeferred(),
            log: _log);

        _resource.RuntimeChanging += OnResourceRuntimeChanging;
        _resource.RuntimeChanged += OnResourceRuntimeChanged;
    }

    public bool IsEnabled => _enabled;

    public TunnelZoomResult TrySetZoom(int direction)
    {
        if (!_enabled)
            return new TunnelZoomResult(false, _inputPolicy.CurrentZoomScale, "tunnel mode not effective");

        var result = _inputPolicy.HandleWheel(direction);
        if (result.AdjustZoom)
            ApplyPlanetZoom();

        return new TunnelZoomResult(true, _inputPolicy.CurrentZoomScale, string.Empty);
    }

    public void Rebind()
    {
        if (_disposed || _tearingDown)
            return;

        // Rebind advances the mount generation even when the controller instance is unchanged.
        // Relinquish any press ownership before resetting released fine inspection so neither an
        // outer commit nor inner accumulated motion can span the lifecycle boundary.
        CancelTunnelGesture("rebind");
        ResetFinePreview(TunnelFineResetReason.BaseTimeChanged);
        var controller = _registry.TryGet<ITimelineController>();
        if (controller is null)
        {
            _generation++;
            DisconnectStagePreparationRetry();
            FailSafeDisable("controller_lost", TunnelFineResetReason.ControllerLost);
            UnsubscribeController();
            UnsubscribeLayerTrackRegistry();
            SeverFilmstrip();
            _sourceTracks = Array.Empty<LayerTrackDescriptor>();
            _focusIndex = -1;
            _fineBinding = TunnelFinePreviewMapper.Bind(
                null,
                false,
                TunnelScrubMapper.ResolveOuterRung());
            _finePreview = TunnelFinePreviewMapper.Reset(
                _fineBinding,
                FineRailCenterZ,
                FineRailHalfLength);
            if (_mount is not null && GodotObject.IsInstanceValid(_mount))
            {
                _mount.Visible = false;
                RestorePreviousCamera();
            }
            _log.LogWarning("Tunnel presentation skipped: ITimelineController is not registered.");
            return;
        }

        BindController(controller);
        BindLayerTrackRegistry(_registry.TryGet<ILayerTrackRegistry>());
        _filmstrip.SetPreviewProvider((request, ct) =>
            _registry.TryGet<FantaSim.App.World.IService>()?.GetLayerFilmstripPreview(request, ct));

        var expectedGeneration = ++_generation;
        DisconnectStagePreparationRetry();
        _stagePreparationRetryFrames = 0;
        _stagePreparationRetryExhaustedLogged = false;
        Callable.From(() => EnsureMounted(expectedGeneration)).CallDeferred();
    }

    public TunnelActivationResult TrySetEnabled(bool enabled)
    {
        if (_disposed)
            return new TunnelActivationResult(enabled, false, "tunnel presentation disposed");

        if (!enabled)
        {
            FailSafeDisable("disabled", TunnelFineResetReason.Disabled);
            return new TunnelActivationResult(false, false, string.Empty);
        }

        if (_tearingDown)
            return new TunnelActivationResult(true, false, "tunnel presentation reloading");

        var body = _planetBodyProvider();
        var reason = TunnelActivationPolicy.FailureReason(new TunnelActivationReadiness(
            BinderAvailable: !_tearingDown,
            WorldLoaded: _resource.IsLoaded(WorldBundleId),
            StageLoaded: _resource.IsLoaded(StageBundleId),
            HasController: _ctl is not null,
            HasMount: _mount is not null && GodotObject.IsInstanceValid(_mount) && _mount.IsInsideTree(),
            HasCamera: _tunnelCamera is not null && GodotObject.IsInstanceValid(_tunnelCamera) && _tunnelCamera.IsInsideTree(),
            HasPlanetBody: body is not null && GodotObject.IsInstanceValid(body) && body.IsInsideTree()));
        if (!string.IsNullOrEmpty(reason))
        {
            FailSafeDisable("activation_failed", TunnelFineResetReason.Disabled);
            // Preparation is independent from this failed request. It always mounts hidden and
            // never records latent enable intent; the user must issue a later explicit command.
            if (!_tearingDown)
                Rebind();
            return new TunnelActivationResult(true, false, reason);
        }

        _mount!.Visible = true;
        ActivateTunnelCamera();
        if (_tunnelCamera is null || !GodotObject.IsInstanceValid(_tunnelCamera) || !_tunnelCamera.Current)
        {
            FailSafeDisable("camera_activation_failed", TunnelFineResetReason.Disabled);
            return new TunnelActivationResult(true, false, "tunnel camera activation failed");
        }
        _enabled = true;
        CapturePlanetZoom();
        if (_builtOnce && _ctl is not null)
        {
            var outsideRequestWindow = !_hasRequestedFrameWindow
                || _ctl.Tick < _requestedFrameStartTick
                || _ctl.Tick > _requestedFrameEndTick;
            RefreshTunnelForBaseTick(_ctl.Tick, outsideRequestWindow);
        }
        TryBuildOnce();
        return new TunnelActivationResult(true, true, string.Empty);
    }

    private void FailSafeDisable(string reason, TunnelFineResetReason resetReason)
    {
        _enabled = false;
        _pendingCorridorRebuild = false;
        CancelTunnelGesture(reason);
        ResetFinePreview(resetReason);
        CancelF9CommandWork(reason);
        _filmstrip.Supersede();
        _filmstrip.CancelInFlight();
        RestorePlanetZoom();
        _inputPolicy.OnTunnelDisabled();
        if (_mount is not null && GodotObject.IsInstanceValid(_mount))
            _mount.Visible = false;
        RestorePreviousCamera();
    }

    private void TryBuildOnce()
    {
        if (_builtOnce || !_enabled || _mount is null || !GodotObject.IsInstanceValid(_mount))
            return;

        _builtOnce = true;
        ResolveSourceTracks();
        UpdateInnerBinding(_generation);
        RebuildTwoRingControls();
        RebuildCorridors();
        UpdateRingLabels();
    }

    private void EnsureMounted(int expectedGeneration)
    {
        if (_disposed || _tearingDown || expectedGeneration != _generation)
            return;

        var stageEnvironment = _sceneRegistry.GetNodeOrNull(StageBundleId, StageEnvironmentPath) as Node3D;
        var body = _planetBodyProvider();
        var action = TunnelStagePreparationPolicy.Decide(new TunnelStagePreparationReadiness(
            ExpectedGeneration: expectedGeneration,
            CurrentGeneration: _generation,
            BinderAlive: !_disposed && !_tearingDown,
            WorldLoaded: _resource.IsLoaded(WorldBundleId),
            StageLoaded: _resource.IsLoaded(StageBundleId),
            HasEnvironment: stageEnvironment is not null
                && GodotObject.IsInstanceValid(stageEnvironment)
                && stageEnvironment.IsInsideTree(),
            HasValidPlanetBody: body is not null && GodotObject.IsInstanceValid(body),
            PlanetBodyInsideTree: body is not null
                && GodotObject.IsInstanceValid(body)
                && body.IsInsideTree()));

        if (action == TunnelStagePreparationAction.Ignore)
            return;
        if (action == TunnelStagePreparationAction.RetryNextFrame)
        {
            if (_mount is not null)
                CleanupDetachedMount(DetachMountState());
            ScheduleStagePreparationRetry(expectedGeneration, stageEnvironment);
            return;
        }

        DisconnectStagePreparationRetry();

        if (_mount is null || !GodotObject.IsInstanceValid(_mount) || !_mount.IsInsideTree())
        {
            if (_mount is not null)
                CleanupDetachedMount(DetachMountState());
            _mount = new Node3D { Name = "TunnelMount", Visible = false };
            stageEnvironment!.AddChild(_mount);
        }
        else
        {
            _mount.Visible = false;
        }

        if (!TryAlignToPlanetBody(body!, expectedGeneration))
        {
            CleanupDetachedMount(DetachMountState());
            ScheduleStagePreparationRetry(expectedGeneration, stageEnvironment);
            return;
        }

        if (_inputRelay is null || !GodotObject.IsInstanceValid(_inputRelay))
        {
            _inputRelay = new TunnelInputRelay
            {
                Name = "TunnelInputRelay",
                OnInput = e => HandleInputEvent(e),
                OnProcess = d => ConsumeTunnelFrame(d),
                OnCancel = r => CancelTunnelGesture(r),
                OnError = ex => _log.LogError(ex, "Tunnel input handler threw; gesture cancelled and event consumed."),
            };
            _mount.AddChild(_inputRelay);
        }

        EnsureTunnelCamera();
        BuildDarkShell();

        var shell = _mount.GetNodeOrNull<MeshInstance3D>("DarkShell/Shell");
        if (_tunnelCamera is null || !GodotObject.IsInstanceValid(_tunnelCamera)
            || !_tunnelCamera.IsInsideTree()
            || _inputRelay is null || !GodotObject.IsInstanceValid(_inputRelay)
            || !_inputRelay.IsInsideTree()
            || shell is null || !GodotObject.IsInstanceValid(shell) || !shell.IsInsideTree())
        {
            CleanupDetachedMount(DetachMountState());
            ScheduleStagePreparationRetry(expectedGeneration, stageEnvironment);
            return;
        }

        _enabled = false;
        _mount.Visible = false;
        _stagePreparationRetryFrames = 0;
        _stagePreparationRetryExhaustedLogged = false;
        _log.LogInformation("Tunnel presentation prepared hidden under stage Environment.");
        _worldRuntimeReload.MarkMounted();
    }

    private bool TryAlignToPlanetBody(Node3D body, int gen)
    {
        if (_disposed || _tearingDown || gen != _generation
            || _mount is null || !GodotObject.IsInstanceValid(_mount) || !_mount.IsInsideTree()
            || !GodotObject.IsInstanceValid(body) || !body.IsInsideTree())
            return false;

        // Mount translation makes the bound planet's real center land on the one canonical current
        // plane in tunnel-local space; corridor samples consume the same framing constant.
        _mount.GlobalPosition = body.GlobalPosition
            + Vector3.Back * -TunnelCameraFraming.CurrentPlaneZ;
        _mount.GlobalBasis = Basis.Identity;
        ApplyPlanetZoom();
        return true;
    }

    private void CapturePlanetZoom()
    {
        var body = _planetBodyProvider();
        if (body is null || !GodotObject.IsInstanceValid(body))
            return;

        // Effective enable is the only moment the shared planet body's original scale may be
        // captured. Hidden preparation (mount created but _enabled == false) never calls this, so
        // a stale snapshot cannot survive across globe/tunnel mode switches.
        _inputPolicy.OnTunnelEnabled(new NumericsVector3(body.Scale.X, body.Scale.Y, body.Scale.Z));
    }

    // Scroll-wheel step (direction > 0 = larger planet). Multiplicative, clamped in the seam helper.
    internal void AdjustPlanetZoom(int direction)
    {
        HandleWheel(direction);
    }

    private void ApplyPlanetZoom()
    {
        var body = _planetBodyProvider();
        if (body is null || !GodotObject.IsInstanceValid(body))
            return;

        var originalScale = _inputPolicy.OriginalScale;
        if (!originalScale.HasValue)
            return;

        var zoom = _inputPolicy.CurrentZoomScale;
        body.Scale = new Vector3(
            originalScale.Value.X * zoom,
            originalScale.Value.Y * zoom,
            originalScale.Value.Z * zoom);
    }

    private void RestorePlanetZoom()
    {
        var body = _planetBodyProvider();
        var originalScale = _inputPolicy.OriginalScale;
        if (originalScale.HasValue
            && body is not null && GodotObject.IsInstanceValid(body))
        {
            body.Scale = new Vector3(
                originalScale.Value.X,
                originalScale.Value.Y,
                originalScale.Value.Z);
        }
    }

    private void ScheduleStagePreparationRetry(int expectedGeneration, Node3D? stageEnvironment)
    {
        if (_disposed || _tearingDown || expectedGeneration != _generation)
            return;
        if (_stagePreparationRetryCallable.HasValue)
            return;
        if (_stagePreparationRetryFrames >= StagePreparationRetryLimit)
        {
            if (!_stagePreparationRetryExhaustedLogged)
            {
                _stagePreparationRetryExhaustedLogged = true;
                _log.LogWarning(
                    "Tunnel preparation remained pending after {FrameCount} process frames; waiting for a later resource change or explicit activation.",
                    StagePreparationRetryLimit);
            }
            return;
        }

        var tree = stageEnvironment?.GetTree()
            ?? _sceneRegistry.GetSceneOrNull(StageBundleId)?.GetTree()
            ?? Engine.GetMainLoop() as SceneTree;
        if (tree is null || !GodotObject.IsInstanceValid(tree))
            return;

        _stagePreparationRetryFrames++;
        var callable = Callable.From(() => OnStagePreparationProcessFrame(expectedGeneration));
        _stagePreparationRetryTree = tree;
        _stagePreparationRetryCallable = callable;
        tree.Connect(
            SceneTree.SignalName.ProcessFrame,
            callable,
            (uint)GodotObject.ConnectFlags.OneShot);
    }

    private void OnStagePreparationProcessFrame(int expectedGeneration)
    {
        _stagePreparationRetryTree = null;
        _stagePreparationRetryCallable = null;
        EnsureMounted(expectedGeneration);
    }

    private void DisconnectStagePreparationRetry()
    {
        var tree = _stagePreparationRetryTree;
        var callable = _stagePreparationRetryCallable;
        _stagePreparationRetryTree = null;
        _stagePreparationRetryCallable = null;
        if (tree is null || !GodotObject.IsInstanceValid(tree) || !callable.HasValue)
            return;
        if (tree.IsConnected(SceneTree.SignalName.ProcessFrame, callable.Value))
            tree.Disconnect(SceneTree.SignalName.ProcessFrame, callable.Value);
    }

    private void ResolveSourceTracks()
    {
        if (_layerTrackRegistry is null)
        {
            _sourceTracks = Array.Empty<LayerTrackDescriptor>();
            _focusIndex = -1;
            return;
        }

        var previousFocused = TunnelCorridorLayout.ResolveFocusedTrack(_sourceTracks, _focusIndex);
        var nextTracks = TunnelCorridorLayout.SelectSourceTracks(_layerTrackRegistry.Current);
        _sourceTracks = nextTracks;
        if (nextTracks.Count == 0)
        {
            _focusIndex = -1;
            return;
        }

        if (previousFocused is not null)
        {
            for (var i = 0; i < nextTracks.Count; i++)
            {
                var candidate = nextTracks[i];
                if (candidate.SphereId == previousFocused.SphereId && candidate.LayerId == previousFocused.LayerId)
                {
                    _focusIndex = i;
                    return;
                }
            }
        }

        var activityFlags = new bool[nextTracks.Count];
        if (_ctl is not null)
        {
            for (var i = 0; i < nextTracks.Count; i++)
            {
                activityFlags[i] = TunnelTrackActivity.IsActive(
                    nextTracks[i],
                    _ctl.Tick,
                    _ctl.GeosphereSchedule,
                    _ctl.AtmosphereSchedule);
            }
        }

        // Only an absent focused identity selects from activity. A retained identity stays locked
        // through later tick/activity changes so playback cannot auto-jump the user's wall focus.
        _focusIndex = TunnelCorridorLayout.InitialFocusIndex(nextTracks, activityFlags);
    }

    private void BindController(ITimelineController? controller)
    {
        if (ReferenceEquals(_ctl, controller))
            return;

        if (_ctl is not null)
            CancelTunnelGesture("controller_replaced");
        UnsubscribeController();
        _ctl = controller;
        if (_ctl is not null)
            _ctl.TickChanged += OnTickChanged;
    }

    private void UnsubscribeController()
    {
        if (_ctl is not null)
            _ctl.TickChanged -= OnTickChanged;
        _ctl = null;
    }

    private void BindLayerTrackRegistry(ILayerTrackRegistry? registry)
    {
        if (ReferenceEquals(_layerTrackRegistry, registry))
            return;

        if (_layerTrackRegistry is not null)
            CancelTunnelGesture("registry_replaced");
        UnsubscribeLayerTrackRegistry();
        _layerTrackRegistry = registry;
        if (_layerTrackRegistry is not null)
            _layerTrackRegistry.Changed += OnRegistryChanged;
    }

    private void UnsubscribeLayerTrackRegistry()
    {
        if (_layerTrackRegistry is not null)
            _layerTrackRegistry.Changed -= OnRegistryChanged;
        _layerTrackRegistry = null;
    }

    private void OnResourceRuntimeChanging(object? sender, ResourceRuntimeChangingEventArgs args)
    {
        var timelineChanging = string.Equals(args.BundleId, TimelineBundleId, StringComparison.OrdinalIgnoreCase);
        var worldChanging = string.Equals(args.BundleId, WorldBundleId, StringComparison.OrdinalIgnoreCase);
        var stageChanging = string.Equals(args.BundleId, StageBundleId, StringComparison.OrdinalIgnoreCase);
        if (!timelineChanging && !worldChanging && !stageChanging)
            return;

        TunnelRuntimeChangeThreadGate.Run(
            isMainThread: static () => OS.GetThreadCallerId() == OS.GetMainThreadId(),
            deferToMainThread: action => Callable.From(action).CallDeferred(),
            applyOnMainThread: () => ApplyResourceRuntimeChangingOnMainThread(
                args,
                timelineChanging,
                worldChanging,
                stageChanging));
    }

    private void ApplyResourceRuntimeChangingOnMainThread(
        ResourceRuntimeChangingEventArgs args,
        bool timelineChanging,
        bool worldChanging,
        bool stageChanging)
    {
        if (timelineChanging)
        {
            // The tunnel and world geometry remain live across a timeline-only reload. Relinquish
            // the current gesture at the lifecycle boundary and drop command work that captured
            // the outgoing timeline bundle's service, without disabling the effective tunnel.
            CancelTunnelGesture("timeline_reload");
            CancelF9CommandWork("timeline_reload");
            ResetF9ModeEpoch();
            return;
        }

        _tearingDown = true;
        _generation++;
        var teardownReason = stageChanging ? "stage_teardown" : "world_teardown";
        var lossEvent = stageChanging ? TunnelModeEvent.StageChanging : TunnelModeEvent.WorldChanging;
        var modeOwner = _registry.TryGet<ITunnelModeOwner>();
        TunnelLossSequence.Run(modeOwner, lossEvent, teardownOnMainThread: () =>
        {
            DisconnectStagePreparationRetry();
            FailSafeDisable(teardownReason, TunnelFineResetReason.BundleTeardown);
            SeverManagedInputCallbacks();
            if (worldChanging)
            {
                UnsubscribeController();
                UnsubscribeLayerTrackRegistry();
                SeverFilmstrip();
                _sourceTracks = Array.Empty<LayerTrackDescriptor>();
                _focusIndex = -1;
            }
            _worldRuntimeReload.MarkRuntimeChanging();
            CleanupDetachedMount(DetachMountState());
            _log.LogInformation(
                "Tunnel presentation released before resource {Operation}: {BundleId}",
                args.Operation,
                args.BundleId);
        });
    }

    private void OnResourceRuntimeChanged(object? sender, EventArgs args)
    {
        if (_disposed || !_worldRuntimeReload.TryScheduleDeferredAttempt())
            return;

        var expectedGeneration = _generation;
        Callable.From(() => TryRebindAfterWorldRuntimeChange(expectedGeneration)).CallDeferred();
    }

    private void TryRebindAfterWorldRuntimeChange(int expectedGeneration)
    {
        var runtimeChangeInProgress = _resource.IsRuntimeChangeInProgress(WorldBundleId)
            || _resource.IsRuntimeChangeInProgress(StageBundleId);
        if (!_worldRuntimeReload.CompleteDeferredAttempt(runtimeChangeInProgress)
            || _disposed
            || expectedGeneration != _generation
            || !_worldRuntimeReload.IsPending
            || !_resource.IsLoaded(WorldBundleId)
            || !_resource.IsLoaded(StageBundleId))
            return;

        _tearingDown = false;
        Rebind();
    }

    private void SeverFilmstrip()
    {
        _filmstrip.Supersede();
        _filmstrip.SetPreviewProvider(null);
        _filmstrip.CancelInFlight();
    }

    private void SeverManagedInputCallbacks()
    {
        if (_inputRelay is not null && GodotObject.IsInstanceValid(_inputRelay))
        {
            _inputRelay.SetProcessInput(false);
            _inputRelay.SetProcess(false);
            _inputRelay.OnInput = null;
            _inputRelay.OnProcess = null;
            _inputRelay.OnCancel = null;
            _inputRelay.OnError = null;
        }
    }

    private Node3D? DetachMountState()
    {
        var detached = _mount;
        ClearRingRoots();
        ClearCorridorsRoot();
        _mount = null;
        _inputRelay = null;
        _tunnelCamera = null;
        _previousCamera = null;
        _builtOnce = false;
        return detached;
    }

    private static void CleanupDetachedMount(Node3D? mount)
    {
        if (mount is null || !GodotObject.IsInstanceValid(mount))
            return;

        mount.GetParent()?.RemoveChild(mount);
        mount.QueueFree();
    }

    private void ClearMount()
    {
        RestorePreviousCamera();
        var detached = DetachMountState();
        CleanupDetachedMount(detached);
    }

    private static string SafeNodeName(string value)
    {
        var chars = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            chars[i] = char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_';
        }

        var name = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(name) ? "Track" : name;
    }

    private static Vector3 CalculateNormal(Vector3 a, Vector3 b, Vector3 c)
        => (b - a).Cross(c - a).Normalized();

    private static ArrayMesh? BuildPlanarAnnulusSectorMesh(
        double startAngleDeg, double spanAngleDeg,
        float innerRadius, float outerRadius, float z,
        double angularStepDeg = 3.0)
    {
        if (spanAngleDeg <= 0.0 || outerRadius <= innerRadius)
            return null;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normal = new Vector3(0f, 0f, 1f);

        var steps = Math.Max(1, (int)Math.Ceiling(spanAngleDeg / angularStepDeg));
        var stepDeg = spanAngleDeg / steps;

        for (var i = 0; i < steps; i++)
        {
            var a0 = Mathf.DegToRad((float)(startAngleDeg + (stepDeg * i)));
            var a1 = Mathf.DegToRad((float)(startAngleDeg + (stepDeg * (i + 1))));

            var inner0 = new Vector3(Mathf.Cos(a0) * innerRadius, Mathf.Sin(a0) * innerRadius, z);
            var outer0 = new Vector3(Mathf.Cos(a0) * outerRadius, Mathf.Sin(a0) * outerRadius, z);
            var inner1 = new Vector3(Mathf.Cos(a1) * innerRadius, Mathf.Sin(a1) * innerRadius, z);
            var outer1 = new Vector3(Mathf.Cos(a1) * outerRadius, Mathf.Sin(a1) * outerRadius, z);

            vertices.Add(inner0); vertices.Add(outer0); vertices.Add(outer1);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(1f, 1f));

            vertices.Add(inner0); vertices.Add(outer1); vertices.Add(inner1);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
        }

        return BuildMeshFromArrays(vertices, normals, uvs);
    }

    private static ArrayMesh? BuildCylinderSectorMesh(
        double startAngleDeg, double spanAngleDeg,
        float radius, float nearZ, float farZ,
        double angularStepDeg = 3.0)
    {
        if (spanAngleDeg <= 0.0 || Math.Abs(farZ - nearZ) < 0.001f)
            return null;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();

        var steps = Math.Max(1, (int)Math.Ceiling(spanAngleDeg / angularStepDeg));
        var stepDeg = spanAngleDeg / steps;

        for (var i = 0; i < steps; i++)
        {
            var a0 = Mathf.DegToRad((float)(startAngleDeg + (stepDeg * i)));
            var a1 = Mathf.DegToRad((float)(startAngleDeg + (stepDeg * (i + 1))));

            var n0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f);
            var n1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);

            var near0 = new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, nearZ);
            var near1 = new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, nearZ);
            var far0 = new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, farZ);
            var far1 = new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, farZ);

            // Inward-facing normals point toward the axis (negate the outward radial).
            var inN0 = -n0;
            var inN1 = -n1;

            vertices.Add(near0); vertices.Add(far0); vertices.Add(far1);
            normals.Add(inN0); normals.Add(inN0); normals.Add(inN1);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f));

            vertices.Add(near0); vertices.Add(far1); vertices.Add(near1);
            normals.Add(inN0); normals.Add(inN1); normals.Add(inN1);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f));
        }

        return BuildMeshFromArrays(vertices, normals, uvs);
    }

    private void BuildDarkShell()
    {
        if (_mount is null)
            return;

        var shellRoot = _mount.GetNodeOrNull<Node3D>("DarkShell");
        if (shellRoot is not null)
            return;

        shellRoot = new Node3D { Name = "DarkShell" };
        _mount.AddChild(shellRoot);

        var bands = TunnelShellDepthPolicy.Plan(MouthZ, ThroatZ);
        for (var index = 0; index < bands.Count; index++)
        {
            var band = bands[index];
            var shellMesh = BuildCylinderSectorMesh(
                0.0,
                360.0,
                TunnelRadius,
                band.NearZ,
                band.FarZ,
                angularStepDeg: 6.0);
            if (shellMesh is null)
                continue;

            shellRoot.AddChild(new MeshInstance3D
            {
                // EnsureMounted's validity gate resolves the first band by this stable path.
                Name = index == 0 ? "Shell" : $"ShellDepth{index}",
                Mesh = shellMesh,
                MaterialOverride = BuildUnlitMaterial(new Color(
                    band.Value * 0.82f,
                    band.Value * 0.91f,
                    band.Value)),
            });
        }
    }

    private static ArrayMesh BuildMeshFromArrays(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static StandardMaterial3D BuildUnlitMaterial(Color color, float alpha = 1f)
    {
        var tinted = new Color(color.R, color.G, color.B, alpha);
        return new StandardMaterial3D
        {
            AlbedoColor = tinted,
            Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            EmissionEnabled = true,
            Emission = tinted,
            EmissionEnergyMultiplier = 0.6f,
            NoDepthTest = false,
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _tearingDown = true;
        _generation++;
        DisconnectStagePreparationRetry();
        FailSafeDisable("disposed", TunnelFineResetReason.Disposed);
        _disposed = true;
        _resource.RuntimeChanging -= OnResourceRuntimeChanging;
        _resource.RuntimeChanged -= OnResourceRuntimeChanged;
        SeverManagedInputCallbacks();
        UnsubscribeController();
        UnsubscribeLayerTrackRegistry();
        SeverFilmstrip();
        _filmstrip.DisposeCache();
        _filmstrip.Dispose();
        ClearMount();
    }
}
