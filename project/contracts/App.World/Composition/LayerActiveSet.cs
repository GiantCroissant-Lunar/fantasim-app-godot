using System.Collections.Generic;

namespace FantaSim.App.World.Composition;

/// <summary>
/// The stacked ACTIVE SET of timeline layers (D5): an insertion-ordered list of
/// <see cref="TimelineLayerSelection"/> supporting toggle membership and exclusive set. Pure (no
/// Godot, no events) so the toggle/select/primary semantics are unit-testable without bootstrapping
/// the engine. Both <c>TimelineController</c> (World.Seam) and <c>PlanetTimelineController</c>
/// (Presentation fallback) delegate to an instance of this type; they own the
/// <see cref="ITimelineController.LayerSelectionChanged"/> event firing on each mutation.
/// </summary>
/// <remarks>
/// <para><b>Primary.</b> The first element of the ordered list (or null when empty). Toggling a
/// layer ON appends it to the END, so the primary stays stable — graph followers reading
/// <see cref="ITimelineController.SelectedLayer"/> are not yanked around when a secondary layer is
/// stacked on top of the current primary. Toggling the primary OFF shifts the primary to the next
/// remaining layer.</para>
/// <para><b>Exclusive set.</b> <see cref="SetExclusive"/> clears the list and adds exactly one
/// selection — the back-compat single-select semantics for <c>SelectLayer</c> /
/// <c>timeline.select_layer</c>.</para>
/// </remarks>
public sealed class LayerActiveSet
{
    private readonly List<TimelineLayerSelection> _layers = new();

    public IReadOnlyList<TimelineLayerSelection> Layers => _layers;

    /// <summary>The first active layer (the single-select back-compat primary), or null when empty.</summary>
    public TimelineLayerSelection? Primary => _layers.Count > 0 ? _layers[0] : null;

    /// <summary>
    /// Replaces the set with EXACTLY <paramref name="selection"/>. Returns true when the set changed
    /// (so the controller can fire <see cref="ITimelineController.LayerSelectionChanged"/> only on a
    /// real mutation, preserving the prior single-select no-op-on-same guard).
    /// </summary>
    public bool SetExclusive(TimelineLayerSelection selection)
    {
        if (_layers.Count == 1 && Equals(_layers[0], selection))
            return false;

        _layers.Clear();
        _layers.Add(selection);
        return true;
    }

    /// <summary>
    /// Toggles <paramref name="selection"/>'s membership. Returns true always (a toggle is always a
    /// mutation) — the controller fires <see cref="ITimelineController.LayerSelectionChanged"/> on
    /// every call so stacked-set consumers reading <see cref="Layers"/> react even when the primary
    /// itself did not change.
    /// </summary>
    public bool Toggle(TimelineLayerSelection selection)
    {
        for (int i = 0; i < _layers.Count; i++)
        {
            if (Equals(_layers[i], selection))
            {
                _layers.RemoveAt(i);
                return true;
            }
        }
        _layers.Add(selection);
        return true;
    }

    public void Clear()
    {
        _layers.Clear();
    }
}
