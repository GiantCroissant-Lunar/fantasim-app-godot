using System.Text.Json;
using FantaSim.App.Common;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Iii.Seam;

public static class IiiComposition
{
    public static void ComposeIii(HostCompositionContext ctx, Godot.SceneTree tree, Godot.Node hostNode)
    {
        var loggerFactory = ctx.LoggerFactory;
        var registry = ctx.Registry;
        var log = loggerFactory.CreateLogger("HostComposition.Iii");

        var bridge = new FantaSim.App.Iii.Seam.IiiBridge(loggerFactory);
        bridge.Name = "IiiBridge";
        hostNode.AddChild(bridge);
        registry.Register<FantaSim.App.Iii.IIiiInvoker>(
            bridge,
            new ServiceRegistration { Tags = new[] { "iii", "invoker" }, Description = "iii bridge invoker (gdext)" });

        var provider = new FantaSim.App.Iii.IiiFunctionProvider(bridge, loggerFactory);
        registry.Register<FantaSim.App.NodeGraph.INodeFunctionProvider>(
            provider,
            new ServiceRegistration { Tags = new[] { "iii", "nodegraph-provider" }, Description = "iii node-function provider" });

        var orchestration = new FantaSim.App.Iii.IiiOrchestrator(
            new[] { (FantaSim.App.NodeGraph.INodeFunctionProvider)provider },
            bridge,
            loggerFactory);
        registry.Register<FantaSim.App.Command.Orchestration.IIiiOrchestration>(
            orchestration,
            new ServiceRegistration { Tags = new[] { "iii", "orchestration" }, Description = "iii orchestration seam" });

        var commandService = registry.Get<FantaSim.App.Command.IService>();
        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: FantaSim.App.Iii.IiiOrchestrator.WellKnownCommands.RunTextTo3d,
                Title: "Run text to 3D", Description: "Executes the text to 3D iii pipeline graph.", Category: "pipeline"),
            async (payload, ct) =>
            {
                var r = await orchestration.TriggerAsync(new FantaSim.App.Command.CommandRequest(
                    Command: FantaSim.App.Iii.IiiOrchestrator.WellKnownCommands.RunTextTo3d, PayloadJson: payload), ct);
                return JsonSerializer.Serialize(r);
            });
        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: FantaSim.App.Iii.IiiOrchestrator.WellKnownCommands.Ping,
                Title: "Ping iii", Description: "Round-trips test.echo through the iii bridge.", Category: "iii"),
            async (payload, ct) =>
            {
                var r = await orchestration.TriggerAsync(new FantaSim.App.Command.CommandRequest(
                    Command: FantaSim.App.Iii.IiiOrchestrator.WellKnownCommands.Ping, PayloadJson: payload), ct);
                return JsonSerializer.Serialize(r);
            });

        log.LogInformation("registered: Iii (bridge, function provider, orchestration, 2 commands)");
    }
}
