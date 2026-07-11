using System;
using Godot;

namespace FantaSim.App.Presentation.Tunnel;

/// <summary>Visual-affordance-free input relay: the binder is a plain C# class (mirrors
/// PlanetPresentationBinder), not a Node, so it cannot override _Input itself. This tiny Node
/// forwards press/motion/release to a delegate the binder supplies -- same shape as
/// TimelinePlayheadHandle's "input handled by the owner, not the Control" precedent.
/// vault/plans/2026-07-11-tunnel-slice1-plan.md.</summary>
internal sealed partial class TunnelInputRelay : Node3D
{
    public Action<InputEvent>? OnEvent;
    public override void _UnhandledInput(InputEvent @event) => OnEvent?.Invoke(@event);
}
