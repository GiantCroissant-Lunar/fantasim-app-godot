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

    ILoggerFactory LoggerFactory { get; }

    double TicksPerSecond { get; }
}
