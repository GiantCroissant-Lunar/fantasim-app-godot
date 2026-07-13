using System;
using System.Collections.Generic;
using FantaSim.App.Common;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.World;

public static class WorldComposition
{
    public static IDisposable ComposeWorld(HostCompositionContext ctx)
    {
        var log = ctx.LoggerFactory.CreateLogger("HostComposition.World");
        var disposables = new List<IDisposable>(2);
        var actorSystem = ctx.Registry.TryGet<Akka.Actor.ActorSystem>();
        var world = new FantaSim.App.World.Services.Service(
            ctx.Registry,
            actorSystem!);
        disposables.Add(ctx.Registry.RegisterOwned<FantaSim.App.World.IService>(
            world,
            new ServiceRegistration { Tags = new[] { "world" }, Description = "World service" }));
        log.LogInformation("[Host] registered: World");

        disposables.Add(world);

        var worldProvider = new FantaSim.App.World.WorldFunctionProvider(ctx.LoggerFactory);
        disposables.Add(ctx.Registry.RegisterOwned<FantaSim.App.NodeGraph.INodeFunctionProvider>(
            worldProvider,
            new ServiceRegistration { Tags = new[] { "world", "nodegraph-provider" }, Description = "World node-function provider (crust pipeline)" }));
        log.LogInformation("[Host] World detail: crust function provider registered");

        return new CompositeDisposable(disposables);
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _items;
        public CompositeDisposable(List<IDisposable> items) => _items = items;
        public void Dispose()
        {
            foreach (var d in _items)
                try { d.Dispose(); } catch { }
            _items.Clear();
        }
    }
}
