using System;

namespace FantaSim.App.Presentation;

/// <summary>Host-facing surface of the tunnel timeline presentation (slice 1). Mirrors
/// IPlanetPresentation's shape exactly -- Rebind on world-bundle availability, teardown -- plus
/// the activation toggle both the remote command and the debug keybind drive.
/// vault/plans/2026-07-11-tunnel-slice1-plan.md.</summary>
public interface ITunnelPresentation : IDisposable
{
    void Rebind();

    /// <summary>Attempts to change effective tunnel ownership. Enabling fails closed when any live
    /// world/stage/controller/geometry dependency is unavailable; disabling is always idempotent.</summary>
    TunnelActivationResult TrySetEnabled(bool enabled);

    bool IsEnabled { get; }
}
