using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantaSim.App.Activity;
using FantaSim.App.Command;
using FantaSim.App.Common;
using FantaSim.App.Ecs;
using FantaSim.App.GpuCompute.Seam;
using FantaSim.App.GpuShader.Seam;
using FantaSim.App.Iii.Seam;
using FantaSim.App.Remote.Seam;
using FantaSim.App.Render.Seam;
using FantaSim.App.Resource.Bundle;
using FantaSim.App.SceneFlow;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.Ui;
using FantaSim.App.Ui.Seam;
using Godot;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Common.Entry;

public partial class Host : Node
{
    private AppComposition? _composition;
    private ILogger _log = null!;
    private CrosscutFoundation.Config.IService? _config;
    private CollectibleBundles? _collectibleBundles;
    private FantaSim.App.Ecs.IService? _ecs;
    private PlanetPresentationBinder? _planetPresentation;
    private FantaSim.App.Resource.IService? _resource;
    private IRenderCompositionHandle? _renderComposition;
    private bool _ecsWorldReady;
    private bool _timelineReloadPending;

    public override void _Ready()
    {
        GD.Print("[Host] composition root starting...");

        _composition = AppComposition.Activate();
        _log = _composition.Bootstrap.LoggerFactory.CreateLogger("Host");
        _config = _composition.Bootstrap.Registry.Get<CrosscutFoundation.Config.IService>();
        ApplyInitialWindowSize();

        _collectibleBundles = LoadCollectibleBundles();
        _composition.Bootstrap.BuildPluginHost(_collectibleBundles);

        _ = _composition.Bootstrap.RunAsync();

        // Each domain's composition body lives in its owning plugin project as a static
        // HostComposition class; Host only orchestrates the fixed order and threads host-owned
        // handles as locals (no shared mutable state bag - App.Common cannot reference the domain
        // plugins, so handles flow as return values + explicit parameters).
        var ctx = new HostCompositionContext(_composition);
        var tree = GetTree();

        ResourceComposition.ComposeResource(ctx, this, _collectibleBundles);
        SubscribeResourceRuntimeEvents(ctx.Registry);
        SceneFlowComposition.ComposeSceneFlow(ctx);
        (_ecs, _ecsWorldReady) = EcsComposition.ComposeEcs(ctx);
        CommandComposition.ComposeCommand(ctx);
        RegisterTimelineReloadHook(ctx.Registry);
        IiiComposition.ComposeIii(ctx, tree, this);
        GpuComposition.ComposeGpu(ctx);
        GpuShaderComposition.ComposeGpuShader(ctx);
        ActivityComposition.ComposeActivity(ctx);
        UiComposition.ComposeUi(ctx, tree);
        RemoteIngressComposition.ComposeRemoteIngress(ctx, this);
        _renderComposition = RenderComposition.ComposeRender(ctx, this);

        _log.LogInformation("composition activated.");
        _log.LogInformation("iii bridge: IiiClient registered = {IiiClientRegistered}", ClassDB.ClassExists("IiiClient"));
        RecordActivity(
            ActivityEntryKind.Log,
            "app.composition.ready",
            "system",
            "app",
            outcome: "composition activated");

        // Enter the root scene tier and KEEP it loaded (the correct flow — re-entry/teardown is a
        // test concern, not the running app). Deferred so _Ready stays non-blocking and the bundle's
        // entry scene mounts on the main thread after the tree is ready.
        Callable.From(EnterInitialScenes).CallDeferred();
        Callable.From(ShowIiiGraph).CallDeferred();
    }

    public override void _ExitTree()
    {
        if (_resource is not null)
        {
            _resource.RuntimeChanging -= OnResourceRuntimeChanging;
            _resource.RuntimeChanged -= OnResourceRuntimeChanged;
            _resource = null;
        }

        base._ExitTree();
    }

