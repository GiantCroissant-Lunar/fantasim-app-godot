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

        // HUD preparation touches Godot state and can throw mid-reload. Teardown must run
        // regardless — skipping it would leave the mount/relay/frame bindings alive past the
        // bundle unload and pin the outgoing ALC — so it is fenced in a finally. A HUD-prep
        // failure still propagates after teardown rather than being swallowed.
        try
        {
            modeOwner?.PrepareForTunnelLoss(lossEvent);
        }
        finally
        {
            teardownOnMainThread();
        }
    }
}
