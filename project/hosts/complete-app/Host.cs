using System;
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
        ComposeCommand(_composition);
        ComposeUi(_composition);

        GD.Print("[Host] composed services: Resource, SceneFlow, Ecs, World, Command, Ui");
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

    // Phase B (env-guarded): render the iii text->3D graph as a BoomHud nodeGraph (the visual editor).
    // Mounts the iii-graph view directly via a ViewRenderer (resident render — the UI service's
    // ShowAsync gates on collectible view bundles, which this resident demo skips).
    private void ShowIiiGraph()
    {
        if (System.Environment.GetEnvironmentVariable("FANTASIM_SHOW_GRAPH") != "1") return;
        var logger = _composition!.Bootstrap.LoggerFactory.CreateLogger("IiiGraph");

        var bridge = new FantaSim.App.Iii.IiiBridge();
        bridge.Name = "IiiBridgeGraphView";
        AddChild(bridge);

        var graph = FantaSim.App.Iii.TextTo3dGraph.Build(
            System.Environment.GetEnvironmentVariable("FANTASIM_GRAPH_PROMPT") ?? "a small red toy cube");
        var source = new FantaSim.App.Iii.IiiGraphViewSource(graph, () => new FantaSim.App.Iii.GraphExecutor(bridge));

        var uiRoot = new Control { Name = "IiiGraphRoot" };
        uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        GetTree().Root.AddChild(uiRoot);

        var renderer = new FantaSim.App.Ui.Seam.ViewRenderer(uiRoot, () => source, _ => null, logger);
        renderer.Bind();
        GD.Print($"[graph] iii-graph view mounted: {source.Nodes.Count} nodes, {source.Wires.Count} wires.");
    }

    // App-side graph executor demo (env-guarded): runs the text->3D pipeline as a DATA graph through
    // the gdext IiiClient bridge — the replacement for the Python pipeline-worker. Quits when done so
    // the windowed verification run terminates.
    private async void RunGraphTest()
    {
        if (System.Environment.GetEnvironmentVariable("FANTASIM_GRAPH_TEST") != "1") return;
        var prompt = System.Environment.GetEnvironmentVariable("FANTASIM_GRAPH_PROMPT") ?? "a small red toy cube";
        var jobId = Guid.NewGuid().ToString("N")[..8];
        GD.Print($"[graph] executing text->3D graph via C# executor (prompt=\"{prompt}\", job={jobId})...");

        var bridge = new FantaSim.App.Iii.IiiBridge();
        bridge.Name = "IiiBridgeGraph";
        AddChild(bridge); // _Ready instantiates the IiiClient child

        try
        {
            var graph = FantaSim.App.Iii.TextTo3dGraph.Build(prompt);
            var shared = new JsonObject { ["job_id"] = jobId };
            var result = await new FantaSim.App.Iii.GraphExecutor(bridge).ExecuteAsync(graph, shared);
            var glb = result["glb_path"]?.ToString() ?? "(none)";
            var usd = result["usd_path"]?.ToString() ?? "(none)";
            Callable.From(() => { GD.Print($"[graph] DONE — usd_path={usd}  glb_path={glb}"); GetTree().Quit(); }).CallDeferred();
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            Callable.From(() => { GD.PushError($"[graph] execution failed: {msg}"); GetTree().Quit(); }).CallDeferred();
        }
    }

    // Phase-1 bridge round-trip check (env-guarded): instantiate the gdext IiiClient node, fire a
    // request at the iii engine, and log the response signal. Proves Godot/C# -> Rust -> engine ->
    // worker -> response works. The IiiClient node must live in the tree so its process() drains the
    // result channel on the main thread.
    private void PingIiiBridge()
    {
        if (System.Environment.GetEnvironmentVariable("FANTASIM_III_PING") != "1") return;
        if (!ClassDB.ClassExists("IiiClient")) { GD.PushError("[iii] IiiClient not registered"); return; }

        var client = ClassDB.Instantiate("IiiClient").As<Node>();
        client.Name = "IiiClient";
        AddChild(client);
        client.Call("set_url", "ws://127.0.0.1:49134");
        client.Connect("response", Callable.From<string, string>((id, payload) =>
            GD.Print($"[iii] response id={id} payload={payload}")));
        client.Call("request", "ping", "test.echo", "{\"hello\":\"bridge\"}");
        GD.Print("[iii] fired request test.echo — awaiting response signal...");
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
