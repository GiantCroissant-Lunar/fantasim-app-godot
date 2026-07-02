using FantaSim.App.World;
using FantaSim.App.World.Rendering;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Boundary-type legibility proof (sub-project P3): convergent / divergent / transform must be
/// unmistakable at a glance via high-contrast distinct colors, thicker ribbons, and an elevated
/// surface offset so they never z-fight or sink into caps. The mapping is the single shared source of
/// boundary styling for the host renderer and future timeline lanes.
/// </summary>
public sealed class BoundaryStyleMapperTests
{
    [Fact]
    public void Convergent_divergent_transform_have_distinct_colors()
    {
        var conv = BoundaryStyleMapper.Resolve(PlateBoundaryKind.Convergent);
        var div = BoundaryStyleMapper.Resolve(PlateBoundaryKind.Divergent);
        var tr = BoundaryStyleMapper.Resolve(PlateBoundaryKind.Transform);

        Assert.NotEqual(conv.Color, div.Color);
        Assert.NotEqual(conv.Color, tr.Color);
        Assert.NotEqual(div.Color, tr.Color);
    }

    [Fact]
    public void Convergent_is_red_orange_dominant()
    {
        var s = BoundaryStyleMapper.Resolve(PlateBoundaryKind.Convergent);
        Assert.True(s.Color.R > s.Color.G, $"convergent R must dominate G: {s.Color}");
        Assert.True(s.Color.R > s.Color.B, $"convergent R must dominate B: {s.Color}");
    }

    [Fact]
    public void Divergent_is_cyan_dominant()
    {
        var s = BoundaryStyleMapper.Resolve(PlateBoundaryKind.Divergent);
        Assert.True(s.Color.G > s.Color.R, $"divergent G must dominate R: {s.Color}");
        Assert.True(s.Color.B > s.Color.R, $"divergent B must dominate R: {s.Color}");
    }

    [Fact]
    public void Transform_is_yellow_dominant()
    {
        var s = BoundaryStyleMapper.Resolve(PlateBoundaryKind.Transform);
        Assert.True(s.Color.R > s.Color.B, $"transform R must dominate B: {s.Color}");
        Assert.True(s.Color.G > s.Color.B, $"transform G must dominate B: {s.Color}");
    }

    [Fact]
    public void All_active_kinds_render_on_top()
    {
        var kinds = new[]
        {
            PlateBoundaryKind.Convergent,
            PlateBoundaryKind.Divergent,
            PlateBoundaryKind.Transform,
        };
        foreach (var kind in kinds)
        {
            var s = BoundaryStyleMapper.Resolve(kind);
            Assert.True(s.RenderOnTop, $"{kind} must render on top of plate caps");
        }
    }

    [Fact]
    public void Active_ribbons_are_wider_than_the_old_default()
    {
        // Old hardcoded RibbonHalfWidth was 0.012; P3 raises it so boundaries read at a glance.
        foreach (var kind in new[] { PlateBoundaryKind.Convergent, PlateBoundaryKind.Divergent, PlateBoundaryKind.Transform })
        {
            var s = BoundaryStyleMapper.Resolve(kind);
            Assert.True(s.RibbonHalfWidth > 0.012,
                $"{kind} ribbon half-width must exceed the old 0.012 default: got {s.RibbonHalfWidth}");
        }
    }

    [Fact]
    public void Active_ribbons_are_offset_above_the_old_default()
    {
        // Old hardcoded RibbonHeight was 1.015; P3 raises it so ribbons never z-fight or sink into caps.
        foreach (var kind in new[] { PlateBoundaryKind.Convergent, PlateBoundaryKind.Divergent, PlateBoundaryKind.Transform })
        {
            var s = BoundaryStyleMapper.Resolve(kind);
            Assert.True(s.SurfaceHeight > 1.015,
                $"{kind} surface height must exceed the old 1.015 default: got {s.SurfaceHeight}");
        }
    }

    [Fact]
    public void Active_ribbons_sit_above_the_unit_sphere()
    {
        foreach (var kind in new[] { PlateBoundaryKind.Convergent, PlateBoundaryKind.Divergent, PlateBoundaryKind.Transform })
        {
            var s = BoundaryStyleMapper.Resolve(kind);
            Assert.True(s.SurfaceHeight > 1.0,
                $"{kind} must sit above the unit sphere surface: got {s.SurfaceHeight}");
        }
    }

    [Fact]
    public void All_styles_have_non_negative_emission()
    {
        foreach (var kind in new[] { PlateBoundaryKind.Convergent, PlateBoundaryKind.Divergent,
                                     PlateBoundaryKind.Transform, PlateBoundaryKind.Inactive })
        {
            var s = BoundaryStyleMapper.Resolve(kind);
            Assert.True(s.EmissionEnergy >= 0.0, $"{kind} emission must be non-negative");
        }
    }

    [Fact]
    public void Active_kinds_have_stronger_emission_than_inactive()
    {
        var inactive = BoundaryStyleMapper.Resolve(PlateBoundaryKind.Inactive);
        var convergent = BoundaryStyleMapper.Resolve(PlateBoundaryKind.Convergent);
        Assert.True(convergent.EmissionEnergy > inactive.EmissionEnergy,
            "active boundaries must glow more than inactive");
    }

    [Fact]
    public void Unknown_kind_falls_back_to_inactive()
    {
        var s = BoundaryStyleMapper.Resolve((PlateBoundaryKind)99);
        Assert.False(s.RenderOnTop, "unknown kind must not render on top");
    }

    [Fact]
    public void Active_kinds_share_the_same_geometry_so_no_kind_vs_kind_z_fighting()
    {
        var conv = BoundaryStyleMapper.Resolve(PlateBoundaryKind.Convergent);
        var div = BoundaryStyleMapper.Resolve(PlateBoundaryKind.Divergent);
        var tr = BoundaryStyleMapper.Resolve(PlateBoundaryKind.Transform);

        Assert.Equal(conv.RibbonHalfWidth, div.RibbonHalfWidth);
        Assert.Equal(conv.RibbonHalfWidth, tr.RibbonHalfWidth);
        Assert.Equal(conv.SurfaceHeight, div.SurfaceHeight);
        Assert.Equal(conv.SurfaceHeight, tr.SurfaceHeight);
    }
}
