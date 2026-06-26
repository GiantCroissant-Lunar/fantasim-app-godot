using System;

namespace FantaSim.App.World.Composition;

public interface ITimelineController
{
    long Tick { get; }
    long MaxTick { get; }
    bool IsPlaying { get; }
    SphereRegimeSchedule GeosphereSchedule { get; }
    SphereRegimeSchedule AtmosphereSchedule { get; }
    TimelineLayerSelection? SelectedLayer { get; }
    void Play();
    void Pause();
    void SeekTo(long tick);
    void SelectLayer(string sphereId, string layerId);
    event Action<long>? TickChanged;
    event Action<TimelineLayerSelection?>? LayerSelectionChanged;

    void PushTick(long tick);
    void RegisterPlayback(Action onPlay, Action onPause, Action<long> onSeek, Func<bool> checkPlaying);
    void UnregisterPlayback();
}
