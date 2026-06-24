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
using FantaSim.App.Ui.Seam;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Seam;
using Godot;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Common.Entry;

public partial class Host : Node
{
    private const string RunWorldGenerationGraphCommand = "world.run_generation_graph";

    private AppComposition? _composition;
    private ILogger _log = null!;
    private CollectibleBundles? _collectibleBundles;
    private FantaSim.App.Ecs.IService? _ecs;
    private bool _ecsWorldReady;
    // Sub-project B: the Godot-free ECS cell model that derives per-cell elevation. Owned here so its
    // lifetime matches the host; the relief render (sub-project C) reads GetElevations() off it.
    private FantaSim.App.World.Cells.CellElevationModel? _cellElevation;

    // World-graph demo view state (env-guarded entry points below). Host-owned because they touch
    // the scene tree + quit the app; they are NOT composition modules.
    private WorldGenerationTimelineGraphBindingSlot? _worldGraphTimelineBinding;
    private FantaSim.App.Ui.Seam.ViewRenderer? _worldGraphRenderer;
    private FantaSim.App.Ui.NodeGraph.NodeGraphViewSource? _worldGraphView;
    private Control? _worldGraphUiRoot;

    public override void _Ready()
    {
        GD.Print("[Host] composition root starting...");

        _composition = AppComposition.Activate();
        _log = _composition.Bootstrap.LoggerFactory.CreateLogger("Host");

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
        WorldComposition.ComposeWorld(ctx);
        var (cellElevation, renderOptions) = CellElevationComposition.ComposeCellElevation(ctx);
        CommandComposition.ComposeCommand(ctx);
        IiiComposition.ComposeIii(ctx, tree, this);
        _gpuComputeService = GpuComposition.ComposeGpu(ctx);
        _gpuShaderService = GpuShaderComposition.ComposeGpuShader(ctx);
        WorldViewComposition.ComposeWorldView(ctx, tree, cellElevation, renderOptions);
        _cellElevation = cellElevation;
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
        Callable.From(RunWorldGraphTest).CallDeferred();
        Callable.From(ShowIiiGraph).CallDeferred();
        Callable.From(ShowWorldGraph).CallDeferred();
        Callable.From(RunGpuSmoke).CallDeferred();
        Callable.From(RunGpuShaderSmoke).CallDeferred();
    }

