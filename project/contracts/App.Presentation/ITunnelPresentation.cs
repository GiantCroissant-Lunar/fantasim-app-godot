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

    /// <summary>Adjusts the shared-planet zoom scale when tunnel mode is effective. Direction > 0
    /// zooms in, < 0 zooms out. Shares the same clamping/ownership path as wheel zoom and reports
    /// the effective scale deterministically.</summary>
    TunnelZoomResult TrySetZoom(int direction);

    bool IsEnabled { get; }

    /// <summary>Raised when effective enablement changes OUT OF BAND — i.e. not as the synchronous
    /// result of a caller's own TrySetEnabled (directive 1: the binder self-applies a pending
    /// default-enable when staged preparation completes after a lost boot/reload race). Payload is
    /// the new IsEnabled. ALC DISCIPLINE (boom-hud lesson): subscribers in OTHER bundles hold a
    /// delegate into this binder's ALC — they MUST unsubscribe on world RuntimeChanging and on
    /// their own shutdown, or the old ALC is pinned across reloads.</summary>
    event Action<bool>? EnabledChangedOutOfBand;
}
