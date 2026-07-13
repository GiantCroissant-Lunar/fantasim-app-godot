using FantaSim.App.Presentation.Tunnel;
using FantaSim.App.Timeline.Seam;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

/// <summary>
/// Headless coverage for the release/motion angle-source policy. The wall carousel and the ring
/// dials are measured on different geometric planes; release must select the same source used for
/// motion or the final delta can be corrupted by off-axis parallax.
/// </summary>
public sealed class TunnelPointerAngleSourcePolicyTests
{
    [Theory]
    [InlineData(TunnelGestureKind.Wall, true)]
    [InlineData(TunnelGestureKind.OuterRing, false)]
    [InlineData(TunnelGestureKind.InnerRing, false)]
    [InlineData(TunnelGestureKind.None, false)]
    public void ShouldUseWallAngleSource_MatchesGestureKind(TunnelGestureKind kind, bool expected)
    {
        Assert.Equal(expected, TunnelPointerAngleSourcePolicy.ShouldUseWallAngleSource(kind));
    }
}
