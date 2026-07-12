namespace FantaSim.App.Presentation;

/// <summary>
/// Resident timeline-seam coordination used by the world presentation immediately before tunnel
/// geometry is torn down. The call is synchronous and must make the safe 2D HUD state observable
/// before returning, including while the collectible timeline plugin is between generations.
/// </summary>
public interface ITunnelModeOwner
{
    void PrepareForTunnelLoss(TunnelModeEvent lossEvent);
}
