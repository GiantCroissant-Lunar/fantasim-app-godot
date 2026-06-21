using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantaSim.App.Common;
using Godot;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Common.Entry;

public partial class Host : Node
{
    private AppComposition? _composition;
    private CollectibleBundles? _collectibleBundles;
    private FantaSim.App.Ecs.IService? _ecs;
    private bool _ecsWorldReady;

    public override void _Ready()
    {
        GD.Print("[Host] composition root starting...");

        _composition = AppComposition.Activate();

        _collectibleBundles = LoadCollectibleBundles();
        _composition.Bootstrap.BuildPluginHost(_collectibleBundles);
        _ = _composition.Bootstrap.RunAsync();

        ComposeResource(_composition);
        ComposeSceneFlow(_composition);
        ComposeEcs(_composition);
        ComposeWorld(_composition);
        ComposeWorldView(_composition);
        ComposeCommand(_composition);
        ComposeIii(_composition);
        ComposeUi(_composition);

        GD.Print("[Host] composed services: Resource, SceneFlow, Ecs, World, Command, Iii, Ui");
        GD.Print("[Host] composition activated.");
        GD.Print($"[Host] iii bridge: IiiClient registered = {ClassDB.ClassExists("IiiClient")}");

        // Enter the root scene tier and KEEP it loaded (the correct flow — re-entry/teardown is a
        // test concern, not the running app). Deferred so _Ready stays non-blocking and the bundle's
        // entry scene mounts on the main thread after the tree is ready.
        Callable.From(EnterInitialScenes).CallDeferred();
        Callable.From(PingIiiBridge).CallDeferred();
        Callable.From(RunGraphTest).CallDeferred();
        Callable.From(ShowIiiGraph).CallDeferred();
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
        GD.Print($"[graph] iii-graph view mounted: {view.Nodes.Count} nodes, {view.Wires.Count} wires.");
    }

    // pipeline.run_text_to_3d via the composed iii axis (env-guarded demo). The graph is authored in
    // App.Iii.Recipes and executed by the general App.NodeGraph.GraphExecutor through the iii function
    // provider. Quits when done so the windowed verification run terminates.
    private async void RunGraphTest()
    {
        if (System.Environment.GetEnvironmentVariable("FANTASIM_GRAPH_TEST") != "1") return;
        var prompt = System.Environment.GetEnvironmentVariable("FANTASIM_GRAPH_PROMPT") ?? "a small red toy cube";
        GD.Print($"[graph] executing text->3D graph via iii axis (prompt=\"{prompt}\")...");

        var client = _composition!.Bootstrap.Registry.Get<FantaSim.App.Command.IClient>();
        try
        {
            var result = await client.CommandAsync(new FantaSim.App.Command.CommandRequest(
                Command: "pipeline.run_text_to_3d",
                PayloadJson: $"{{\"prompt\":\"{prompt}\"}}"));
            Callable.From(() =>
            {
                if (result.Ok) GD.Print($"[graph] DONE — {result.ResultJson}");
                else GD.PushError($"[graph] failed: {result.Error?.Message}");
                GetTree().Quit();
            }).CallDeferred();
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            Callable.From(() => { GD.PushError($"[graph] execution failed: {msg}"); GetTree().Quit(); }).CallDeferred();
        }
    }

