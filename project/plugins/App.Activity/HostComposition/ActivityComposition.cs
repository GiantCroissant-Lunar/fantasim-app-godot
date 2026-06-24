using FantaSim.App.Common;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Activity;

public static class ActivityComposition
{
    public static void ComposeActivity(HostCompositionContext ctx)
    {
        var log = ctx.LoggerFactory.CreateLogger("HostComposition.Activity");
        var activity = new FantaSim.App.Activity.Services.Service(
            ctx.Registry.Get<CrosscutFoundation.Messaging.IMessageBus>(),
            ctx.LoggerFactory);
        ctx.Registry.Register<FantaSim.App.Activity.IService>(
            activity,
            new ServiceRegistration { Tags = new[] { "activity" }, Description = "Activity ledger service" });
        log.LogInformation("[Host] registered: Activity");
    }
}
