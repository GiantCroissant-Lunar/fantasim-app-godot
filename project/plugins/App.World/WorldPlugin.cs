using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Common;
using FantaSim.App.World.GenerationGraph;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PluginArchi.Extensibility.Abstractions;
using ServiceArchi.Contracts;

namespace FantaSim.App.World;

/// <summary>
/// Data-bundle entry for the world domain. Loaded from world.pck into its collectible ALC; composes
/// the world service graph (IService + FieldView + node provider) via <see cref="WorldComposition"/>
/// and self-registers the <c>world.run_generation_graph</c> command against the resident App.Command
/// service. Pure C# (no Godot): the globe/view composition stays dormant here and is revived by a
/// follow-up bundle once the Environment scene-tree handoff is in place.
/// </summary>
[Plugin("app.world", Name = "World", Description = "Composes the world data service graph and the world.run_generation_graph command.", Tags = "domain-bundle")]
public sealed partial class WorldPlugin : ILifecyclePlugin
{
    private const string RunWorldGenerationGraphCommand = "world.run_generation_graph";

    private IDisposable? _worldCompositionHandle;
    private IRegistry? _registry;
    private ILogger? _log;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        var registry = context.Services.GetRequiredService<IRegistry>();
        var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
        _registry = registry;
        _log = loggerFactory.CreateLogger("WorldPlugin");

        var ctx = new HostCompositionContext(registry, loggerFactory);
        _worldCompositionHandle = WorldComposition.ComposeWorld(ctx);

        RegisterWorldCommand(registry);

        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _worldCompositionHandle?.Dispose();
        _worldCompositionHandle = null;
        _registry = null;
        _log = null;
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
