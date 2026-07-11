using System;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Regime-active track join for the two-ring prototype's corridor styling and inner-ring
/// enablement (vault/plans/2026-07-12-rotating-tunnel-two-ring-prototype-plan.md Task 2B). Resolves
/// which sphere schedule applies by exact sphere id, then asks the regime-at-tick whether the
/// descriptor's layer is active (ordinal LayerId comparison). Activity styles/enables; it never
/// filters the registry list.
/// </summary>
public static class TunnelTrackActivity
{
    /// <summary>
    /// True when the descriptor's sphere has a known schedule whose regime at <paramref name="tick"/>
    /// lists a <see cref="LayerId"/> matching <paramref name="descriptor"/>.<see cref="LayerTrackDescriptor.LayerId"/>
    /// under <see cref="StringComparison.Ordinal"/>. Unknown spheres and empty regimes return false.
    /// </summary>
    public static bool IsActive(
        LayerTrackDescriptor descriptor,
        long tick,
        SphereRegimeSchedule geosphereSchedule,
        SphereRegimeSchedule atmosphereSchedule)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var schedule = ResolveSchedule(descriptor.SphereId, geosphereSchedule, atmosphereSchedule);
        if (schedule is null)
            return false;

        var active = schedule.RegimeAt(tick)?.ActiveLayers;
        if (active is null)
            return false;

        foreach (var layer in active)
        {
            if (string.Equals(layer.Value, descriptor.LayerId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static SphereRegimeSchedule? ResolveSchedule(
        string sphereId,
        SphereRegimeSchedule geosphereSchedule,
        SphereRegimeSchedule atmosphereSchedule)
        => sphereId switch
        {
            "geosphere" => geosphereSchedule,
            "atmosphere" => atmosphereSchedule,
            _ => null,
        };
}
