using Godot;

namespace FantaSim.App.Presentation;

internal readonly record struct BoundarySectionMaterialPolicy(
    bool NoDepthTest,
    BaseMaterial3D.DepthDrawModeEnum DepthDrawMode)
{
    public static BoundarySectionMaterialPolicy Overlay { get; } = new(
        NoDepthTest: true,
        DepthDrawMode: BaseMaterial3D.DepthDrawModeEnum.Disabled);

    public void ApplyTo(StandardMaterial3D material)
    {
        material.NoDepthTest = NoDepthTest;
        material.DepthDrawMode = DepthDrawMode;
    }
}
