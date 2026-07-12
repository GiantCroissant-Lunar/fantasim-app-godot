using System.Threading.Tasks;
using FantaSim.Cross;

namespace FantaSim.App.Timeline.Providers;

public readonly record struct TimelineHudState(bool Visible, long ModeEpoch);

/// <summary>Godot-free guard shared by the resident face and lifecycle tests.</summary>
public static class TimelineHudReplayPolicy
{
    public static bool CanApply(
        int capturedBindGeneration,
        int currentBindGeneration,
        long incomingModeEpoch,
        long currentModeEpoch,
        bool incomingVisible = true,
        bool forceHudVisible = false)
        => capturedBindGeneration == currentBindGeneration
            && incomingModeEpoch >= currentModeEpoch
            && (incomingVisible || !forceHudVisible);
}

/// <summary>
/// The timeline service's engine seam: the Godot-facing backend that renders the timeline
/// UI and drives the AnimationPlayer/Tree playback. The T3 service owns this seam and
/// delegates engine work to it (implemented by App.Timeline.Seam's TimelineFace). Mirrors
/// App.Camera's Providers/ICameraRig. Deliberately LEANER than IService: playback state
/// tracking, tick accounting, and ViewChanged fan-out are the service's job, not the face's.
/// </summary>
public interface ITimelineFace
{
    /// <summary>
    /// Re-resolve the registry-mediated resident context. Used when the world controller changes
    /// without re-instantiating the timeline scene.
    /// </summary>
    [CrossDelegate]
    void RebindResidentContext();

    /// <summary>
    /// Start the animation playback (transitions the AnimationTree to the "playing" state).
    /// Called on the main thread by the T3 (which may receive the request off-thread).
    /// </summary>
    [CrossDelegate]
    void Play();

    /// <summary>
    /// Pause the animation playback (transitions the AnimationTree to the "idle" state).
    /// Called on the main thread by the T3.
    /// </summary>
    [CrossDelegate]
    void Pause();

    /// <summary>
    /// Seek the AnimationPlayer to the tick and transition to "scrub". Called on the main
    /// thread by the T3. The face must NOT call back into the service during this method
    /// (the service already knows the tick - it called Seek).
    /// </summary>
    [CrossDelegate]
    void SeekTo(long tick);

    /// <summary>
    /// Apply a view snapshot to the face (update status label, playhead position, band
    /// highlighting, ruler). Called after every tick or state change. The face may marshal
    /// this onto the main thread if called off-thread.
    /// </summary>
    [CrossDelegate]
    void ApplyView(TimelineViewSnapshot snapshot);

    /// <summary>
    /// Show or hide the whole 2D timeline HUD. Owned by the tunnel-view product rule (rotating
    /// tunnel design §4a): while the 3D tunnel timeline is enabled the 2D HUD is hidden, and it
    /// returns when the tunnel is disabled. The face may marshal onto the main thread if called
    /// off-thread. Visibility is presentation-only state — playback/tick accounting continue
    /// unaffected while hidden.
    /// </summary>
    [CrossDelegate]
    void ApplyHudState(TimelineHudState state);
}