    private void SubscribeResourceRuntimeEvents(IRegistry registry)
    {
        _resource = registry.TryGet<FantaSim.App.Resource.IService>();
        if (_resource is null)
        {
            _log.LogWarning("resource runtime events skipped: resource service is not registered.");
            return;
        }

        _resource.RuntimeChanging += OnResourceRuntimeChanging;
        _resource.RuntimeChanged += OnResourceRuntimeChanged;
    }

    private void OnResourceRuntimeChanging(object? sender, FantaSim.App.Resource.ResourceRuntimeChangingEventArgs e)
    {
        if (e.Operation == FantaSim.App.Resource.ResourceRuntimeOperation.Reload
            && string.Equals(e.BundleId, "timeline", StringComparison.OrdinalIgnoreCase))
        {
            _timelineReloadPending = true;
        }
    }

    private void OnResourceRuntimeChanged(object? sender, EventArgs e)
    {
        if (!_timelineReloadPending)
            return;

        _timelineReloadPending = false;
        if (_composition is null)
            return;

        var registry = _composition.Bootstrap.Registry;
        var resource = registry.TryGet<FantaSim.App.Resource.IService>();
        if (resource?.IsLoaded("timeline") != true)
            return;

        HandleTimelineBundleReloaded();
    }

    private void RegisterTimelineReloadHook(IRegistry registry)
    {
        registry.Register<FantaSim.App.Command.IBundleReloadHook>(
            new TimelineReloadHook(this),
            new ServiceRegistration { Tags = new[] { "timeline", "hot-reload" }, Description = "Timeline resident rebind after bundle reload" });
    }

    private void HandleTimelineBundleReloaded()
    {
        if (_composition is null)
            return;

        TimelineComposition.ComposeTimeline(new HostCompositionContext(_composition));
        _log.LogInformation("timeline resident composition rebound after bundle reload.");
        RecordActivity(
            ActivityEntryKind.Log,
            "timeline.composition.rebound",
            "system",
            "timeline",
            outcome: "rebound after bundle reload");

        Callable.From(RebindTimelineFaceAndPushCurrentView).CallDeferred();
    }

    private void RebindTimelineFaceAndPushCurrentView()
    {
        if (_composition is null)
            return;

        var registry = _composition.Bootstrap.Registry;
        var sceneRegistry = registry.TryGet<IBundleSceneRegistry>();
        if (sceneRegistry?.GetSceneOrNull("timeline") is TimelineFace face)
            face.RebindResidentContext();

        var controller = registry.TryGet<FantaSim.App.World.Composition.ITimelineController>();
        var timeline = registry.TryGet<FantaSim.App.Timeline.IService>();
        if (controller is null || timeline is null)
            return;

        _ = timeline.SeekAsync(controller.Tick);
    }

    private sealed class TimelineReloadHook : FantaSim.App.Command.IBundleReloadHook
    {
        private readonly Host _host;

        public TimelineReloadHook(Host host)
        {
            _host = host;
        }

        public Task AfterReloadAsync(string bundleId, CancellationToken cancellationToken = default)
        {
            if (string.Equals(bundleId, "timeline", StringComparison.OrdinalIgnoreCase))
                _host.HandleTimelineBundleReloaded();

            return Task.CompletedTask;
        }
    }

    private Task ShowActivityViewIfConfiguredAsync()
    {
        if (_config?.GetValue("activity:show", true) != true)
            return Task.CompletedTask;

        var ui = _composition!.Bootstrap.Registry.TryGet<FantaSim.App.Ui.IService>();
        if (ui is null)
        {
            _log.LogWarning("activity view skipped: UI service is not registered.");
            return Task.CompletedTask;
        }

        return ShowActivityViewAsync(ui);
    }

