using System;
using FantaSim.App.World.Composition;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// Keeps a world-generation graph family source aligned with a timeline cursor.
/// Regime changes select the bound regime graph; ticks inside the same regime only
/// recompose the current graph so manually opened layer subgraphs remain usable.
/// </summary>
public sealed class WorldGenerationTimelineGraphBinding : IDisposable
{
    private readonly ITimelineController _timeline;
    private readonly WorldGenerationGraphFamilySource _source;
    private readonly string _scheduleKind;
    private readonly string? _sphereId;
    private string? _currentRegimeId;
    private bool _disposed;

    private WorldGenerationTimelineGraphBinding(
        ITimelineController timeline,
        WorldGenerationGraphFamilySource source,
        string scheduleKind,
        string? sphereId)
    {
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _scheduleKind = string.IsNullOrWhiteSpace(scheduleKind)
            ? throw new ArgumentException("Schedule kind must be non-empty.", nameof(scheduleKind))
            : scheduleKind;
        _sphereId = sphereId;

        _timeline.TickChanged += OnTickChanged;
        Follow(_timeline.Tick);
    }

    public static WorldGenerationTimelineGraphBinding BindGeosphere(
        ITimelineController timeline,
        WorldGenerationGraphFamilySource source)
        => new(
            timeline,
            source,
            WorldRegimeScheduleKinds.Sphere,
            WorldGenerationGraphDefaults.GeosphereSphereId);

    public void Dispose()
    {
        if (_disposed) return;
        _timeline.TickChanged -= OnTickChanged;
        _disposed = true;
    }

    private void OnTickChanged(long tick) => Follow(tick);

    private void Follow(long tick)
    {
        var regime = _timeline.GeosphereSchedule.RegimeAt(tick);
        if (regime is null)
        {
            _currentRegimeId = null;
            _source.SetTick(tick);
            return;
        }

        if (!string.Equals(_currentRegimeId, regime.RegimeId, StringComparison.Ordinal))
        {
            _source.SelectRegime(_scheduleKind, regime.RegimeId, tick, _sphereId);
            _currentRegimeId = regime.RegimeId;
            return;
        }

        _source.SetTick(tick);
    }
}
