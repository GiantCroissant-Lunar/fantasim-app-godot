using System;
using System.Collections.Generic;
using FantaSim.App.Presentation;
using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace App.Presentation.Tests;

public sealed class TunnelLossSequenceTests
{
    [Fact]
    public void HudPreparationThrow_StillPerformsGeometryTeardown_ThenRethrows()
    {
        // The HUD callback touches Godot state and can throw during a reload wave. If a throw here
        // skipped teardown, the mount/relay/frame bindings would survive the bundle unload and pin
        // the outgoing ALC (this repo's #1 bug class). Teardown must run regardless; the failure
        // still surfaces (rethrown) rather than being swallowed.
        var tornDown = false;
        var owner = new ThrowingModeOwner();

        var ex = Assert.Throws<InvalidOperationException>(() => TunnelLossSequence.Run(
            owner,
            TunnelModeEvent.WorldChanging,
            teardownOnMainThread: () => tornDown = true));

        Assert.True(tornDown);
        Assert.Equal("hud boom", ex.Message);
    }

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

    private sealed class ThrowingModeOwner : ITunnelModeOwner
    {
        public TunnelHudSafetyState CurrentHudSafety { get; }

        public void PrepareForTunnelLoss(TunnelModeEvent lossEvent)
            => throw new InvalidOperationException("hud boom");

        public bool TryReleaseHudSafety(long expectedEpoch) => false;
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
