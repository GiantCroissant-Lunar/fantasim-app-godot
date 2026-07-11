using System;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Presentation.Tunnel;

internal sealed partial class TunnelPresentationBinder
{
    private readonly TunnelGestureCoordinator _coordinator = new();
    private bool _gestureOwned;

    private bool HandleInputEvent(InputEvent @event)
    {
        if (_disposed)
            return false;

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } pressed:
                return TryBeginGesture(pressed);
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                return HandleOwnedRelease();
            case InputEventMouseMotion motion when _gestureOwned:
                return HandleOwnedMotion(motion);
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.F9 }:
                SetEnabled(!IsEnabled);
                return true;
            default:
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

        var context = BuildPressContext();
        var update = _coordinator.Press(hit, context);
        if (!update.Handled)
            return false;

        _gestureOwned = true;
        _lastPointerAngleDeg = LocalPointerAngle(press.Position);

        LogOwnership(update.Gesture, press);

        if (update.OuterTick is { } outerMapping)
            UpdateOuterRingVisual(outerMapping);

        if (update.FinePreview is { } preview)
            UpdateInnerRingVisual(_fineBinding, preview);

        if (update.ScrubAction.ShouldApply && update.OuterTick is { } om)
            ApplyOuterScrubAction(update.ScrubAction, om);

        return true;
    }

    private bool HandleOwnedMotion(InputEventMouseMotion motion)
    {
        if (!_gestureOwned)
            return false;

        var currentAngle = LocalPointerAngle(motion.Position);
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
                    LogInnerMotion(preview);
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

    private bool HandleOwnedRelease()
    {
        if (!_gestureOwned)
            return false;

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
                        ApplyOuterScrubAction(update.ScrubAction, outerMapping);
                    UpdateOuterRingVisual(outerMapping);
                }
                break;
            case TunnelGestureKind.InnerRing:
                if (update.FinePreview is { } preview)
                {
                    _finePreview = preview;
                    UpdateInnerRingVisual(_fineBinding, preview);
                }
                break;
            case TunnelGestureKind.Wall:
                if (update.CarouselSnap is { } snap)
                {
                    if (_corridorsRoot is not null && GodotObject.IsInstanceValid(_corridorsRoot))
                        _corridorsRoot.RotationDegrees = Vector3.Zero;

                    _focusIndex = snap.FocusIndex;
                    _filmstrip.Supersede();
                    RebuildCorridors();
                    ResetFinePreview(TunnelFineResetReason.FocusChanged);
                    LogWallRelease(snap);
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

        var outerHit = TryProjectToMouthPlaneBand(screenPosition, camera, OuterRingInnerRadius, OuterRingOuterRadius);
        var innerHit = TryProjectToMouthPlaneBand(screenPosition, camera, InnerRingInnerRadius, InnerRingOuterRadius);
        var wallHit = TryIntersectTunnelWall(screenPosition, camera);

        hit = TunnelScrubMapper.ResolveHitRegion(outerHit, innerHit, wallHit);
        return hit != TunnelHitRegion.None;
    }

    private bool TryProjectToMouthPlaneBand(Vector2 screenPosition, Camera3D camera, float innerR, float outerR)
    {
        var rayOrigin = camera.ProjectRayOrigin(screenPosition);
        var rayDir = camera.ProjectRayNormal(screenPosition);

        var inv = _mount.GlobalTransform.AffineInverse();
        var localOrigin = inv * rayOrigin;
        var localDir = inv.Basis * rayDir;

        var ray = new TunnelRay3(
            new TunnelPoint3(localOrigin.X, localOrigin.Y, localOrigin.Z),
            new TunnelPoint3(localDir.X, localDir.Y, localDir.Z));

        if (!TunnelRayHitMapper.TryIntersectMouthPlane(ray, MouthZ, out var point))
            return false;

        var radius = Math.Sqrt(point.X * point.X + point.Y * point.Y);
        return radius >= innerR && radius <= outerR;
    }

    private bool TryIntersectTunnelWall(Vector2 screenPosition, Camera3D camera)
    {
        var rayOrigin = camera.ProjectRayOrigin(screenPosition);
        var rayDir = camera.ProjectRayNormal(screenPosition);

        var inv = _mount.GlobalTransform.AffineInverse();
        var localOrigin = inv * rayOrigin;
        var localDir = inv.Basis * rayDir;

        var ray = new TunnelRay3(
            new TunnelPoint3(localOrigin.X, localOrigin.Y, localOrigin.Z),
            new TunnelPoint3(localDir.X, localDir.Y, localDir.Z));

        return TunnelRayHitMapper.TryIntersectCylinder(ray, CorridorSurfaceRadius, ThroatZ, MouthZ, out _);
    }

    private double LocalPointerAngle(Vector2 screenPosition)
    {
        var camera = _inputRelay?.GetViewport()?.GetCamera3D();
        if (camera is null || _mount is null)
            return 0.0;

        if (!TryProjectToMouthPlaneGlobal(screenPosition, camera, out var localPoint))
            return 0.0;

        return Math.Atan2(localPoint.Y, localPoint.X) * 180.0 / Math.PI;
    }

    private bool TryProjectToMouthPlaneGlobal(Vector2 screenPosition, Camera3D camera, out Vector3 localPoint)
    {
        localPoint = Vector3.Zero;
        var rayOrigin = camera.ProjectRayOrigin(screenPosition);
        var rayDir = camera.ProjectRayNormal(screenPosition);

        var inv = _mount.GlobalTransform.AffineInverse();
        var lo = inv * rayOrigin;
        var ld = inv.Basis * rayDir;

        var ray = new TunnelRay3(
            new TunnelPoint3(lo.X, lo.Y, lo.Z),
            new TunnelPoint3(ld.X, ld.Y, ld.Z));

        if (!TunnelRayHitMapper.TryIntersectMouthPlane(ray, MouthZ, out var point))
            return false;

        localPoint = new Vector3((float)point.X, (float)point.Y, (float)point.Z);
        return true;
    }

    private void ApplyOuterScrubAction(TimelineScrubAction action, TunnelOuterTickMapping mapping)
    {
        if (_ctl is null)
            return;

        var tick = Math.Clamp(action.Tick, 0L, Math.Max(0L, _ctl.MaxTick));
        _ctl.PushTick(tick, action.Origin);
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
            kind, "mouse", press.ButtonIndex, true);
    }

    private void LogOuterMotion(TunnelOuterTickMapping mapping)
    {
        _log.LogInformation(
            "tunnel outer gesture: phase={Phase} pressTick={PressTick} accumulatedDegrees={AccumulatedDegrees} unitSymbol={UnitSymbol} rawTickQuantity={RawTickQuantity} roundedTargetTick={RoundedTargetTick} clampedTargetTick={ClampedTargetTick} origin={Origin}",
            "motion", 0L, mapping.AccumulatedDegrees, mapping.Rung.Symbol,
            mapping.RawTickDelta, mapping.RoundedTargetTick, mapping.ClampedTargetTick, "tunnel");
    }

    private void LogInnerMotion(TunnelFinePreview preview)
    {
        var desc = preview.Binding.Descriptor;
        _log.LogInformation(
            "tunnel inner gesture: phase={Phase} sphereId={SphereId} layerId={LayerId} rung={Rung} active={Active} accumulatedDegrees={AccumulatedDegrees} rawTickQuantity={RawTickQuantity} cursorZ={CursorZ} authoritativeTick={AuthoritativeTick} mutated=false",
            "motion",
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
        _log.LogInformation(
            "tunnel wall gesture: phase={Phase} focusBefore={FocusBefore} stepDelta={StepDelta} focusAfter={FocusAfter} snappedDegrees={SnappedDegrees}",
            "motion", _focusIndex, 0L, _focusIndex, accumulatedDegrees);
    }

    private void LogWallRelease(TunnelCorridorLayout.TunnelCarouselSnap snap)
    {
        _log.LogInformation(
            "tunnel wall gesture: phase={Phase} focusBefore={FocusBefore} stepDelta={StepDelta} focusAfter={FocusAfter} snappedDegrees={SnappedDegrees}",
            "release", _focusIndex, snap.StepDelta, snap.FocusIndex, snap.SnappedAngleDegrees);
    }
}
