using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Timeline;

public sealed record TimelineBand(string RegimeId, double StartFraction, double WidthFraction, string Variant, bool IsActive);
public sealed record TimelineTrack(string LayerId, bool IsActive);

public static class TimelineModel
{
    public static IReadOnlyList<TimelineBand> Bands(SphereRegimeSchedule schedule, long maxTick, long currentTick)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (maxTick <= 0) throw new ArgumentOutOfRangeException(nameof(maxTick));
        double max = maxTick;
        var bands = new List<TimelineBand>(schedule.Regimes.Count);
        foreach (var r in schedule.Regimes)
        {
            long end = Math.Min(r.EndTick, maxTick);          // clamp open-end (long.MaxValue) to maxTick
            if (r.StartTick >= maxTick) continue;             // regime entirely past the view
            double start = r.StartTick / max;
            double width = Math.Max(0.0, (end - r.StartTick) / max);
            bands.Add(new TimelineBand(r.RegimeId, start, width, VariantFor(r.RegimeId), r.Contains(currentTick)));
        }
        return bands;
    }

    public static IReadOnlyList<TimelineTrack> Tracks(SphereRegimeSchedule schedule, long currentTick)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var active = schedule.RegimeAt(currentTick)?.ActiveLayers.Select(l => l.Value).ToHashSet() ?? new HashSet<string>();
        // Full track list = union of every regime's layers, in first-seen order.
        var seen = new List<string>();
        var set = new HashSet<string>();
        foreach (var r in schedule.Regimes)
            foreach (var l in r.ActiveLayers)
                if (set.Add(l.Value)) seen.Add(l.Value);
        return seen.Select(layer => new TimelineTrack(layer, active.Contains(layer))).ToList();
    }

    // Stable variant (color) key per regime — themed by the boom-hud renderer.
    public static string VariantFor(string regimeId) => regimeId switch
    {
        "magma-ocean"     => "danger",   // hot
        "stagnant-lid"    => "warning",  // cooling
        "mobile-plate"    => "success",  // plates
        "primordial-steam" or "secondary-co2" or "coupled-climate" => "info",
        _ => "default",
    };
}
