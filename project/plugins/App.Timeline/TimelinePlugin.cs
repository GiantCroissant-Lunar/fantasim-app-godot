using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Resource;
using FantaSim.App.SceneFlow;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.World.Composition;
using Godot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PluginArchi.Extensibility.Abstractions;
using ServiceArchi.Contracts;
using ResourceService = FantaSim.App.Resource.IService;

namespace FantaSim.App.Timeline;

[Plugin("app.timeline", Name = "Timeline HUD", Description = "Registers the timeline scene activator.", Tags = "scene-tier")]
public sealed partial class TimelinePlugin : ILifecyclePlugin
{
    internal const string SeekCommandId = "timeline.seek";
    internal const string SelectLayerCommandId = "timeline.select_layer";
    internal const string ToggleLayerCommandId = "timeline.toggle_layer";

    private readonly ITimelineResidentBridge _residentBridge;
    private IDisposable? _activatorRegistration;
    private IDisposable? _timelineRegistration;
    private IRegistry? _registry;
    private ILoggerFactory? _loggerFactory;
    private ILogger? _log;
    private ResourceService? _resource;
    private Services.Service? _timelineService;
    private Action<long>? _tickChangedHandler;
    private ITimelineController? _subscribedController;
    private bool _worldRebindPending;

    public TimelinePlugin()
        : this(new TimelineResidentBridge())
    {
    }

    internal TimelinePlugin(ITimelineResidentBridge residentBridge)
    {
        _residentBridge = residentBridge ?? throw new ArgumentNullException(nameof(residentBridge));
    }

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        var registry = context.Services.GetRequiredService<IRegistry>();
        var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
        _registry = registry;
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger("TimelinePlugin");

        _activatorRegistration = registry.RegisterOwned<ISceneActivator>(
            new TimelineActivator(),
            new ServiceRegistration { Tags = new[] { "scene-activator" }, Description = "timeline activator (bundle)" });

        _resource = registry.TryGet<ResourceService>();
        if (_resource is not null)
        {
            _resource.RuntimeChanging += OnResourceRuntimeChanging;
            _resource.RuntimeChanged += OnResourceRuntimeChanged;
        }
        else
        {
            _log.LogWarning("TimelinePlugin: resource service not registered; world reload rebind events unavailable.");
        }

        ComposeTimeline(markPendingWhenMissing: true);

        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _log?.LogInformation("TimelinePlugin: shutdown started.");

        UnsubscribeResourceEvents();
        UnregisterTimelineCommands();
        SeverTimelineService();
        _residentBridge.ClearAll();

