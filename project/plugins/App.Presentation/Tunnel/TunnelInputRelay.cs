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
        // WM_FOCUS_OUT / WM_EXIT_TREE — cancel any owned gesture so focus loss or tree exit
        // does not strand a drag or leave a stale commit pending.
        const int NotificationWmClose = 1006;
        const int NotificationWmFocusOut = 1007;
        const int NotificationPredelete = 1010;

        if (what == NotificationWmFocusOut || what == NotificationWmClose || what == NotificationPredelete)
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
