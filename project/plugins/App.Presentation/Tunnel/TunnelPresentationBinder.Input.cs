using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using Godot;
using Microsoft.Extensions.Logging;
using CommandService = FantaSim.App.Command.IService;

namespace FantaSim.App.Presentation.Tunnel;

internal readonly record struct TunnelInstrumentPoint3(double X, double Y, double Z);

/// <summary>
/// Pure camera-local picking seam. Godot supplies world-space ProjectRay* endpoints; production
/// transforms them through InstrumentRoot.GlobalTransform.AffineInverse before calling this policy.
/// </summary>
internal static class TunnelInstrumentHitPolicy
{
    private const double ParallelEpsilon = 1e-9;

    internal static bool TryIntersectPlane(
        TunnelInstrumentPoint3 origin,
        TunnelInstrumentPoint3 end,
        out TunnelInstrumentPoint3 point)
    {
        point = default;
        var directionX = end.X - origin.X;
        var directionY = end.Y - origin.Y;
        var directionZ = end.Z - origin.Z;
        if (Math.Abs(directionZ) <= ParallelEpsilon)
            return false;

        var t = (TunnelInstrumentContract.GeometryPlaneZ - origin.Z) / directionZ;
        if (t < 0d)
            return false;

        point = new TunnelInstrumentPoint3(
            origin.X + (directionX * t),
            origin.Y + (directionY * t),
            origin.Z + (directionZ * t));
        return true;
    }

    internal static bool IsInBand(TunnelInstrumentPoint3 point, float innerRadius, float outerRadius)
    {
        if (innerRadius < 0f || outerRadius < innerRadius)
            return false;

        var radiusSquared = (point.X * point.X) + (point.Y * point.Y);
        return radiusSquared >= innerRadius * innerRadius
            && radiusSquared <= outerRadius * outerRadius;
    }
}

internal sealed partial class TunnelPresentationBinder
{
    // Mirrors TimelinePlugin.TunnelViewCommandId (project/plugins/App.Timeline/TimelinePlugin.cs).
    // Duplicated rather than referenced: that const is internal to the timeline bundle, a
    // different collectible ALC than this world-bundle binder.
    private const string TunnelViewCommandId = "timeline.tunnel_view";

    private readonly TunnelGestureCoordinator _coordinator = new();
    private bool _gestureOwned;
    private bool _applyingOuterScrubAction;
    private long _gesturePressTick;
    private int _gestureFocusBefore = -1;
    private long _fineEpoch;
    private long? _fineBucket;
    private bool _fineInspectionActive;
    private readonly object _f9CommandGate = new();
    private CancellationTokenSource? _f9CommandCts;
    private long _lastF9ModeEpoch = -1L;

    /// <summary>
    /// Rechecks binder-owned lifecycle and truth state at the actual texture write. This wraps both
    /// the controller's synchronous cache-hit path and its deferred provider-completion path.
    /// </summary>
    private sealed class GuardedFineInspectionSink : IFilmstripFrameSink
    {
        private readonly TunnelPresentationBinder _owner;
        private readonly SnapshotSphereFilmstripSink _inner;
        private readonly int _generation;
        private readonly long _epoch;
        private readonly long _bucket;
        private readonly int _graphRevision;
        private readonly string _sphereId;
        private readonly string _layerId;

        internal GuardedFineInspectionSink(
            TunnelPresentationBinder owner,
            SnapshotSphereFilmstripSink inner,
            LayerFilmstripPreviewRequest request,
            int generation,
            long epoch,
            long bucket)
        {
            _owner = owner;
            _inner = inner;
            _generation = generation;
            _epoch = epoch;
            _bucket = bucket;
            _graphRevision = request.GraphRevision;
            _sphereId = request.SphereId;
            _layerId = request.LayerId;
        }

        public bool IsAlive
            => _inner.IsAlive
               && _owner.CanApplyFineFrame(
                   _generation,
                   _epoch,
                   _bucket,
                   _graphRevision,
                   _sphereId,
                   _layerId);

        public void SetFrame(FilmstripFramePayload frame)
        {
            if (IsAlive)
                _inner.SetFrame(frame);
        }
    }

