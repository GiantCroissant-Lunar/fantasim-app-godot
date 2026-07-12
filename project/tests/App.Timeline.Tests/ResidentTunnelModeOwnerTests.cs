using System;
using FantaSim.App.Presentation;
using FantaSim.App.Timeline.Seam;
using Xunit;

namespace App.Timeline.Tests;

public sealed class ResidentTunnelModeOwnerTests
{
    [Theory]
    [InlineData(TunnelModeEvent.WorldChanging)]
    [InlineData(TunnelModeEvent.StageChanging)]
    public void LossPreparationSynchronouslyRestoresResidentHud(TunnelModeEvent lossEvent)
    {
        var restores = 0;
        var owner = new ResidentTunnelModeOwner(() => restores++);

        owner.PrepareForTunnelLoss(lossEvent);

        Assert.Equal(1, restores);
    }

    [Fact]
    public void NonLossEventIsRejectedWithoutChangingHud()
    {
        var restores = 0;
        var owner = new ResidentTunnelModeOwner(() => restores++);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            owner.PrepareForTunnelLoss(TunnelModeEvent.EnableSucceeded);
        });
        Assert.Equal(0, restores);
    }
}
