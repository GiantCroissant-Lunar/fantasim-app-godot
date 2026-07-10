using FantaSim.App.World;
using FantaSim.App.World.Composition;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Timeline.Providers;

/// <summary>
/// Registry-mediated resident context consumed by the Godot timeline face. The timeline bundle
/// owns this object and unregisters it before its ALC or the world controller can unload.
/// </summary>
public interface ITimelineFaceContext
{
    ITimelineController Controller { get; }

    ITimelineFaceProxy Proxy { get; }

    object? CommandClient { get; }

    Func<long, WorldGenerationGraphFamilyDocument?> GenerationGraphFamilyProvider { get; }

    Func<LayerFilmstripPreviewRequest, LayerFilmstripPreviewMap?> FilmstripPreviewProvider { get; }

    /// <summary>
    /// The layer-&gt;track registry (slice 1). Reaches the face through this resident-context
    /// proxy property, same as <see cref="Controller"/> -- never a static. Null only if the
    /// timeline bundle failed to compose it (degrade to no tracks, never throw).
    /// </summary>
    ILayerTrackRegistry? LayerTrackRegistry { get; }

    ILoggerFactory LoggerFactory { get; }

    double TicksPerSecond { get; }
}
