using System;
using FantaSim.App.World.Composition;

namespace FantaSim.App.World.Seam;

public sealed class TimelineController : ITimelineController
{
    private readonly GlobeView _globe;
    private long _tick;
    private TimelineLayerSelection? _selectedLayer;
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
    public TimelineLayerSelection? SelectedLayer => _selectedLayer;

    public void Play() => _onPlay?.Invoke();
    public void Pause() => _onPause?.Invoke();
    public void SeekTo(long tick) => _onSeek?.Invoke(tick);

    public void SelectLayer(string sphereId, string layerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sphereId);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);

        var next = new TimelineLayerSelection(sphereId, layerId);
        if (Equals(_selectedLayer, next))
            return;

        _selectedLayer = next;
        LayerSelectionChanged?.Invoke(_selectedLayer);
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
