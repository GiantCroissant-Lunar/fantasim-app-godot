using FantaSim.App.Timeline.Providers;
using FantaSim.Cross;

namespace FantaSim.App.Timeline;

/// <summary>
/// Collectible timeline face proxy. The generated partial forwards ITimelineFace calls to the
/// resident Godot face that binds itself through ITimelineFaceContext.
/// </summary>
[CrossService(typeof(ITimelineFace))]
public sealed partial class DeferredTimelineFace : ITimelineFaceProxy
{
}
