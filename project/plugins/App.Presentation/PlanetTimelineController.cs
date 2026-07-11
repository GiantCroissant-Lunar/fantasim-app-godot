using FantaSim.App.World;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Presentation;

internal sealed class PlanetTimelineController : ITimelineController
{
    private readonly Action<long, TimelineTickOrigin> _applyTick;
    private long _tick;
    private long _maxTick = 1;
    // D5: stacked active set (pure helper); SelectedLayer is the primary (first or null).
    private readonly LayerActiveSet _activeLayers = new();
    private Action? _onPlay;
    private Action? _onPause;
    private Action<long>? _onSeek;
    private Func<bool>? _checkPlaying;

    public PlanetTimelineController(Action<long, TimelineTickOrigin> applyTick)
    {
        _applyTick = applyTick ?? throw new ArgumentNullException(nameof(applyTick));
        GeosphereSchedule = EmptySchedule("geosphere");
        AtmosphereSchedule = EmptySchedule("atmosphere");
    }

    public long Tick => _tick;

    public long MaxTick => _maxTick;

    public bool IsPlaying => _checkPlaying?.Invoke() ?? false;

    public SphereRegimeSchedule GeosphereSchedule { get; private set; }

    public SphereRegimeSchedule AtmosphereSchedule { get; private set; }

    public TimelineLayerSelection? SelectedLayer => _activeLayers.Primary;

    public IReadOnlyList<TimelineLayerSelection> ActiveLayers => _activeLayers.Layers;

    public event Action<long>? TickChanged;
    public event Action<TimelineLayerSelection?>? LayerSelectionChanged;

    public void UpdateFrom(PlanetPresentationDocument document)
    {
        GeosphereSchedule = document.GeosphereSchedule ?? EmptySchedule("geosphere");
        AtmosphereSchedule = document.AtmosphereSchedule ?? EmptySchedule("atmosphere");
        _maxTick = Math.Max(1L, document.MaxTick);
        PushTick(Math.Clamp(_tick, 0L, _maxTick));
    }

    public void Play() => _onPlay?.Invoke();

    public void Pause() => _onPause?.Invoke();

    public void SeekTo(long tick)
        => SeekTo(tick, TimelineTickOrigin.Standard);

    public void SeekTo(long tick, TimelineTickOrigin origin)
        => _onSeek?.Invoke(Math.Clamp(tick, 0L, _maxTick));

    // D5 back-compat: SelectLayer makes the active set EXACTLY {layer}.
    public void SelectLayer(string sphereId, string layerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sphereId);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);

        if (_activeLayers.SetExclusive(new TimelineLayerSelection(sphereId, layerId)))
            LayerSelectionChanged?.Invoke(_activeLayers.Primary);
    }

    // D5: toggle membership; LayerSelectionChanged always fires (stacked-set consumers react to
    // every toggle even when the primary is unchanged).
    public void ToggleLayer(string sphereId, string layerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sphereId);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);

        _activeLayers.Toggle(new TimelineLayerSelection(sphereId, layerId));
        LayerSelectionChanged?.Invoke(_activeLayers.Primary);
    }

    public void PushTick(long tick)
        => PushTick(tick, TimelineTickOrigin.Standard);

    public void PushTick(long tick, TimelineTickOrigin origin)
    {
        _tick = Math.Clamp(tick, 0L, _maxTick);
        _applyTick(_tick, origin);
        if (origin != TimelineTickOrigin.ScrubPreview)
            TickChanged?.Invoke(_tick);
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

    private static SphereRegimeSchedule EmptySchedule(string sphereId)
        => new(new SphereId(sphereId), Array.Empty<SphereRegime>());
}
