using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Common;
using FantaSim.App.World.Cells;
using FantaSim.App.World.GenerationGraph;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PluginArchi.Extensibility.Abstractions;
using ServiceArchi.Contracts;

namespace FantaSim.App.World.Seam;

[Plugin("app.world", Name = "World Bundle", Description = "Composes the world service graph and globe view under stage.", Tags = "scene-tier")]
public sealed partial class WorldPlugin : ILifecyclePlugin
{
    private const string RunWorldGenerationGraphCommand = "world.run_generation_graph";

    private IDisposable? _worldCompositionHandle;
    private CellElevationModel? _cellElevation;
    private IRegistry? _registry;
    private ILogger? _log;

    // Static handoff (locked Q2): Host.cs sets this before plugin host init so InitializeAsync
    // can pass the Godot SceneTree to WorldViewComposition. Phase 2 moves this to the Environment
    // entry scene's _Ready.
    public static Godot.SceneTree? PendingSceneTree;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        var registry = context.Services.GetRequiredService<IRegistry>();
        var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
        _registry = registry;
        _log = loggerFactory.CreateLogger("WorldPlugin");

        var ctx = new HostCompositionContext(registry, loggerFactory);

        _worldCompositionHandle = WorldComposition.ComposeWorld(ctx);

        var (cellElevation, renderOptions) = CellElevationComposition.ComposeCellElevation(ctx);
        _cellElevation = cellElevation;

        var tree = PendingSceneTree;
        if (tree is not null)
        {
            WorldViewComposition.ComposeWorldView(ctx, tree, cellElevation, renderOptions);
        }
        else
        {
            _log.LogWarning("WorldPlugin: no PendingSceneTree set; GlobeView will not mount.");
        }

        // The command service is registered by CommandComposition which runs AFTER plugin init in
        // the host composition sequence. Defer registration to the next idle frame.
        Godot.Callable.From(() => RegisterWorldCommand(registry)).CallDeferred();

        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _cellElevation?.Dispose();
        _cellElevation = null;
        _worldCompositionHandle?.Dispose();
        _worldCompositionHandle = null;
        PendingSceneTree = null;
        return ValueTask.CompletedTask;
    }

    private void RegisterWorldCommand(IRegistry registry)
    {
        var commandService = registry.TryGet<FantaSim.App.Command.IService>();
        if (commandService is null)
        {
            _log?.LogWarning("WorldPlugin: command service not registered; {Command} unavailable.", RunWorldGenerationGraphCommand);
            return;
        }

        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: RunWorldGenerationGraphCommand,
                Title: "Run world generation graph",
                Description: "Executes a compiled App.NodeGraph world-generation graph through registered node providers.",
                Category: "world"),
            async (payloadJson, ct) =>
            {
                var request = WorldGenerationGraphExecutionPayload.Deserialize(payloadJson ?? string.Empty);

                var providers = registry.GetAll<FantaSim.App.NodeGraph.INodeFunctionProvider>().ToArray();
                var runner = new WorldGenerationGraphRunner(providers);
                var run = await runner.RunAsync(request.Graph, request.SharedParams, request.ExecutionScopeKey, ct);
                var result = WorldGenerationGraphRunner.ToCommandResult(run);
                var generation = PublishWorldGenerationGraphRun(registry, run);
                if (generation is not null)
                    result["generation"] = JsonSerializer.SerializeToNode(generation);
                return result.ToJsonString();
            });
        _log?.LogInformation("WorldPlugin: registered {Command}", RunWorldGenerationGraphCommand);
    }

    private static FantaSim.App.World.Dto.WorldGenerationResult? PublishWorldGenerationGraphRun(
        IRegistry registry,
        WorldGenerationGraphRunOutput run)
    {
        var world = registry.TryGet<FantaSim.App.World.IService>();
        if (world is null)
            return null;

        var generation = world.RunGenerationAsync(WorldGenerationGraphRunner.ToGenerationRequest(run));
        if (generation.Success)
            registry.TryGet<FantaSim.App.Ecs.IService>()?.UpdateAll(0f);

        return generation;
    }
}
