using FantaSim.App.World.Composition;
using Godot;

namespace FantaSim.App.Presentation;

internal readonly record struct PlanetLightingTuning(
    Color SunColor,
    float SunLightEnergy,
    Color AmbientColor,
    float AmbientLightEnergy)
{
    public static PlanetLightingTuning ForView(GlobeViewMode viewMode)
        => viewMode switch
        {
            GlobeViewMode.HypsometricTerrain => new PlanetLightingTuning(
                new Color(1.00f, 1.00f, 0.98f),
                1.10f,
                new Color(0.36f, 0.36f, 0.35f),
                0.38f),
            _ => new PlanetLightingTuning(
                new Color(1.02f, 0.96f, 0.88f),
                1.8f,
                new Color(0.38f, 0.34f, 0.30f),
                0.42f),
        };

    public void ApplyTo(DirectionalLight3D sun, WorldEnvironment environment)
    {
        sun.LightColor = SunColor;
        sun.LightEnergy = SunLightEnergy;

        environment.Environment ??= new Godot.Environment();
        environment.Environment.AmbientLightColor = AmbientColor;
        environment.Environment.AmbientLightEnergy = AmbientLightEnergy;
    }
}
