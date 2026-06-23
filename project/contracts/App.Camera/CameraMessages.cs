namespace FantaSim.App.Camera;

/// <summary>
/// Bus message requesting a camera be activated - a decoupled trigger for IService.ActivateAsync.
/// Publishing this on the crosscut message bus asks the camera service to ActivateAsync the camera,
/// which lets the remote command surface drive cameras without referencing the service directly.
/// </summary>
public sealed record ActivateCameraMessage(string CameraId);