    private bool HandleInputEvent(InputEvent @event)
    {
        if (_disposed)
            return false;

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } pressed:
                if (_gestureOwned)
                {
                    // A nested press (a release we never saw) must not fall through and start a
                    // concurrent globe drag. Consume it; the owned gesture still ends on its own
                    // release, focus-loss, or lifecycle cancel.
                    _log.LogDebug("tunnel gesture: nested left press consumed while a gesture is owned.");
                    return true;
                }
                return TryBeginGesture(pressed);
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } released:
                return HandleOwnedRelease(released);
            case InputEventMouseMotion motion when _gestureOwned:
                return HandleOwnedMotion(motion);
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.F9 }:
                RouteTunnelToggleThroughCommand();
                return true;
            default:
                return false;
        }
    }

    // F9 no longer flips this binder directly: every enable path (command, F9) must flow through
    // timeline.tunnel_view so TimelinePlugin can own the 2D HUD visibility that rides along with
    // it (vault/specs/2026-07-12-rotating-tunnel-two-ring-prototype-design.md §4a). Resolved at
    // press time, never cached -- the command service lives in the timeline bundle's collectible
    // ALC and may reload independently of this binder, same discipline as every other
    // _registry.TryGet call here.
    private void RouteTunnelToggleThroughCommand()
    {
        var desired = !IsEnabled;
        var commandService = _registry.TryGet<CommandService>();
        if (commandService is null)
        {
            _log.LogWarning("tunnel F9: command service unavailable; request ignored.");
            return;
        }

        var payload = new JsonObject { ["enabled"] = desired }.ToJsonString();
        var request = new FantaSim.App.Command.CommandRequest(
            Command: TunnelViewCommandId,
            PayloadJson: payload,
            ActorKind: "user",
            ActorId: "godot-f9");

        var cts = ReplaceF9CommandWork();
        var expectedGeneration = _generation;
        // Fire-and-forget: input events run synchronously on the main thread and must not await.
        // The lifecycle CTS is cancelled before any timeline/world/stage ALC can sever.
        _ = RunTunnelToggleCommandAsync(commandService, request, desired, expectedGeneration, cts);
    }

    private async Task RunTunnelToggleCommandAsync(
        CommandService commandService,
        FantaSim.App.Command.CommandRequest request,
        bool desired,
        int expectedGeneration,
        CancellationTokenSource cts)
    {
        try
        {
            var result = await commandService.ExecuteAsync(request, cts.Token).ConfigureAwait(false);
            if (!result.Ok)
            {
                _log.LogWarning(
                    "tunnel F9: {CommandId} reported failure ({Error}); request ignored.",
                    TunnelViewCommandId, result.Error?.Message ?? "unknown error");
                return;
            }

            var responseValid = TryReadTunnelCommandResponse(result.ResultJson, out var responseEpoch, out var effective);
            if (!responseValid)
            {
                _log.LogWarning("tunnel F9: {CommandId} returned malformed JSON; request ignored.", TunnelViewCommandId);
                return;
            }

            lock (_f9CommandGate)
            {
                var completion = new TunnelF9CommandCompletion(
                    ExpectedGeneration: expectedGeneration,
                    CurrentGeneration: _generation,
                    Cancelled: cts.Token.IsCancellationRequested || !ReferenceEquals(_f9CommandCts, cts),
                    TransportOk: result.Ok,
                    ResponseValid: responseValid,
                    ResponseEpoch: responseEpoch,
                    LastAcceptedEpoch: _lastF9ModeEpoch);
                if (_disposed || !TunnelF9CommandPolicy.CanAccept(completion))
                {
                    _log.LogInformation("tunnel F9: stale command completion ignored.");
                    return;
                }
                _lastF9ModeEpoch = responseEpoch;
            }

            _log.LogInformation(
                "tunnel F9: routed through {CommandId} (requested={Requested}, effective={Effective}, modeEpoch={ModeEpoch}).",
                TunnelViewCommandId, desired, effective, responseEpoch);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _log.LogInformation("tunnel F9: {CommandId} cancelled by lifecycle change.", TunnelViewCommandId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "tunnel F9: {CommandId} faulted; request ignored.",
                TunnelViewCommandId);
        }
        finally
        {
            lock (_f9CommandGate)
            {
                if (ReferenceEquals(_f9CommandCts, cts))
                    _f9CommandCts = null;
            }
            cts.Dispose();
        }
    }

    private CancellationTokenSource ReplaceF9CommandWork()
    {
        CancellationTokenSource? outgoing;
        var incoming = new CancellationTokenSource();
        lock (_f9CommandGate)
        {
            outgoing = _f9CommandCts;
            _f9CommandCts = incoming;
        }
        CancelOnly(outgoing);
        return incoming;
    }

    private void CancelF9CommandWork(string reason)
    {
        CancellationTokenSource? outgoing;
        lock (_f9CommandGate)
        {
            outgoing = _f9CommandCts;
            _f9CommandCts = null;
        }
        if (outgoing is not null)
            _log.LogInformation("tunnel F9: command work cancelled ({Reason}).", reason);
        CancelOnly(outgoing);
    }

    private void ResetF9ModeEpoch()
    {
        lock (_f9CommandGate)
            _lastF9ModeEpoch = -1L;
    }

    private void CancelOnly(CancellationTokenSource? source)
    {
        if (source is null)
            return;
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (AggregateException ex)
        {
            _log.LogWarning(ex, "tunnel F9: cancellation callback faulted; lifecycle cancellation still requested.");
        }
    }

    private static bool TryReadTunnelCommandResponse(
        string? resultJson,
        out long modeEpoch,
        out bool effective)
    {
        modeEpoch = -1L;
        effective = false;
        if (string.IsNullOrWhiteSpace(resultJson))
            return false;
        try
        {
            var payload = JsonNode.Parse(resultJson) as JsonObject;
            return payload is not null
                && payload["modeEpoch"] is JsonValue epochValue
                && epochValue.TryGetValue(out modeEpoch)
                && payload["effective"] is JsonValue effectiveValue
                && effectiveValue.TryGetValue(out effective);
        }
        catch
        {
            return false;
        }
    }

    private void ConsumeTunnelFrame(double delta)
    {
        if (_disposed || !_gestureOwned)
            return;

        if (_coordinator.ActiveGesture != TunnelGestureKind.OuterRing)
            return;

        var frame = _coordinator.ConsumeFrame();
        if (frame.ScrubAction.ShouldApply && frame.OuterTick is { } mapping)
            ApplyOuterScrubAction(frame.ScrubAction, mapping);
    }

    private bool TryBeginGesture(InputEventMouseButton press)
    {
        if (!_enabled || _ctl is null)
            return false;

        if (!TryResolveHit(press.Position, out var hit))
            return false;
        if (!TryGetPointerAngle(hit == TunnelHitRegion.Wall, press.Position, out var pointerAngle))
            return false;

        if (hit == TunnelHitRegion.InnerRing && _fineInspectionActive)
            ResetFinePreview(TunnelFineResetReason.BaseTimeChanged);

        var context = BuildPressContext();
        var update = _coordinator.Press(hit, context);
        if (!update.Handled)
            return false;

        _gestureOwned = true;
        _lastPointerAngleDeg = pointerAngle;
        _gesturePressTick = context.CurrentTick;
        _gestureFocusBefore = context.FocusIndex;

        LogOwnership(update.Gesture, press);

        if (update.OuterTick is { } outerMapping)
            UpdateOuterRingVisual(outerMapping);

        if (update.FinePreview is { } preview)
        {
            _finePreview = preview;
            UpdateInnerRingVisual(_fineBinding, preview);
            LogInnerGesture("press", preview);
        }

        if (update.ScrubAction.ShouldApply && update.OuterTick is { } om)
            ApplyOuterScrubAction(update.ScrubAction, om);

        return true;
    }

    private bool HandleOwnedMotion(InputEventMouseMotion motion)
    {
        if (!_gestureOwned)
            return false;

        var aboutTunnelAxis = _coordinator.ActiveGesture == TunnelGestureKind.Wall;
        if (!TryGetPointerAngle(aboutTunnelAxis, motion.Position, out var currentAngle))
            return true;

        var delta = TunnelScrubMapper.NormalizeClockwiseDeltaDegrees(_lastPointerAngleDeg, currentAngle);
        _lastPointerAngleDeg = currentAngle;

        var update = _coordinator.Motion(delta);
        if (!update.Handled)
            return true;

        switch (update.Gesture)
        {
            case TunnelGestureKind.OuterRing:
                if (update.OuterTick is { } outerMapping)
                {
                    UpdateOuterRingVisual(outerMapping);
                    LogOuterMotion(outerMapping);
                }
                break;
            case TunnelGestureKind.InnerRing:
                if (update.FinePreview is { } preview)
                {
                    _finePreview = preview;
                    UpdateInnerRingVisual(_fineBinding, preview);
                    UpdateFineInspection(preview);
                    LogInnerGesture("motion", preview, LogLevel.Debug);
                }
                break;
            case TunnelGestureKind.Wall:
                if (_corridorsRoot is not null && GodotObject.IsInstanceValid(_corridorsRoot))
                    _corridorsRoot.RotationDegrees = new Vector3(0f, 0f, -(float)update.AccumulatedDegrees);
                LogWallMotion(update.AccumulatedDegrees);
                break;
        }

        return true;
    }

    private bool HandleOwnedRelease(InputEventMouseButton release)
    {
        if (!_gestureOwned)
            return false;

        // A release can arrive at a new position without a final motion event. Fold that last
        // pointer segment into the coordinator so the commit/snap/readout reflects the real drop.
        if (TryGetLocalPointerAngle(release.Position, out var releaseAngle))
        {
            var finalDelta = TunnelScrubMapper.NormalizeClockwiseDeltaDegrees(
                _lastPointerAngleDeg,
                releaseAngle);
            _lastPointerAngleDeg = releaseAngle;
            _coordinator.Motion(finalDelta);
        }

        var update = _coordinator.Release();
        _gestureOwned = false;

        if (!update.Handled)
            return true;

        switch (update.Gesture)
        {
            case TunnelGestureKind.OuterRing:
                if (update.OuterTick is { } outerMapping)
                {
                    if (update.ScrubAction.ShouldApply)
                    {
                        ApplyOuterScrubAction(update.ScrubAction, outerMapping);
                        LogOuterGesture("commit", outerMapping, update.ScrubAction.Origin);
                    }
                    UpdateOuterRingVisual(outerMapping);
                }
                break;
            case TunnelGestureKind.InnerRing:
                if (update.FinePreview is { } preview)
                {
                    _finePreview = preview;
                    UpdateInnerRingVisual(_fineBinding, preview);
                    UpdateFineInspection(preview);
                    LogInnerGesture("release", preview);
                }
                break;
            case TunnelGestureKind.Wall:
                if (update.CarouselSnap is { } snap)
                {
                    var focusBefore = _focusIndex;
                    if (_corridorsRoot is not null && GodotObject.IsInstanceValid(_corridorsRoot))
                        _corridorsRoot.RotationDegrees = Vector3.Zero;

                    ResetFinePreview(TunnelFineResetReason.FocusChanged);
                    _focusIndex = snap.FocusIndex;
                    RebuildCorridors();
                    LogWallRelease(focusBefore, snap);
                }
                break;
        }

        return true;
    }

    private TunnelGesturePressContext BuildPressContext()
    {
        return new TunnelGesturePressContext(
            CurrentTick: _ctl?.Tick ?? 0L,
            MaxTick: _ctl?.MaxTick ?? 0L,
            FocusIndex: _focusIndex,
            TrackCount: _sourceTracks.Count,
            FineBinding: _fineBinding,
            FineRailCenterZ: FineRailCenterZ,
            FineRailHalfLength: FineRailHalfLength);
    }

    private bool TryResolveHit(Vector2 screenPosition, out TunnelHitRegion hit)
    {
        hit = TunnelHitRegion.None;
        var camera = _tunnelCamera;
        if (camera is null || !GodotObject.IsInstanceValid(camera) || !camera.Current)
            return false;

        if (!TryProjectToInstrumentPlane(screenPosition, camera, out var instrumentPoint))
            return false;

        var projected = new TunnelInstrumentPoint3(
            instrumentPoint.X,
            instrumentPoint.Y,
            instrumentPoint.Z);
        var outerHit = TunnelInstrumentHitPolicy.IsInBand(
            projected,
            TunnelInstrumentContract.OuterRingInnerRadius,
            TunnelInstrumentContract.OuterRingOuterRadius);
        var innerHit = TunnelInstrumentHitPolicy.IsInBand(
            projected,
            TunnelInstrumentContract.InnerRingInnerRadius,
            TunnelInstrumentContract.InnerRingOuterRadius);
        var wallHit = TryIntersectTunnelWall(screenPosition, camera, out var wallLocal);
        // The opaque planet fills the view center; under the interior camera its rays also strike the
        // wall behind it. Reject a wall pick the planet occludes so clicking the globe never starts a
        // carousel drag. Rings win over the wall regardless (a ring band is nearer than the planet).
        if (wallHit && IsPlanetOccludingWall(screenPosition, camera, wallLocal))
            wallHit = false;

        hit = TunnelScrubMapper.ResolveHitRegion(outerHit, innerHit, wallHit);
        return hit != TunnelHitRegion.None;
    }

    private bool TryBuildMountLocalRay(
        Vector2 screenPosition, Camera3D camera, out TunnelRay3 ray, out TunnelPoint3 origin)
    {
        ray = default;
        origin = default;
        var mount = _mount;
        if (mount is null || !GodotObject.IsInstanceValid(mount))
            return false;

        var inv = mount.GlobalTransform.AffineInverse();
        var localOrigin = inv * camera.ProjectRayOrigin(screenPosition);
        var localDir = inv.Basis * camera.ProjectRayNormal(screenPosition);
        origin = new TunnelPoint3(localOrigin.X, localOrigin.Y, localOrigin.Z);
        ray = new TunnelRay3(origin, new TunnelPoint3(localDir.X, localDir.Y, localDir.Z));
        return true;
    }

    private bool TryIntersectTunnelWall(Vector2 screenPosition, Camera3D camera, out TunnelPoint3 wallLocal)
    {
        wallLocal = default;
        if (!TryBuildMountLocalRay(screenPosition, camera, out var ray, out _))
            return false;

        return TunnelRayHitMapper.TryIntersectCylinder(ray, CorridorSurfaceRadius, ThroatZ, MouthZ, out wallLocal);
    }

    private bool IsPlanetOccludingWall(Vector2 screenPosition, Camera3D camera, TunnelPoint3 wallLocal)
    {
        if (!TryBuildMountLocalRay(screenPosition, camera, out var ray, out var origin))
            return false;

        var center = new TunnelPoint3(0d, 0d, TunnelCameraFraming.CurrentPlaneZ);
        if (!TunnelRayHitMapper.TryIntersectSphere(ray, center, TunnelCameraFraming.PlanetVisualRadius, out var planetLocal))
            return false;

        // Both hits lie on the same ray, so nearer-along-ray reduces to squared distance from origin.
        return SquaredDistance(origin, planetLocal) < SquaredDistance(origin, wallLocal);
    }

    private static double SquaredDistance(TunnelPoint3 a, TunnelPoint3 b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var dz = b.Z - a.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    // Reference plane (mount-local Z, perpendicular to the tunnel axis and centered on it) used to
    // measure the wall carousel's pointer angle. Any axis-perpendicular plane in front of the
    // interior camera yields the angular position ABOUT THE AXIS — which is what the corridors root
    // rotates about — unlike the off-axis instrument dial plane the rings are measured on.
    private const float WallAngleReferenceZ = (MouthZ + ThroatZ) * 0.5f;

    // Outer/inner rings are dials on the off-axis instrument plane; the wall carousel rotates about
    // the tunnel axis. Measuring both with the same instrument-plane angle made a wall drag pivot
    // around the wrong center, so a one-pixel move near the dial singularity could snap the carousel
    // several steps at once. Route each gesture to the angle source that matches its rotation axis.
    private bool TryGetPointerAngle(bool aboutTunnelAxis, Vector2 screenPosition, out double angleDegrees)
        => aboutTunnelAxis
            ? TryGetWallPointerAngle(screenPosition, out angleDegrees)
            : TryGetLocalPointerAngle(screenPosition, out angleDegrees);

    private bool TryGetLocalPointerAngle(Vector2 screenPosition, out double angleDegrees)
    {
        angleDegrees = 0d;
        var camera = _tunnelCamera;
        if (camera is null || !GodotObject.IsInstanceValid(camera) || !camera.Current)
            return false;

        if (!TryProjectToInstrumentPlane(screenPosition, camera, out var localPoint))
            return false;

        angleDegrees = Math.Atan2(localPoint.Y, localPoint.X) * 180.0 / Math.PI;
        return double.IsFinite(angleDegrees);
    }

    private bool TryGetWallPointerAngle(Vector2 screenPosition, out double angleDegrees)
    {
        angleDegrees = 0d;
        var camera = _tunnelCamera;
        var mount = _mount;
        if (camera is null || !GodotObject.IsInstanceValid(camera) || !camera.Current
            || mount is null || !GodotObject.IsInstanceValid(mount))
            return false;

        var inv = mount.GlobalTransform.AffineInverse();
        var localOrigin = inv * camera.ProjectRayOrigin(screenPosition);
        var localDir = inv.Basis * camera.ProjectRayNormal(screenPosition);
        var ray = new TunnelRay3(
            new TunnelPoint3(localOrigin.X, localOrigin.Y, localOrigin.Z),
            new TunnelPoint3(localDir.X, localDir.Y, localDir.Z));

        if (!TunnelRayHitMapper.TryIntersectMouthPlane(ray, WallAngleReferenceZ, out var hit))
            return false;

        angleDegrees = Math.Atan2(hit.Y, hit.X) * 180.0 / Math.PI;
        return double.IsFinite(angleDegrees);
    }

    private bool TryProjectToInstrumentPlane(
        Vector2 screenPosition,
        Camera3D camera,
        out Vector3 instrumentPoint)
    {
        instrumentPoint = Vector3.Zero;
        var instrumentRoot = _instrumentRoot;
        if (instrumentRoot is null || !GodotObject.IsInstanceValid(instrumentRoot))
            return false;

        // Camera3D.ProjectRayOrigin/Normal return a world-space picking ray. Transform both
        // endpoints into the camera-local InstrumentRoot and intersect its Z=0 geometry plane.
        // Source:
        // https://docs.godotengine.org/en/4.7/classes/class_camera3d.html#class-camera3d-method-project-ray-origin
        var rayOriginGlobal = camera.ProjectRayOrigin(screenPosition);
        var rayEndGlobal = rayOriginGlobal + camera.ProjectRayNormal(screenPosition) * 1000f;
        var inverse = instrumentRoot.GlobalTransform.AffineInverse();
        var origin = inverse * rayOriginGlobal;
        var end = inverse * rayEndGlobal;
        if (!TunnelInstrumentHitPolicy.TryIntersectPlane(
                new TunnelInstrumentPoint3(origin.X, origin.Y, origin.Z),
                new TunnelInstrumentPoint3(end.X, end.Y, end.Z),
                out var point))
            return false;

        instrumentPoint = new Vector3((float)point.X, (float)point.Y, (float)point.Z);
        return true;
    }

    private void ApplyOuterScrubAction(TimelineScrubAction action, TunnelOuterTickMapping mapping)
    {
        if (_ctl is null)
            return;

        var tick = Math.Clamp(action.Tick, 0L, Math.Max(0L, _ctl.MaxTick));
        _applyingOuterScrubAction = true;
        try
        {
            _ctl.PushTick(tick, action.Origin);
            // Production deliberately suppresses TickChanged for ScrubPreview, so the tunnel owns
            // its cheap preview refresh. ScrubCommit performs the one supersede/request rebuild.
            RefreshTunnelForBaseTick(
                tick,
                rebuildFrameRequests: action.Origin == TimelineTickOrigin.ScrubCommit);
        }
        finally
        {
            _applyingOuterScrubAction = false;
        }
    }

    private void CancelTunnelGesture(string reason)
    {
        if (!_gestureOwned && _coordinator.ActiveGesture == TunnelGestureKind.None)
            return;

        var kind = _coordinator.ActiveGesture;
        _coordinator.Cancel();
        _gestureOwned = false;

        if (kind != TunnelGestureKind.None)
            _log.LogInformation(
                "tunnel gesture cancelled: kind={GestureKind} reason={Reason}",
                kind, reason);

        if (_corridorsRoot is not null && GodotObject.IsInstanceValid(_corridorsRoot))
            _corridorsRoot.RotationDegrees = Vector3.Zero;
        UpdateOuterRingVisualForTick(_ctl?.Tick ?? 0L);

        ResetFinePreview(TunnelFineResetReason.BaseTimeChanged);
    }

    private void ResetFinePreview(TunnelFineResetReason reason)
    {
        ClearFineInspectionResources();
        var reset = _coordinator.ResetFinePreview(reason, _fineBinding, FineRailCenterZ, FineRailHalfLength);
        if (reset.FinePreview is { } preview)
        {
            _finePreview = preview;
            UpdateInnerRingVisual(_fineBinding, preview);
        }
    }

    private void UpdateFineInspection(TunnelFinePreview preview)
    {
        var descriptor = preview.Binding.Descriptor;
        var rung = preview.Binding.Rung;
        if (!preview.Binding.CanAdjust
            || descriptor is null
            || rung is null
            || preview.RawTickQuantity == 0d)
        {
            ClearFineInspectionResources();
            return;
        }

        _fineInspectionActive = true;
        ApplyFineFrameEmphasis(inspectionActive: true);
        var sample = TunnelFineSamplePolicy.Map(
            _gesturePressTick,
            _ctl?.MaxTick ?? 0L,
            preview.RawTickQuantity,
            descriptor.Content.CadenceTicks,
            _fineBucket);
        if (!sample.TextureChanged)
            return;

        _fineBucket = sample.Bucket;
        var sink = EnsureInspectionLens();
        if (sink is null)
            return;

        var graphRevision = ResolveGraphRevision();
        if (graphRevision is not > 0)
            return;

        var request = new LayerFilmstripPreviewRequest(
            descriptor.SphereId,
            descriptor.LayerId,
            sample.SampleTick,
            rung.Symbol,
            graphRevision.Value,
            TimelineFilmstrip.ThumbnailWidth,
            TimelineFilmstrip.ThumbnailHeight);
        var guardedSink = new GuardedFineInspectionSink(
            this,
            sink,
            request,
            _generation,
            _fineEpoch,
            sample.Bucket);
        _filmstrip.RequestFineTexture(
            request,
            mountGeneration: _generation,
            fineEpoch: _fineEpoch,
            bucket: sample.Bucket,
            guardedSink);
    }

    private bool CanApplyFineFrame(
        int expectedGeneration,
        long expectedEpoch,
        long expectedBucket,
        int expectedGraphRevision,
        string expectedSphereId,
        string expectedLayerId)
    {
        var currentGraphRevision = ResolveGraphRevision();
        var focused = TunnelCorridorLayout.ResolveFocusedTrack(_sourceTracks, _focusIndex);
        return currentGraphRevision is > 0
            && TunnelFineApplyPolicy.CanApply(new TunnelFineApplyReadiness(
                BinderAlive: !_disposed && !_tearingDown && _enabled && _fineInspectionActive,
                ExpectedGeneration: expectedGeneration,
                CurrentGeneration: _generation,
                ExpectedEpoch: expectedEpoch,
                CurrentEpoch: _fineEpoch,
                ExpectedBucket: expectedBucket,
                CurrentBucket: _fineBucket,
                ExpectedGraphRevision: expectedGraphRevision,
                CurrentGraphRevision: currentGraphRevision.Value,
                ExpectedSphereId: expectedSphereId,
                CurrentSphereId: focused?.SphereId,
                ExpectedLayerId: expectedLayerId,
                CurrentLayerId: focused?.LayerId));
    }

    private void ClearFineInspectionResources()
    {
        _fineEpoch = _fineEpoch == long.MaxValue ? long.MaxValue : _fineEpoch + 1L;
        _fineInspectionActive = false;
        _fineBucket = null;
        _filmstrip.CancelFineRequests();
        ApplyFineFrameEmphasis(inspectionActive: false);
        ClearInspectionLens();
    }

    private void ApplyFineFrameEmphasis(bool inspectionActive)
    {
        var focused = TunnelCorridorLayout.ResolveFocusedTrack(_sourceTracks, _focusIndex);
        foreach (var binding in _frameBindings)
        {
            if (binding.MountGeneration != _generation
                || !binding.Material.IsAlive)
                continue;

            var isFocused = focused is not null
                && string.Equals(binding.Descriptor.SphereId, focused.SphereId, StringComparison.Ordinal)
                && string.Equals(binding.Descriptor.LayerId, focused.LayerId, StringComparison.Ordinal);
            var tone = TunnelFineEmphasisPolicy.Resolve(inspectionActive, isFocused);
            binding.Material.SetEmphasis(tone);
        }
    }

    private void LogOwnership(TunnelGestureKind kind, InputEventMouseButton press)
    {
        _log.LogInformation(
            "tunnel gesture ownership: kind={GestureKind} pointer={Pointer} button={Button} handled={Handled}",
            kind, press.Position, press.ButtonIndex, true);
    }

    private void LogOuterMotion(TunnelOuterTickMapping mapping)
    {
        // Per-motion telemetry: Debug only. At Information it emitted hundreds of boxed records/sec
        // during a drag. Press/commit boundaries stay at Information as the gate's evidence trail.
        if (!_log.IsEnabled(LogLevel.Debug))
            return;
        _log.LogDebug(
            "tunnel outer gesture: phase={Phase} pressTick={PressTick} accumulatedDegrees={AccumulatedDegrees} unitSymbol={UnitSymbol} rawTickQuantity={RawTickQuantity} roundedTargetTick={RoundedTargetTick} clampedTargetTick={ClampedTargetTick} origin={Origin}",
            "motion", _gesturePressTick, mapping.AccumulatedDegrees, mapping.Rung.Symbol,
            mapping.RawTickDelta, mapping.RoundedTargetTick, mapping.ClampedTargetTick, TimelineTickOrigin.ScrubPreview);
    }

    private void LogOuterGesture(string phase, TunnelOuterTickMapping mapping, TimelineTickOrigin origin)
    {
        _log.LogInformation(
            "tunnel outer gesture: phase={Phase} pressTick={PressTick} accumulatedDegrees={AccumulatedDegrees} unitSymbol={UnitSymbol} rawTickQuantity={RawTickQuantity} roundedTargetTick={RoundedTargetTick} clampedTargetTick={ClampedTargetTick} origin={Origin}",
            phase, _gesturePressTick, mapping.AccumulatedDegrees, mapping.Rung.Symbol,
            mapping.RawTickDelta, mapping.RoundedTargetTick, mapping.ClampedTargetTick, origin);
    }

    private void LogInnerGesture(string phase, TunnelFinePreview preview, LogLevel level = LogLevel.Information)
    {
        // Boundary phases (press/release) stay at Information as evidence that the inner ring never
        // mutates authoritative time; the per-frame motion phase passes Debug to avoid drag spam.
        if (!_log.IsEnabled(level))
            return;
        var desc = preview.Binding.Descriptor;
        _log.Log(
            level,
            "tunnel inner gesture: phase={Phase} sphereId={SphereId} layerId={LayerId} rung={Rung} active={Active} accumulatedDegrees={AccumulatedDegrees} rawTickQuantity={RawTickQuantity} cursorZ={CursorZ} authoritativeTick={AuthoritativeTick} mutated=false",
            phase,
            desc?.SphereId ?? "",
            desc?.LayerId ?? "",
            preview.Binding.Rung?.Symbol ?? "",
            preview.Binding.IsActive,
            preview.AccumulatedDegrees,
            preview.RawTickQuantity,
            preview.CursorZ,
            _ctl?.Tick ?? 0L);
    }

    private void LogWallMotion(double accumulatedDegrees)
    {
        // Debug only, and the SnapFocus recompute stays behind the level guard so a drag pays
        // nothing for it when Debug is off. The authoritative snap is logged once on release.
        if (!_log.IsEnabled(LogLevel.Debug))
            return;
        var snap = TunnelCorridorLayout.SnapFocus(
            _gestureFocusBefore, _sourceTracks.Count, accumulatedDegrees);
        _log.LogDebug(
            "tunnel wall gesture: phase={Phase} focusBefore={FocusBefore} stepDelta={StepDelta} focusAfter={FocusAfter} snappedDegrees={SnappedDegrees}",
            "motion", _gestureFocusBefore, snap.StepDelta, snap.FocusIndex, snap.SnappedAngleDegrees);
    }

    private void LogWallRelease(int focusBefore, TunnelCorridorLayout.TunnelCarouselSnap snap)
    {
        _log.LogInformation(
            "tunnel wall gesture: phase={Phase} focusBefore={FocusBefore} stepDelta={StepDelta} focusAfter={FocusAfter} snappedDegrees={SnappedDegrees}",
            "release", focusBefore, snap.StepDelta, snap.FocusIndex, snap.SnappedAngleDegrees);
    }
}
