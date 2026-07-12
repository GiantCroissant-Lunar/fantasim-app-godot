using System;
using FantaSim.App.Presentation;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Resident safety-phase owner. It outlives collectible timeline plugin generations, so a world
/// or stage reload can make the current HUD visible even while timeline.pck is between owners.
/// Normal desired-state and mode-epoch transitions remain owned by TimelinePlugin.
/// </summary>
public sealed class ResidentTunnelModeOwner : ITunnelModeOwner
{
    private readonly Action _restoreSafeHud;

    public ResidentTunnelModeOwner(Action restoreSafeHud)
        => _restoreSafeHud = restoreSafeHud ?? throw new ArgumentNullException(nameof(restoreSafeHud));

    public void PrepareForTunnelLoss(TunnelModeEvent lossEvent)
    {
        if (lossEvent is not TunnelModeEvent.WorldChanging and not TunnelModeEvent.StageChanging)
            throw new ArgumentOutOfRangeException(nameof(lossEvent), lossEvent, "Expected a world or stage loss event.");

        _restoreSafeHud();
    }
}
