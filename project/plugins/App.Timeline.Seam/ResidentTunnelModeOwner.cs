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
    private readonly object _gate = new();
    private readonly Action<TunnelHudSafetyState> _applyHudSafety;
    private TunnelHudSafetyState _currentHudSafety;

    public ResidentTunnelModeOwner(Action<TunnelHudSafetyState> applyHudSafety)
        => _applyHudSafety = applyHudSafety ?? throw new ArgumentNullException(nameof(applyHudSafety));

    public TunnelHudSafetyState CurrentHudSafety
    {
        get
        {
            lock (_gate)
                return _currentHudSafety;
        }
    }

    public bool TryReleaseHudSafety(long expectedEpoch)
    {
        lock (_gate)
        {
            if (_currentHudSafety.Epoch != expectedEpoch)
                return false;
            if (!_currentHudSafety.ForceHudVisible)
                return true;

            _currentHudSafety = _currentHudSafety with { ForceHudVisible = false };
            return true;
        }
    }

    public void PrepareForTunnelLoss(TunnelModeEvent lossEvent)
    {
        if (lossEvent is not TunnelModeEvent.WorldChanging and not TunnelModeEvent.StageChanging)
            throw new ArgumentOutOfRangeException(nameof(lossEvent), lossEvent, "Expected a world or stage loss event.");

        TunnelHudSafetyState next;
        lock (_gate)
        {
            // Equality, not ordering, is the CAS token contract; wrapping preserves a distinct
            // token for each adjacent loss instead of saturating and re-validating an old capture.
            var epoch = unchecked(_currentHudSafety.Epoch + 1L);
            next = new TunnelHudSafetyState(epoch, ForceHudVisible: true);
            _currentHudSafety = next;
        }

        // Invoke outside the state lock: the face re-reads CurrentHudSafety while applying normal
        // HUD writes, and durability does not depend on whether an active face exists right now.
        _applyHudSafety(next);
    }
}
