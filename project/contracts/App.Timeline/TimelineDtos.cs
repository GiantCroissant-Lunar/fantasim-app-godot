namespace FantaSim.App.Timeline;

/// <summary>Playback state the timeline service reports to consumers.</summary>
public enum TimelinePlaybackState
{
    Idle,
    Playing,
    Scrubbing,
}

/// <summary>
/// Engine-agnostic view snapshot the T3 service sends to the T4 seam (and any other
/// subscriber). Contains everything the face needs to render one frame: the tick, the
/// playback state, the active regime id, and the max tick. The seam maps this to Godot
/// UI state. Mirrors the CameraSpec pattern: pure data, no Godot types.
/// </summary>
/// <param name="Tick">Current canonical tick.</param>
/// <param name="State">Current playback state (Idle/Playing/Scrubbing).</param>
/// <param name="ActiveRegimeId">The geosphere regime active at <paramref name="Tick"/>, or null.</param>
/// <param name="MaxTick">The maximum tick the timeline can reach.</param>
public sealed record TimelineViewSnapshot(
    long Tick,
    TimelinePlaybackState State,
    string? ActiveRegimeId,
    long MaxTick);

/// <summary>One regime band on the timeline lane. Pure data - the seam renders it.</summary>
public sealed record TimelineBand(
    string RegimeId,
    double StartFraction,
    double WidthFraction,
    string Variant,
    bool IsActive,
    long StartTick,
    long EndTick);

/// <summary>One track (layer) on the timeline lane.</summary>
public sealed record TimelineTrack(string LayerId, bool IsActive);

/// <summary>One ruler mark.</summary>
public sealed record TimelineRulerMark(long Tick, double Fraction, string Label);