    // iii.ping via the composed iii axis (env-guarded demo). Routes through App.Command so the
    // round-trip exercises the real dispatch path (router -> IIiiOrchestration -> bridge), not an
    // inline bridge instantiation.
    private async void PingIiiBridge()
    {
        if (System.Environment.GetEnvironmentVariable("FANTASIM_III_PING") != "1") return;
        if (!ClassDB.ClassExists("IiiClient")) { GD.PushError("[iii] IiiClient not registered"); return; }

        var client = _composition!.Bootstrap.Registry.Get<FantaSim.App.Command.IClient>();
        var result = await client.CommandAsync(new FantaSim.App.Command.CommandRequest(
            Command: "iii.ping",
            PayloadJson: "{\"hello\":\"bridge\"}"));
        GD.Print($"[iii] ping result ok={result.Ok} payload={result.ResultJson}");
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
            GD.Print($"[Host] entered scene '{stage.SceneId}'; bundleLoaded={resource.IsLoaded("stage")}; activeScenes={sceneFlow.ActiveScenes.Count}");

            // Enter assist UNDER stage — a nested dynamic parent. Assist shares the one app kernel
            // through stage's child provider, across two collectible ALCs (same kernel hash in the log).
            var assist = await sceneFlow.EnterAsync(new FantaSim.App.SceneFlow.SceneRequest("assist", "stage"));
            GD.Print($"[Host] entered scene '{assist.SceneId}' under '{assist.ParentSceneId}'; bundleLoaded={resource.IsLoaded("assist")}; activeScenes={sceneFlow.ActiveScenes.Count}");
        }
        catch (Exception ex)
        {
            GD.PushError($"[Host] initial scene entry failed: {ex}");
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

    private void ComposeResource(AppComposition composition)
    {
        var loggerFactory = composition.Bootstrap.LoggerFactory;
        var providerRegistry = new RegistryArchi.Core.Registry();
        providerRegistry.Register<FantaSim.App.Resource.Providers.IProvider>(
            new FantaSim.App.Resource.Bundle.BundleProvider(
                this, composition.Bootstrap.PluginHost, loggerFactory,
                _collectibleBundles!.ContainsAssembly));

        var resource = new FantaSim.App.Resource.Services.Service(
            providerRegistry,
            new FantaSim.App.Resource.Bundle.GodotBundleDirectoryResolver(),
            loggerFactory);
        composition.Bootstrap.Registry.Register<FantaSim.App.Resource.IService>(
            resource,
            new ServiceRegistration { Tags = new[] { "resource" }, Description = "Resource (bundle) service" });
        GD.Print("[Host] registered: Resource");
    }

    private void ComposeSceneFlow(AppComposition composition)
    {
        var sceneFlow = new FantaSim.App.SceneFlow.Services.Service(
            composition.RootServices,
            composition.Bootstrap.Registry,
            composition.Bootstrap.LoggerFactory);
        composition.Bootstrap.Registry.Register<FantaSim.App.SceneFlow.IService>(
            sceneFlow,
            new ServiceRegistration { Tags = new[] { "scene-flow" }, Description = "SceneFlow service" });
        GD.Print("[Host] registered: SceneFlow");
    }

    private void ComposeEcs(AppComposition composition)
    {
        var ecs = new FantaSim.App.Ecs.Services.Service(
            composition.Bootstrap.ActorSystem,
            composition.Bootstrap.LoggerFactory);
        composition.Bootstrap.Registry.Register<FantaSim.App.Ecs.IService>(
            ecs,
            new ServiceRegistration { Tags = new[] { "ecs" }, Description = "ECS service" });
        _ecs = ecs;
        try
        {
            ecs.CreateWorld(new FantaSim.App.Ecs.EcsWorldSpec("main"));
            ecs.InitializeWorld("main");
            _ecsWorldReady = true;
            GD.Print("[Host] ECS world 'main' created + initialized");
        }
        catch (Exception ex)
        {
            _ecsWorldReady = false;
            GD.PushError($"[Host] ECS bootstrap failed: {ex.Message}");
        }
        GD.Print("[Host] registered: Ecs");
    }

    private void ComposeWorld(AppComposition composition)
    {
        var world = new FantaSim.App.World.Services.Service(composition.Bootstrap.Registry);
        composition.Bootstrap.Registry.Register<FantaSim.App.World.IService>(
            world,
            new ServiceRegistration { Tags = new[] { "world" }, Description = "World service" });
        GD.Print("[Host] registered: World");

        var projection = new FantaSim.App.World.Projection.Services.FieldProjectionService(
            world,
            new[] { "app.elevation-m" },
            new[] { "app.elevation-m" });
        composition.Bootstrap.Registry.Register<FantaSim.App.World.Projection.Services.FieldProjectionService>(
            projection,
            new ServiceRegistration { Tags = new[] { "world", "projection" }, Description = "Field projection service" });
        GD.Print("[Host] World detail: projection registered");

        // Register the World axis as a node-function provider (mirrors how ComposeIii registers the iii
        // provider). It claims the world/geosphere/crust function families; the general App.NodeGraph
        // GraphExecutor resolves crust.generate to it. Pure C# (no Godot rendering yet).
        var worldProvider = new FantaSim.App.World.WorldFunctionProvider(composition.Bootstrap.LoggerFactory);
        composition.Bootstrap.Registry.Register<FantaSim.App.NodeGraph.INodeFunctionProvider>(
            worldProvider,
            new ServiceRegistration { Tags = new[] { "world", "nodegraph-provider" }, Description = "World node-function provider (crust pipeline)" });
        GD.Print("[Host] World detail: crust function provider registered");
    }

    // World view (T4 seam): mount the geodesic plate globe as the real 3D world surface. The T3
    // GlobeReconstructor builds the seeded snapshot (Godot-free); the GlobeView seam turns it into a
    // GPU-rotated ArrayMesh. Always-on (not an env-guarded demo).
    private void ComposeWorldView(AppComposition composition)
    {
        var model = new FantaSim.App.World.Globe.GlobeReconstructor();
        var snapshot = model.BuildGlobe();

        // Precompute crust features at evenly-spaced snapshots (one pipeline run); the scrubber snaps
        // to the nearest so dragging stays instant. Features accumulate, so a mountain grows in over ticks.
        var snapshotTicks = new System.Collections.Generic.List<long>();
        for (long ka = 0; ka <= 100; ka += 5) snapshotTicks.Add(ka * snapshot.TicksPerMegaAnnum);
        var featuresByTick = model.RunCrustFeatures(snapshotTicks);
        System.Func<long, byte[]> featuresAt = tick =>
        {
            long best = snapshotTicks[0];
            foreach (var s in snapshotTicks)
                if (System.Math.Abs(s - tick) < System.Math.Abs(best - tick)) best = s;
            return featuresByTick[best];
        };

        var view = new FantaSim.App.World.Seam.GlobeView(
            snapshot,
            tick => FantaSim.App.World.Globe.CanonicalTimeLabel.ForTick(tick, snapshot.TicksPerMegaAnnum),
            featuresAt);
        GetTree().Root.CallDeferred("add_child", view);
        GD.Print($"[Host] World view: globe mounted ({snapshot.CellCount} cells, {snapshot.PlateCount} plates, {snapshotTicks.Count} feature snapshots)");
    }

    private void ComposeCommand(AppComposition composition)
    {
        var loggerFactory = composition.Bootstrap.LoggerFactory;
        var registry = composition.Bootstrap.Registry;

        var orchestration = new FantaSim.App.Command.Orchestration.LocalOrchestrator(registry, loggerFactory);
        registry.Register<FantaSim.App.Command.Orchestration.IWorldOrchestration>(
            orchestration,
            new ServiceRegistration { Tags = new[] { "command", "orchestration" }, Description = "World orchestration seam (local in-process)" });

        var dispatcher = new FantaSim.App.Command.Providers.ImmediateMainThreadDispatcher();
        var commands = new FantaSim.App.Command.Services.Service(dispatcher, registry, loggerFactory, orchestration);
        registry.Register<FantaSim.App.Command.IService>(
            commands,
            new ServiceRegistration { Tags = new[] { "command" }, Description = "Command service" });

        var client = new FantaSim.App.Command.Clients.InProcessClient(commands, loggerFactory);
        registry.Register<FantaSim.App.Command.IClient>(
            client,
            new ServiceRegistration { Tags = new[] { "command", "client" }, Description = "In-process command client" });

        var health = orchestration.HealthAsync().GetAwaiter().GetResult();
        GD.Print($"[Host] registered: Command (orchestration {(health.Ok ? "healthy" : "degraded")}, {health.Commands} commands)");
    }

    private void ComposeIii(AppComposition composition)
    {
        var loggerFactory = composition.Bootstrap.LoggerFactory;
        var registry = composition.Bootstrap.Registry;

        var bridge = new FantaSim.App.Iii.Seam.IiiBridge();
        bridge.Name = "IiiBridge";
        AddChild(bridge);
        registry.Register<FantaSim.App.Iii.IIiiInvoker>(
            bridge,
            new ServiceRegistration { Tags = new[] { "iii", "invoker" }, Description = "iii bridge invoker (gdext)" });

        var provider = new FantaSim.App.Iii.IiiFunctionProvider(bridge, loggerFactory);
        registry.Register<FantaSim.App.NodeGraph.INodeFunctionProvider>(
            provider,
            new ServiceRegistration { Tags = new[] { "iii", "nodegraph-provider" }, Description = "iii node-function provider" });

        var orchestration = new FantaSim.App.Iii.IiiOrchestrator(
            new[] { (FantaSim.App.NodeGraph.INodeFunctionProvider)provider },
            bridge,
            loggerFactory);
        registry.Register<FantaSim.App.Command.Orchestration.IIiiOrchestration>(
            orchestration,
            new ServiceRegistration { Tags = new[] { "iii", "orchestration" }, Description = "iii orchestration seam" });

        var commandService = registry.Get<FantaSim.App.Command.IService>();
        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: FantaSim.App.Iii.IiiOrchestrator.WellKnownCommands.RunTextTo3d,
                Title: "Run text to 3D", Description: "Executes the text to 3D iii pipeline graph.", Category: "pipeline"),
            async (payload, ct) =>
            {
                var r = await orchestration.TriggerAsync(new FantaSim.App.Command.CommandRequest(
                    Command: FantaSim.App.Iii.IiiOrchestrator.WellKnownCommands.RunTextTo3d, PayloadJson: payload), ct);
                return JsonSerializer.Serialize(r);
            });
        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: FantaSim.App.Iii.IiiOrchestrator.WellKnownCommands.Ping,
                Title: "Ping iii", Description: "Round-trips test.echo through the iii bridge.", Category: "iii"),
            async (payload, ct) =>
            {
                var r = await orchestration.TriggerAsync(new FantaSim.App.Command.CommandRequest(
                    Command: FantaSim.App.Iii.IiiOrchestrator.WellKnownCommands.Ping, PayloadJson: payload), ct);
                return JsonSerializer.Serialize(r);
            });

        GD.Print("[Host] registered: Iii (bridge, function provider, orchestration, 2 commands)");
    }

    private void ComposeUi(AppComposition composition)
    {
        var uiRoot = new Control { Name = "UiRoot" };
        uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        GetTree().Root.CallDeferred("add_child", uiRoot);

        var viewHost = new FantaSim.App.Ui.Seam.ViewHost(
            uiRoot,
            composition.Bootstrap.Registry,
            composition.Bootstrap.Registry.Get<FantaSim.App.Resource.IService>(),
            composition.Bootstrap.LoggerFactory);

        var orchestration = composition.Bootstrap.Registry.Get<FantaSim.App.Command.Orchestration.IWorldOrchestration>();
        var runtimeSource = new RuntimeStatusViewSource(
            orchestration,
            composition.Bootstrap.LoggerFactory.CreateLogger<RuntimeStatusViewSource>());
        composition.Bootstrap.Registry.Register<FantaSim.App.Ui.IViewSource>(
            runtimeSource,
            new ServiceRegistration { Tags = new[] { "ui", "runtime-status" }, Description = "Runtime status view source" });

        var ui = new FantaSim.App.Ui.Services.Service(
            viewHost,
            composition.Bootstrap.Registry.Get<FantaSim.App.Resource.IService>(),
            composition.Bootstrap.Registry.Get<CrosscutFoundation.Messaging.IMessageBus>(),
            composition.Bootstrap.LoggerFactory);
        composition.Bootstrap.Registry.Register<FantaSim.App.Ui.IService>(
            ui,
            new ServiceRegistration { Tags = new[] { "ui" }, Description = "UI view service" });
        GD.Print("[Host] registered: Ui");
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
