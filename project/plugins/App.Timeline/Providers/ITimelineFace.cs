using System.Threading.Tasks;

namespace FantaSim.App.Timeline.Providers;

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
    /// Start the animation playback (transitions the AnimationTree to the "playing" state).
    /// Called on the main thread by the T3 (which may receive the request off-thread).
    /// </summary>
    void Play();

    /// <summary>
    /// Pause the animation playback (transitions the AnimationTree to the "idle" state).
    /// Called on the main thread by the T3.
    /// </summary>
    void Pause();

    /// <summary>
    /// Seek the AnimationPlayer to the tick and transition to "scrub". Called on the main
    /// thread by the T3. The face must NOT call back into the service during this method
    /// (the service already knows the tick - it called Seek).
    /// </summary>
    void SeekTo(long tick);

    /// <summary>
    /// Apply a view snapshot to the face (update status label, playhead position, band
    /// highlighting, ruler). Called after every tick or state change. The face may marshal
    /// this onto the main thread if called off-thread.
    /// </summary>
    void ApplyView(TimelineViewSnapshot snapshot);
}
