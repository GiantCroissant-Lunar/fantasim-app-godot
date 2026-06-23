using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.World.Composition;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Timeline.Services;

/// <summary>
/// The timeline service (<see cref="IService"/>) orchestrator - engine-agnostic (NO Godot).
/// Owns the playback state machine (Idle/Playing/Scrubbing) and delegates engine work to the
/// <see cref="ITimelineFace"/> seam (implemented by the Godot App.Timeline.Seam.TimelineFace).
/// Reads regime/layer schedules from the injected <see cref="SphereRegimeSchedule"/> pair to
/// build <see cref="TimelineViewSnapshot"/> for the face. Mirrors App.Camera.Services.Service.
/// </summary>
public sealed class Service : IService, IDisposable
{
    private readonly ITimelineFace _face;
    private readonly SphereRegimeSchedule _geosphere;
    private readonly SphereRegimeSchedule _atmosphere;
    private readonly ILogger _log;
    private long _tick;
    private TimelinePlaybackState _state = TimelinePlaybackState.Idle;
    private bool _disposed;

    public Service(
        ITimelineFace face,
        SphereRegimeSchedule geosphere,
        SphereRegimeSchedule atmosphere,
        long maxTick,
        ILoggerFactory loggerFactory)
    {
        _face = face ?? throw new ArgumentNullException(nameof(face));
        _geosphere = geosphere ?? throw new ArgumentNullException(nameof(geosphere));
        _atmosphere = atmosphere ?? throw new ArgumentNullException(nameof(atmosphere));
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));
        _log = loggerFactory.CreateLogger("App.Timeline.Service");
        MaxTick = maxTick;
    }

    public long Tick => _tick;
    public long MaxTick { get; }
    public TimelinePlaybackState State => _state;
    public event Action<TimelineViewSnapshot>? ViewChanged;

    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        _state = TimelinePlaybackState.Playing;
        _face.Play();
        PushView();
        _log.LogInformation("Timeline playing at tick {Tick}.", _tick);
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        _state = TimelinePlaybackState.Idle;
        _face.Pause();
        PushView();
        _log.LogInformation("Timeline paused at tick {Tick}.", _tick);
        return Task.CompletedTask;
    }

    public Task SeekAsync(long tick, CancellationToken cancellationToken = default)
    {
        tick = Math.Clamp(tick, 0L, MaxTick);
        _tick = tick;
        _state = TimelinePlaybackState.Scrubbing;
        _face.SeekTo(tick);
        PushView();
        _log.LogDebug("Timeline seek to tick {Tick}.", tick);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the face (via the resident controller callback) when the AnimationPlayer
    /// advances a frame. The face pushes the tick into the resident ITimelineController,
    /// which updates the globe; the service then updates its own tick + state + view.
    /// </summary>
    internal void AcceptTickFromFace(long tick)
    {
        if (_state != TimelinePlaybackState.Playing) return;
        _tick = Math.Clamp(tick, 0L, MaxTick);
        PushView();
    }

    private void PushView()
    {
        var regime = _geosphere.RegimeAt(_tick);
        var snap = new TimelineViewSnapshot(_tick, _state, regime?.RegimeId, MaxTick);
        _face.ApplyView(snap);
        ViewChanged?.Invoke(snap);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
