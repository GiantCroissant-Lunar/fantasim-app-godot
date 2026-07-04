using FantaSim.App.Presentation;
using Godot;
using Xunit;

namespace App.Presentation.Tests;

public sealed class BoundarySectionMaterialPolicyTests
{
    [Fact]
    public void Overlay_disables_depth_testing_so_section_panels_remain_readable_in_front_of_the_globe()
    {
        var policy = BoundarySectionMaterialPolicy.Overlay;

        Assert.True(policy.NoDepthTest);
        Assert.Equal(BaseMaterial3D.DepthDrawModeEnum.Disabled, policy.DepthDrawMode);
    }
}
