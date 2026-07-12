namespace FantaSim.App.Presentation.Tunnel;

internal readonly record struct TunnelActivationReadiness(
    bool WorldLoaded,
    bool StageLoaded,
    bool HasController,
    bool HasMount,
    bool HasCamera,
    bool HasPlanetBody);

internal static class TunnelActivationPolicy
{
    internal static string FailureReason(TunnelActivationReadiness value)
    {
        if (!value.WorldLoaded) return "world unavailable";
        if (!value.StageLoaded) return "stage unavailable";
        if (!value.HasController) return "timeline controller unavailable";
        if (!value.HasMount) return "tunnel mount unavailable";
        if (!value.HasCamera) return "tunnel camera unavailable";
        if (!value.HasPlanetBody) return "planet body unavailable";
        return string.Empty;
    }
}

internal readonly record struct TunnelStagePreparationReadiness(
    long ExpectedGeneration,
    long CurrentGeneration,
    bool BinderAlive,
    bool WorldLoaded,
    bool StageLoaded,
    bool HasEnvironment,
    bool HasValidPlanetBody,
    bool PlanetBodyInsideTree);

internal enum TunnelStagePreparationAction
{
    Ignore,
    RetryNextFrame,
    PrepareHidden,
}

internal static class TunnelStagePreparationPolicy
{
    internal static TunnelStagePreparationAction Decide(TunnelStagePreparationReadiness value)
    {
        if (!value.BinderAlive || value.ExpectedGeneration != value.CurrentGeneration)
            return TunnelStagePreparationAction.Ignore;
        if (!value.WorldLoaded || !value.StageLoaded)
            return TunnelStagePreparationAction.Ignore;
        return value.HasEnvironment && value.HasValidPlanetBody && value.PlanetBodyInsideTree
            ? TunnelStagePreparationAction.PrepareHidden
            : TunnelStagePreparationAction.RetryNextFrame;
    }
}

internal readonly record struct TunnelF9CommandCompletion(
    int ExpectedGeneration,
    int CurrentGeneration,
    bool Cancelled,
    bool TransportOk,
    bool ResponseValid,
    long ResponseEpoch,
    long LastAcceptedEpoch);

internal static class TunnelF9CommandPolicy
{
    internal static bool CanAccept(TunnelF9CommandCompletion value)
        => value.ExpectedGeneration == value.CurrentGeneration
            && !value.Cancelled
            && value.TransportOk
            && value.ResponseValid
            && value.ResponseEpoch >= value.LastAcceptedEpoch;
}
