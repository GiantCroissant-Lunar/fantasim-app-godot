using System;
using FantaSim.App.Common;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Ecs;

public static class EcsComposition
{
    public static (FantaSim.App.Ecs.IService?, bool) ComposeEcs(HostCompositionContext ctx)
    {
        var log = ctx.LoggerFactory.CreateLogger("HostComposition.Ecs");
        var ecs = new FantaSim.App.Ecs.Services.Service(
            ctx.Composition.Bootstrap.ActorSystem,
            ctx.LoggerFactory);
        ctx.Registry.Register<FantaSim.App.Ecs.IService>(
            ecs,
            new ServiceRegistration { Tags = new[] { "ecs" }, Description = "ECS service" });
        bool ready;
        try
        {
            ecs.CreateWorld(new FantaSim.App.Ecs.EcsWorldSpec("main"));
            ecs.InitializeWorld("main");
            ready = true;
            log.LogInformation("[Host] ECS world 'main' created + initialized");
        }
        catch (Exception ex)
        {
            ready = false;
            log.LogError("[Host] ECS bootstrap failed: {Message}", ex.Message);
        }
        log.LogInformation("[Host] registered: Ecs");
        return (ready ? ecs : null, ready);
    }
}
