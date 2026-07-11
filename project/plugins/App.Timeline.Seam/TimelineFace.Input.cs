using System;
using Godot;
using FantaSim.App.Timeline;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Timeline.Seam;

public partial class TimelineFace
{
    // True while a scrub gesture owns the mouse (press landed on a scrub surface — lanes or
    // ruler/chrome). Motion is tracked in _Input: exactly like GlobeOrbitControls, held-button
    // motion is routed through the viewport's GUI focus path and does not reliably reach
    // gui_input handlers, so per-frame drag updates must be captured at the _Input stage.
    private bool _scrubDragging;

    public override void _Input(InputEvent @event)
    {
        if (!_nodesInitialized || _ctl is null || _rulerRoot is null) return;

        switch (@event)
        {
            case InputEventMouseButton mouseBtn when mouseBtn.Pressed && TryHandleTimelineWheelZoom(mouseBtn):
                AcceptEvent();
                break;
            case InputEventMagnifyGesture magnify when TryHandleTimelineMagnifyZoom(magnify):
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouseBtn
                when TryStartPlayheadLineScrub(mouseBtn.Position):
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mouseBtn:
                if (_scrubDragging)
                {
                    HandleScrubRelease(mouseBtn.Position.X - _rulerRoot.GlobalPosition.X, _rulerRoot.Size.X);
                    AcceptEvent();
                }
                _scrubDragging = false;
                break;
            case InputEventMouseMotion motion when _scrubDragging:
                QueueScrubMotion(motion.Position.X - _rulerRoot.GlobalPosition.X, _rulerRoot.Size.X);
                break;
        }
    }

    private bool TryStartPlayheadLineScrub(Vector2 globalPosition)
    {
        if (_playheadLine is null || _lanesContainer is null || _rulerRoot is null)
            return false;

        var lineRect = _playheadLine.GetGlobalRect();
        var lanesRect = _lanesContainer.GetGlobalRect();
        var grabRect = new Rect2(
            new Vector2(lineRect.Position.X - PlayheadLineGrabMargin, lanesRect.Position.Y),
            new Vector2(lineRect.Size.X + (PlayheadLineGrabMargin * 2f), lanesRect.Size.Y));
        if (!grabRect.HasPoint(globalPosition))
            return false;

        _scrubDragging = true;
        HandleScrubPress(globalPosition.X - _rulerRoot.GlobalPosition.X, _rulerRoot.Size.X);
        return true;
    }

    private bool TryHandleTimelineWheelZoom(InputEventMouseButton mouseBtn)
    {
        if (_ctl is null || _rulerRoot is null || !IsTimelineZoomPosition(mouseBtn.Position))
            return false;

        TimelineLadderRung? targetRung = mouseBtn.ButtonIndex switch
        {
            MouseButton.WheelUp => TimelineModel.TryGetFinerRung(SelectedRung),
            MouseButton.WheelDown => TimelineModel.TryGetCoarserRung(SelectedRung),
            _ => null
        };
        if (targetRung is null)
            return false;

        return ZoomToSpanAroundLocalX(
            TimelineModel.SpanTicksForRung(targetRung, RungSpanUnits),
            mouseBtn.Position.X - _rulerRoot.GlobalPosition.X);
    }

    private bool TryHandleTimelineMagnifyZoom(InputEventMagnifyGesture magnify)
    {
        if (_ctl is null || _rulerRoot is null || magnify.Factor <= 0f || !IsTimelineZoomPosition(magnify.Position))
            return false;

        var currentSpan = Math.Max(MinViewSpanTicks, _viewEndTick - _viewStartTick);
        var targetSpan = Math.Max(MinViewSpanTicks, (long)Math.Round(currentSpan / magnify.Factor));
        if (targetSpan == currentSpan)
            return false;

        return ZoomToSpanAroundLocalX(targetSpan, magnify.Position.X - _rulerRoot.GlobalPosition.X);
    }

