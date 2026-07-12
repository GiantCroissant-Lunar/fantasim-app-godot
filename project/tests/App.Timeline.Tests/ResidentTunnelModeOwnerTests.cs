using System;
using System.Collections.Generic;
using FantaSim.App.Presentation;
using FantaSim.App.Timeline.Providers;
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
        var applied = new List<TunnelHudSafetyState>();
        var owner = new ResidentTunnelModeOwner(applied.Add);
        var beforeLoss = owner.CurrentHudSafety;

        owner.PrepareForTunnelLoss(lossEvent);

        Assert.True(owner.CurrentHudSafety.ForceHudVisible);
        Assert.True(owner.CurrentHudSafety.Epoch > beforeLoss.Epoch);
        Assert.Equal(owner.CurrentHudSafety, applied[^1]);
    }

    [Fact]
    public void LossStateRemainsDurableWhenNoFaceAcceptedTheImmediateApply()
    {
        var owner = new ResidentTunnelModeOwner(_ => { });
        var beforeLoss = owner.CurrentHudSafety;

        owner.PrepareForTunnelLoss(TunnelModeEvent.WorldChanging);

        var stateReplayedByTheNextFace = owner.CurrentHudSafety;
        Assert.True(stateReplayedByTheNextFace.ForceHudVisible);
        Assert.False(owner.TryReleaseHudSafety(beforeLoss.Epoch));
    }

    [Fact]
    public void SecondLossInvalidatesPreviouslyCapturedSafetyRelease()
    {
        var owner = new ResidentTunnelModeOwner(_ => { });
        owner.PrepareForTunnelLoss(TunnelModeEvent.WorldChanging);
        var firstLossEpoch = owner.CurrentHudSafety.Epoch;

        owner.PrepareForTunnelLoss(TunnelModeEvent.StageChanging);

        Assert.False(owner.TryReleaseHudSafety(firstLossEpoch));
        Assert.True(owner.CurrentHudSafety.ForceHudVisible);
        Assert.True(owner.TryReleaseHudSafety(owner.CurrentHudSafety.Epoch));
        Assert.False(owner.CurrentHudSafety.ForceHudVisible);
    }

    [Fact]
    public void NonLossEventIsRejectedWithoutChangingHud()
    {
        var restores = 0;
        var owner = new ResidentTunnelModeOwner(_ => restores++);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            owner.PrepareForTunnelLoss(TunnelModeEvent.EnableSucceeded);
        });
        Assert.Equal(0, restores);
    }
}