        _activatorRegistration?.Dispose();
        _activatorRegistration = null;
        _registry = null;
        _loggerFactory = null;
        _resource = null;
        _worldRebindPending = false;
        _log?.LogInformation("TimelinePlugin: shutdown completed.");
        _log = null;
        return ValueTask.CompletedTask;
    }

    private bool ComposeTimeline(bool markPendingWhenMissing)
    {
        if (_registry is null || _loggerFactory is null)
            return false;

        var controller = _registry.TryGet<ITimelineController>();
        if (controller is null)
        {
            SeverTimelineService();
            _residentBridge.ClearWorldBinding();
            if (markPendingWhenMissing)
                _worldRebindPending = true;
            _log?.LogWarning("TimelinePlugin: no ITimelineController registered; timeline service inert pending world registration.");
            return false;
        }

        SeverTimelineService();

        foreach (var existing in _registry.GetAll<IService>())
        {
            if (existing is IDisposable disposable)
                disposable.Dispose();
        }
        _registry.UnregisterAll<IService>();

        var deferredFace = _residentBridge.CreateDeferredFace();
        _residentBridge.BindResidentContext(controller, deferredFace, _registry, _loggerFactory);

        var timelineService = new Services.Service(deferredFace, controller, _loggerFactory);
        _tickChangedHandler = tick => timelineService.AcceptTickFromFace(tick);
        _subscribedController = controller;
        controller.TickChanged += _tickChangedHandler;

        _timelineService = timelineService;
        _timelineRegistration = _registry.RegisterOwned<IService>(
            timelineService,
            new ServiceRegistration
            {
                OwnerId = "timeline.plugin",
                Priority = 100,
                Tags = new[] { "timeline", "timeline-bundle" },
                Description = "Timeline playback service (timeline bundle)"
            });

        RegisterTimelineCommands(controller, timelineService);

        _worldRebindPending = false;
        _log?.LogInformation("TimelinePlugin: IService registered.");
        return true;
    }

    private void SeverTimelineService()
    {
        UnregisterTimelineCommands();

        if (_tickChangedHandler is not null && _subscribedController is not null)
            _subscribedController.TickChanged -= _tickChangedHandler;
        _tickChangedHandler = null;
        _subscribedController = null;

        _timelineRegistration?.Dispose();
        _timelineRegistration = null;

        _timelineService?.Dispose();
        _timelineService = null;
    }

    private void RegisterTimelineCommands(ITimelineController controller, Services.Service timelineService)
    {
        var commandService = _registry?.TryGet<FantaSim.App.Command.IService>();
        if (commandService is null)
        {
            _log?.LogWarning("TimelinePlugin: command service not registered; timeline commands unavailable.");
            return;
        }

        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: SeekCommandId,
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

        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: SelectLayerCommandId,
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

        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: ToggleLayerCommandId,
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
    }

    private void UnregisterTimelineCommands()
    {
        var commandService = _registry?.TryGet<FantaSim.App.Command.IService>();
        commandService?.Unregister(SeekCommandId);
        commandService?.Unregister(SelectLayerCommandId);
        commandService?.Unregister(ToggleLayerCommandId);
    }

    private void OnResourceRuntimeChanging(object? sender, ResourceRuntimeChangingEventArgs args)
    {
        if (!string.Equals(args.BundleId, "world", StringComparison.Ordinal))
            return;

        SeverTimelineService();
        _residentBridge.ClearWorldBinding();
        _worldRebindPending = true;
        _log?.LogInformation("TimelinePlugin: world runtime changing; timeline binding severed.");
    }

    private void OnResourceRuntimeChanged(object? sender, EventArgs args)
        => TryConsumePendingWorldRebind();

    internal bool TryConsumePendingWorldRebind()
    {
        if (!_worldRebindPending || _registry is null || _resource is null)
            return false;
        if (!_resource.IsLoaded("world"))
            return false;
        if (_registry.TryGet<ITimelineController>() is null)
            return false;
        if (!ComposeTimeline(markPendingWhenMissing: false))
            return false;

        if (_timelineService is not null && _subscribedController is not null)
        {
            _residentBridge
                .RebindSceneFaceAndPushAsync(_registry, _timelineService, _subscribedController, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        return true;
    }

    private void UnsubscribeResourceEvents()
    {
        if (_resource is null)
            return;

        _resource.RuntimeChanging -= OnResourceRuntimeChanging;
        _resource.RuntimeChanged -= OnResourceRuntimeChanged;
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

    private static bool IsLayerActive(ITimelineController controller, string sphereId, string layerId)
    {
        var schedule = string.Equals(sphereId, "atmosphere", StringComparison.Ordinal)
            ? controller.AtmosphereSchedule
            : controller.GeosphereSchedule;

        return schedule.RegimeAt(controller.Tick)?.ActiveLayers.Any(layer =>
            string.Equals(layer.Value, layerId, StringComparison.Ordinal)) == true;
    }
}

internal interface ITimelineResidentBridge
{
    ITimelineFace CreateDeferredFace();

    void BindResidentContext(
        ITimelineController controller,
        ITimelineFace proxy,
        IRegistry registry,
        ILoggerFactory loggerFactory);

    void ClearWorldBinding();

    void ClearAll();

    Task RebindSceneFaceAndPushAsync(
        IRegistry registry,
        IService service,
        ITimelineController controller,
        CancellationToken cancellationToken);
}

internal sealed class TimelineResidentBridge : ITimelineResidentBridge
{
    public ITimelineFace CreateDeferredFace()
        => new Seam.DeferredTimelineFace();

    public void BindResidentContext(
        ITimelineController controller,
        ITimelineFace proxy,
        IRegistry registry,
        ILoggerFactory loggerFactory)
    {
        Seam.TimelineFace.ResidentController = controller;
        Seam.TimelineFace.ResidentProxy = proxy as Seam.DeferredTimelineFace;
        Seam.TimelineFace.ResidentLoggerFactory = loggerFactory;
        Seam.TimelineFace.ResidentCommandClient = registry.TryGet<FantaSim.App.Command.IClient>();
        Seam.TimelineFace.ResidentGenerationGraphFamilyProvider =
            tick => registry.TryGet<FantaSim.App.World.IService>()?.GetPlanetPresentationAsync(tick).GenerationGraphFamily;
        Seam.TimelineFace.ResidentFilmstripPreviewProvider =
            request => registry.TryGet<FantaSim.App.World.IService>()?.GetLayerFilmstripPreview(request);
        Seam.TimelineFace.ResidentTicksPerSecond = 5_000_000.0;
    }

    public void ClearWorldBinding()
    {
        Seam.TimelineFace.ResidentController?.UnregisterPlayback();
        Seam.TimelineFace.ResidentController = null;
        Seam.TimelineFace.ResidentGenerationGraphFamilyProvider = null;
        Seam.TimelineFace.ResidentFilmstripPreviewProvider = null;
    }

    public void ClearAll()
    {
        ClearWorldBinding();
        Seam.TimelineFace.ResidentProxy?.UnbindCrossTarget();
        Seam.TimelineFace.ResidentProxy = null;
        Seam.TimelineFace.ResidentLoggerFactory = null;
        Seam.TimelineFace.ResidentCommandClient = null;
    }

    public async Task RebindSceneFaceAndPushAsync(
        IRegistry registry,
        IService service,
        ITimelineController controller,
        CancellationToken cancellationToken)
    {
        var sceneRegistry = registry.TryGet<FantaSim.App.Resource.Bundle.IBundleSceneRegistry>();
        if (sceneRegistry?.GetSceneOrNull("timeline") is not Seam.TimelineFace face)
        {
            await service.SeekAsync(controller.Tick, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (OS.GetThreadCallerId() == OS.GetMainThreadId())
        {
            face.RebindResidentContext();
            await service.SeekAsync(controller.Tick, cancellationToken).ConfigureAwait(false);
            return;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Callable.From(() =>
        {
            try
            {
                face.RebindResidentContext();
                service.SeekAsync(controller.Tick, cancellationToken).GetAwaiter().GetResult();
                done.TrySetResult();
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }).CallDeferred();

        await done.Task.ConfigureAwait(false);
    }
}
