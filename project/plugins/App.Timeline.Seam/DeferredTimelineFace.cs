using FantaSim.Cross;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Resident-to-collectible-ALC binder for ITimelineFace. The source generator emits
/// BindCrossTarget / UnbindCrossTarget / IsCrossBound + forwarding methods for Play,
/// Pause, SeekTo, ApplyView (the [CrossDelegate]-marked surface). When the timeline
/// bundle hot-reloads, Host.cs calls UnbindCrossTarget() before the old ALC unloads,
/// then BindCrossTarget(newFace) after the new scene instantiates.
///
/// The T3 Service holds a reference to this binder (as ITimelineFace). When no target
/// is bound, the generated forwarding methods return default/no-op -- the T3 Service
/// already has its own ITimelineController reference for the fallback path.
/// </summary>
[CrossService(typeof(ITimelineFace))]
public sealed partial class DeferredTimelineFace : ITimelineFace
{
    // The source generator emits all forwarding members. This hand-written part is
    // intentionally empty -- the binder is a pure forwarder with no custom state.
}
