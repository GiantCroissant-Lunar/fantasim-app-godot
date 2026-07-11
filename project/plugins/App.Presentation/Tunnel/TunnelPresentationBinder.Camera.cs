using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Presentation.Tunnel;

internal sealed partial class TunnelPresentationBinder
{
    private Camera3D? _tunnelCamera;
    private Camera3D? _previousCamera;

    private void EnsureTunnelCamera()
    {
        if (_mount is null)
            return;

        if (_tunnelCamera is null || !GodotObject.IsInstanceValid(_tunnelCamera))
        {
            _tunnelCamera = new Camera3D
            {
                Name = "TunnelCamera",
                Position = TunnelCameraLocalPosition,
                Fov = TunnelCameraFovDeg,
            };
            _mount.AddChild(_tunnelCamera);
            _tunnelCamera.LookAt(TunnelCameraLocalTarget, Vector3.Up);
        }
    }

    private void ActivateTunnelCamera()
    {
        if (_tunnelCamera is null || !GodotObject.IsInstanceValid(_tunnelCamera))
            return;

        if (_previousCamera is null)
        {
            var viewport = _tunnelCamera.GetViewport();
            _previousCamera = viewport?.GetCamera3D();
        }

        _tunnelCamera.MakeCurrent();
    }

    private void RestorePreviousCamera()
    {
        if (_previousCamera is null)
            return;

        if (GodotObject.IsInstanceValid(_previousCamera) && _previousCamera.IsInsideTree())
            _previousCamera.MakeCurrent();
        else
            _log.LogWarning("Tunnel camera restore: previously-current camera is gone; viewport left as-is.");

        _previousCamera = null;
    }

    private void ClearTunnelCamera()
    {
        _tunnelCamera = null;
    }
}
