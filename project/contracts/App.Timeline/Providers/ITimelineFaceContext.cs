using System.Threading;
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

    /// <summary>
    /// Cancellation-aware: the face cancels the token at sever/unbind and the render MUST honor
    /// it promptly. A filmstrip render executes bundle code on a threadpool thread, so its call
    /// stack roots BOTH the timeline and world ALCs — an uncancelled render outlives the
    /// hot-reload collection probe and fails the "old ALC collected" gate (clrstack-proven
    /// 2026-07-10: MantleAnomalyField.EvaluateAt still on the stack at verdict time).
    /// </summary>
    Func<LayerFilmstripPreviewRequest, CancellationToken, LayerFilmstripPreviewMap?> FilmstripPreviewProvider { get; }

    /// <summary>
    /// The layer-&gt;track registry (slice 1). Reaches the face through this resident-context
    /// proxy property, same as <see cref="Controller"/> -- never a static. Null only if the
    /// timeline bundle failed to compose it (degrade to no tracks, never throw).
    /// </summary>
    ILayerTrackRegistry? LayerTrackRegistry { get; }

    ILoggerFactory LoggerFactory { get; }

    double TicksPerSecond { get; }
}
