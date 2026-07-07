using System;
using System.Collections.Generic;

namespace FantaSim.App.World.Composition;

public interface ITimelineController
{
    long Tick { get; }
    long MaxTick { get; }
    bool IsPlaying { get; }
    SphereRegimeSchedule GeosphereSchedule { get; }
    SphereRegimeSchedule AtmosphereSchedule { get; }
    TimelineLayerSelection? SelectedLayer { get; }

    /// <summary>
    /// The stacked ACTIVE SET of timeline layers (D5), in insertion order. Empty means the world
    /// view (no diagnostic layer focused). Several layers can be active simultaneously; the
    /// presentation composes each active layer's contribution. <see cref="SelectedLayer"/> is the
    /// PRIMARY (the first active layer, or null when empty) preserved for single-select back-compat
    /// (graph followers, the atmosphere-rim gate).
    /// </summary>
    /// <remarks>
    /// Defaulted to an empty set so single-select test fakes (crust-trigger, graph-port) compile
    /// unchanged; production controllers override with a real ordered set.
    /// </remarks>
    IReadOnlyList<TimelineLayerSelection> ActiveLayers => Array.Empty<TimelineLayerSelection>();

    void Play();
    void Pause();
    void SeekTo(long tick);
    void SeekTo(long tick, TimelineTickOrigin origin) => SeekTo(tick);
    void SelectLayer(string sphereId, string layerId);

    /// <summary>
    /// Toggles a layer's membership in the <see cref="ActiveLayers"/> set (D5). Idempotent: an
    /// already-active layer is removed; an inactive one is appended (preserving insertion order so
    /// the primary stays stable). <see cref="LayerSelectionChanged"/> fires on every successful
    /// mutation carrying the new primary (even when the primary itself did not change), so stacked-
    /// set consumers reading <see cref="ActiveLayers"/> react to every toggle.
    /// </summary>
    /// <remarks>
    /// Defaulted to a no-op for single-select test fakes; production controllers override.
    /// </remarks>
    void ToggleLayer(string sphereId, string layerId) { }

    event Action<long>? TickChanged;
    event Action<TimelineLayerSelection?>? LayerSelectionChanged;

    void PushTick(long tick);
    void PushTick(long tick, TimelineTickOrigin origin) => PushTick(tick);
    void RegisterPlayback(Action onPlay, Action onPause, Action<long> onSeek, Func<bool> checkPlaying);
    void UnregisterPlayback();
}
