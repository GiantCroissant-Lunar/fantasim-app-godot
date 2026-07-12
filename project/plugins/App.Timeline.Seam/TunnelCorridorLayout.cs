using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.Timeline;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Five-slot carousel layout for the asymmetric cockpit's focused track window
/// (vault/plans/2026-07-12-asymmetric-cockpit-tunnel-plan.md Task 6). Pure Godot-free:
/// selects non-archived registry tracks in stable order, normalizes a cyclic focus index, builds
/// the focus-0/-1/+1/-2/+2 window with (SphereId, LayerId) dedup, and snaps accumulated wall angle
/// to 30deg steps. Also retains the rung-resolution path consumed by the focused-corridor rebinding.
/// </summary>
public static class TunnelCorridorLayout
{
    public const int VisibleTrackSlots = 5;
    public const double TrackSlotPitchDegrees = 30d;
    public const double LeftFocusAngleDegrees = 180d;

    private static readonly int[] WindowRelativeSlots = { 0, -1, 1, -2, 2 };

    public readonly record struct TunnelTrackSlot(
        LayerTrackDescriptor Descriptor,
        int RelativeSlot,
        double CenterAngleDegrees)
    {
        public bool IsFocused => RelativeSlot == 0;
    }

    public readonly record struct TunnelCarouselSnap(
        long StepDelta,
        int FocusIndex,
        double SnappedAngleDegrees);

    /// <summary>Source tracks in stable registry order with archived entries dropped.</summary>
    public static IReadOnlyList<LayerTrackDescriptor> SelectSourceTracks(LayerTrackRegistrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Tracks
            .Where(t => !string.Equals(t.State, LayerTrackStates.Archived, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>Empty focus is -1; any non-empty count focuses index 0.</summary>
    public static int InitialFocusIndex(int trackCount)
        => trackCount > 0 ? 0 : -1;

    /// <summary>
    /// Returns the first active, non-archived track in registry order; when none is active, returns
    /// the first non-archived track. An all-archived or empty list returns -1.
    /// </summary>
    public static int InitialFocusIndex(
        IReadOnlyList<LayerTrackDescriptor> tracks,
        IReadOnlyList<bool> activityFlags)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(activityFlags);
        if (tracks.Count != activityFlags.Count)
            throw new ArgumentException("Track and activity counts must match.", nameof(activityFlags));

        var fallback = -1;
        for (var i = 0; i < tracks.Count; i++)
        {
            if (string.Equals(tracks[i].State, LayerTrackStates.Archived, StringComparison.Ordinal))
                continue;

            if (fallback < 0)
                fallback = i;
            if (activityFlags[i])
                return i;
        }

        return fallback;
    }

    /// <summary>Cyclic normalization into [0, trackCount). Zero or negative count returns -1.</summary>
    public static int NormalizeFocusIndex(int focusIndex, int trackCount)
    {
        if (trackCount <= 0)
            return -1;

        var norm = focusIndex % trackCount;
        return norm < 0 ? norm + trackCount : norm;
    }

    /// <summary>The descriptor at the normalized focus, or null for an empty/invalid window.</summary>
    public static LayerTrackDescriptor? ResolveFocusedTrack(IReadOnlyList<LayerTrackDescriptor> tracks, int focusIndex)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        if (tracks.Count == 0 || focusIndex < 0 || focusIndex >= tracks.Count)
            return null;

        return tracks[focusIndex];
    }

    /// <summary>
    /// Builds the five-or-fewer-slot focused window. Candidates are resolved in the order focus 0,
    /// previous -1, next +1, second previous -2, second next +2; each is deduplicated by
    /// (SphereId, LayerId) so one track mounts at most once. The returned slots are sorted by
    /// relative slot (-2..+2). CenterAngleDegrees = LeftFocusAngleDegrees + relativeSlot *
    /// 30deg - accumulatedDegrees, so a positive (clockwise) accumulated angle shifts the next
    /// track toward left-center before the focus index advances on snap.
    /// </summary>
    public static IReadOnlyList<TunnelTrackSlot> BuildFocusedWindow(
        IReadOnlyList<LayerTrackDescriptor> tracks,
        int focusIndex,
        double accumulatedDegrees = 0d)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        var count = tracks.Count;
        if (count == 0)
            return Array.Empty<TunnelTrackSlot>();

        var normalized = NormalizeFocusIndex(focusIndex, count);
        var mounted = new Dictionary<(string SphereId, string LayerId), int>();
        var result = new List<TunnelTrackSlot>();

        foreach (var relative in WindowRelativeSlots)
        {
            var absoluteIndex = NormalizeCyclic(normalized + relative, count);
            var candidate = tracks[absoluteIndex];
            var identity = (candidate.SphereId, candidate.LayerId);
            if (mounted.ContainsKey(identity))
                continue;

            mounted[identity] = relative;
            result.Add(new TunnelTrackSlot(
                Descriptor: candidate,
                RelativeSlot: relative,
                CenterAngleDegrees: LeftFocusAngleDegrees + (relative * TrackSlotPitchDegrees) - accumulatedDegrees));
        }

        return result.OrderBy(s => s.RelativeSlot).ToList();
    }

    /// <summary>
    /// Snaps accumulated wall angle to whole 30deg steps. StepDelta is
    /// RoundAwayFromZero(accumulatedDegrees / 30) as a long; the focus index advances cyclically.
    /// trackCount &lt;= 1 is a hard no-op (returns zero delta, unchanged focus).
    /// </summary>
    public static TunnelCarouselSnap SnapFocus(int focusIndex, int trackCount, double accumulatedDegrees)
    {
        if (!double.IsFinite(accumulatedDegrees))
            throw new ArgumentOutOfRangeException(
                nameof(accumulatedDegrees),
                accumulatedDegrees,
                "Accumulated angle must be finite; a non-finite value would cast to a garbage carousel step.");

        if (trackCount <= 1)
            return new TunnelCarouselSnap(StepDelta: 0, FocusIndex: focusIndex, SnappedAngleDegrees: 0d);

        var stepDelta = (long)Math.Round(accumulatedDegrees / TrackSlotPitchDegrees, MidpointRounding.AwayFromZero);
        var normalizedLong = ((long)focusIndex + (stepDelta % trackCount)) % trackCount;
        if (normalizedLong < 0)
            normalizedLong += trackCount;
        var nextFocus = (int)normalizedLong;
        var snappedAngle = stepDelta * TrackSlotPitchDegrees;
        return new TunnelCarouselSnap(StepDelta: stepDelta, FocusIndex: nextFocus, SnappedAngleDegrees: snappedAngle);
    }

    /// <summary>
    /// Resolves a track's declared rung symbol against the canonical ladder; a null or unrecognized
    /// symbol degrades to globalFallback (the Unity round-trip guarantee), never a throw.
    /// </summary>
    public static TimelineLadderRung ResolveCorridorRung(string? trackRungSymbol, TimelineLadderRung globalFallback)
    {
        if (trackRungSymbol is null)
            return globalFallback;

        return TimelineModel.GetLadderRungs().FirstOrDefault(r => r.Symbol == trackRungSymbol) ?? globalFallback;
    }

    private static int NormalizeCyclic(int value, int modulus)
    {
        var norm = value % modulus;
        return norm < 0 ? norm + modulus : norm;
    }
}
