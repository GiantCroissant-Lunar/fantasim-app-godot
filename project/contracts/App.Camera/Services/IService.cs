using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ServiceArchi.Contracts;            // SelectionMode
using ServiceArchi.Contracts.Attributes; // ServiceContract, SelectionStrategy

namespace FantaSim.App.Camera;

/// <summary>
/// T1 virtual-camera contract.
/// </summary>
[ServiceContract]
[SelectionStrategy(SelectionMode.HighestPriority)]
public interface IService
{
    /// <summary>
    /// RegisterAsync adds (or replaces) a camera viewpoint.
    /// </summary>
    Task RegisterAsync(CameraSpec spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// UnregisterAsync removes a camera viewpoint (a removed active camera leaves the viewport on the highest-priority remaining camera).
    /// </summary>
    Task UnregisterAsync(string cameraId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ActivateAsync makes the specified camera the live camera of its viewport (the seam tweens the transition; activating an unknown id throws InvalidOperationException).
    /// </summary>
    Task ActivateAsync(string cameraId, CancellationToken cancellationToken = default);

    /// <summary>
    /// RegisteredCameras is a snapshot of all registered cameras.
    /// </summary>
    IReadOnlyList<CameraSpec> RegisteredCameras { get; }

    /// <summary>
    /// ActiveCameraId returns the active camera of a viewport or null.
    /// </summary>
    string? ActiveCameraId(string viewportId = "main");

    /// <summary>
    /// Raised after any register, unregister, or activate event. May be raised OFF the engine main
    /// thread (bus-triggered activates run on thread-pool continuations) - subscribers that touch
    /// the engine must marshal first before doing so.
    /// </summary>
    event Action? CamerasChanged;
}
