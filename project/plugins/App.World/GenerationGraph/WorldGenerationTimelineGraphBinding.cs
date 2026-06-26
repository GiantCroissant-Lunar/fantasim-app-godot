using System;
using FantaSim.App.World.Composition;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// Keeps a world-generation graph family source aligned with a timeline cursor.
/// Regime changes select the bound regime graph; an explicit layer selection switches
/// to the layer graph resolved for the active regime at the current tick. If the selected
/// layer has no binding for the current regime/tick, the binding falls back to the regime
/// graph rather than fabricating an unavailable layer graph.
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
        _timeline.LayerSelectionChanged += OnLayerSelectionChanged;
        Follow(_timeline.Tick, _timeline.SelectedLayer);
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
        _timeline.LayerSelectionChanged -= OnLayerSelectionChanged;
        _disposed = true;
    }

    private void OnTickChanged(long tick) => Follow(tick, _timeline.SelectedLayer);
    private void OnLayerSelectionChanged(TimelineLayerSelection? selection) => Follow(_timeline.Tick, selection);

    private void Follow(long tick, TimelineLayerSelection? selectedLayer)
    {
        if (selectedLayer is not null)
        {
            var schedule = ScheduleFor(selectedLayer.SphereId);
            var regime = schedule.RegimeAt(tick);
            if (regime is not null)
            {
                var layerBinding = WorldGenerationGraphFamilyComposer.TryFindLayerBinding(
                    _source.Family,
                    selectedLayer.SphereId,
                    selectedLayer.LayerId,
                    regime.RegimeId);

                if (layerBinding is not null)
                {
                    if (string.Equals(_source.ActiveGraphId, layerBinding.GraphId, StringComparison.Ordinal))
                    {
                        _source.SetTick(tick);
                    }
                    else
                    {
                        _source.SelectGraph(layerBinding.GraphId, tick);
                    }

                    _currentRegimeId = regime.RegimeId;
                    return;
                }
            }
        }

        var defaultRegime = _timeline.GeosphereSchedule.RegimeAt(tick);
        if (defaultRegime is null)
        {
            _currentRegimeId = null;
            _source.SetTick(tick);
            return;
        }

        if (!string.Equals(_currentRegimeId, defaultRegime.RegimeId, StringComparison.Ordinal))
        {
            _source.SelectRegime(_scheduleKind, defaultRegime.RegimeId, tick, _sphereId);
            _currentRegimeId = defaultRegime.RegimeId;
            return;
        }

        _source.SetTick(tick);
    }

    private SphereRegimeSchedule ScheduleFor(string sphereId)
        => string.Equals(sphereId, WorldGenerationGraphDefaults.AtmosphereSphereId, StringComparison.Ordinal)
            ? _timeline.AtmosphereSchedule
            : _timeline.GeosphereSchedule;
}
