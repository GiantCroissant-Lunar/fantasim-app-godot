using FantaSim.App.Presentation;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Globe;
using Xunit;

namespace App.Presentation.Tests;

public sealed class PlateSurfaceColorModePolicyTests
{
    [Fact]
    public void ForView_keeps_world_surface_on_smoothed_vertex_envelope()
    {
        Assert.Equal(
            PlateCapMeshColorMode.VertexEnvelope,
            PlateSurfaceColorModePolicy.ForView(GlobeViewMode.World));
    }

    [Fact]
    public void ForView_uses_source_cell_facets_for_crust_diagnostic()
    {
        Assert.Equal(
            PlateCapMeshColorMode.SourceCellFacet,
            PlateSurfaceColorModePolicy.ForView(GlobeViewMode.HypsometricTerrain));
    }

    [Fact]
    public void ForView_leaves_plate_identity_on_default_mesh_color_mode()
    {
        Assert.Equal(
            PlateCapMeshColorMode.VertexEnvelope,
            PlateSurfaceColorModePolicy.ForView(GlobeViewMode.PlateIdentity));
    }
}

public sealed class PlateSurfaceNormalModePolicyTests
{
    [Fact]
    public void ForView_uses_smooth_normals_for_world()
    {
        Assert.Equal(
            PlateCapMeshNormalMode.Smooth,
            PlateSurfaceNormalModePolicy.ForView(GlobeViewMode.World));
    }

    [Fact]
    public void ForView_uses_smooth_normals_for_crust_diagnostic()
    {
        Assert.Equal(
            PlateCapMeshNormalMode.Smooth,
            PlateSurfaceNormalModePolicy.ForView(GlobeViewMode.HypsometricTerrain));
    }

    [Fact]
    public void ForView_uses_flat_normals_for_plate_identity()
    {
        Assert.Equal(
            PlateCapMeshNormalMode.Flat,
            PlateSurfaceNormalModePolicy.ForView(GlobeViewMode.PlateIdentity));
    }
}
