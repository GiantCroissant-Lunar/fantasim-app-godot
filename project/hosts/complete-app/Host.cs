using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantaSim.App.Common;
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
    private CollectibleBundles? _collectibleBundles;
    private FantaSim.App.Ecs.IService? _ecs;
    private bool _ecsWorldReady;
    // Sub-project B: the Godot-free ECS cell model that derives per-cell elevation. Owned here so its
    // lifetime matches the host; the relief render (sub-project C) reads GetElevations() off it.
    private FantaSim.App.World.Cells.CellElevationModel? _cellElevation;

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
        ComposeCellElevation(_composition);
        ComposeCommand(_composition);
        ComposeIii(_composition);
        ComposeGpu(_composition);
        ComposeGpuShader(_composition);
        // World view (the T4 relief render) is composed AFTER the cell-elevation model and the GPU
        // compute service so it can feed per-cell elevation through the compute displacement path.
        ComposeWorldView(_composition);
        ComposeUi(_composition);

        GD.Print("[Host] composed services: Resource, SceneFlow, Ecs, World, Command, Iii, Gpu, GpuShader, Ui");
        GD.Print("[Host] composition activated.");
        GD.Print($"[Host] iii bridge: IiiClient registered = {ClassDB.ClassExists("IiiClient")}");

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
        GD.Print($"[graph] iii-graph view mounted: {view.Nodes.Count} nodes, {view.Wires.Count} wires.");
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
            GD.PushError($"[graph] world-generation graph selection failed: {ex.Message}");
            return;
        }

        var client = _composition.Bootstrap.Registry.Get<FantaSim.App.Command.IClient>();
        var view = new FantaSim.App.Ui.NodeGraph.NodeGraphViewSource(
            graphSource,
            runAsync: async () =>
            {
                var compiled = graphSource.CompileForExecution();
                var payload = JsonSerializer.Serialize(compiled.Document);
                var result = await client.CommandAsync(new FantaSim.App.Command.CommandRequest(
                    Command: RunWorldGenerationGraphCommand,
                    PayloadJson: payload));
                return JsonSerializer.SerializeToNode(result)?.AsObject() ?? new JsonObject();
            },
            title: "world generation graph");

        var uiRoot = new Control { Name = "WorldGraphRoot" };
        uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        GetTree().Root.AddChild(uiRoot);

        var renderer = new FantaSim.App.Ui.Seam.ViewRenderer(uiRoot, () => view, _ => null, logger);
        renderer.Bind();
        if (graphSource.CompositionWarnings.Count > 0)
            GD.PushWarning($"[graph] world-generation graph warnings: {string.Join("; ", graphSource.CompositionWarnings)}");
        GD.Print($"[graph] world-generation graph view mounted: graph={graphSource.ActiveGraphId}, tick={graphSource.ActiveTick}, subgraphs={graphSource.ActiveSubgraphs.Count}, uiSubgraphs={view.Subgraphs.Count}, {view.Nodes.Count} nodes, {view.Wires.Count} wires.");
    }

    private static WorldGenerationGraphFamilySource CreateWorldGenerationGraphSource()
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

    private static long ReadWorldGraphTick()
    {
        var value = System.Environment.GetEnvironmentVariable("FANTASIM_WORLD_GRAPH_TICK");
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tick))
            return tick;

        GD.PushWarning($"[graph] invalid FANTASIM_WORLD_GRAPH_TICK '{value}', using 0.");
        return 0;
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

    // world.run_generation_graph via the composed World axis (env-guarded smoke). Executes the default
    // typed world-generation graph through App.Command -> GraphExecutor -> WorldFunctionProvider and
    // quits when done.
    private async void RunWorldGraphTest()
    {
        if (System.Environment.GetEnvironmentVariable("FANTASIM_WORLD_GRAPH_TEST") != "1") return;
        GD.Print("[graph] executing world-generation graph via world axis...");

        var client = _composition!.Bootstrap.Registry.Get<FantaSim.App.Command.IClient>();
        try
        {
            var graphSource = CreateWorldGenerationGraphSource();
            GD.Print($"[graph] selected world-generation graph: graph={graphSource.ActiveGraphId}, tick={graphSource.ActiveTick}, subgraphs={graphSource.ActiveSubgraphs.Count}");
            if (graphSource.CompositionWarnings.Count > 0)
                GD.PushWarning($"[graph] world-generation graph warnings: {string.Join("; ", graphSource.CompositionWarnings)}");
            var compiled = graphSource.CompileForExecution();
            var payload = JsonSerializer.Serialize(compiled.Document);
            var result = await client.CommandAsync(new FantaSim.App.Command.CommandRequest(
                Command: RunWorldGenerationGraphCommand,
                PayloadJson: payload));
            Callable.From(() =>
            {
                if (result.Ok) GD.Print($"[graph] WORLD DONE - {result.ResultJson}");
                else GD.PushError($"[graph] world generation failed: {result.Error?.Message}");
                GetTree().Quit();
            }).CallDeferred();
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            Callable.From(() => { GD.PushError($"[graph] world generation execution failed: {msg}"); GetTree().Quit(); }).CallDeferred();
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

            // Enter the timeline bundle under stage. ITimelineController is already registered
            // (ComposeWorldView ran sync in _Ready before this deferred call). SceneFlowProvider
            // loads the PCK first, then calls ActivateAsync — so IsLoaded("timeline") is true
            // by the time TimelinePlugin.InitializeAsync resolves the controller and mounts the view.
            var timeline = await sceneFlow.EnterAsync(new FantaSim.App.SceneFlow.SceneRequest("timeline", "stage"));
            GD.Print($"[Host] entered scene '{timeline.SceneId}' under '{timeline.ParentSceneId}'; bundleLoaded={resource.IsLoaded("timeline")}; activeScenes={sceneFlow.ActiveScenes.Count}");
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

        var projection = new FantaSim.App.World.FieldView.Services.FieldViewService(
            world,
            new[] { "app.elevation-m" },
            new[] { "app.elevation-m" });
        composition.Bootstrap.Registry.Register<FantaSim.App.World.FieldView.Services.FieldViewService>(
            projection,
            new ServiceRegistration { Tags = new[] { "world", "projection" }, Description = "Field view service" });
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
    // GlobeReconstructor (onset-aware, via FromOnsetRoster) builds the seeded snapshot (Godot-free);
    // the GlobeView seam turns it into GPU-rotated ArrayMeshes. The RegimeTimelineTransport drives
    // tick playback + regime threading (SetTick + SetRegime on GlobeView). Always-on (not env-guarded).
    //
    // World seed + tessellation frequency: no per-world config exists yet; using fixed defaults that
    // produce a stable, deterministic globe. Seed 2024, frequency 3 (1280 cells, ~6 plates at onset).
    // These match the values used by ComposeCellElevation (both share one OnsetRoster build).
    private const int WorldSeed = 2024;
    private const int TessellationFrequency = 3;

    private void ComposeWorldView(AppComposition composition)
    {
        // PLAN4-TASK4: onset-aware path — replaces new GlobeReconstructor() (parameterless legacy).
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var roster = OnsetRoster.Build(WorldSeed, onsetTick, TessellationFrequency);
        var schedule = SphereRegimeScheduleDefaults.GeosphereDefault;
        var model = GlobeReconstructor.FromOnsetRoster(roster, onsetTick, schedule, TessellationFrequency);

        // Build the base snapshot at onset (full N-plate globe used for topology caching).
        // GlobePlateSurfaces caches watertight topology from this snapshot — since plate count/cell
        // assignment is fixed, one topology build covers all ticks (including pre-onset lid ticks
        // where caps are hidden by SetRegime(showsPlateFeatures=false)).
        var snapshot = model.BuildGlobeAt(onsetTick);

        // Precompute crust features at evenly-spaced snapshots (one pipeline run); the scrubber snaps
        // to the nearest so dragging stays instant. Features accumulate, so a mountain grows in over ticks.
        // Range: [0, 120 anchors] at every 5 anchors — covers pre-onset (all-zero, gated) + mobile-plate.
        long maxTransportTick = onsetTick + 20_000_000L; // ~20 Ma past onset (well into mobile-plate)
        var snapshotTicks = new System.Collections.Generic.List<long>();
        for (long anchor = 0; anchor <= 120; anchor += 5) snapshotTicks.Add(anchor * snapshot.TicksPerAnchor);
        var featuresByTick = model.RunCrustFeatures(snapshotTicks);
        System.Func<long, byte[]> featuresAt = tick =>
        {
            long best = snapshotTicks[0];
            foreach (var s in snapshotTicks)
                if (System.Math.Abs(s - tick) < System.Math.Abs(best - tick)) best = s;
            return featuresByTick[best];
        };

        // Per-cell elevation feed for the watertight caps: drive sub-project B's CellElevationModel to
        // the tick and hand the seam a per-cell double[] (indexed by cell id). The seam folds it into the
        // T3 GlobePlateSurfaces, which builds one WATERTIGHT per-plate cap per tick via cartography.
        var elevationModel = _cellElevation;
        System.Func<long, double[]>? elevationsAt = elevationModel is null ? null : tick =>
        {
            elevationModel.UpdateForTick(tick);
            return elevationModel.GetElevations();
        };

        // T3 (Godot-free) un-shattering: cache the per-plate watertight topology once from the snapshot;
        // the seam rebuilds heights per tick. Replaces the old loose-tile (1 tri/cell, unshared corners)
        // mesh + GPU-compute relief displacement.
        var plateSurfaces = new FantaSim.App.World.Globe.GlobePlateSurfaces(snapshot);

        var view = new FantaSim.App.World.Seam.GlobeView(
            snapshot,
            plateSurfaces,
            tick => FantaSim.App.World.Globe.CanonicalTimeLabel.ForTick(tick, snapshot.TicksPerAnchor),
            featuresAt,
            elevationsAt);
        // Extend the scrubber to cover the full transport range so the user can drag to onset and beyond.
        view.SetMaxTick(maxTransportTick);
        GetTree().Root.CallDeferred("add_child", view);


        // Build the atmosphere schedule (same onset tick) so the timeline HUD can show both spheres.
        var atmosphereSchedule = SphereRegimeScheduleDefaults.AtmosphereFor(onsetTick);

        // Register the resident ITimelineController adapter. Must happen here (sync, before any
        // deferred EnterAsync calls) so the timeline bundle can resolve it during ActivateAsync.
        var controller = new FantaSim.App.World.Seam.TimelineController(
            view, schedule, atmosphereSchedule, maxTransportTick);
        composition.Bootstrap.Registry.Register<FantaSim.App.World.Composition.ITimelineController>(controller);

        // Seed the initial regime so GlobeView starts in the correct state before the first tick fires.
        var initialRegime = schedule.RegimeAt(0);
        if (initialRegime is not null)
            view.SetRegime(initialRegime.RegimeId, initialRegime.ShowsPlateFeatures, initialRegime.DefaultColorByField);

        GD.Print($"[Host] World view: globe mounted ({snapshot.CellCount} cells, {snapshot.PlateCount} plates, " +
                 $"{snapshotTicks.Count} feature snapshots, elevationFeed={(elevationsAt is not null)}, watertight caps, " +
                 $"onset={onsetTick:N0}, seed={WorldSeed}, freq={TessellationFrequency}); ITimelineController registered");
    }

    // Sub-project B (ECS cell model + elevation derivation): model each globe cell as an ECS entity
    // carrying its crust fields and derive a per-cell elevation via CellElevationSystem. Godot-free,
    // fully unit-testable; the relief render (sub-project C) consumes GetElevations() to upload to the
    // GPU. No rendering here. CellElevationSystem is registered (via ArchSystemRunner.Register) into the
    // model's own ECS world — the same mechanism EcsWorldActor uses for ReduceFieldsSystem — and that
    // world is the load-bearing one C reads from. The actor "main" heartbeat world is left untouched
    // (registering there would need IService surface it does not expose, and isn't what C consumes).
    //
    // PLAN4-TASK4: onset-aware path — replaces new GlobeReconstructor() (parameterless legacy).
    // Uses the same WorldSeed + TessellationFrequency constants as ComposeWorldView so both share
    // the same deterministic plate geometry.
    private void ComposeCellElevation(AppComposition composition)
    {
        try
        {
            long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
            var roster = OnsetRoster.Build(WorldSeed, onsetTick, TessellationFrequency);
            var schedule = SphereRegimeScheduleDefaults.GeosphereDefault;
            var reconstructor = GlobeReconstructor.FromOnsetRoster(roster, onsetTick, schedule, TessellationFrequency);

            // Build snapshot at onset (the full N-plate mesh) to get TicksPerAnchor for the cadence.
            var snapshot = reconstructor.BuildGlobeAt(onsetTick);

            // Same anchor cadence the world view uses (120 anchors, every 5), so the cell model and
            // the feature scrubber sample the same crust run. Pre-onset ticks get empty states (gated
            // by ShowsPlateFeatures — RunCrustEvolution short-circuits them).
            var snapshotTicks = new System.Collections.Generic.List<long>();
            for (long anchor = 0; anchor <= 120; anchor += 5) snapshotTicks.Add(anchor * snapshot.TicksPerAnchor);

            // Build populates one ECS entity per cell and registers CellElevationSystem into the model's
            // ArchSystemRunner (mirrors how ReduceFieldsSystem registers into EcsWorldActor's runner).
            _cellElevation = FantaSim.App.World.Cells.CellElevationModel.Build(reconstructor, snapshotTicks);

            // Populate/derive for the onset tick (first active tick — pre-onset would be empty)
            // and report the relief extent C will upload.
            _cellElevation.UpdateForTick(onsetTick);
            var elevations = _cellElevation.GetElevations();
            double min = double.MaxValue, max = double.MinValue;
            foreach (var e in elevations) { if (e < min) min = e; if (e > max) max = e; }
            GD.Print($"[Host] Cell elevation: {elevations.Length} cells derived (onset tick={onsetTick:N0}), range [{min:F1}, {max:F1}]");
        }
        catch (Exception ex)
        {
            GD.PushError($"[Host] Cell elevation model failed: {ex.Message}");
        }
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

        commands.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: RunWorldGenerationGraphCommand,
                Title: "Run world generation graph",
                Description: "Executes a compiled App.NodeGraph world-generation graph through registered node providers.",
                Category: "world"),
            async (payloadJson, ct) =>
            {
                if (string.IsNullOrWhiteSpace(payloadJson))
                    throw new ArgumentException("World generation graph payload is required.", nameof(payloadJson));

                var graph = JsonSerializer.Deserialize<FantaSim.App.NodeGraph.GraphDocument>(
                    payloadJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("World generation graph payload could not be deserialized.");

                var providers = registry.GetAll<FantaSim.App.NodeGraph.INodeFunctionProvider>().ToArray();
                var executor = new FantaSim.App.NodeGraph.GraphExecutor(providers);
                var result = await executor.ExecuteAsync(graph, cancellationToken: ct);
                return result.ToJsonString();
            });

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

        // Register IViewHost so bundle plugins (e.g. TimelinePlugin) can resolve it and call
        // Mount() directly without going through IService.ShowAsync — which would re-enter the
        // BundleHost gate and deadlock when called from within a plugin's InitializeAsync.
        composition.Bootstrap.Registry.Register<FantaSim.App.Ui.Providers.IViewHost>(
            viewHost,
            new ServiceRegistration { Tags = new[] { "ui" }, Description = "UI view host" });

        var ui = new FantaSim.App.Ui.Services.Service(
            viewHost,
            composition.Bootstrap.Registry.Get<FantaSim.App.Resource.IService>(),
            composition.Bootstrap.Registry.Get<CrosscutFoundation.Messaging.IMessageBus>(),
            composition.Bootstrap.LoggerFactory);
        composition.Bootstrap.Registry.Register<FantaSim.App.Ui.IService>(
            ui,
            new ServiceRegistration { Tags = new[] { "ui" }, Description = "UI view service" });
        GD.Print("[Host] registered: Ui (IViewHost + IService)");
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationExitTree)
        {
            _cellElevation?.Dispose();
            _composition?.Dispose();
        }
        base._Notification(what);
    }
}
