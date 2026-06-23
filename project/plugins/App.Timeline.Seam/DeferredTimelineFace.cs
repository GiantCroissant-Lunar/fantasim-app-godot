using System;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// A deferred-binding ITimelineFace proxy. Constructed by Host.cs BEFORE the timeline bundle
/// scene instantiates the real TimelineFace. Buffers Play/Pause/Seek/ApplyView calls (no-op
/// until Connect is called), then forwards to the real face. The real face calls Connect(this)
/// in its _Ready, at which point the proxy swaps to live forwarding.
/// </summary>
public sealed class DeferredTimelineFace : ITimelineFace
{
    private readonly ITimelineController _controller;
    private ITimelineFace? _target;

    public DeferredTimelineFace(ITimelineController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public void Connect(ITimelineFace target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public void Play()
    {
        if (_target is not null) _target.Play();
        else _controller.Play();
    }

    public void Pause()
    {
        if (_target is not null) _target.Pause();
        else _controller.Pause();
    }

    public void SeekTo(long tick)
    {
        if (_target is not null) _target.SeekTo(tick);
        else _controller.SeekTo(tick);
    }

    public void ApplyView(TimelineViewSnapshot snapshot)
    {
        _target?.ApplyView(snapshot);
    }
}
