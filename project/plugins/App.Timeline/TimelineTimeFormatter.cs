using System;
using FantaSim.World.Contracts.Quantities;
using FantaSim.World.Contracts.Units;

namespace FantaSim.App.Timeline;

public static class TimelineTimeFormatter
{
    public static string ForTick(long tick)
        => ForTick(tick, targetScaleSymbol: null);

    public static string ForViewRange(long viewStartTick, long viewEndTick, long stepTick)
    {
        var scale = TimelineModel.SelectTimeLadderScale(viewStartTick, viewEndTick, stepTick);
        string viewRange = $"view {ForTick(viewStartTick, scale)} - {ForTick(viewEndTick, scale)}";
        return stepTick > 0
            ? $"{viewRange} | step {ForTick(stepTick, scale)}"
            : viewRange;
    }

    internal static string ForTick(long tick, string? targetScaleSymbol)
    {
        double anchorAmount = tick / (double)UnitConverter.TicksPerMegaAnnum;
        return CanonicalDisplayFormatter.Format(
            anchorAmount,
            BaselineScaleProfiles.GeospherePlateTimeV1,
            new CanonicalFormatterOptions { IncludeUnitSuffix = true, TargetScaleSymbol = targetScaleSymbol });
    }
}
