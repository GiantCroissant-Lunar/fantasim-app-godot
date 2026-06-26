using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;
using FantaSim.App.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        FantaSim.App.Timeline.Seam.TimelineFace.ResidentCommandClient =
            registry.TryGet<FantaSim.App.Command.IClient>();

        // Recomposition happens after timeline bundle reload. Registry registrations are additive,
        // so replace the previous resident service before registering the proxy for the freshly
        // instantiated face.
        foreach (var existing in registry.GetAll<FantaSim.App.Timeline.IService>())
        {
            if (existing is IDisposable disposable)
                disposable.Dispose();
        }
        registry.UnregisterAll<FantaSim.App.Timeline.IService>();

        // Build the T3 service with the controller's schedules. The T3 Service drives the face
        // via ITimelineFace; the face also calls back into the controller (PushTick) during
        // animation playback.
        var timelineService = new FantaSim.App.Timeline.Services.Service(
            deferredFace,
            controller,
            ctx.LoggerFactory);
        registry.Register<FantaSim.App.Timeline.IService>(
            timelineService,
            new ServiceRegistration
            {
                OwnerId = "timeline.resident",
                Priority = 100,
                Tags = new[] { "timeline" },
                Description = "Timeline playback service"
            });

        var commandService = registry.TryGet<FantaSim.App.Command.IService>();
        commandService?.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: "timeline.seek",
                Title: "Seek timeline",
                Description: "Moves the timeline playhead to a tick through the active timeline service. Payload: {\"tick\":123}.",
                Category: "timeline"),
            async (payloadJson, cancellationToken) =>
            {
                var tick = ParseSeekTick(payloadJson, controller.MaxTick);
                await timelineService.SeekAsync(tick, cancellationToken).ConfigureAwait(false);
                if (controller.Tick != tick)
                    controller.PushTick(tick);
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    requestedTick = tick,
                    tick = controller.Tick,
                    serviceTick = timelineService.Tick,
                    maxTick = controller.MaxTick,
                    state = timelineService.State.ToString(),
                });
            });

        commandService?.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: "timeline.select_layer",
                Title: "Select timeline layer",
                Description: "Selects the active layer track and updates the bound world-generation graph.",
                Category: "timeline"),
            (payloadJson, _) =>
            {
                var (sphereId, layerId) = ParseLayerSelection(payloadJson);
                if (!IsLayerActive(controller, sphereId, layerId))
                    throw new InvalidOperationException(
                        $"Layer '{layerId}' in sphere '{sphereId}' is not active at tick {controller.Tick}.");

                controller.SelectLayer(sphereId, layerId);
                return Task.FromResult<string?>(JsonSerializer.Serialize(new
                {
                    ok = true,
                    sphereId,
                    layerId,
                    tick = controller.Tick,
                }));
            });

        log.LogInformation("registered: Timeline (IService + resident TimelineFace)");
    }

    private static long ParseSeekTick(string? payloadJson, long maxTick)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new ArgumentException("timeline.seek payload is required.");

        var payload = JsonNode.Parse(payloadJson) as JsonObject
            ?? throw new ArgumentException("timeline.seek payload must be a JSON object.");
        if (!TryReadLong(payload["tick"], out var tick))
            throw new ArgumentException("timeline.seek requires numeric 'tick'.");
        return Math.Clamp(tick, 0L, Math.Max(0L, maxTick));
    }

    private static (string SphereId, string LayerId) ParseLayerSelection(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new ArgumentException("timeline.select_layer payload is required.");

        var payload = JsonNode.Parse(payloadJson) as JsonObject
            ?? throw new ArgumentException("timeline.select_layer payload must be a JSON object.");
        var sphereId = payload["sphereId"]?.GetValue<string>();
        var layerId = payload["layerId"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(sphereId) || string.IsNullOrWhiteSpace(layerId))
            throw new ArgumentException("timeline.select_layer requires 'sphereId' and 'layerId'.");
        return (sphereId, layerId);
    }

    private static bool TryReadLong(JsonNode? node, out long value)
    {
        value = 0;
        if (node is not JsonValue jsonValue)
            return false;
        if (jsonValue.TryGetValue<long>(out value))
            return true;
        if (jsonValue.TryGetValue<string>(out var text)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }
        return false;
    }

    private static bool IsLayerActive(
        FantaSim.App.World.Composition.ITimelineController controller,
        string sphereId,
        string layerId)
    {
        var schedule = string.Equals(sphereId, "atmosphere", StringComparison.Ordinal)
            ? controller.AtmosphereSchedule
            : controller.GeosphereSchedule;

        return schedule.RegimeAt(controller.Tick)?.ActiveLayers.Any(layer =>
            string.Equals(layer.Value, layerId, StringComparison.Ordinal)) == true;
    }
}