    private bool IsTimelineZoomPosition(Vector2 globalPosition)
        => _rulerRoot?.GetGlobalRect().HasPoint(globalPosition) == true
           || _lanesContainer?.GetGlobalRect().HasPoint(globalPosition) == true;

    private void OnLanesGuiInput(InputEvent @event)
    {
        if (_ctl is null || _lanesContainer is null) return;

        if (@event is InputEventMouseButton mouseBtn)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Left && mouseBtn.Pressed)
            {
                _scrubDragging = true;
                HandleScrubPress(mouseBtn.Position.X);
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion)
        {
            if ((mouseMotion.ButtonMask & MouseButtonMask.Left) != 0)
            {
                QueueScrubMotion(mouseMotion.Position.X);
            }
        }
    }

    // Face-root scrub surface: fires for every mouse event the child controls (buttons, lane
    // container, band/track buttons) did NOT consume — i.e. the ruler band, the playhead handle
    // (visual-only, MouseFilter.Ignore), and any empty timeline chrome. Maps the face-local X
    // into ruler-local X so the tick arithmetic matches the drawn ruler exactly.
    private void OnFaceGuiInput(InputEvent @event)
    {
        if (_ctl is null || _rulerRoot is null) return;

        if (@event is InputEventMouseButton mouseBtn)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Left && mouseBtn.Pressed)
            {
                _scrubDragging = true;
                HandleScrubPress(FaceToRulerLocalX(mouseBtn.Position.X), _rulerRoot.Size.X);
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion)
        {
            if ((mouseMotion.ButtonMask & MouseButtonMask.Left) != 0)
            {
                QueueScrubMotion(FaceToRulerLocalX(mouseMotion.Position.X), _rulerRoot.Size.X);
                AcceptEvent();
            }
        }
    }

    private float FaceToRulerLocalX(float faceLocalX)
        => faceLocalX - (_rulerRoot!.GlobalPosition.X - GlobalPosition.X);

    private void HandleScrubPress(float localX)
    {
        if (_ctl is null || _lanesContainer is null) return;
        HandleScrubPress(localX, _lanesContainer.Size.X);
    }

    private void HandleScrubPress(float localX, float surfaceWidth)
    {
        if (!TryScrubTick(localX, surfaceWidth, out var tick))
            return;

        ApplyScrubAction(_scrubCoalescer.Press(tick));
    }

    private void QueueScrubMotion(float localX)
    {
        if (_ctl is null || _lanesContainer is null) return;
        QueueScrubMotion(localX, _lanesContainer.Size.X);
    }

    private void QueueScrubMotion(float localX, float surfaceWidth)
    {
        if (!TryScrubTick(localX, surfaceWidth, out var tick))
            return;

        ApplyScrubAction(_scrubCoalescer.Motion(tick));
    }

    private void HandleScrubRelease(float localX, float surfaceWidth)
    {
        if (!TryScrubTick(localX, surfaceWidth, out var tick))
        {
            _scrubCoalescer.Cancel();
            return;
        }

        ApplyScrubAction(_scrubCoalescer.Release(tick));
    }

    private bool TryScrubTick(float localX, float surfaceWidth, out long tick)
    {
        tick = 0L;
        if (_ctl is null)
            return false;

        if (TimelineScrubMapper.TryLocalXToTick(localX, surfaceWidth, _viewStartTick, _viewEndTick, out tick))
            return true;

        // Loud failure per the ingress doctrine: a scrub that maps to nothing is a layout bug.
        _log.LogInformation("timeline scrub rejected: localX={X} width={W}", localX, surfaceWidth);
        return false;
    }

    private void ApplyScrubAction(TimelineScrubAction action)
    {
        if (_ctl is null || !action.ShouldApply)
            return;

        var tick = Math.Clamp(action.Tick, 0L, _ctl.MaxTick);
        EchoSeekTo(tick);
        _ctl.PushTick(tick, action.Origin);
    }
}