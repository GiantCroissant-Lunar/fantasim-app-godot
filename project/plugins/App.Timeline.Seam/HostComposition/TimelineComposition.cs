using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;
using FantaSim.App.Common;

namespace FantaSim.App.Timeline.Seam;

public static class TimelineComposition
{
    public static void ComposeTimeline(HostCompositionContext ctx)
    {
        var log = ctx.LoggerFactory.CreateLogger("HostComposition.Timeline");
        var registry = ctx.Registry;
        var controller = registry.TryGet<FantaSim.App.World.Composition.ITimelineController>();
        if (controller is null)
        {
            log.LogWarning("Timeline: no ITimelineController registered; timeline service will be inert.");
            return;
        }

        // Build the deferred face proxy and set the resident statics the T4 face reads in _Ready.
        var deferredFace = new FantaSim.App.Timeline.Seam.DeferredTimelineFace();
        FantaSim.App.Timeline.Seam.TimelineFace.ResidentController = controller;
        FantaSim.App.Timeline.Seam.TimelineFace.ResidentProxy = deferredFace;
        FantaSim.App.Timeline.Seam.TimelineFace.ResidentLoggerFactory = ctx.LoggerFactory;

        // Build the T3 service with the controller's schedules. The T3 Service drives the face
        // via ITimelineFace; the face also calls back into the controller (PushTick) during
        // animation playback.
        var timelineService = new FantaSim.App.Timeline.Services.Service(
            deferredFace,
            controller.GeosphereSchedule,
            controller.AtmosphereSchedule,
            controller.MaxTick,
            ctx.LoggerFactory);
        registry.Register<FantaSim.App.Timeline.IService>(
            timelineService,
            new ServiceRegistration { Tags = new[] { "timeline" }, Description = "Timeline playback service" });

        log.LogInformation("registered: Timeline (IService + resident TimelineFace)");
    }
}
