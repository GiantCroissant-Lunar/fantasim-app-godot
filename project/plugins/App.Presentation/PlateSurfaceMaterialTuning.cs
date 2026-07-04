using FantaSim.App.World.Composition;
using Godot;

namespace FantaSim.App.Presentation;

internal readonly record struct PlateSurfaceMaterialTuning(
    float AlbedoGain,
    float LightFloor,
    float WrapStrength,
    float LightContrast,
    Vector3 ColorBalance)
{
    public static PlateSurfaceMaterialTuning ForView(GlobeViewMode viewMode)
        => viewMode switch
        {
            GlobeViewMode.HypsometricTerrain => new PlateSurfaceMaterialTuning(
                0.98f,
                0.11f,
                0.32f,
                1.18f,
                new Vector3(0.96f, 1.0f, 1.04f)),
            GlobeViewMode.World => new PlateSurfaceMaterialTuning(1.0f, 0.08f, 1.0f, 1.0f, Vector3.One),
            _ => new PlateSurfaceMaterialTuning(1.0f, 0.10f, 1.0f, 1.0f, Vector3.One),
        };

    public void ApplyTo(ShaderMaterial material)
    {
        material.SetShaderParameter("u_albedo_gain", AlbedoGain);
        material.SetShaderParameter("u_light_floor", LightFloor);
        material.SetShaderParameter("u_wrap_strength", WrapStrength);
        material.SetShaderParameter("u_light_contrast", LightContrast);
        material.SetShaderParameter("u_color_balance", ColorBalance);
    }
}
