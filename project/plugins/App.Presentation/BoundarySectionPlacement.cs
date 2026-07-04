using Godot;

namespace FantaSim.App.Presentation;

internal readonly record struct BoundarySectionPlacement(
    Vector3 Position,
    Vector3 RotationDegrees,
    Vector3 Scale)
{
    public static BoundarySectionPlacement Default { get; } = new(
        Position: new Vector3(0.0f, -1.80f, 1.15f),
        RotationDegrees: new Vector3(-6.0f, 0.0f, 0.0f),
        Scale: Vector3.One * 0.36f);
}
