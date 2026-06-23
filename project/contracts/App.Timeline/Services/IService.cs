using System.Threading;
using System.Threading.Tasks;
using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.Timeline;

/// <summary>
/// T1 timeline service contract. The timeline is a paradigm UI that drives playback of the
/// world's ITimelineController. Other plugins resolve this via the registry to drive Play/Pause/
/// Seek without referencing the bundle or seam directly. The T3 orchestrator
/// (plugins/App.Timeline/Services/Service.cs) implements this; the T2 proxy forwards.
/// </summary>
[ServiceContract]
[SelectionStrategy(SelectionMode.HighestPriority)]
public interface IService
{
    /// <summary>Current canonical tick.</summary>
    long Tick { get; }

    /// <summary>Maximum tick the timeline can reach.</summary>
    long MaxTick { get; }

    /// <summary>Current playback state.</summary>
    TimelinePlaybackState State { get; }

    /// <summary>Start playback (transitions to Playing).</summary>
    Task PlayAsync(CancellationToken cancellationToken = default);

    /// <summary>Pause playback (transitions to Idle).</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Seek to a specific tick (clamped to [0, MaxTick]). Transitions to Scrubbing.</summary>
    Task SeekAsync(long tick, CancellationToken cancellationToken = default);

    /// <summary>Raised after any tick/state change. May be raised off the main thread.</summary>
    event Action<TimelineViewSnapshot>? ViewChanged;
}
