using System;
using Godot;

namespace FantaSim.App.Presentation.Tunnel;

internal sealed partial class TunnelInputRelay : Node3D
{
    public Func<InputEvent, bool>? OnInput;
    public Func<InputEvent, bool>? OnUnhandledInput;
    public Action<double>? OnProcess;
    public Action<string>? OnCancel;
    public Action<Exception>? OnError;

    public override void _Input(InputEvent @event)
        => DispatchInput(OnInput, @event);

    public override void _UnhandledInput(InputEvent @event)
        => DispatchInput(OnUnhandledInput, @event);

    private void DispatchInput(Func<InputEvent, bool>? handler, InputEvent @event)
    {
        if (handler is null)
            return;

        bool handled;
        try
        {
            handled = handler(@event);
        }
        catch (Exception ex)
        {
            // A throw here (e.g. a fail-loud seam guard) must never be swallowed silently and must
            // never leave a half-owned gesture that also falls through to globe orbit (dual-drag).
            // Surface it, relinquish any ownership, and fail closed by consuming the faulted event.
            OnError?.Invoke(ex);
            OnCancel?.Invoke("input_exception");
            handled = true;
        }

        if (handled)
            GetViewport()?.SetInputAsHandled();
    }

    public override void _Process(double delta)
        => OnProcess?.Invoke(delta);

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut
            || what == NotificationApplicationFocusOut
            || what == NotificationWMCloseRequest
            || what == NotificationPredelete)
            OnCancel?.Invoke("notification:" + what);
    }

    public override void _ExitTree()
    {
        OnCancel?.Invoke("exit_tree");
        OnInput = null;
        OnUnhandledInput = null;
        OnProcess = null;
        OnCancel = null;
        OnError = null;
    }
}
