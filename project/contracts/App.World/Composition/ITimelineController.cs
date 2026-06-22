using System;

namespace FantaSim.App.World.Composition;

public interface ITimelineController
{
    long Tick { get; }
    long MaxTick { get; }
    bool IsPlaying { get; }
    SphereRegimeSchedule GeosphereSchedule { get; }
    SphereRegimeSchedule AtmosphereSchedule { get; }
    void Play();
    void Pause();
    void SeekTo(long tick);
    event Action<long>? TickChanged;

    void PushTick(long tick);
    void RegisterPlayback(Action onPlay, Action onPause, Action<long> onSeek, Func<bool> checkPlaying);
    void UnregisterPlayback();
}
