using System;

namespace FantaSim.App.World.Composition;

/// <summary>
/// Pure (no Godot) layer-activation predicate shared by every layer-selection ingress —
/// <c>timeline.select_layer</c>, <c>timeline.toggle_layer</c>, and the DEPRECATED
/// <c>render.mantle</c> alias (directive 2, 2026-07-16). Centralizing the check here means the alias
/// rejects an inactive mantle layer through the EXACT same code path as <c>select_layer</c>, rather
/// than adding a third drift-prone copy (the <c>select_layer</c> silent-failure gotcha burned gates
/// twice; this is the single source of truth). Ordinal, case-sensitive: regime/layer ids are stable
/// lowercase strings.
/// </summary>
public static class LayerActivation
{
    /// <summary>
    /// True when <paramref name="layerId"/> is active under the regime current at
    /// <paramref name="tick"/> in <paramref name="schedule"/>. The pure core of the predicate —
    /// unit-testable without an <see cref="ITimelineController"/>.
    /// </summary>
    public static bool IsLayerActive(SphereRegimeSchedule schedule, long tick, string layerId)
    {
        if (schedule is null)
            return false;
        return schedule.RegimeAt(tick)?.ActiveLayers is { } layers
            && layers.Count > 0
            && AnyLayer(layers, layerId);
    }

    /// <summary>
    /// True when <paramref name="layerId"/> in <paramref name="sphereId"/> is active at the
    /// controller's current tick. Selects the geosphere or atmosphere schedule by sphere id (mirrors
    /// the original inlined check). Used by the production ingresses (the binder, the timeline
    /// plugin) so they never duplicate the predicate.
    /// </summary>
    public static bool IsLayerActive(ITimelineController controller, string sphereId, string layerId)
    {
        if (controller is null)
            return false;

        var schedule = string.Equals(sphereId, "atmosphere", StringComparison.Ordinal)
            ? controller.AtmosphereSchedule
            : controller.GeosphereSchedule;

        return IsLayerActive(schedule, controller.Tick, layerId);
    }

    private static bool AnyLayer(System.Collections.Generic.IReadOnlyList<LayerId> layers, string layerId)
    {
        for (int i = 0; i < layers.Count; i++)
        {
            if (string.Equals(layers[i].Value, layerId, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
