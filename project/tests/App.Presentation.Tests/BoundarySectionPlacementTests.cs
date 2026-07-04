using FantaSim.App.Presentation;
using Godot;
using Xunit;

namespace App.Presentation.Tests;

public sealed class BoundarySectionPlacementTests
{
    [Fact]
    public void Default_keeps_section_panels_visible_without_covering_the_globe_center()
    {
        var placement = BoundarySectionPlacement.Default;

        Assert.True(placement.Position.Y <= -1.70f);
        Assert.True(placement.Position.Z >= 1.0f);
        Assert.Equal(new Vector3(-6.0f, 0.0f, 0.0f), placement.RotationDegrees);
        Assert.Equal(Vector3.One * 0.36f, placement.Scale);
    }
}
