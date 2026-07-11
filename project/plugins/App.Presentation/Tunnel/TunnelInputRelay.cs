using System;
using Godot;

namespace FantaSim.App.Presentation.Tunnel;

internal sealed partial class TunnelInputRelay : Node3D
{
    public Func<InputEvent, bool>? OnInput;
    public Action<double>? OnProcess;
    public Action<string>? OnCancel;

    public override void _Input(InputEvent @event)
    {
        if (OnInput is null)
            return;

        bool handled;
        try
        {
            handled = OnInput(@event);
        }
        catch
        {
            handled = false;
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
    }
}