    // Mount the iii text->3D graph as a BoomHud nodeGraph (env-guarded demo). Uses the GENERAL
    // App.Ui.NodeGraph view over a read-only graph source; RUN routes through App.Command like the
    // other demos. No per-domain view-source duplication.
    private void ShowIiiGraph()
    {
        if (System.Environment.GetEnvironmentVariable("FANTASIM_SHOW_GRAPH") != "1") return;
        var logger = _composition!.Bootstrap.LoggerFactory.CreateLogger("IiiGraph");
        var prompt = System.Environment.GetEnvironmentVariable("FANTASIM_GRAPH_PROMPT") ?? "a small red toy cube";

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

    // Mount the current world-generation graph as a BoomHud nodeGraph (env-guarded demo). Uses the
    // typed App.World authoring source projected into the GENERAL App.Ui.NodeGraph view; RUN compiles
    // the live edited graph and routes execution through App.Command.
    private void ShowWorldGraph()
    {
        if (System.Environment.GetEnvironmentVariable("FANTASIM_SHOW_WORLD_GRAPH") != "1") return;
        var logger = _composition!.Bootstrap.LoggerFactory.CreateLogger("WorldGraph");

        WorldGenerationGraphFamilySource graphSource;
        try
        {
            graphSource = CreateWorldGenerationGraphSource();
        }
        catch (Exception ex)
        {
            _log.LogError("world-generation graph selection failed: {Message}", ex.Message);
            return;
        }

        var followTimeline = ReadWorldGraphFollowTimeline();
        var timeline = followTimeline
            ? _composition.Bootstrap.Registry.TryGet<FantaSim.App.World.Composition.ITimelineController>()
            : null;
        _worldGraphTimelineBinding ??= new WorldGenerationTimelineGraphBindingSlot();
        _worldGraphTimelineBinding.Rebind(timeline, graphSource, followTimeline);
        if (followTimeline && timeline is null)
        {
            _log.LogWarning("timeline follow requested, but no ITimelineController is registered.");
        }

        // Tear down any previous world-graph view stack before rebinding so we do not leak
        // timeline -> graphSource -> view -> renderer subscriptions.
        _worldGraphRenderer?.Dispose();
        _worldGraphView?.Dispose();
        _worldGraphUiRoot?.QueueFree();

        var client = _composition.Bootstrap.Registry.Get<FantaSim.App.Command.IClient>();
        _worldGraphView = new FantaSim.App.Ui.NodeGraph.NodeGraphViewSource(
            graphSource,
            runAsync: async () =>
            {
                var compiled = graphSource.CompileForExecution();
                var payload = WorldGenerationGraphExecutionPayload.Serialize(
                    compiled.Document,
                    WorldGraphSharedParams(graphSource.ActiveTick),
                    graphSource.TryBuildExecutionScopeKey());
                var result = await client.CommandAsync(new FantaSim.App.Command.CommandRequest(
                    Command: RunWorldGenerationGraphCommand,
                    PayloadJson: payload));
                return JsonSerializer.SerializeToNode(result)?.AsObject() ?? new JsonObject();
            },
            title: "world generation graph");

        _worldGraphUiRoot = new Control { Name = "WorldGraphRoot" };
        _worldGraphUiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        GetTree().Root.AddChild(_worldGraphUiRoot);

        _worldGraphRenderer = new FantaSim.App.Ui.Seam.ViewRenderer(_worldGraphUiRoot, () => _worldGraphView, _ => null, logger);
        _worldGraphRenderer.Bind();
        if (graphSource.CompositionWarnings.Count > 0)
            _log.LogWarning("world-generation graph warnings: {Warnings}", string.Join("; ", graphSource.CompositionWarnings));
        _log.LogInformation(
            "world-generation graph view mounted: graph={GraphId}, tick={Tick}, subgraphs={Subgraphs}, uiSubgraphs={UiSubgraphs}, {NodeCount} nodes, {WireCount} wires.",
            graphSource.ActiveGraphId,
            graphSource.ActiveTick,
            graphSource.ActiveSubgraphs.Count,
            _worldGraphView.Subgraphs.Count,
            _worldGraphView.Nodes.Count,
            _worldGraphView.Wires.Count);
    }

    private WorldGenerationGraphFamilySource CreateWorldGenerationGraphSource()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var scheduleKind = ReadWorldGraphEnv("FANTASIM_WORLD_GRAPH_SCHEDULE", WorldRegimeScheduleKinds.Sphere);
        var defaultRegime = string.Equals(scheduleKind, WorldRegimeScheduleKinds.BodyFormation, StringComparison.Ordinal)
            ? "planetesimal-swarm"
            : "mobile-plate";
        var regimeId = ReadWorldGraphEnv("FANTASIM_WORLD_GRAPH_REGIME", defaultRegime);
        var sphereId = System.Environment.GetEnvironmentVariable("FANTASIM_WORLD_GRAPH_SPHERE");
        if (string.IsNullOrWhiteSpace(sphereId)
            && string.Equals(scheduleKind, WorldRegimeScheduleKinds.Sphere, StringComparison.Ordinal))
        {
            sphereId = WorldGenerationGraphDefaults.GeosphereSphereId;
        }

        return WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            scheduleKind,
            regimeId,
            ReadWorldGraphTick(),
            string.IsNullOrWhiteSpace(sphereId) ? null : sphereId);
    }

    private static string ReadWorldGraphEnv(string key, string fallback)
    {
        var value = System.Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private long ReadWorldGraphTick()
    {
        var value = System.Environment.GetEnvironmentVariable("FANTASIM_WORLD_GRAPH_TICK");
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tick))
            return tick;

        _log.LogWarning("invalid FANTASIM_WORLD_GRAPH_TICK '{Value}', using 0.", value);
        return 0;
    }

    private static bool ReadWorldGraphFollowTimeline()
    {
        var value = System.Environment.GetEnvironmentVariable("FANTASIM_WORLD_GRAPH_FOLLOW_TIMELINE");
        return !string.Equals(value, "0", StringComparison.Ordinal)
               && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject WorldGraphSharedParams(long tick)
        => new()
        {
            ["canonicalTick"] = tick,
            ["tick"] = tick,
        };

    // pipeline.run_text_to_3d via the composed iii axis (env-guarded demo). The graph is authored in
    // App.Iii.Recipes and executed by the general App.NodeGraph.GraphExecutor through the iii function
    // provider. Quits when done so the windowed verification run terminates.
    private async void RunGraphTest()
    {
        if (System.Environment.GetEnvironmentVariable("FANTASIM_GRAPH_TEST") != "1") return;
        var prompt = System.Environment.GetEnvironmentVariable("FANTASIM_GRAPH_PROMPT") ?? "a small red toy cube";
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

    // world.run_generation_graph via the composed World axis (env-guarded smoke). Executes the default
    // typed world-generation graph through App.Command -> GraphExecutor -> WorldFunctionProvider and
    // quits when done.
    private async void RunWorldGraphTest()
    {
        if (System.Environment.GetEnvironmentVariable("FANTASIM_WORLD_GRAPH_TEST") != "1") return;
        _log.LogInformation("executing world-generation graph via world axis...");

        var client = _composition!.Bootstrap.Registry.Get<FantaSim.App.Command.IClient>();
        try
        {
            var graphSource = CreateWorldGenerationGraphSource();
            _log.LogInformation(
                "selected world-generation graph: graph={GraphId}, tick={Tick}, subgraphs={Subgraphs}",
                graphSource.ActiveGraphId,
                graphSource.ActiveTick,
                graphSource.ActiveSubgraphs.Count);
            if (graphSource.CompositionWarnings.Count > 0)
                _log.LogWarning("world-generation graph warnings: {Warnings}", string.Join("; ", graphSource.CompositionWarnings));
            var compiled = graphSource.CompileForExecution();
            var payload = WorldGenerationGraphExecutionPayload.Serialize(
                compiled.Document,
                WorldGraphSharedParams(graphSource.ActiveTick),
                graphSource.TryBuildExecutionScopeKey());
            var result = await client.CommandAsync(new FantaSim.App.Command.CommandRequest(
                Command: RunWorldGenerationGraphCommand,
                PayloadJson: payload));
            Callable.From(() =>
            {
                if (result.Ok) _log.LogInformation("world-generation graph DONE - {ResultJson}", result.ResultJson);
                else _log.LogError("world generation failed: {Message}", result.Error?.Message);
                GetTree().Quit();
            }).CallDeferred();
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            Callable.From(() => { _log.LogError("world generation execution failed: {Message}", msg); GetTree().Quit(); }).CallDeferred();
        }
    }

    // iii.ping via the composed iii axis (env-guarded demo). Routes through App.Command so the
    // round-trip exercises the real dispatch path (router -> IIiiOrchestration -> bridge), not an
    // inline bridge instantiation.
    private async void PingIiiBridge()
    {
        if (System.Environment.GetEnvironmentVariable("FANTASIM_III_PING") != "1") return;
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
            _worldGraphRenderer?.Dispose();
            _worldGraphRenderer = null;
            _worldGraphView?.Dispose();
            _worldGraphView = null;
            _worldGraphUiRoot?.QueueFree();
            _worldGraphUiRoot = null;
            _worldGraphTimelineBinding?.Dispose();
            _worldGraphTimelineBinding = null;

            _cellElevation?.Dispose();
            _composition?.Dispose();
        }
        base._Notification(what);
    }
}
