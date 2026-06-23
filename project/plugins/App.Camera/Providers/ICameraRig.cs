using System.Threading.Tasks;

namespace FantaSim.App.Camera.Providers;

/// <summary>
/// The camera service's engine seam: the engine-specific backend that positions and transitions the
/// virtual camera. The T3 service (<see cref="FantaSim.App.Camera.Services.Service"/>) owns this seam
/// and delegates the Godot/phantom-camera work to it (implemented by App.Camera.Seam's
/// <c>CameraRig</c>). Deliberately LEANER than <see cref="IService"/>: spec validation, active-id
/// tracking, and the bus trigger are the service's orchestration, not the rig's job. Mirrors
/// App.Ui's <c>Providers/IViewHost</c> seam (T3 owns it; the T4 Godot project implements it; T3
/// never references T4).
/// </summary>
public interface ICameraRig
{
    /// <summary>
    /// Register (or replace) a camera viewpoint. Idempotent per <see cref="CameraSpec.CameraId"/>:
    /// re-registering replaces the viewpoint parameters for that id. Calls may arrive OFF the engine
    /// main thread; the rig marshals internally. The returned task completes after the engine main
    /// thread applied the operation (or faults with the seam exception).
    /// </summary>
    Task RegisterAsync(CameraSpec spec);

    /// <summary>
    /// Make the camera with <paramref name="cameraId"/> the live camera of its viewport (the rig
    /// tweens the transition). Calls may arrive OFF the engine main thread; the rig marshals
    /// internally. The returned task completes after the engine main thread applied the operation
    /// (or faults with the seam exception).
    /// </summary>
    Task ActivateAsync(string cameraId);

    /// <summary>
    /// Remove a camera viewpoint. Unregister of an unknown id is a no-op. Calls may arrive OFF
    /// the engine main thread; the rig marshals internally. The returned task completes after the
    /// engine main thread applied the operation (or faults with the seam exception).
    /// </summary>
    Task UnregisterAsync(string cameraId);
}
