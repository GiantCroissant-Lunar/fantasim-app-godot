using FantaSim.App.World.Composition;
using Godot;

namespace FantaSim.App.Presentation;

internal readonly record struct PlateSurfaceMaterialTuning(float AlbedoGain, float LightFloor)
{
    public static PlateSurfaceMaterialTuning ForView(GlobeViewMode viewMode)
        => viewMode switch
        {
            GlobeViewMode.HypsometricTerrain => new PlateSurfaceMaterialTuning(1.03f, 0.18f),
            GlobeViewMode.World => new PlateSurfaceMaterialTuning(1.0f, 0.08f),
            _ => new PlateSurfaceMaterialTuning(1.0f, 0.10f),
        };

    public void ApplyTo(ShaderMaterial material)
    {
        material.SetShaderParameter("u_albedo_gain", AlbedoGain);
        material.SetShaderParameter("u_light_floor", LightFloor);
    }
}
