using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Command;
using FantaSim.App.Resource;
using FantaSim.App.SceneFlow;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
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

    internal const string SetTrackArchivedCommandId = "timeline.set_track_archived";

    // Tunnel slice-1 (vault/plans/2026-07-11-tunnel-slice1-plan.md Task 11).
    internal const string TunnelViewCommandId = "timeline.tunnel_view";

    private readonly Func<ITimelineFaceProxy> _faceProxyFactory;

    // Serializes compose/sever/shutdown across threads: world reloads raise RuntimeChanging/
    // RuntimeChanged from the resource watcher's threadpool thread while this bundle's own
    // scene-tier reload shuts the plugin down on the Godot main thread. Without the gate an
    // in-flight RuntimeChanged recompose can finish registering AFTER ShutdownAsync cleaned up,
    // leaving this dying generation rooted by the resident registry/command service forever —
    // the "old ALC still pinned for bundle timeline" 1-in-~6 multi-pck-install pin (2026-07-10).
    private readonly object _lifecycleGate = new();
    private bool _shutdown;

    // Outgoing world controller severed at RuntimeChanging. TryConsumePendingWorldRebind must
    // not consume the flag against this instance: IsLoaded("world") stays true and the OLD
    // registration is only disposed mid-unload, so an interleaved Changed event from another
    // bundle's reload would otherwise rebind the timeline to the dying world ALC (same guard
    // the Host grew for pin class 5 in 511bffe). WeakReference so the stash itself never pins.
    private WeakReference? _outgoingController;

    private IDisposable? _activatorRegistration;
    private IDisposable? _timelineRegistration;
    private IDisposable? _faceContextRegistration;
    private TimelineFaceContext? _faceContext;
    private IRegistry? _registry;
    private ILoggerFactory? _loggerFactory;
    private ILogger? _log;
    private ResourceService? _resource;
    private Services.Service? _timelineService;
    private ITimelineFaceProxy? _faceProxy;
    private Action<long>? _tickChangedHandler;
    private ITimelineController? _subscribedController;
    private bool _worldRebindPending;

    public TimelinePlugin()
        : this(static () => new DeferredTimelineFace())
    {
    }

    internal TimelinePlugin(Func<ITimelineFaceProxy> faceProxyFactory)
    {
        _faceProxyFactory = faceProxyFactory ?? throw new ArgumentNullException(nameof(faceProxyFactory));
    }

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        lock (_lifecycleGate)
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
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        // Taking the gate first means an in-flight recompose (RuntimeChanged on the threadpool)
        // either completed before this cleanup — and gets cleaned up here — or observes
        // _shutdown afterward and no-ops. Nothing can register past this point.
        lock (_lifecycleGate)
        {
            _shutdown = true;
            _log?.LogInformation("TimelinePlugin: shutdown started.");

            UnsubscribeResourceEvents();
            UnregisterTimelineCommands();
            SeverTimelineService(unbindProxy: true);

            _activatorRegistration?.Dispose();
            _activatorRegistration = null;
            _registry = null;
            _loggerFactory = null;
            _resource = null;
            _worldRebindPending = false;
            _outgoingController = null;
            _log?.LogInformation("TimelinePlugin: shutdown completed.");
            _log = null;
            return ValueTask.CompletedTask;
        }
    }

    private bool ComposeTimeline(bool markPendingWhenMissing)
    {
        if (_shutdown || _registry is null || _loggerFactory is null)
            return false;

        var controller = _registry.TryGet<ITimelineController>();
        if (controller is null)
        {
            SeverTimelineService(unbindProxy: false);
            if (markPendingWhenMissing)
                _worldRebindPending = true;
            _log?.LogWarning("TimelinePlugin: no ITimelineController registered; timeline service inert pending world registration.");
            return false;
        }

        // Sever BEFORE creating the proxy: on first compose the proxy must not receive a
        // rebind for a binding that never existed (the rebind after recompose is pushed by
        // TryConsumePendingWorldRebind, not here).
        SeverTimelineService(unbindProxy: false);
        var proxy = _faceProxy ??= _faceProxyFactory();

        foreach (var existing in _registry.GetAll<IService>())
        {
            if (existing is IDisposable disposable)
                disposable.Dispose();
        }
        _registry.UnregisterAll<IService>();

        var faceContext = new TimelineFaceContext(
            controller,
            proxy,
            _registry.TryGet<IClient>(),
            tick => _registry.TryGet<FantaSim.App.World.IService>()?.GetPlanetPresentationAsync(tick).GenerationGraphFamily,
            (request, cancellationToken) => _registry.TryGet<FantaSim.App.World.IService>()?.GetLayerFilmstripPreview(request, cancellationToken),
            // Owned and registered by the WORLD bundle (WorldPlugin), consumed here through the
            // shared T1 contract only -- a plugin-assembly reference from timeline to
            // App.World.Composition dual-copies 8 Unify assemblies across the two collectible
            // ALCs (stage_bundle --check-dual, 2026-07-10), the codebase's type-identity-split
            // incident class. Null when world composes later; the face context property is
            // nullable and ComposeTimeline reruns on world rebind.
            _registry.TryGet<ILayerTrackRegistry>(),
            _loggerFactory,
            ticksPerSecond: 5_000_000.0);
        _faceContext = faceContext;
        _faceContextRegistration = _registry.RegisterOwned<ITimelineFaceContext>(
            faceContext,
            new ServiceRegistration
            {
                OwnerId = "timeline.plugin",
                Priority = 100,
                Tags = new[] { "timeline", "timeline-face-context" },
                Description = "Timeline face resident context (timeline bundle)"
            });

        var timelineService = new Services.Service(proxy, controller, _loggerFactory);
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

        // World reloads reset the tunnel binder to disabled (slice-1 residue) and this compose
        // reruns on every world rebind -- re-deriving HUD visibility here keeps the 2D face and
        // the 3D tunnel consistent across reloads (rotating-tunnel design §4a).
        ApplyHudVisibilityFromTunnelState();

        _worldRebindPending = false;
        _log?.LogInformation("TimelinePlugin: IService registered.");
        return true;
    }

    private void ApplyHudVisibilityFromTunnelState()
    {
        var tunnelEnabled = _registry?.TryGet<FantaSim.App.Presentation.ITunnelPresentation>()?.IsEnabled ?? false;
        _faceProxy?.SetHudVisible(!tunnelEnabled);
    }

    private void SeverTimelineService(bool unbindProxy)
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

        _faceContextRegistration?.Dispose();
        _faceContextRegistration = null;

        // Disposing the registration only removes it from the registry; the context instance
        // must be disposed explicitly so UnregisterPlayback releases the (possibly dying)
        // world controller — the playback pin from the plan's Pin map.
        _faceContext?.Dispose();
        _faceContext = null;

        _faceProxy?.RebindResidentContext();
        if (unbindProxy)
        {
            _faceProxy?.UnbindCrossTarget();
            _faceProxy = null;
        }
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
                Description: "Moves the timeline playhead to a tick through the active timeline service. Payload: {\"tick\":123,\"origin\":\"standard|scrubPreview|scrubCommit\"}. 'origin' is optional (case-insensitive, default \"standard\").",
                Category: "timeline"),
            async (payloadJson, cancellationToken) =>
            {
                var payload = ParseSeekPayload(payloadJson);
                var tick = ParseSeekTick(payload, controller.MaxTick);
                var origin = ParseSeekOrigin(payload);
                // D8b: the origin-carrying push goes FIRST and UNCONDITIONALLY. SeekAsync's face
                // echo re-applies the tick as a Standard/no-heavy tick (a scrub-policy no-op),
                // but when it ran first it consumed the content transition as Standard and the
                // old `controller.Tick != tick` guard skipped the origin push entirely — remote
                // scrub origins never reached the presentation (D8b gate, 2026-07-11).
                controller.PushTick(tick, origin);
                await timelineService.SeekAsync(tick, cancellationToken).ConfigureAwait(false);
                // JsonObject, NOT JsonSerializer.Serialize(anonymous): anonymous types compile
                // into this collectible assembly, and the resident serializer's per-Type cache
                // roots their LoaderAllocator — the ALC never collects (gcroot-proven 2026-07-08).
                return new JsonObject
                {
                    ["ok"] = true,
                    ["requestedTick"] = tick,
                    ["tick"] = controller.Tick,
                    ["serviceTick"] = timelineService.Tick,
                    ["maxTick"] = controller.MaxTick,
                    ["state"] = timelineService.State.ToString(),
                }.ToJsonString();
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
                return Task.FromResult<string?>(new JsonObject
                {
                    ["ok"] = true,
                    ["sphereId"] = sphereId,
                    ["layerId"] = layerId,
                    ["tick"] = controller.Tick,
                }.ToJsonString());
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
                var activeLayers = new JsonArray();
                foreach (var l in controller.ActiveLayers)
                    activeLayers.Add(new JsonObject { ["sphereId"] = l.SphereId, ["layerId"] = l.LayerId });

                return Task.FromResult<string?>(new JsonObject
                {
                    ["ok"] = true,
                    ["sphereId"] = sphereId,
                    ["layerId"] = layerId,
                    ["active"] = !alreadyActive,
                    ["tick"] = controller.Tick,
                    ["activeLayers"] = activeLayers,
                }.ToJsonString());
            });

        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: SetTrackArchivedCommandId,
                Title: "Archive/restore a layer track",
                Description: "Archives or restores one layer track in the layer-track registry. The underlying declared/discovered data is never deleted. Payload: {\"sphereId\":\"geosphere\",\"layerId\":\"geosphere.crust\",\"archived\":true}.",
                Category: "timeline"),
            (payloadJson, _) =>
            {
                var payload = ParseTrackArchivedRequestPayload(payloadJson);
                var (sphereId, layerId, archived) = ParseSetTrackArchivedPayload(payload);

                // Resolved lazily on every invocation, never captured at registration: the WORLD
                // bundle owns the registry and may compose (or hot-reload to a new instance)
                // after this timeline bundle registered its commands.
                var layerTrackRegistry = _registry?.TryGet<ILayerTrackRegistry>();
                if (layerTrackRegistry is null)
                {
                    return Task.FromResult<string?>(new JsonObject
                    {
                        ["ok"] = false,
                        ["message"] = "layer-track registry not available",
                    }.ToJsonString());
                }

                layerTrackRegistry.SetArchived(sphereId, layerId, archived);
                // JsonObject, NOT JsonSerializer.Serialize(anonymous): see the ALC-pin note on
                // timeline.seek above -- the same collectible-assembly LoaderAllocator pin applies
                // to every command handler in this bundle.
                return Task.FromResult<string?>(new JsonObject
                {
                    ["ok"] = true,
                    ["sphereId"] = sphereId,
                    ["layerId"] = layerId,
                    ["archived"] = archived,
                    ["revision"] = layerTrackRegistry.Current.Revision,
                }.ToJsonString());
            });

        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: TunnelViewCommandId,
                Title: "Toggle tunnel view",
                Description: "Enables/disables the 3D tunnel timeline. Payload: {\"enabled\":true}.",
                Category: "timeline"),
            (payloadJson, _) =>
            {
                var payload = ParseTunnelViewPayload(payloadJson);
                if (!TryReadBool(payload["enabled"], out var enabled))
                    throw new ArgumentException("timeline.tunnel_view requires boolean 'enabled'.");

                // Resolved lazily on every invocation, never captured at registration: the tunnel
                // binder lives in the WORLD bundle (a different collectible ALC than this timeline
                // bundle) and may reload independently after this command registered.
                var tunnel = _registry?.TryGet<FantaSim.App.Presentation.ITunnelPresentation>();
                tunnel?.SetEnabled(enabled);
                // The 2D HUD hides while the tunnel view is on (design §4a). Derived from the
                // tunnel's EFFECTIVE state, not the requested one, so a missing/failed tunnel
                // never strands the HUD hidden.
                _faceProxy?.SetHudVisible(!(tunnel?.IsEnabled ?? false));
                return Task.FromResult<string?>(new JsonObject
                {
                    ["ok"] = tunnel is not null,
                    ["enabled"] = tunnel?.IsEnabled ?? false,
                }.ToJsonString());
            });
    }

    private void UnregisterTimelineCommands()
    {
        var commandService = _registry?.TryGet<FantaSim.App.Command.IService>();
        commandService?.Unregister(SeekCommandId);
        commandService?.Unregister(SelectLayerCommandId);
        commandService?.Unregister(ToggleLayerCommandId);
        commandService?.Unregister(SetTrackArchivedCommandId);
        commandService?.Unregister(TunnelViewCommandId);
    }

    private void OnResourceRuntimeChanging(object? sender, ResourceRuntimeChangingEventArgs args)
    {
        if (!string.Equals(args.BundleId, "world", StringComparison.Ordinal))
            return;

        lock (_lifecycleGate)
        {
            if (_shutdown)
                return;

            // Stash the OUTGOING generation's controller before severing: the bound controller
            // when composed, else whatever is still registered (both belong to the dying world
            // ALC). TryConsumePendingWorldRebind refuses to rebind to this exact instance.
            var outgoing = _subscribedController ?? _registry?.TryGet<ITimelineController>();
            _outgoingController = outgoing is not null ? new WeakReference(outgoing) : null;

            SeverTimelineService(unbindProxy: false);
            _worldRebindPending = true;
            _log?.LogInformation("TimelinePlugin: world runtime changing; timeline binding severed.");
        }
    }

    private void OnResourceRuntimeChanged(object? sender, EventArgs args)
        => TryConsumePendingWorldRebind();

    internal bool TryConsumePendingWorldRebind()
    {
        lock (_lifecycleGate)
        {
            if (_shutdown || !_worldRebindPending || _registry is null || _resource is null)
                return false;
            if (!_resource.IsLoaded("world"))
                return false;
            if (_registry.TryGet<ITimelineController>() is not { } controller)
                return false;

            // Leave the flag armed while only the OUTGOING registration resolves: IsLoaded("world")
            // is still true for the old copy during its own unload and the old registration is only
            // disposed mid-unload, so "some controller registered" is not enough (pin class 5's
            // guard rule). The world reload's own Changed event completes the rebind against the
            // NEW generation's instance.
            if (ReferenceEquals(controller, _outgoingController?.Target))
                return false;

            if (!ComposeTimeline(markPendingWhenMissing: false))
                return false;

            _outgoingController = null;

            // No manual tick push here: the face's BindResidentContext ends with SeekTo(_ctl.Tick),
            // so the rebind itself delivers the current controller tick — on the main thread, after
            // the cross target is actually bound (a seek pushed before the bind lands is a no-op).
            _faceProxy?.RebindResidentContext();

            return true;
        }
    }

    private void UnsubscribeResourceEvents()
    {
        if (_resource is null)
            return;

        _resource.RuntimeChanging -= OnResourceRuntimeChanging;
        _resource.RuntimeChanged -= OnResourceRuntimeChanged;
    }

    private static JsonObject ParseSeekPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new ArgumentException("timeline.seek payload is required.");

        return JsonNode.Parse(payloadJson) as JsonObject
            ?? throw new ArgumentException("timeline.seek payload must be a JSON object.");
    }

    private static long ParseSeekTick(JsonObject payload, long maxTick)
    {
        if (!TryReadLong(payload["tick"], out var tick))
            throw new ArgumentException("timeline.seek requires numeric 'tick'.");
        return Math.Clamp(tick, 0L, Math.Max(0L, maxTick));
    }

    internal static TimelineTickOrigin ParseSeekOrigin(JsonObject payload)
    {
        var node = payload["origin"];
        if (node is JsonValue value
            && value.TryGetValue<string>(out var text)
            && Enum.TryParse(text, true, out TimelineTickOrigin origin))
            return origin;
        return TimelineTickOrigin.Standard;
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

    private static JsonObject ParseTrackArchivedRequestPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new ArgumentException("timeline.set_track_archived payload is required.");

        return JsonNode.Parse(payloadJson) as JsonObject
            ?? throw new ArgumentException("timeline.set_track_archived payload must be a JSON object.");
    }

    private static JsonObject ParseTunnelViewPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new ArgumentException("timeline.tunnel_view payload is required.");

        return JsonNode.Parse(payloadJson) as JsonObject
            ?? throw new ArgumentException("timeline.tunnel_view payload must be a JSON object.");
    }

    internal static (string SphereId, string LayerId, bool Archived) ParseSetTrackArchivedPayload(JsonObject payload)
    {
        var sphereId = payload["sphereId"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(sphereId))
            throw new ArgumentException("timeline.set_track_archived requires 'sphereId'.");

        var layerId = payload["layerId"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(layerId))
            throw new ArgumentException("timeline.set_track_archived requires 'layerId'.");

        if (!TryReadBool(payload["archived"], out var archived))
            throw new ArgumentException("timeline.set_track_archived requires boolean 'archived'.");

        return (sphereId, layerId, archived);
    }

    private static bool TryReadBool(JsonNode? node, out bool value)
    {
        value = false;
        if (node is not JsonValue jsonValue)
            return false;
        if (jsonValue.TryGetValue<bool>(out value))
            return true;
        if (jsonValue.TryGetValue<string>(out var text)
            && bool.TryParse(text, out value))
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

internal sealed class TimelineFaceContext : ITimelineFaceContext, IDisposable
{
    private bool _disposed;

    public TimelineFaceContext(
        ITimelineController controller,
        ITimelineFaceProxy proxy,
        object? commandClient,
        Func<long, WorldGenerationGraphFamilyDocument?> generationGraphFamilyProvider,
        Func<LayerFilmstripPreviewRequest, CancellationToken, LayerFilmstripPreviewMap?> filmstripPreviewProvider,
        ILayerTrackRegistry? layerTrackRegistry,
        ILoggerFactory loggerFactory,
        double ticksPerSecond)
    {
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
        CommandClient = commandClient;
        GenerationGraphFamilyProvider = generationGraphFamilyProvider ?? throw new ArgumentNullException(nameof(generationGraphFamilyProvider));
        FilmstripPreviewProvider = filmstripPreviewProvider ?? throw new ArgumentNullException(nameof(filmstripPreviewProvider));
        LayerTrackRegistry = layerTrackRegistry;
        LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        TicksPerSecond = ticksPerSecond;
    }

    public ITimelineController Controller { get; }
    public ITimelineFaceProxy Proxy { get; }
    public object? CommandClient { get; }
    public Func<long, WorldGenerationGraphFamilyDocument?> GenerationGraphFamilyProvider { get; }
    public Func<LayerFilmstripPreviewRequest, CancellationToken, LayerFilmstripPreviewMap?> FilmstripPreviewProvider { get; }
    public ILayerTrackRegistry? LayerTrackRegistry { get; }
    public ILoggerFactory LoggerFactory { get; }
    public double TicksPerSecond { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Controller.UnregisterPlayback();
    }
}