    private async Task ShowActivityViewAsync(FantaSim.App.Ui.IService ui)
    {
        try
        {
            RecordActivity(
                ActivityEntryKind.UiOperation,
                "ui.activity.show.requested",
                "system",
                "ui",
                payload: new JsonObject { ["viewId"] = "activity" },
                outcome: "requested");
            await ui.ShowAsync("activity").ConfigureAwait(false);
            _log.LogInformation("activity view requested.");
            RecordActivity(
                ActivityEntryKind.UiOperation,
                "ui.activity.show",
                "system",
                "ui",
                payload: new JsonObject { ["viewId"] = "activity" },
                outcome: "shown");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "activity view request failed.");
            RecordActivity(
                ActivityEntryKind.UiOperation,
                "ui.activity.show.failed",
                "system",
                "ui",
                payload: new JsonObject { ["viewId"] = "activity" },
                outcome: "failed",
                error: ex.Message);
        }
    }

    private void ApplyInitialWindowSize()
    {
        // Godot window sizes are pixels. On macOS Retina, DisplayServer.ScreenGetScale returns 2.0,
        // so convert the configured desktop/logical size before applying Window.Size.
        // Sources:
        // https://docs.godotengine.org/en/stable/classes/class_displayserver.html#class-displayserver-method-window-set-size
        // https://docs.godotengine.org/en/stable/classes/class_displayserver.html#class-displayserver-method-screen-get-scale
        const int defaultWidth = 2400;
        const int defaultHeight = 1350;
        const int defaultMinWidth = 1280;
        const int defaultMinHeight = 720;

        var requestedSize = new Vector2I(
            ReadConfigInt("window:initialWidth", defaultWidth),
            ReadConfigInt("window:initialHeight", defaultHeight));
        var requestedMinSize = new Vector2I(
            ReadConfigInt("window:minWidth", defaultMinWidth),
            ReadConfigInt("window:minHeight", defaultMinHeight));

        var screen = DisplayServer.WindowGetCurrentScreen();
        var screenScale = MathF.Max(1.0f, DisplayServer.ScreenGetScale(screen));
        var target = ScaleWindowSize(requestedSize, screenScale);
        var minSize = ScaleWindowSize(requestedMinSize, screenScale);
        var usableRect = DisplayServer.ScreenGetUsableRect(screen);
        if (usableRect.Size.X > 0 && usableRect.Size.Y > 0)
            target = ClampWindowSize(target, minSize, usableRect.Size);

        var window = GetWindow();
        window.MinSize = minSize;
        window.Size = target;

        if (usableRect.Size.X > target.X && usableRect.Size.Y > target.Y)
        {
            DisplayServer.WindowSetPosition(new Vector2I(
                usableRect.Position.X + (usableRect.Size.X - target.X) / 2,
                usableRect.Position.Y + (usableRect.Size.Y - target.Y) / 2));
        }

        _log.LogInformation(
            "initial window size requested: {RequestedWidth}x{RequestedHeight} logical, applied={Width}x{Height} px, scale={Scale:0.##}, min={MinWidth}x{MinHeight} px.",
            requestedSize.X,
            requestedSize.Y,
            target.X,
            target.Y,
            screenScale,
            minSize.X,
            minSize.Y);
    }

    private static Vector2I ScaleWindowSize(Vector2I size, float scale)
    {
        if (scale <= 1.0f)
            return size;

        return new Vector2I(
            Math.Max(1, (int)MathF.Ceiling(size.X * scale)),
            Math.Max(1, (int)MathF.Ceiling(size.Y * scale)));
    }

    private static Vector2I ClampWindowSize(Vector2I size, Vector2I minSize, Vector2I maxSize)
    {
        var maxWidth = Math.Max(minSize.X, maxSize.X);
        var maxHeight = Math.Max(minSize.Y, maxSize.Y);
        return new Vector2I(
            Math.Clamp(size.X, minSize.X, maxWidth),
            Math.Clamp(size.Y, minSize.Y, maxHeight));
    }

    private int ReadConfigInt(string key, int fallback)
    {
        var value = _config?.Get(key);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            return parsed;
        _log.LogWarning("invalid {Key} '{Value}', using {Fallback}.", key, value, fallback);
        return fallback;
    }

    private void RecordActivity(
        ActivityEntryKind kind,
        string name,
        string actorKind,
        string category,
        JsonObject? payload = null,
        string? correlationId = null,
        string? outcome = null,
        string? error = null,
        string? causationId = null)
    {
        try
        {
            var registry = _composition?.Bootstrap.Registry;
            if (registry is null)
                return;

            var entry = new ActivityEntry(
                EntryId: Guid.NewGuid().ToString("N"),
                Kind: kind,
                Timestamp: DateTimeOffset.UtcNow,
                Actor: new ActivityActor(actorKind, actorKind == "user" ? "godot" : "app"),
                Name: name,
                Category: category,
                PayloadJson: payload?.ToJsonString(),
                CausationId: causationId,
                CorrelationId: correlationId,
                Outcome: outcome,
                Error: error);

            var bus = registry.TryGet<CrosscutFoundation.Messaging.IMessageBus>();
            if (bus is not null)
            {
                bus.Publish(entry);
                return;
            }

            registry.TryGet<FantaSim.App.Activity.IService>()?.Append(entry);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Activity ledger unavailable while recording {Name}.", name);
        }
    }

    private static void CopyIfPresent(JsonObject source, JsonObject target, string key)
    {
        if (source.TryGetPropertyValue(key, out var value) && value is not null)
            target[key] = value.DeepClone();
    }

    // Mount the iii text->3D graph as a BoomHud nodeGraph. Uses the general App.Ui.NodeGraph view over
    // a read-only graph source; RUN routes through App.Command. No per-domain view-source duplication.
    private void ShowIiiGraph()
    {
        if (_config?.GetValue("graph:show", false) != true) return;
        var logger = _composition!.Bootstrap.LoggerFactory.CreateLogger("IiiGraph");
        var prompt = _config.Get("graph:prompt") ?? "a small red toy cube";
        const string recipe = "text-to-3d";

        var graph = FantaSim.App.Iii.Recipes.TextTo3dGraph.Build(prompt);
        var graphSource = new FantaSim.App.NodeGraph.ReadOnlyGraphSource("iii-text-to-3d", graph);
        var runtime = new FantaSim.App.NodeGraph.GraphRuntimeStateTracker();
        var view = new FantaSim.App.Ui.NodeGraph.NodeGraphViewSource(
            graphSource,
            runAsync: async () =>
            {
                var runId = Guid.NewGuid().ToString("N")[..8];
                RecordActivity(
                    ActivityEntryKind.UiOperation,
                    "ui.graph.run",
                    "user",
                    "node-graph",
                    payload: new JsonObject
                    {
                        ["runId"] = runId,
                        ["recipe"] = recipe,
                        ["nodeCount"] = graph.Nodes.Count,
                        ["wireCount"] = graph.Wires.Count,
                    },
                    correlationId: runId,
                    outcome: "run requested");
                var providers = _composition.Bootstrap.Registry
                    .GetAll<FantaSim.App.NodeGraph.INodeFunctionProvider>()
                    .ToArray();
                var executor = new FantaSim.App.NodeGraph.GraphExecutor(
                    providers,
                    CreateAuditedRunContext(runtime, runId, recipe));
                var result = await executor.ExecuteAsync(
                    graph,
                    new JsonObject { ["job_id"] = runId });
                _log.LogInformation(
                    "iii graph runtime complete: run={RunId} state={RunState} nodes={NodeStates}",
                    runId,
                    runtime.RunState.Status,
                    string.Join(", ", runtime.NodeStates.Select(kv => $"{kv.Key}:{kv.Value.Status}")));
                return result;
            },
            title: "iii text to 3D graph",
            runtimeState: runtime);

        var uiRoot = new Control { Name = "IiiGraphRoot" };
        uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        uiRoot.SetOffsetsPreset(Control.LayoutPreset.FullRect);
        GetTree().Root.AddChild(uiRoot);

        var renderer = new FantaSim.App.Ui.Seam.ViewRenderer(uiRoot, () => view, _ => null, logger);
        renderer.Bind();
        if (_config.GetValue("graph:autoRun", false))
            Callable.From(() => view.Dispatch("run", "btn-run")).CallDeferred();
        _log.LogInformation("iii-graph view mounted: {NodeCount} nodes, {WireCount} wires.", view.Nodes.Count, view.Wires.Count);
        RecordActivity(
            ActivityEntryKind.UiOperation,
            "ui.graph.show",
            "system",
            "node-graph",
            payload: new JsonObject
            {
                ["viewId"] = view.ViewId,
                ["recipe"] = recipe,
                ["nodeCount"] = view.Nodes.Count,
                ["wireCount"] = view.Wires.Count,
            },
            outcome: "mounted");
    }

    private FantaSim.App.NodeGraph.RunContext CreateAuditedRunContext(
        FantaSim.App.NodeGraph.GraphRuntimeStateTracker runtime,
        string runId,
        string recipe)
    {
        var tracker = runtime.CreateRunContext(runId);
        return new FantaSim.App.NodeGraph.RunContext
        {
            BeforeRun = async (graph, ct) =>
            {
                if (tracker.BeforeRun is not null)
                    await tracker.BeforeRun(graph, ct).ConfigureAwait(false);
                RecordActivity(
                    ActivityEntryKind.DomainCommand,
                    "graph.run.started",
                    "system",
                    "node-graph",
                    payload: new JsonObject
                    {
                        ["runId"] = runId,
                        ["recipe"] = recipe,
                        ["status"] = "Running",
                        ["nodeCount"] = graph.Nodes.Count,
                        ["wireCount"] = graph.Wires.Count,
                    },
                    correlationId: runId,
                    outcome: "started");
            },
            BeforeNode = async (node, payload, ct) =>
            {
                if (tracker.BeforeNode is not null)
                    await tracker.BeforeNode(node, payload, ct).ConfigureAwait(false);
                RecordActivity(
                    ActivityEntryKind.Log,
                    "graph.node.started",
                    "system",
                    "node-graph",
                    payload: new JsonObject
                    {
                        ["runId"] = runId,
                        ["nodeId"] = node.Id,
                        ["functionId"] = node.FunctionId,
                        ["status"] = "Running",
                    },
                    correlationId: runId,
                    outcome: "started");
            },
            AfterNode = async (node, payload, result, ct) =>
            {
                if (tracker.AfterNode is not null)
                    await tracker.AfterNode(node, payload, result, ct).ConfigureAwait(false);
                var details = new JsonObject
                {
                    ["runId"] = runId,
                    ["nodeId"] = node.Id,
                    ["functionId"] = node.FunctionId,
                    ["status"] = "Completed",
                };
                CopyIfPresent(result, details, "path");
                CopyIfPresent(result, details, "mesh");
                CopyIfPresent(result, details, "image");
                CopyIfPresent(result, details, "usd_path");
                CopyIfPresent(result, details, "glb_path");
                CopyIfPresent(result, details, "fallback");
                RecordActivity(
                    ActivityEntryKind.Log,
                    "graph.node.completed",
                    "system",
                    "node-graph",
                    payload: details,
                    correlationId: runId,
                    outcome: "completed");
            },
            AfterRun = async (graph, ct) =>
            {
                if (tracker.AfterRun is not null)
                    await tracker.AfterRun(graph, ct).ConfigureAwait(false);
                RecordActivity(
                    ActivityEntryKind.CommandResult,
                    "graph.run.completed",
                    "system",
                    "node-graph",
                    payload: new JsonObject
                    {
                        ["runId"] = runId,
                        ["recipe"] = recipe,
                        ["status"] = "Completed",
                        ["nodeCount"] = graph.Nodes.Count,
                    },
                    correlationId: runId,
                    outcome: "completed");
            },
            OnNodeFailed = async (node, ex, ct) =>
            {
                if (tracker.OnNodeFailed is not null)
                    await tracker.OnNodeFailed(node, ex, ct).ConfigureAwait(false);
                RecordActivity(
                    ActivityEntryKind.Log,
                    "graph.node.failed",
                    "system",
                    "node-graph",
                    payload: new JsonObject
                    {
                        ["runId"] = runId,
                        ["nodeId"] = node.Id,
                        ["functionId"] = node.FunctionId,
                        ["status"] = "Failed",
                    },
                    correlationId: runId,
                    outcome: "failed",
                    error: ex.Message);
            },
            OnRunFailed = async (graph, ex, ct) =>
            {
                if (tracker.OnRunFailed is not null)
                    await tracker.OnRunFailed(graph, ex, ct).ConfigureAwait(false);
                RecordActivity(
                    ActivityEntryKind.CommandResult,
                    "graph.run.failed",
                    "system",
                    "node-graph",
                    payload: new JsonObject
                    {
                        ["runId"] = runId,
                        ["recipe"] = recipe,
                        ["status"] = "Failed",
                        ["nodeCount"] = graph.Nodes.Count,
                    },
                    correlationId: runId,
                    outcome: "failed",
                    error: ex.Message);
            },
        };
    }

    // Boot the real scene flow: enter the "stage" tier under app-root. SceneFlow finds no resident
    // activator, loads stage.pck via the Resource service into a collectible ALC, the bundle's
    // StagePlugin registers its activator across the ALC boundary, and SceneFlow activates it.
    private async void EnterInitialScenes()
    {
        try
        {
            var registry = _composition!.Bootstrap.Registry;
            var sceneFlow = registry.Get<FantaSim.App.SceneFlow.IService>();
            var resource = registry.Get<FantaSim.App.Resource.IService>();

            var stage = await sceneFlow.EnterAsync(new FantaSim.App.SceneFlow.SceneRequest("stage"));
            _log.LogInformation(
                "entered scene '{SceneId}'; bundleLoaded={StageLoaded}; activeScenes={ActiveScenes}",
                stage.SceneId,
                resource.IsLoaded("stage"),
                sceneFlow.ActiveScenes.Count);
            RecordSceneActivity(stage.SceneId, stage.ParentSceneId, resource.IsLoaded("stage"), sceneFlow.ActiveScenes.Count);

            await LoadWorldBundleAndMountPlanetAsync(registry, resource).ConfigureAwait(false);

            // Timeline depends on the resident planet timeline controller. Compose it after the
            // world domain bundle has supplied the planet presentation document, but before the
            // timeline scene bundle instantiates its resident TimelineFace.
            TimelineComposition.ComposeTimeline(new HostCompositionContext(_composition!));

            // Enter assist UNDER stage — a nested dynamic parent. Assist shares the one app kernel
            // through stage's child provider, across two collectible ALCs (same kernel hash in the log).
            var assist = await sceneFlow.EnterAsync(new FantaSim.App.SceneFlow.SceneRequest("assist", "stage"));
            _log.LogInformation(
                "entered scene '{SceneId}' under '{ParentSceneId}'; bundleLoaded={AssistLoaded}; activeScenes={ActiveScenes}",
                assist.SceneId,
                assist.ParentSceneId,
                resource.IsLoaded("assist"),
                sceneFlow.ActiveScenes.Count);
            RecordSceneActivity(assist.SceneId, assist.ParentSceneId, resource.IsLoaded("assist"), sceneFlow.ActiveScenes.Count);

            // Enter the timeline bundle under stage. ITimelineController is already registered by the
            // resident planet presentation binder. SceneFlowProvider loads the PCK first, then calls
            // ActivateAsync — so IsLoaded("timeline") is true by the time TimelinePlugin.InitializeAsync
            // resolves the controller and mounts the view.
            var timeline = await sceneFlow.EnterAsync(new FantaSim.App.SceneFlow.SceneRequest("timeline", "stage"));
            _log.LogInformation(
                "entered scene '{SceneId}' under '{ParentSceneId}'; bundleLoaded={TimelineLoaded}; activeScenes={ActiveScenes}",
                timeline.SceneId,
                timeline.ParentSceneId,
                resource.IsLoaded("timeline"),
                sceneFlow.ActiveScenes.Count);
            RecordSceneActivity(timeline.SceneId, timeline.ParentSceneId, resource.IsLoaded("timeline"), sceneFlow.ActiveScenes.Count);
        }
        catch (Exception ex)
        {
            _log.LogError("initial scene entry failed: {Exception}", ex);
            RecordActivity(
                ActivityEntryKind.Log,
                "scene.enter.failed",
                "system",
                "scene",
                outcome: "failed",
                error: ex.Message);
        }
        finally
        {
            await ShowActivityViewIfConfiguredAsync().ConfigureAwait(false);
        }
    }

    private async Task LoadWorldBundleAndMountPlanetAsync(
        IRegistry registry,
        FantaSim.App.Resource.IService resource)
    {
        if (!resource.IsLoaded("world"))
            await resource.LoadFromDirectoryAsync("world").ConfigureAwait(false);

        _log.LogInformation("loaded domain bundle 'world'; bundleLoaded={WorldLoaded}", resource.IsLoaded("world"));
        RecordActivity(
            ActivityEntryKind.Log,
            "bundle.load",
            "system",
            "world",
            payload: new JsonObject
            {
                ["bundleId"] = "world",
                ["bundleLoaded"] = resource.IsLoaded("world"),
            },
            outcome: resource.IsLoaded("world") ? "bundle loaded" : "bundle unavailable");

        if (!resource.IsLoaded("world"))
            return;

        var sceneRegistry = registry.Get<IBundleSceneRegistry>();
        _planetPresentation?.Dispose();
        _planetPresentation = new PlanetPresentationBinder(
            registry,
            resource,
            sceneRegistry,
            _composition!.Bootstrap.LoggerFactory);
        _planetPresentation.Rebind();
        _renderComposition?.SetCutawayTarget(_planetPresentation.UpdateCutaway);
    }

    private void RecordSceneActivity(string sceneId, string? parentSceneId, bool bundleLoaded, int activeScenes)
        => RecordActivity(
            ActivityEntryKind.Log,
            "scene.enter",
            "system",
            "scene",
            payload: new JsonObject
            {
                ["sceneId"] = sceneId,
                ["parentSceneId"] = parentSceneId ?? string.Empty,
                ["bundleLoaded"] = bundleLoaded,
                ["activeScenes"] = activeScenes,
            },
            outcome: bundleLoaded ? "bundle loaded" : "scene entered");

    public override void _Process(double delta)
    {
        if (!_ecsWorldReady || _ecs is null) return;
        _ecs.UpdateAll((float)delta);
    }

    private CollectibleBundles LoadCollectibleBundles()
    {
        const string configPath = "res://config/collectible-bundles.json";
        if (!Godot.FileAccess.FileExists(configPath))
            return CollectibleBundles.Empty;
        var json = Godot.FileAccess.GetFileAsString(configPath);
        return CollectibleBundles.ParseJson(json);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationExitTree)
        {
            // Unregister the render.screenshot handler so it does not pin a collectible ALC
            // (mirrors WorldPlugin.ShutdownAsync unregister discipline).
            if (_renderComposition is not null && _composition is not null)
            {
                _renderComposition.Unregister(_composition.Bootstrap.Registry);
                _renderComposition.Dispose();
                _renderComposition = null;
            }

            _planetPresentation?.Dispose();
            _renderComposition?.SetCutawayTarget(null);
            _planetPresentation = null;
            _composition?.Dispose();
        }
        base._Notification(what);
    }
}
