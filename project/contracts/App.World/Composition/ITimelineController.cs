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
    event Action<long>? TickChanged;   // fired when Tick advances (per frame while playing, or on seek)
}
