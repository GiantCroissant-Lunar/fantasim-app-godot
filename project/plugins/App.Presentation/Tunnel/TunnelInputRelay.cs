using System;
using Godot;

namespace FantaSim.App.Presentation.Tunnel;

internal sealed partial class TunnelInputRelay : Node3D
{
    public Func<InputEvent, bool>? OnInput;
    public Action<double>? OnProcess;
    public Action<string>? OnCancel;
    public Action<Exception>? OnError;

    public override void _Input(InputEvent @event)
    {
        if (OnInput is null)
            return;

        bool handled;
        try
        {
            handled = OnInput(@event);
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
        OnProcess = null;
        OnCancel = null;
        OnError = null;
    }
}
