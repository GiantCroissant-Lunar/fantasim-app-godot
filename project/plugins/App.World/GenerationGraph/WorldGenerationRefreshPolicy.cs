using System;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World.GenerationGraph;

public static class WorldGenerationRefreshPolicy
{
    public static bool ShouldRefreshGlobe(WorldGenerationChangedEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return string.Equals(evt.ChangeType, "generation", StringComparison.Ordinal);
    }
}
