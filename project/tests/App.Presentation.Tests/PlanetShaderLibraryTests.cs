using FantaSim.App.Presentation;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

public sealed class PlanetShaderLibraryTests
{
    [Fact]
    public void AllShaderSourcesAreSpatialShaders()
    {
        var sources = new[]
        {
            PlanetShaderLibrary.MantleIsosurfaceOpaqueShaderCode,
            PlanetShaderLibrary.MantleIsosurfaceTranslucentShaderCode,
            PlanetShaderLibrary.MagmaShaderCode,
            PlanetShaderLibrary.StagnantShaderCode,
            PlanetShaderLibrary.HypsoPlateShaderCode,
            PlanetShaderLibrary.AtmosphereRimShaderCode,
        };
        Assert.All(sources, s => Assert.Contains("shader_type spatial;", s));
    }

    [Fact]
    public void HypsoPlateShaderKeepsCutawayWedgeUniformContract()
    {
        // UpdateCutawayPlateShader sets these by name; renaming one silently kills the cutaway.
        foreach (var uniform in new[]
        {
            "u_wedge_active", "u_wedge_axis", "u_wedge_reference",
            "u_wedge_reference_cross", "u_wedge_start_rad", "u_wedge_width_rad",
        })
            Assert.Contains(uniform, PlanetShaderLibrary.HypsoPlateShaderCode);
    }
}
