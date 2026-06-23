namespace FantaSim.App.Camera;

/// <summary>
/// Defines a virtual-camera viewpoint specification.
/// </summary>
/// <param name="CameraId">The stable id the service manages the camera by.</param>
/// <param name="Position">The world-space position (System.Numerics, engine-agnostic - the T4 seam converts).</param>
/// <param name="LookAt">The world-space look-at target (System.Numerics, engine-agnostic - the T4 seam converts).</param>
/// <param name="ViewportId">Names the render viewport the camera belongs to ("main" = the app's root viewport; SubViewport ids come later).</param>
/// <param name="FieldOfViewDegrees">The perspective field of view in degrees.</param>
/// <param name="VisibleCategories">Names the semantic world categories this camera can see (e.g. "base", "crust", "geosphere", "hydrosphere"). Engine-agnostic names; the T4 seam maps them to Godot 3D render layers and applies them as the camera's cull mask when the camera activates. Null (the default) means the camera sees everything. Unknown names are logged and ignored by the seam. Categories are the SEMANTIC layer axis (world content kinds), kept deliberately separate from any engine-internal routing masks.</param>
public sealed record CameraSpec(
    string CameraId,
    System.Numerics.Vector3 Position,
    System.Numerics.Vector3 LookAt,
    string ViewportId = "main",
    float FieldOfViewDegrees = 75f,
    System.Collections.Generic.IReadOnlyList<string>? VisibleCategories = null);
