using Godot;
using Microsoft.Extensions.Logging;
using NumericsVector3 = System.Numerics.Vector3;

namespace FantaSim.App.Presentation.Tunnel;

internal readonly record struct TunnelCameraRuntimeSettings(
    NumericsVector3 LocalPosition,
    NumericsVector3 LocalTarget,
    float FieldOfViewDegrees,
    float NearClip,
    Camera3D.KeepAspectEnum KeepAspect);

internal static class TunnelCameraRuntimeContract
{
    internal static TunnelCameraRuntimeSettings Settings => new(
        TunnelCameraFraming.LocalPosition,
        TunnelCameraFraming.LocalTarget,
        TunnelCameraFraming.FieldOfViewDegrees,
        TunnelCameraFraming.NearClip,
        Camera3D.KeepAspectEnum.Height);
}

internal sealed partial class TunnelPresentationBinder
{
    private Camera3D? _tunnelCamera;
    private Camera3D? _previousCamera;

    private void EnsureTunnelCamera()
    {
        if (_mount is null)
            return;

        var settings = TunnelCameraRuntimeContract.Settings;
        if (_tunnelCamera is null || !GodotObject.IsInstanceValid(_tunnelCamera))
        {
            _tunnelCamera = new Camera3D
            {
                Name = "TunnelCamera",
                Position = ToGodot(settings.LocalPosition),
                Fov = settings.FieldOfViewDegrees,
                Near = settings.NearClip,
                KeepAspect = settings.KeepAspect,
                Projection = Camera3D.ProjectionType.Perspective,
            };
            _mount.AddChild(_tunnelCamera);
        }

        // Godot 4.7 locks the selected FOV axis through KeepAspect and defines ProjectRay* in
        // world space. Keeping this camera policy explicit makes the tested widescreen projection
        // and the instrument-local picking inverse agree.
        // Sources:
        // https://docs.godotengine.org/en/4.7/classes/class_camera3d.html#class-camera3d-property-keep-aspect
        // https://docs.godotengine.org/en/4.7/classes/class_camera3d.html#class-camera3d-method-project-ray-origin
        _tunnelCamera.Position = ToGodot(settings.LocalPosition);
        _tunnelCamera.Fov = settings.FieldOfViewDegrees;
        _tunnelCamera.Near = settings.NearClip;
        _tunnelCamera.KeepAspect = settings.KeepAspect;
        _tunnelCamera.Projection = Camera3D.ProjectionType.Perspective;
        _tunnelCamera.LookAt(_mount.ToGlobal(ToGodot(settings.LocalTarget)), Vector3.Up);
    }

    private void ActivateTunnelCamera()
    {
        if (_tunnelCamera is null || !GodotObject.IsInstanceValid(_tunnelCamera))
            return;

        if (_previousCamera is null)
        {
            var viewport = _tunnelCamera.GetViewport();
            var current = viewport?.GetCamera3D();
            if (current is not null && !ReferenceEquals(current, _tunnelCamera))
                _previousCamera = current;
        }

        _tunnelCamera.MakeCurrent();
    }

    private void RestorePreviousCamera()
    {
        if (_previousCamera is not null
            && GodotObject.IsInstanceValid(_previousCamera)
            && _previousCamera.IsInsideTree())
        {
            _previousCamera.MakeCurrent();
        }
        else if (_tunnelCamera is not null && GodotObject.IsInstanceValid(_tunnelCamera))
        {
            _log.LogWarning("Tunnel camera restore: previously-current camera is gone; selecting the viewport's next camera.");
            _tunnelCamera.ClearCurrent(enableNext: true);
        }

        _previousCamera = null;
    }

    private void ClearTunnelCamera()
    {
        _tunnelCamera = null;
    }

    private static Vector3 ToGodot(NumericsVector3 value)
        => new(value.X, value.Y, value.Z);
}
