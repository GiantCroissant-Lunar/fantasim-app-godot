using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World.Composition;
using Godot;
using Microsoft.Extensions.Logging;
using CommandService = FantaSim.App.Command.IService;

namespace FantaSim.App.Presentation.Tunnel;

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
    private readonly object _f9CommandGate = new();
    private CancellationTokenSource? _f9CommandCts;
    private long _lastF9ModeEpoch = -1L;

    private bool HandleInputEvent(InputEvent @event)
    {
        if (_disposed)
            return false;

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } pressed:
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
        if (!TryGetLocalPointerAngle(press.Position, out var pointerAngle))
            return false;

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

        if (!TryGetLocalPointerAngle(motion.Position, out var currentAngle))
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
                    LogInnerGesture("motion", preview);
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
                    LogInnerGesture("release", preview);
                }
                break;
            case TunnelGestureKind.Wall:
                if (update.CarouselSnap is { } snap)
                {
                    var focusBefore = _focusIndex;
                    if (_corridorsRoot is not null && GodotObject.IsInstanceValid(_corridorsRoot))
                        _corridorsRoot.RotationDegrees = Vector3.Zero;

                    _focusIndex = snap.FocusIndex;
                    _filmstrip.Supersede();
                    RebuildCorridors();
                    ResetFinePreview(TunnelFineResetReason.FocusChanged);
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
        if (_mount is null || !GodotObject.IsInstanceValid(_mount))
            return false;

        var camera = _inputRelay?.GetViewport()?.GetCamera3D();
        if (camera is null)
            return false;

        var outerHit = TryProjectToRingPlaneBand(screenPosition, camera, OuterRingInnerRadius, OuterRingOuterRadius);
        var innerHit = TryProjectToRingPlaneBand(screenPosition, camera, InnerRingInnerRadius, InnerRingOuterRadius);
        var wallHit = TryIntersectTunnelWall(screenPosition, camera);

        hit = TunnelScrubMapper.ResolveHitRegion(outerHit, innerHit, wallHit);
        return hit != TunnelHitRegion.None;
    }

    // Ring hit-testing happens on the RING plane (design §4a: the dials sit between camera and
    // globe, no longer on the mouth plane) -- the plane here must always match Rings.cs visuals.
    private bool TryProjectToRingPlaneBand(Vector2 screenPosition, Camera3D camera, float innerR, float outerR)
    {
        var mount = _mount;
        if (mount is null || !GodotObject.IsInstanceValid(mount))
            return false;

        var rayOrigin = camera.ProjectRayOrigin(screenPosition);
        var rayDir = camera.ProjectRayNormal(screenPosition);

        var inv = mount.GlobalTransform.AffineInverse();
        var localOrigin = inv * rayOrigin;
        var localDir = inv.Basis * rayDir;

        var ray = new TunnelRay3(
            new TunnelPoint3(localOrigin.X, localOrigin.Y, localOrigin.Z),
            new TunnelPoint3(localDir.X, localDir.Y, localDir.Z));

        if (!TunnelRayHitMapper.TryIntersectMouthPlane(ray, RingPlaneZ, out var point))
            return false;

        var radius = Math.Sqrt(point.X * point.X + point.Y * point.Y);
        return radius >= innerR && radius <= outerR;
    }

    private bool TryIntersectTunnelWall(Vector2 screenPosition, Camera3D camera)
    {
        var mount = _mount;
        if (mount is null || !GodotObject.IsInstanceValid(mount))
            return false;

        var rayOrigin = camera.ProjectRayOrigin(screenPosition);
        var rayDir = camera.ProjectRayNormal(screenPosition);

        var inv = mount.GlobalTransform.AffineInverse();
        var localOrigin = inv * rayOrigin;
        var localDir = inv.Basis * rayDir;

        var ray = new TunnelRay3(
            new TunnelPoint3(localOrigin.X, localOrigin.Y, localOrigin.Z),
            new TunnelPoint3(localDir.X, localDir.Y, localDir.Z));

        return TunnelRayHitMapper.TryIntersectCylinder(ray, CorridorSurfaceRadius, ThroatZ, MouthZ, out _);
    }

    private bool TryGetLocalPointerAngle(Vector2 screenPosition, out double angleDegrees)
    {
        angleDegrees = 0d;
        var camera = _inputRelay?.GetViewport()?.GetCamera3D();
        if (camera is null || _mount is null)
            return false;

        if (!TryProjectToRingPlane(screenPosition, camera, out var localPoint))
            return false;

        angleDegrees = Math.Atan2(localPoint.Y, localPoint.X) * 180.0 / Math.PI;
        return double.IsFinite(angleDegrees);
    }

    private bool TryProjectToRingPlane(Vector2 screenPosition, Camera3D camera, out Vector3 localPoint)
    {
        localPoint = Vector3.Zero;
        var mount = _mount;
        if (mount is null || !GodotObject.IsInstanceValid(mount))
            return false;

        var rayOrigin = camera.ProjectRayOrigin(screenPosition);
        var rayDir = camera.ProjectRayNormal(screenPosition);

        var inv = mount.GlobalTransform.AffineInverse();
        var lo = inv * rayOrigin;
        var ld = inv.Basis * rayDir;

        var ray = new TunnelRay3(
            new TunnelPoint3(lo.X, lo.Y, lo.Z),
            new TunnelPoint3(ld.X, ld.Y, ld.Z));

        if (!TunnelRayHitMapper.TryIntersectMouthPlane(ray, RingPlaneZ, out var point))
            return false;

        localPoint = new Vector3((float)point.X, (float)point.Y, (float)point.Z);
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
        if (_outerRingRoot is not null && GodotObject.IsInstanceValid(_outerRingRoot))
            _outerRingRoot.RotationDegrees = Vector3.Zero;
        if (_outerLabel is not null && GodotObject.IsInstanceValid(_outerLabel))
            _outerLabel.Text = BuildOuterLabelText();

        ResetFinePreview(TunnelFineResetReason.BaseTimeChanged);
    }

    private void ResetFinePreview(TunnelFineResetReason reason)
    {
        var reset = _coordinator.ResetFinePreview(reason, _fineBinding, FineRailCenterZ, FineRailHalfLength);
        if (reset.FinePreview is { } preview)
        {
            _finePreview = preview;
            UpdateInnerRingVisual(_fineBinding, preview);
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
        LogOuterGesture("motion", mapping, TimelineTickOrigin.ScrubPreview);
    }

    private void LogOuterGesture(string phase, TunnelOuterTickMapping mapping, TimelineTickOrigin origin)
    {
        _log.LogInformation(
            "tunnel outer gesture: phase={Phase} pressTick={PressTick} accumulatedDegrees={AccumulatedDegrees} unitSymbol={UnitSymbol} rawTickQuantity={RawTickQuantity} roundedTargetTick={RoundedTargetTick} clampedTargetTick={ClampedTargetTick} origin={Origin}",
            phase, _gesturePressTick, mapping.AccumulatedDegrees, mapping.Rung.Symbol,
            mapping.RawTickDelta, mapping.RoundedTargetTick, mapping.ClampedTargetTick, origin);
    }

    private void LogInnerGesture(string phase, TunnelFinePreview preview)
    {
        var desc = preview.Binding.Descriptor;
        _log.LogInformation(
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
        var snap = TunnelCorridorLayout.SnapFocus(
            _gestureFocusBefore, _sourceTracks.Count, accumulatedDegrees);
        _log.LogInformation(
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
