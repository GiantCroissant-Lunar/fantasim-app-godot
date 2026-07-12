using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace App.Presentation.Tests;

public sealed class TunnelCorridorDepthPolicyTests
{
    [Fact]
    public void Track_walls_begin_at_current_time_and_recede_to_the_throat()
    {
        var bands = TunnelCorridorDepthPolicy.Plan(
            TunnelCameraFraming.CurrentPlaneZ,
            TunnelCameraFraming.ThroatZ);

        Assert.Equal(4, bands.Count);
        Assert.Equal(TunnelCameraFraming.CurrentPlaneZ, bands[0].NearZ);
        Assert.Equal(TunnelCameraFraming.ThroatZ, bands[^1].FarZ);
        for (var index = 0; index < bands.Count; index++)
        {
            Assert.True(bands[index].NearZ <= TunnelCameraFraming.CurrentPlaneZ);
            Assert.True(bands[index].FarZ < bands[index].NearZ);
            if (index > 0)
                Assert.Equal(bands[index - 1].FarZ, bands[index].NearZ);
        }
    }

    [Fact]
    public void Track_walls_are_shaded_without_self_illumination()
    {
        var near = TunnelCorridorDepthPolicy.ToneAt(0f);
        var far = TunnelCorridorDepthPolicy.ToneAt(1f);

        Assert.InRange(near.Brightness, 0.45f, 0.65f);
        Assert.InRange(far.Brightness, 0.10f, 0.25f);
        Assert.True(far.Brightness < near.Brightness);
        Assert.False(near.EmissionEnabled);
        Assert.False(far.EmissionEnabled);
    }
}
