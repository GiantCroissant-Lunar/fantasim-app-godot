using System.Collections.Generic;
using FantaSim.App.Presentation;
using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace App.Presentation.Tests;

public sealed class TunnelLossSequenceTests
{
    [Fact]
    public void ModeOwnerCompletesHudPreparationBeforeGeometryTeardownStarts()
    {
        var events = new List<string>();
        var owner = new RecordingModeOwner(events);

        TunnelLossSequence.Run(
            owner,
            TunnelModeEvent.WorldChanging,
            teardownOnMainThread: () => events.Add("geometry"));

        Assert.Equal(new[] { "hud", "geometry" }, events);
    }

    [Fact]
    public void MissingModeOwnerStillPerformsFailSafeGeometryTeardown()
    {
        var tornDown = false;

        TunnelLossSequence.Run(
            modeOwner: null,
            TunnelModeEvent.StageChanging,
            teardownOnMainThread: () => tornDown = true);

        Assert.True(tornDown);
    }

    private sealed class RecordingModeOwner(List<string> events) : ITunnelModeOwner
    {
        public TunnelHudSafetyState CurrentHudSafety { get; private set; }

        public void PrepareForTunnelLoss(TunnelModeEvent lossEvent)
        {
            Assert.Contains(lossEvent, new[] { TunnelModeEvent.WorldChanging, TunnelModeEvent.StageChanging });
            CurrentHudSafety = new TunnelHudSafetyState(CurrentHudSafety.Epoch + 1L, true);
            events.Add("hud");
        }

        public bool TryReleaseHudSafety(long expectedEpoch) => false;
    }
}
