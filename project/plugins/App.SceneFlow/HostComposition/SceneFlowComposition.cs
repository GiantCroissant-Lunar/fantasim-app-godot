using FantaSim.App.Common;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.SceneFlow;

public static class SceneFlowComposition
{
    public static void ComposeSceneFlow(HostCompositionContext ctx)
    {
        var log = ctx.LoggerFactory.CreateLogger("HostComposition.SceneFlow");
        var sceneFlow = new FantaSim.App.SceneFlow.Services.Service(
            ctx.Composition.RootServices,
            ctx.Registry,
            ctx.LoggerFactory);
        ctx.Registry.Register<FantaSim.App.SceneFlow.IService>(
            sceneFlow,
            new ServiceRegistration { Tags = new[] { "scene-flow" }, Description = "SceneFlow service" });
        log.LogInformation("[Host] registered: SceneFlow");
    }
}
