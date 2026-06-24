using FantaSim.App.Common;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.World;

public static class WorldComposition
{
    public static void ComposeWorld(HostCompositionContext ctx)
    {
        var log = ctx.LoggerFactory.CreateLogger("HostComposition.World");
        var world = new FantaSim.App.World.Services.Service(ctx.Registry);
        ctx.Registry.Register<FantaSim.App.World.IService>(
            world,
            new ServiceRegistration { Tags = new[] { "world" }, Description = "World service" });
        log.LogInformation("[Host] registered: World");

        var projection = new FantaSim.App.World.FieldView.Services.FieldViewService(
            world,
            new[] { "app.elevation-m" },
            new[] { "app.elevation-m" });
        ctx.Registry.Register<FantaSim.App.World.FieldView.Services.FieldViewService>(
            projection,
            new ServiceRegistration { Tags = new[] { "world", "projection" }, Description = "Field view service" });
        log.LogInformation("[Host] World detail: projection registered");

        // Register the World axis as a node-function provider (mirrors how ComposeIii registers the iii
        // provider). It claims the world/geosphere/crust function families; the general App.NodeGraph
        // GraphExecutor resolves crust.generate to it. Pure C# (no Godot rendering yet).
        var worldProvider = new FantaSim.App.World.WorldFunctionProvider(ctx.LoggerFactory);
        ctx.Registry.Register<FantaSim.App.NodeGraph.INodeFunctionProvider>(
            worldProvider,
            new ServiceRegistration { Tags = new[] { "world", "nodegraph-provider" }, Description = "World node-function provider (crust pipeline)" });
        log.LogInformation("[Host] World detail: crust function provider registered");
    }
}
