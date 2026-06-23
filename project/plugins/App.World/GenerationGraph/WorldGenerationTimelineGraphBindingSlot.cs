using System;
using FantaSim.App.World.Composition;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// Owns the currently mounted timeline-follow binding for a world-generation graph view.
/// Rebinding always tears down the previous subscription before optionally creating a new one.
/// </summary>
public sealed class WorldGenerationTimelineGraphBindingSlot : IDisposable
{
    private WorldGenerationTimelineGraphBinding? _binding;
    private bool _disposed;

    public void Rebind(
        ITimelineController? timeline,
        WorldGenerationGraphFamilySource source,
        bool followTimeline)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);

        _binding?.Dispose();
        _binding = null;

        if (!followTimeline || timeline is null)
            return;

        _binding = WorldGenerationTimelineGraphBinding.BindGeosphere(timeline, source);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _binding?.Dispose();
        _binding = null;
        _disposed = true;
    }
}
