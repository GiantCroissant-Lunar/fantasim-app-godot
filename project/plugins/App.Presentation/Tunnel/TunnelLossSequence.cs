using System;

namespace FantaSim.App.Presentation.Tunnel;

/// <summary>Enforces the cross-owner HUD-before-geometry ordering for world/stage loss.</summary>
internal static class TunnelLossSequence
{
    public static void Run(
        ITunnelModeOwner? modeOwner,
        TunnelModeEvent lossEvent,
        Action teardownOnMainThread)
    {
        ArgumentNullException.ThrowIfNull(teardownOnMainThread);
        if (lossEvent is not TunnelModeEvent.WorldChanging and not TunnelModeEvent.StageChanging)
            throw new ArgumentOutOfRangeException(nameof(lossEvent), lossEvent, "Expected a world or stage loss event.");

        modeOwner?.PrepareForTunnelLoss(lossEvent);
        teardownOnMainThread();
    }
}
