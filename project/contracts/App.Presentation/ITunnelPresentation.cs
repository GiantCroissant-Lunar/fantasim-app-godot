using System;

namespace FantaSim.App.Presentation;

/// <summary>Host-facing surface of the tunnel timeline presentation (slice 1). Mirrors
/// IPlanetPresentation's shape exactly -- Rebind on world-bundle availability, teardown -- plus
/// the activation toggle both the remote command and the debug keybind drive.
/// vault/plans/2026-07-11-tunnel-slice1-plan.md.</summary>
public interface ITunnelPresentation : IDisposable
{
    void Rebind();

    /// <summary>Shows/hides the tunnel geometry. false leaves the binder mounted-but-empty (the
    /// always-present input relay still captures the debug keybind while hidden).</summary>
    void SetEnabled(bool enabled);

    bool IsEnabled { get; }
}
