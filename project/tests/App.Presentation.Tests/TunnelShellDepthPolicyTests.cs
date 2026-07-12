using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace App.Presentation.Tests;

public sealed class TunnelShellDepthPolicyTests
{
    [Fact]
    public void Shell_bands_are_contiguous_and_darken_toward_the_throat()
    {
        var bands = TunnelShellDepthPolicy.Plan(
            TunnelCameraFraming.MouthZ,
            TunnelCameraFraming.ThroatZ);

        Assert.Equal(4, bands.Count);
        Assert.Equal(TunnelCameraFraming.MouthZ, bands[0].NearZ);
        Assert.Equal(TunnelCameraFraming.ThroatZ, bands[^1].FarZ);
        for (var i = 1; i < bands.Count; i++)
        {
            Assert.Equal(bands[i - 1].FarZ, bands[i].NearZ);
            Assert.True(bands[i].Value < bands[i - 1].Value);
        }
    }
}
