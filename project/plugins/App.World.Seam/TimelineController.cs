using System;
using FantaSim.App.World.Composition;

namespace FantaSim.App.World.Seam;

/// <summary>Resident adapter: bridges the bundled HUD (via ITimelineController in the shared kernel)
/// to the Godot RegimeTimelineTransport + GlobeView. Registered into the shared registry in ComposeWorldView.</summary>
public sealed class TimelineController : ITimelineController
{
    private readonly RegimeTimelineTransport _transport;
    private readonly GlobeView _globe;
    private long _lastTick = -1;

    public TimelineController(RegimeTimelineTransport transport, GlobeView globe,
        SphereRegimeSchedule geosphere, SphereRegimeSchedule atmosphere, long maxTick)
    {
        _transport = transport; _globe = globe;
        GeosphereSchedule = geosphere; AtmosphereSchedule = atmosphere; MaxTick = maxTick;
    }

    public long Tick => _globe.Tick;
    public long MaxTick { get; }
    public bool IsPlaying => _transport.IsPlaying;
    public SphereRegimeSchedule GeosphereSchedule { get; }
    public SphereRegimeSchedule AtmosphereSchedule { get; }
    public void Play() => _transport.SetPlaying(true);
    public void Pause() => _transport.SetPlaying(false);
    public void SeekTo(long tick) => _transport.JumpTo(tick);
    public event Action<long>? TickChanged;

    /// <summary>Call once per frame from a resident _Process (e.g. the transport) to emit TickChanged.</summary>
    public void PumpTick()
    {
        var t = _globe.Tick;
        if (t != _lastTick) { _lastTick = t; TickChanged?.Invoke(t); }
    }
}
