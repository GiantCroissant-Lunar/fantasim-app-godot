using System;
using System.Collections.Generic;
using FantaSim.App.World.Composition;

namespace FantaSim.App.World.Seam;

public sealed class TimelineController : ITimelineController
{
    private readonly GlobeView _globe;
    private long _tick;
    // D5: the stacked active set (pure helper). SelectedLayer (primary) is the first element, or
    // null when empty — preserved for single-select back-compat (graph followers, atmosphere-rim
    // gate). LayerSelectionChanged fires on every mutation carrying the new primary.
    private readonly LayerActiveSet _activeLayers = new();
    private Action? _onPlay;
    private Action? _onPause;
    private Action<long>? _onSeek;
    private Func<bool>? _checkPlaying;

    public TimelineController(GlobeView globe,
        SphereRegimeSchedule geosphere, SphereRegimeSchedule atmosphere, long maxTick)
    {
        _globe = globe ?? throw new ArgumentNullException(nameof(globe));
        GeosphereSchedule = geosphere ?? throw new ArgumentNullException(nameof(geosphere));
        AtmosphereSchedule = atmosphere ?? throw new ArgumentNullException(nameof(atmosphere));
        MaxTick = maxTick;
    }

    public long Tick => _tick;
    public long MaxTick { get; }
    public bool IsPlaying => _checkPlaying?.Invoke() ?? false;
    public SphereRegimeSchedule GeosphereSchedule { get; }
    public SphereRegimeSchedule AtmosphereSchedule { get; }
    public TimelineLayerSelection? SelectedLayer => _activeLayers.Primary;
    public IReadOnlyList<TimelineLayerSelection> ActiveLayers => _activeLayers.Layers;

    public void Play() => _onPlay?.Invoke();
    public void Pause() => _onPause?.Invoke();
    public void SeekTo(long tick) => _onSeek?.Invoke(tick);

    // D5 back-compat: SelectLayer makes the active set EXACTLY {layer}. Single-select callers (the
    // timeline.select_layer ingress, graph followers) see no behavior change.
    public void SelectLayer(string sphereId, string layerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sphereId);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);

        if (_activeLayers.SetExclusive(new TimelineLayerSelection(sphereId, layerId)))
            LayerSelectionChanged?.Invoke(_activeLayers.Primary);
    }

    // D5: toggle a layer's membership. Always a mutation, so LayerSelectionChanged always fires
    // (carrying the new primary, even when the primary did not change — stacked-set consumers
    // reading ActiveLayers need to react to every toggle).
    public void ToggleLayer(string sphereId, string layerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sphereId);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);

        _activeLayers.Toggle(new TimelineLayerSelection(sphereId, layerId));
        LayerSelectionChanged?.Invoke(_activeLayers.Primary);
    }

    public event Action<long>? TickChanged;
    public event Action<TimelineLayerSelection?>? LayerSelectionChanged;

    public void PushTick(long tick)
    {
        _tick = tick;
        _globe.SetTick(tick);
        var regime = GeosphereSchedule.RegimeAt(tick);
        if (regime is not null)
        {
            _globe.SetRegime(regime.RegimeId, regime.ShowsPlateFeatures, regime.DefaultColorByField);
        }
        else
        {
            _globe.SetRegime("mobile-plate", true, null);
        }
        TickChanged?.Invoke(tick);
    }

    public void RegisterPlayback(Action onPlay, Action onPause, Action<long> onSeek, Func<bool> checkPlaying)
    {
        _onPlay = onPlay;
        _onPause = onPause;
        _onSeek = onSeek;
        _checkPlaying = checkPlaying;
    }

    public void UnregisterPlayback()
    {
        _onPlay = null;
        _onPause = null;
        _onSeek = null;
        _checkPlaying = null;
    }
}
