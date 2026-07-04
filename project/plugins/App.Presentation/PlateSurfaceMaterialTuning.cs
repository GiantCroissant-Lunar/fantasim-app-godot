using FantaSim.App.World.Composition;
using Godot;

namespace FantaSim.App.Presentation;

internal readonly record struct PlateSurfaceMaterialTuning(
    float AlbedoGain,
    float LightFloor,
    float WrapStrength,
    float LightContrast,
    float AlbedoCeiling,
    Vector3 ColorBalance)
{
    public static PlateSurfaceMaterialTuning ForView(GlobeViewMode viewMode)
        => viewMode switch
        {
            GlobeViewMode.HypsometricTerrain => new PlateSurfaceMaterialTuning(
                0.88f,
                0.20f,
                0.30f,
                1.05f,
                0.72f,
                new Vector3(0.98f, 1.0f, 1.02f)),
            GlobeViewMode.World => new PlateSurfaceMaterialTuning(1.0f, 0.08f, 1.0f, 1.0f, 1.0f, Vector3.One),
            _ => new PlateSurfaceMaterialTuning(1.0f, 0.10f, 1.0f, 1.0f, 1.0f, Vector3.One),
        };

    public void ApplyTo(ShaderMaterial material)
    {
        material.SetShaderParameter("u_albedo_gain", AlbedoGain);
        material.SetShaderParameter("u_light_floor", LightFloor);
        material.SetShaderParameter("u_wrap_strength", WrapStrength);
        material.SetShaderParameter("u_light_contrast", LightContrast);
        material.SetShaderParameter("u_albedo_ceiling", AlbedoCeiling);
        material.SetShaderParameter("u_color_balance", ColorBalance);
    }
}
