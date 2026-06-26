using System.Text.Json;
using FantaSim.App.Common;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Iii.Seam;

public static class IiiComposition
{
    private static IDisposable? _iiiBundleWatch;

    public static void ComposeIii(HostCompositionContext ctx, Godot.SceneTree tree, Godot.Node hostNode)
    {
        var loggerFactory = ctx.LoggerFactory;
        var registry = ctx.Registry;
        var log = loggerFactory.CreateLogger("HostComposition.Iii");

        // Load the data-only iii worker manifest bundle. No pluginAssembly means no collectible ALC:
        // the bundle contributes workers.json, which the resident IiiFunctionProvider reads to decide
        // which function families the iii axis claims. A reload of iii.pck refreshes the catalog.
        var resource = registry.Get<FantaSim.App.Resource.IService>();
        if (!resource.IsLoaded("iii"))
        {
            // Data-only load completes synchronously on the main thread; block briefly so the
            // provider is created with the bundle-backed catalog.
            resource.LoadFromDirectoryAsync("iii").ConfigureAwait(false).GetAwaiter().GetResult();
        }

        var catalog = new FantaSim.App.Iii.Seam.IiiWorkerBundleCatalog();
        catalog.Refresh();
        registry.Register<FantaSim.App.Iii.IIiiWorkerCatalog>(
            catalog,
            new ServiceRegistration { Tags = new[] { "iii", "worker-catalog" }, Description = "iii worker catalog (bundle-backed)" });
        resource.RuntimeChanged += (_, _) => catalog.Refresh();
        _iiiBundleWatch?.Dispose();
        _iiiBundleWatch = resource.WatchResource("iii");

        var bridge = new FantaSim.App.Iii.Seam.IiiBridge(loggerFactory);
        bridge.Name = "IiiBridge";
        hostNode.AddChild(bridge);
        registry.Register<FantaSim.App.Iii.IIiiInvoker>(
            bridge,
            new ServiceRegistration { Tags = new[] { "iii", "invoker" }, Description = "iii bridge invoker (gdext)" });

        var provider = new FantaSim.App.Iii.IiiFunctionProvider(bridge, catalog, loggerFactory);
        registry.Register<FantaSim.App.NodeGraph.INodeFunctionProvider>(
            provider,
            new ServiceRegistration { Tags = new[] { "iii", "nodegraph-provider" }, Description = "iii node-function provider" });

        var orchestration = new FantaSim.App.Iii.IiiOrchestrator(
            new[] { (FantaSim.App.NodeGraph.INodeFunctionProvider)provider },
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
        log.LogInformation(
            "registered: Iii (bridge, function provider, orchestration, 1 command, {WorkerCount} worker definitions)",
            catalog.Workers.Count);
    }
}
