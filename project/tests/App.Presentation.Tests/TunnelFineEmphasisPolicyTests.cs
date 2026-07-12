using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace App.Presentation.Tests;

public sealed class TunnelFineEmphasisPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Normal_or_focused_spheres_keep_full_color_and_value(
        bool inspectionActive,
        bool focused)
    {
        var tone = TunnelFineEmphasisPolicy.Resolve(inspectionActive, focused);

        Assert.Equal(1f, tone.Saturation);
        Assert.Equal(1f, tone.Value);
    }

    [Fact]
    public void Active_inspection_grays_only_nonfocused_spheres()
    {
        var tone = TunnelFineEmphasisPolicy.Resolve(
            inspectionActive: true,
            focused: false);

        Assert.Equal(0f, tone.Saturation);
        Assert.InRange(tone.Value, 0.30f, 0.60f);
    }
}
