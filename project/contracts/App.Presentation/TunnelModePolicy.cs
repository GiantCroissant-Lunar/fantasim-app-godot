namespace FantaSim.App.Presentation;

public readonly record struct TunnelActivationResult(
    bool RequestedEnabled,
    bool EffectiveEnabled,
    string FailureReason);

public readonly record struct TunnelZoomResult(
    bool Ok,
    float EffectiveScale,
    string FailureReason);

public enum TunnelModeEvent
{
    EnableSucceeded,
    EnableFailed,
    DisableRequested,
    TimelineReload,
    WorldChanging,
    StageChanging,
    ControllerLost,
    Disposed,
}

public readonly record struct TunnelModeDecision(
    long ModeEpoch,
    bool EffectiveEnabled,
    bool HudVisible,
    bool CancelInteractionWork,
    bool CancelCommandWork,
    bool RestoreCamera,
    bool AutoReenable);

public static class TunnelModePolicy
{
    public static TunnelModeDecision Decide(
        TunnelModeEvent modeEvent,
        bool currentEffective,
        long currentEpoch)
    {
        var nextEpoch = currentEpoch == long.MaxValue ? long.MaxValue : currentEpoch + 1L;
        if (modeEvent == TunnelModeEvent.EnableSucceeded)
            return new(nextEpoch, true, false, false, false, false, false);
        if (modeEvent == TunnelModeEvent.TimelineReload)
            return new(nextEpoch, currentEffective, !currentEffective, false, true, false, false);
        return new(nextEpoch, false, true, true, true, true, false);
    }
}
