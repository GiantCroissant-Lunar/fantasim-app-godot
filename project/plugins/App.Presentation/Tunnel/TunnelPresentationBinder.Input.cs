using System;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using Godot;

namespace FantaSim.App.Presentation.Tunnel;

// Real-mouse scrub input on the current-tick ring (plan Task 10): TunnelInputRelay forwards every
// unhandled input event here; a press is dispatched through TunnelScrubMapper's radius-gated band
// test (spec §5.1's mode='time' vs 'wall' split) into the SAME TimelineScrubCoalescer ->
// PushTick(tick, origin) pipeline TimelineFace.Input.cs drives for the 2D ruler -- reused directly,
// never reimplemented. Split from the core file 2026-07-11
// (vault/plans/2026-07-11-tunnel-slice1-plan.md).
internal sealed partial class TunnelPresentationBinder
{
    private const float ScrubRingBandPx = 24f;

    private readonly TimelineScrubCoalescer _scrubCoalescer = new();
    private bool _dragging;
    private long? _lastAppliedTick;

    private void HandleInputEvent(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } pressed:
                HandlePress(pressed);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } released:
                HandleRelease(released);
                break;
            case InputEventMouseMotion motion when _dragging:
                HandleMotion(motion);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.F9 }:
                // Task 11 Step 2: the debug keybind routes through the SAME SetEnabled toggle the
                // timeline.tunnel_view command drives, on the SAME binder instance -- no duplicated
                // toggle logic to drift. F9 verified free repo-wide before picking it (plan Task 11
                // Step 2): grep -rn "Key.F9" project --include="*.cs" (empty before this file).
                SetEnabled(!IsEnabled);
                break;
        }
    }

    private void HandlePress(InputEventMouseButton mouseBtn)
    {
        // Gated on _enabled (unlike the F9 keybind above, which must always work): the ring
        // geometry still exists in the tree while hidden (SetEnabled only toggles Visible), so
        // without this guard a click at the ring's on-screen position could scrub an invisible
        // tunnel.
        if (_ctl is null || !_enabled || !TryProjectCurrentTickRing(out var centerScreen, out var ringRadiusPx))
            return;

        var pressRadiusPx = mouseBtn.Position.DistanceTo(centerScreen);
        if (!TunnelScrubMapper.IsWithinRingBand(pressRadiusPx, ringRadiusPx, ScrubRingBandPx))
            return; // Press outside the ring band: a pure camera/wall-spin gesture in slice 1
                     // (spec §5.1) -- no tick side effect, and no fallback wired here on purpose.

        _dragging = true;
        ApplyScrubAction(_scrubCoalescer.Press(_ctl.Tick));
    }

    private void HandleMotion(InputEventMouseMotion motion)
    {
        if (_ctl is null)
            return;

        var viewportWidth = _inputRelay?.GetViewport()?.GetVisibleRect().Size.X ?? 0f;
        var baseTick = _lastAppliedTick ?? _ctl.Tick;
        var tick = TunnelScrubMapper.DragDeltaToTick(
            motion.Relative.X, viewportWidth, 0L, Math.Max(0L, _ctl.MaxTick), baseTick);

        ApplyScrubAction(_scrubCoalescer.Motion(tick));
    }

    private void HandleRelease(InputEventMouseButton mouseBtn)
    {
        if (_ctl is null || !_dragging)
            return;

        _dragging = false;
        ApplyScrubAction(_scrubCoalescer.Release(_lastAppliedTick ?? _ctl.Tick));
    }

    // Mirrors TimelineFace.Input.cs' ApplyScrubAction shape exactly (plan Task 10 Step 3): clamp,
    // echo (the current-tick ring rebuild is the tunnel's local echo -- cheap, one ring, never the
    // whole corridor set), then push through the unchanged origin-carrying pipeline.
    private void ApplyScrubAction(TimelineScrubAction action)
    {
        if (_ctl is null || !action.ShouldApply)
            return;

        var tick = Math.Clamp(action.Tick, 0L, Math.Max(0L, _ctl.MaxTick));
        _lastAppliedTick = tick;
        RebuildCurrentTickRing();
        _ctl.PushTick(tick, action.Origin);
    }

    // Projects the current-tick ring's screen-space center and radius through the viewport's
    // active Camera3D, so HandlePress can radius-gate against the ring exactly as it is currently
    // drawn (spec §5.1's radius-based dispatch). Behind-camera points degrade to "no hit" rather
    // than an UnprojectPosition garbage coordinate.
    private bool TryProjectCurrentTickRing(out Vector2 centerScreen, out float ringRadiusPx)
    {
        centerScreen = Vector2.Zero;
        ringRadiusPx = 0f;

        if (_mount is null || !GodotObject.IsInstanceValid(_mount) || _currentTickRingRadius <= 0f)
            return false;

        var camera = _inputRelay?.GetViewport()?.GetCamera3D();
        if (camera is null)
            return false;

        var center3D = _mount.GlobalTransform.Origin;
        var edge3D = center3D + (_mount.GlobalTransform.Basis.X * _currentTickRingRadius);
        if (camera.IsPositionBehind(center3D) || camera.IsPositionBehind(edge3D))
            return false;

        centerScreen = camera.UnprojectPosition(center3D);
        var edgeScreen = camera.UnprojectPosition(edge3D);
        ringRadiusPx = centerScreen.DistanceTo(edgeScreen);
        return true;
    }
}
