using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Presentation.Tunnel;

// Dedicated face-on camera for the flat annulus (spec Decision Point 12 framing pass): without it
// the orbit rig's ~6.5-unit eye distance sits INSIDE the annulus and the tunnel reads as meaningless
// intersecting planes. TunnelCamera is a child of _mount at local (0,0,Distance) looking down -Z,
// making the annulus read as a radial dartboard with the globe visible through the throat hole. The
// viewport's previously-current camera is captured on enable and restored on disable/teardown so the
// orbit rig is handed back cleanly. No Godot reference is held past ClearMount (ALC-unload safe).
// Input.cs's radius-gating resolves the CURRENT viewport camera, so with the tunnel camera current
// it is automatically correct -- no change needed there.
internal sealed partial class TunnelPresentationBinder
{
    private Camera3D? _tunnelCamera;
    // Captured on enable, cleared on every restore path -- never survives ClearMount.
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
                Position = new Vector3(0f, 0f, TunnelCameraDistance),
                Fov = TunnelCameraFovDeg,
            };
            _mount.AddChild(_tunnelCamera);
            // Face-on: look at the mount's global origin straight down the annulus normal (-Z).
            _tunnelCamera.LookAt(_mount.GlobalTransform.Origin, Vector3.Up);
        }
    }

    // Called from SetEnabled(true) and from EnsureMounted when the deferred mount catches up to an
    // already-enabled binder: capture whatever camera is currently active (the orbit rig) BEFORE
    // making the tunnel camera current. Capture happens only once per enable cycle (the _previousCamera
    // null guard), so a re-enable after a disable re-captures the restored orbit rig correctly.
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

    // Called from SetEnabled(false) and from ClearMount/Dispose BEFORE the mount is freed: restore
    // the captured camera if it is still alive and in the tree, else warn and leave the viewport
    // as-is. _previousCamera is cleared either way -- no Godot reference survives past teardown.
    private void RestorePreviousCamera()
    {
        if (_previousCamera is null)
            return;

        if (GodotObject.IsInstanceValid(_previousCamera) && _previousCamera.IsInsideTree())
            _previousCamera.MakeCurrent();
        else
            _log.LogWarning("Tunnel camera restore: previously-current camera is gone (freed or reparented); viewport left as-is.");

        _previousCamera = null;
    }

    private void ClearTunnelCamera()
    {
        _tunnelCamera = null;
    }
}
