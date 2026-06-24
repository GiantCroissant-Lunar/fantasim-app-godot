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
using FantaSim.App.Resource.Bundle;
using FantaSim.App.SceneFlow;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.Ui;
using FantaSim.App.Ui.ExternalTools;
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
    private bool _ecsWorldReady;

    private FantaSim.App.Ui.Seam.ViewRenderer? _toolPreviewRenderer;
    private ExternalToolResultViewSource? _toolPreviewView;
    private Control? _toolPreviewUiRoot;

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
        SceneFlowComposition.ComposeSceneFlow(ctx);
        (_ecs, _ecsWorldReady) = EcsComposition.ComposeEcs(ctx);
        CommandComposition.ComposeCommand(ctx);
        IiiComposition.ComposeIii(ctx, tree, this);
        GpuComposition.ComposeGpu(ctx);
        GpuShaderComposition.ComposeGpuShader(ctx);
        TimelineComposition.ComposeTimeline(ctx);
        ActivityComposition.ComposeActivity(ctx);
        UiComposition.ComposeUi(ctx, tree);
        RemoteIngressComposition.ComposeRemoteIngress(ctx, this);

        _log.LogInformation("composition activated.");
        _log.LogInformation("iii bridge: IiiClient registered = {IiiClientRegistered}", ClassDB.ClassExists("IiiClient"));

        // Enter the root scene tier and KEEP it loaded (the correct flow — re-entry/teardown is a
        // test concern, not the running app). Deferred so _Ready stays non-blocking and the bundle's
        // entry scene mounts on the main thread after the tree is ready.
        Callable.From(EnterInitialScenes).CallDeferred();
        Callable.From(PingIiiBridge).CallDeferred();
        Callable.From(RunGraphTest).CallDeferred();
        Callable.From(ShowIiiGraph).CallDeferred();
        Callable.From(ShowExternalToolPreview).CallDeferred();
        Callable.From(ShowActivityViewPreview).CallDeferred();
    }

    private void ShowExternalToolPreview()
    {
        if (_config?.GetValue("toolPreview:show", false) != true) return;
        var logger = _composition!.Bootstrap.LoggerFactory.CreateLogger("ExternalToolPreview");

        _toolPreviewRenderer?.Dispose();
        _toolPreviewUiRoot?.QueueFree();

        _toolPreviewView = ExternalToolResultViewSource.CreateVplanetSmokePreview();
        _toolPreviewUiRoot = new Control
        {
            Name = "ExternalToolPreviewRoot",
            Scale = Vector2.One,
            ZIndex = 100,
        };
        _toolPreviewUiRoot.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _toolPreviewUiRoot.Position = new Vector2(32, 32);
        _toolPreviewUiRoot.Size = new Vector2(760, 640);
        GetTree().Root.AddChild(_toolPreviewUiRoot);

        _toolPreviewRenderer = new FantaSim.App.Ui.Seam.ViewRenderer(_toolPreviewUiRoot, () => _toolPreviewView, _ => null, logger);
        _toolPreviewRenderer.Bind();
        RecordExternalToolPreviewActivity(_toolPreviewView);
        _log.LogInformation("external-tool preview mounted: {ViewId}.", _toolPreviewView.ViewId);
    }

    private void ShowActivityViewPreview()
    {
        if (_config?.GetValue("activity:show", false) != true) return;

        var ui = _composition!.Bootstrap.Registry.TryGet<FantaSim.App.Ui.IService>();
        if (ui is null)
        {
            _log.LogWarning("activity view skipped: UI service is not registered.");
            return;
        }

        _toolPreviewRenderer?.Dispose();
        _toolPreviewRenderer = null;
        _toolPreviewUiRoot?.QueueFree();
        _toolPreviewUiRoot = null;

        _ = ShowActivityViewPreviewAsync(ui);
    }

    private async Task ShowActivityViewPreviewAsync(FantaSim.App.Ui.IService ui)
    {
        try
        {
            await ui.ShowAsync("activity").ConfigureAwait(false);
            _log.LogInformation("activity view requested.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "activity view request failed.");
        }
    }

    private void ApplyInitialWindowSize()
    {
        // Godot's docs recommend Window.Size for runtime window sizing; this keeps exported macOS
        // launches from depending on editor history or command-line --resolution overrides.
        // Source: https://docs.godotengine.org/en/stable/classes/class_displayserver.html#class-displayserver-method-window-set-size
        const int defaultWidth = 2400;
        const int defaultHeight = 1350;
        const int defaultMinWidth = 1280;
        const int defaultMinHeight = 720;

        var target = new Vector2I(
            ReadConfigInt("window:initialWidth", defaultWidth),
            ReadConfigInt("window:initialHeight", defaultHeight));
        var minSize = new Vector2I(
            ReadConfigInt("window:minWidth", defaultMinWidth),
            ReadConfigInt("window:minHeight", defaultMinHeight));

        var window = GetWindow();
        window.MinSize = minSize;
        window.Size = target;

        var screen = DisplayServer.WindowGetCurrentScreen();
        var screenSize = DisplayServer.ScreenGetSize(screen);
        if (screenSize.X > target.X && screenSize.Y > target.Y)
        {
            DisplayServer.WindowSetPosition(new Vector2I(
                (screenSize.X - target.X) / 2,
                (screenSize.Y - target.Y) / 2));
        }

        _log.LogInformation("initial window size requested: {Width}x{Height}, min={MinWidth}x{MinHeight}.", target.X, target.Y, minSize.X, minSize.Y);
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

    private void RecordExternalToolPreviewActivity(ExternalToolResultViewSource view)
    {
        var activity = _composition!.Bootstrap.Registry.TryGet<FantaSim.App.Activity.IService>();
        if (activity is null)
        {
            _log.LogWarning("external-tool preview activity skipped: Activity service is not registered.");
            return;
        }

        var payload = view.BuildActivityPayload();
        var now = DateTimeOffset.UtcNow;
        var entry = new ActivityEntry(
            EntryId: $"external-tool.inspect.{view.ViewId}.{now.ToUnixTimeMilliseconds()}",
            Kind: ActivityEntryKind.UiOperation,
            Timestamp: now,
            Actor: new ActivityActor("user", "godot"),
            Name: "external-tool.inspect",
            Category: "external-tool",
            PayloadJson: payload.ToJsonString(),
            CorrelationId: payload["jobId"]?.GetValue<string>(),
            Outcome: $"inspector mounted for {view.Title}");
        activity.Append(entry);
        _log.LogInformation("external-tool preview activity recorded: {EntryId}.", entry.EntryId);
    }

    // Mount the iii text->3D graph as a BoomHud nodeGraph (env-guarded demo). Uses the GENERAL
    // App.Ui.NodeGraph view over a read-only graph source; RUN routes through App.Command like the
    // other demos. No per-domain view-source duplication.
    private void ShowIiiGraph()
    {
        if (_config?.GetValue("graph:show", false) != true) return;
        var logger = _composition!.Bootstrap.LoggerFactory.CreateLogger("IiiGraph");
        var prompt = _config.Get("graph:prompt") ?? "a small red toy cube";

        var graph = FantaSim.App.Iii.Recipes.TextTo3dGraph.Build(prompt);
        var graphSource = new FantaSim.App.NodeGraph.ReadOnlyGraphSource("iii-text-to-3d", graph);

        var client = _composition.Bootstrap.Registry.Get<FantaSim.App.Command.IClient>();
        var view = new FantaSim.App.Ui.NodeGraph.NodeGraphViewSource(
            graphSource,
            runAsync: async () =>
            {
                var result = await client.CommandAsync(new FantaSim.App.Command.CommandRequest(
                    Command: "pipeline.run_text_to_3d",
                    PayloadJson: $"{{\"prompt\":\"{prompt}\"}}"));
                return JsonSerializer.SerializeToNode(result)?.AsObject() ?? new JsonObject();
            },
            title: "iii text to 3D graph");

        var uiRoot = new Control { Name = "IiiGraphRoot" };
        uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        GetTree().Root.AddChild(uiRoot);

        var renderer = new FantaSim.App.Ui.Seam.ViewRenderer(uiRoot, () => view, _ => null, logger);
        renderer.Bind();
        _log.LogInformation("iii-graph view mounted: {NodeCount} nodes, {WireCount} wires.", view.Nodes.Count, view.Wires.Count);
    }

    // pipeline.run_text_to_3d via the composed iii axis (env-guarded demo). The graph is authored in
    // App.Iii.Recipes and executed by the general App.NodeGraph.GraphExecutor through the iii function
    // provider. Quits when done so the windowed verification run terminates.
    private async void RunGraphTest()
    {
        if (_config?.GetValue("graph:test", false) != true) return;
        var prompt = _config?.Get("graph:prompt") ?? "a small red toy cube";
        _log.LogInformation("executing text->3D graph via iii axis (prompt=\"{Prompt}\")...", prompt);

        var client = _composition!.Bootstrap.Registry.Get<FantaSim.App.Command.IClient>();
        try
        {
            var result = await client.CommandAsync(new FantaSim.App.Command.CommandRequest(
                Command: "pipeline.run_text_to_3d",
                PayloadJson: $"{{\"prompt\":\"{prompt}\"}}"));
            Callable.From(() =>
            {
                if (result.Ok) _log.LogInformation("text->3D graph DONE — {ResultJson}", result.ResultJson);
                else _log.LogError("text->3D graph failed: {Message}", result.Error?.Message);
                GetTree().Quit();
            }).CallDeferred();
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            Callable.From(() => { _log.LogError("text->3D graph execution failed: {Message}", msg); GetTree().Quit(); }).CallDeferred();
        }
    }

    // iii.ping via the composed iii axis (env-guarded demo). Routes through App.Command so the
    // round-trip exercises the real dispatch path (router -> IIiiOrchestration -> bridge), not an
    // inline bridge instantiation.
    private async void PingIiiBridge()
    {
        if (_config?.GetValue("iii:ping", false) != true) return;
        if (!ClassDB.ClassExists("IiiClient"))
        {
            _log.LogError("IiiClient not registered");
            return;
        }

        var client = _composition!.Bootstrap.Registry.Get<FantaSim.App.Command.IClient>();
        var result = await client.CommandAsync(new FantaSim.App.Command.CommandRequest(
            Command: "iii.ping",
            PayloadJson: "{\"hello\":\"bridge\"}"));
        _log.LogInformation("iii ping result ok={Ok} payload={Payload}", result.Ok, result.ResultJson);
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

            // Enter assist UNDER stage — a nested dynamic parent. Assist shares the one app kernel
            // through stage's child provider, across two collectible ALCs (same kernel hash in the log).
            var assist = await sceneFlow.EnterAsync(new FantaSim.App.SceneFlow.SceneRequest("assist", "stage"));
            _log.LogInformation(
                "entered scene '{SceneId}' under '{ParentSceneId}'; bundleLoaded={AssistLoaded}; activeScenes={ActiveScenes}",
                assist.SceneId,
                assist.ParentSceneId,
                resource.IsLoaded("assist"),
                sceneFlow.ActiveScenes.Count);

            // Enter the timeline bundle under stage. ITimelineController is already registered
            // (WorldViewComposition ran sync in _Ready before this deferred call). SceneFlowProvider
            // loads the PCK first, then calls ActivateAsync — so IsLoaded("timeline") is true
            // by the time TimelinePlugin.InitializeAsync resolves the controller and mounts the view.
            var timeline = await sceneFlow.EnterAsync(new FantaSim.App.SceneFlow.SceneRequest("timeline", "stage"));
            _log.LogInformation(
                "entered scene '{SceneId}' under '{ParentSceneId}'; bundleLoaded={TimelineLoaded}; activeScenes={ActiveScenes}",
                timeline.SceneId,
                timeline.ParentSceneId,
                resource.IsLoaded("timeline"),
                sceneFlow.ActiveScenes.Count);
        }
        catch (Exception ex)
        {
            _log.LogError("initial scene entry failed: {Exception}", ex);
        }
    }

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
            _composition?.Dispose();
        }
        base._Notification(what);
    }
}
