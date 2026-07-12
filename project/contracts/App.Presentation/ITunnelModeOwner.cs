namespace FantaSim.App.Presentation;

/// <summary>
/// Timeline-owned coordination seam used by the world presentation immediately before tunnel
/// geometry is torn down. The call is synchronous and must make the safe 2D HUD state observable
/// before returning; callers resolve it method-locally and must never retain the collectible owner.
/// </summary>
public interface ITunnelModeOwner
{
    void PrepareForTunnelLoss(TunnelModeEvent lossEvent);
}
