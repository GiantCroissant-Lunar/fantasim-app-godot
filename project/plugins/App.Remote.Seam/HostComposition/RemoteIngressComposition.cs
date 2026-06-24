using FantaSim.App.Common;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Remote.Seam;

/// <summary>
/// T4 remote-ingress module. When FANTASIM_REMOTE_ENABLED=1, stands up the Godot main-thread bridge
/// (the real <see cref="FantaSim.App.Remote.IMainThreadDispatcher"/>), registers it, then composes and
/// starts the transport. Zero footprint when disabled. Mirrors the other HostComposition seams.
/// </summary>
public static class RemoteIngressComposition
{
    public static void ComposeRemoteIngress(HostCompositionContext ctx, Godot.Node hostNode)
    {
        if (!FantaSim.App.Remote.RemoteOptions.FromEnvironment().Enabled)
            return; // zero footprint when off

        var log = ctx.LoggerFactory.CreateLogger("HostComposition.Remote");

        var bridge = new RemoteBridgeNode();
        hostNode.AddChild(bridge);
        ctx.Registry.Register<FantaSim.App.Remote.IMainThreadDispatcher>(
            bridge,
            new ServiceRegistration { Tags = new[] { "remote", "dispatcher" }, Description = "Godot main-thread bridge (remote ingress)" });

        FantaSim.App.Remote.RemoteComposition.Compose(ctx.Registry, ctx.LoggerFactory);

        log.LogInformation("registered: Remote ingress (main-thread bridge + transport).");
    }
}
