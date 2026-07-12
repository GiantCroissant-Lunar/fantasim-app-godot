using System.Collections.Generic;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Godot-free formatter for a tunnel corridor's header (vault/plans/
/// 2026-07-13-tunnel-visual-slice1-plan.md Part A). Mirrors the normal 2D track header's
/// name-plus-state minimalism and adds the canonical display rung. <see cref="IsActive"/> is the
/// regime-activity flag the renderer color-codes (active vs dimmed), matching the 2D header's
/// style-swap; it is never used to hide a track.
/// </summary>
public readonly record struct TunnelCorridorHeader(string Title, string Subtitle, bool IsActive)
{
    public static TunnelCorridorHeader Build(LayerTrackDescriptor descriptor, bool isActive)
    {
        var title = string.IsNullOrWhiteSpace(descriptor.DisplayName)
            ? descriptor.LayerId
            : descriptor.DisplayName;

        var parts = new List<string>(2);
        var rung = descriptor.TimeDomain?.Rung;
        if (!string.IsNullOrWhiteSpace(rung))
            parts.Add(rung);
        parts.Add(isActive ? "active" : "inactive");

        return new TunnelCorridorHeader(title, string.Join(" · ", parts), isActive);
    }
}
