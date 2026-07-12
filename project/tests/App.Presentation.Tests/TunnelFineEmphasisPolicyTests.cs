using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace App.Presentation.Tests;

public sealed class TunnelFineEmphasisPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Normal_or_focused_spheres_keep_neutral_modulation(
        bool inspectionActive,
        bool focused)
    {
        var tone = TunnelFineEmphasisPolicy.Resolve(inspectionActive, focused);

        Assert.Equal(1f, tone.Red);
        Assert.Equal(1f, tone.Green);
        Assert.Equal(1f, tone.Blue);
        Assert.Equal(1f, tone.Alpha);
    }

    [Fact]
    public void Active_inspection_grays_only_nonfocused_spheres()
    {
        var tone = TunnelFineEmphasisPolicy.Resolve(
            inspectionActive: true,
            focused: false);

        Assert.Equal(tone.Red, tone.Green);
        Assert.Equal(tone.Green, tone.Blue);
        Assert.InRange(tone.Red, 0.30f, 0.60f);
        Assert.Equal(1f, tone.Alpha);
    }
}
