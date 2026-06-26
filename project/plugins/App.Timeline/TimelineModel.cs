using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Composition;
using FantaSim.World.Contracts.Quantities;
using FantaSim.World.Contracts.Units;

namespace FantaSim.App.Timeline;

/// <summary>One canonical time ladder rung (e.g. jz, ka, kb).</summary>
public sealed record TimelineLadderRung(string Symbol, double UnitTicks);

public static class TimelineModel
{
    private static readonly int[] NiceMultipliers = { 1, 2, 5, 10, 20, 50, 100, 200, 500 };

    private static readonly IReadOnlyList<TimelineLadderRung> LadderRungs = BuildLadderRungs();

    /// <summary>Exposes the canonical odometer ladder rungs, ordered from finest to coarsest.</summary>
    public static IReadOnlyList<TimelineLadderRung> GetLadderRungs() => LadderRungs;

    public static IReadOnlyList<TimelineBand> Bands(
        SphereRegimeSchedule schedule,
        long maxTick,
        long currentTick,
        long? viewStartTick = null,
        long? viewEndTick = null)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (maxTick <= 0) throw new ArgumentOutOfRangeException(nameof(maxTick));
        var (viewStart, viewEnd) = NormalizeViewRange(maxTick, viewStartTick, viewEndTick);
        double span = viewEnd - viewStart;
        var bands = new List<TimelineBand>(schedule.Regimes.Count);
        foreach (var r in schedule.Regimes)
        {
            long end = Math.Min(r.EndTick, maxTick);          // clamp open-end (long.MaxValue) to maxTick
            if (r.StartTick >= viewEnd || end <= viewStart) continue;

            long visibleStart = Math.Max(r.StartTick, viewStart);
            long visibleEnd = Math.Min(end, viewEnd);
            if (visibleEnd <= visibleStart) continue;

            double start = (visibleStart - viewStart) / span;
            double width = (visibleEnd - visibleStart) / span;
            bands.Add(new TimelineBand(
                r.RegimeId,
                start,
                width,
                VariantFor(r.RegimeId),
                r.Contains(currentTick),
                visibleStart,
                visibleEnd));
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

    public static IReadOnlyList<TimelineRulerMark> Ruler(
        long viewStartTick,
        long viewEndTick,
        int targetMarkCount = 8)
    {
        if (targetMarkCount <= 0) throw new ArgumentOutOfRangeException(nameof(targetMarkCount));
        if (viewEndTick <= viewStartTick) return Array.Empty<TimelineRulerMark>();

        long step = RulerStepTicks(viewStartTick, viewEndTick, targetMarkCount);
        long first = AlignUp(viewStartTick, step);
        double span = viewEndTick - viewStartTick;
        var scale = SelectTimeLadderScale(viewStartTick, viewEndTick, step);
        var marks = new List<TimelineRulerMark>();

        for (long tick = first; tick <= viewEndTick; tick += step)
        {
            double fraction = (tick - viewStartTick) / span;
            marks.Add(new TimelineRulerMark(tick, fraction, TimelineTimeFormatter.ForTick(tick, scale)));
            if (long.MaxValue - tick < step) break;
        }

        return marks;
    }

    /// <summary>Ruler whose step and labels are locked to the selected ladder rung.</summary>
    public static IReadOnlyList<TimelineRulerMark> Ruler(
        long viewStartTick,
        long viewEndTick,
        TimelineLadderRung rung)
    {
        if (viewEndTick <= viewStartTick) return Array.Empty<TimelineRulerMark>();

        long step = Math.Max(1L, (long)Math.Round(rung.UnitTicks));
        long first = AlignUp(viewStartTick, step);
        double span = viewEndTick - viewStartTick;
        var scale = rung.Symbol;
        var marks = new List<TimelineRulerMark>();

        for (long tick = first; tick <= viewEndTick; tick += step)
        {
            double fraction = (tick - viewStartTick) / span;
            marks.Add(new TimelineRulerMark(tick, fraction, TimelineTimeFormatter.ForTick(tick, scale)));
            if (long.MaxValue - tick < step) break;
        }

        return marks;
    }

    public static long RulerStepTicks(
        long viewStartTick,
        long viewEndTick,
        int targetMarkCount = 8)
    {
        if (targetMarkCount <= 0) throw new ArgumentOutOfRangeException(nameof(targetMarkCount));
        if (viewEndTick <= viewStartTick) return 0L;

        double ideal = (viewEndTick - viewStartTick) / (double)targetMarkCount;
        var candidates = RulerStepCandidates().ToArray();
        return candidates.FirstOrDefault(step => step >= ideal, candidates[^1]);
    }

    private static IEnumerable<long> RulerStepCandidates()
    {
        var candidates = new SortedSet<long> { 1L };
        foreach (var rung in LadderRungs)
        {
            foreach (int multiplier in NiceMultipliers)
            {
                long step = Math.Max(1L, (long)Math.Round(rung.UnitTicks * multiplier, MidpointRounding.AwayFromZero));
                candidates.Add(step);
            }
        }

        AppendDecadeLadder(candidates, candidates.Max);
        return candidates;
    }

    // Guarantees a contiguous 1/2/5 * 10^k ladder up to ceiling, independent of
    // the time ladder, so collapsed sub-ka rungs cannot open a candidate gap.
    private static void AppendDecadeLadder(SortedSet<long> candidates, long ceiling)
    {
        for (long decade = 1L; decade > 0 && decade <= ceiling;)
        {
            candidates.Add(decade);
            AddIfPositive(candidates, decade * 2L);
            AddIfPositive(candidates, decade * 5L);
            if (decade > long.MaxValue / 10L) break;
            decade *= 10L;
        }
    }

    private static void AddIfPositive(SortedSet<long> set, long value)
    {
        if (value > 0L) set.Add(value);
    }

    private static IReadOnlyList<TimelineLadderRung> BuildLadderRungs()
    {
        var profile = BaselineScaleProfiles.GeospherePlateTime;
        var ladder = new List<(string Symbol, double Cumulative)> { (profile.Steps[0].FromScaleSymbol, 1.0) };
        foreach (var step in profile.Steps)
        {
            var ratio = (double)step.RatioNumerator / step.RatioDenominator;
            ladder.Add((step.ToScaleSymbol, ladder[^1].Cumulative * ratio));
        }

        var anchor = ladder.Single(entry => entry.Symbol == profile.AnchorScaleSymbol).Cumulative;
        return ladder
            .Select(entry => new TimelineLadderRung(entry.Symbol, UnitConverter.TicksPerMegaAnnum * entry.Cumulative / anchor))
            .ToArray();
    }

    internal static string SelectTimeLadderScale(long viewStartTick, long viewEndTick, long stepTick)
    {
        var viewSpan = Math.Abs((double)viewEndTick - viewStartTick);
        var maxMagnitude = Math.Max(viewSpan, Math.Abs((double)stepTick));
        if (maxMagnitude <= 0)
            return BaselineScaleProfiles.GeospherePlateTime.AnchorScaleSymbol;

        return SelectRungForSpan((long)maxMagnitude).Symbol;
    }

    /// <summary>Selects the coarsest rung whose unit still fits inside the view span.</summary>
    public static TimelineLadderRung SelectRungForSpan(long viewSpanTicks)
    {
        if (LadderRungs.Count == 0)
            throw new InvalidOperationException("Timeline ladder profile produced no rungs.");
        if (viewSpanTicks <= 0L)
            return LadderRungs[0];

        var selected = LadderRungs[0];
        foreach (var rung in LadderRungs)
        {
            if (viewSpanTicks >= rung.UnitTicks)
                selected = rung;
            else
                break;
        }

        return selected;
    }

    /// <summary>Returns the next finer rung, or null if <paramref name="current"/> is already the finest.</summary>
    public static TimelineLadderRung? TryGetFinerRung(TimelineLadderRung current)
    {
        int index = FindRungIndex(current);
        if (index <= 0)
            return null;
        return LadderRungs[index - 1];
    }

    /// <summary>Returns the next coarser rung, or null if <paramref name="current"/> is already the coarsest.</summary>
    public static TimelineLadderRung? TryGetCoarserRung(TimelineLadderRung current)
    {
        int index = FindRungIndex(current);
        if (index < 0 || index >= LadderRungs.Count - 1)
            return null;
        return LadderRungs[index + 1];
    }

    private static int FindRungIndex(TimelineLadderRung rung)
    {
        for (int i = 0; i < LadderRungs.Count; i++)
        {
            if (LadderRungs[i].Symbol == rung.Symbol)
                return i;
        }
        return -1;
    }

    /// <summary>Visible span for a rung, expressed as a fixed number of rung units.</summary>
    public static long SpanTicksForRung(TimelineLadderRung rung, int units = 10)
        => Math.Max(1L, (long)Math.Round(rung.UnitTicks * units));

    private static long AlignUp(long value, long step)
    {
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        long remainder = value % step;
        return remainder == 0 ? value : value + step - remainder;
    }

    private static (long Start, long End) NormalizeViewRange(long maxTick, long? viewStartTick, long? viewEndTick)
    {
        long start = Math.Clamp(viewStartTick ?? 0L, 0L, Math.Max(0L, maxTick - 1));
        long end = Math.Clamp(viewEndTick ?? maxTick, start + 1L, maxTick);
        return (start, end);
    }
}
