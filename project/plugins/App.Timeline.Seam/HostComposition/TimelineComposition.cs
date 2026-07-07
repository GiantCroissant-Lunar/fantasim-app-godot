using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;
using FantaSim.App.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FantaSim.App.Timeline.Seam;

public static class TimelineComposition
{
    // Held statically so re-compose (hot-reload) can unsubscribe the prior handler before
    // subscribing a new one, preventing duplicate TickChanged subscriptions that would
    // double-advance the service tick on every playhead frame.
    private static Action<long>? _tickChangedHandler;

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
        FantaSim.App.Timeline.Seam.TimelineFace.ResidentGenerationGraphFamilyProvider =
            tick => registry.TryGet<FantaSim.App.World.IService>()?.GetPlanetPresentationAsync(tick).GenerationGraphFamily;
        FantaSim.App.Timeline.Seam.TimelineFace.ResidentFilmstripPreviewProvider =
            request => registry.TryGet<FantaSim.App.World.IService>()?.GetLayerFilmstripPreview(request);
        // Default 5M ticks/sec (crust snapshot spacing); overridable by setting the static
        // before calling ComposeTimeline. The face reads this in BindResidentContext.
        FantaSim.App.Timeline.Seam.TimelineFace.ResidentTicksPerSecond = 5_000_000.0;

        // Recomposition happens after timeline bundle reload. Registry registrations are additive,
        // so replace the previous resident service before registering the proxy for the freshly
        // instantiated face.
        foreach (var existing in registry.GetAll<FantaSim.App.Timeline.IService>())
        {
            if (existing is IDisposable disposable)
                disposable.Dispose();
        }
        registry.UnregisterAll<FantaSim.App.Timeline.IService>();

        // Unsubscribe the prior TickChanged handler before re-subscribing so a hot-reload
        // re-compose does not stack two handlers (which would double-advance the service tick).
        if (_tickChangedHandler is not null)
            controller.TickChanged -= _tickChangedHandler;

        // Build the T3 service with the controller's schedules. The T3 Service drives the face
        // via ITimelineFace; the face also calls back into the controller (PushTick) during
        // animation playback.
        var timelineService = new FantaSim.App.Timeline.Services.Service(
            deferredFace,
            controller,
            ctx.LoggerFactory);

        // Wire the controller's TickChanged into the service so playhead advances during Play
        // flow through the SAME PushTick path the scrubber uses, and the service's tick/state
        // stay in sync with the canonical controller tick. AcceptTickFromFace guards on
        // State == Playing so scrub-only PushTick calls do not double-advance the service.
        _tickChangedHandler = tick => timelineService.AcceptTickFromFace(tick);
        controller.TickChanged += _tickChangedHandler;

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

        // D5: toggle a layer's membership in the stacked active set (several layers can be active at
        // once). Mirrors timeline.select_layer: toggling ON requires the layer to be schedule-active
        // at the current tick; toggling OFF is always permitted so a stale set can be cleared. The
        // response carries the full active set so track-button multi-highlight stays in sync.
        commandService?.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: "timeline.toggle_layer",
                Title: "Toggle timeline layer",
                Description: "Toggles a layer's membership in the stacked active set (D5). Payload: {\"sphereId\":\"geosphere\",\"layerId\":\"geosphere.crust\"}. Several layers may be active at once.",
                Category: "timeline"),
            (payloadJson, _) =>
            {
                var (sphereId, layerId) = ParseLayerSelection(payloadJson);
                bool alreadyActive = controller.ActiveLayers.Any(l =>
                    string.Equals(l.SphereId, sphereId, StringComparison.Ordinal)
                    && string.Equals(l.LayerId, layerId, StringComparison.Ordinal));

                if (!alreadyActive && !IsLayerActive(controller, sphereId, layerId))
                    throw new InvalidOperationException(
                        $"Layer '{layerId}' in sphere '{sphereId}' is not active at tick {controller.Tick}.");

                controller.ToggleLayer(sphereId, layerId);
                return Task.FromResult<string?>(JsonSerializer.Serialize(new
                {
                    ok = true,
                    sphereId,
                    layerId,
                    active = !alreadyActive,
                    tick = controller.Tick,
                    activeLayers = controller.ActiveLayers.Select(l => new { sphereId = l.SphereId, layerId = l.LayerId }),
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
