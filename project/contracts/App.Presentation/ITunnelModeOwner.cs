namespace FantaSim.App.Presentation;

public readonly record struct TunnelHudSafetyState(long Epoch, bool ForceHudVisible);

/// <summary>
/// Resident timeline-seam coordination used by the world presentation immediately before tunnel
/// geometry is torn down. The call is synchronous and must make the safe 2D HUD state observable
/// before returning, including while the collectible timeline plugin is between generations.
/// </summary>
public interface ITunnelModeOwner
{
    TunnelHudSafetyState CurrentHudSafety { get; }

    void PrepareForTunnelLoss(TunnelModeEvent lossEvent);

    /// <summary>
    /// Releases the forced-visible safety lease only when no newer loss superseded the caller's
    /// captured epoch. Only an explicitly successful tunnel activation may call this method.
    /// </summary>
    bool TryReleaseHudSafety(long expectedEpoch);
}
