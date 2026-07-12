using System;
using System.Collections.Generic;

namespace FantaSim.App.Timeline.Seam;

internal readonly record struct TunnelCorridorFramePlan(
    int TrackIndex,
    TimelineFilmstripFrameSlot Slot,
    double DepthFraction);

internal readonly record struct TunnelAxialCuePlan(
    int Index,
    double Z,
    double FractionFromCurrent);

/// <summary>Bounds the first tunnel slice to five visible tracks and four samples per track.
/// A partial range at MaxTick still divides depth by the canonical coarse unit, so it leaves the
/// unused far segment empty instead of stretching the remaining ticks to the throat.</summary>
internal static class TunnelCorridorFilmstripPolicy
{
    private static readonly double[] AxialCueFractions = { 0.22d, 0.68d };
    internal const int MaxVisibleTracks = TunnelCorridorLayout.VisibleTrackSlots;
    internal const int SamplesPerTrack = 4;
    private const double TrackIdentityLabelOffset = 0.18d;

    internal static IReadOnlyList<TunnelCorridorFramePlan> PlanVisibleTracks(
        int visibleTrackCount,
        long currentTick,
        long maxTick,
        long coarseUnitTicks)
    {
        if (visibleTrackCount <= 0 || maxTick <= currentTick || coarseUnitTicks <= 0)
            return Array.Empty<TunnelCorridorFramePlan>();

        var boundedTrackCount = Math.Min(visibleTrackCount, MaxVisibleTracks);
        var availableTicks = maxTick - currentTick;
        var requestEnd = availableTicks < coarseUnitTicks
            ? maxTick
            : currentTick + coarseUnitTicks;
        var slots = TimelineFilmstrip.PlanSlots(
            currentTick,
            requestEnd,
            TimelineFilmstrip.ThumbnailWidth * SamplesPerTrack);
        var result = new List<TunnelCorridorFramePlan>(boundedTrackCount * slots.Count);

        for (var trackIndex = 0; trackIndex < boundedTrackCount; trackIndex++)
        {
            foreach (var slot in slots)
            {
                result.Add(new TunnelCorridorFramePlan(
                    trackIndex,
                    slot,
                    (slot.Tick - currentTick) / (double)coarseUnitTicks));
            }
        }

        return result;
    }

    internal static IReadOnlyList<TunnelAxialCuePlan> PlanAxialCues(
        double currentPlaneZ,
        double throatZ)
    {
        if (!double.IsFinite(currentPlaneZ)
            || !double.IsFinite(throatZ)
            || throatZ >= currentPlaneZ)
            return Array.Empty<TunnelAxialCuePlan>();

        var span = throatZ - currentPlaneZ;
        var result = new TunnelAxialCuePlan[AxialCueFractions.Length];
        for (var i = 0; i < AxialCueFractions.Length; i++)
        {
            var fraction = AxialCueFractions[i];
            result[i] = new TunnelAxialCuePlan(
                i,
                currentPlaneZ + (span * fraction),
                fraction);
        }

        return result;
    }

    internal static double TrackIdentityLabelZ(double currentPlaneZ)
        => double.IsFinite(currentPlaneZ)
            ? currentPlaneZ + TrackIdentityLabelOffset
            : currentPlaneZ;

    internal static bool ShouldSupersedeWaitersOnReset(bool controllerDisposed)
        => !controllerDisposed;
}
