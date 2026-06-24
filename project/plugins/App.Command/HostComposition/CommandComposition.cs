using FantaSim.App.Common;
using FantaSim.App.World.GenerationGraph;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.Command;

public static class CommandComposition
{
    public static void ComposeCommand(HostCompositionContext ctx)
    {
        var log = ctx.LoggerFactory.CreateLogger("HostComposition.Command");
        var loggerFactory = ctx.LoggerFactory;
        var registry = ctx.Registry;

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
                Id: "world.run_generation_graph",
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

        commands.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: "resource.reload_bundle",
                Title: "Reload bundle",
                Description: "Unloads and hot-reloads a collectible bundle's ALC by id. Payload: {\"bundleId\":\"stage|assist|timeline\"}.",
                Category: "resource"),
            async (payloadJson, ct) =>
            {
                var bundleId = ParseBundleId(payloadJson);
                if (string.IsNullOrWhiteSpace(bundleId))
                    return JsonSerializer.Serialize(new { ok = false, error = "missing 'bundleId'" });

                var resource = registry.TryGet<FantaSim.App.Resource.IService>();
                if (resource is null)
                    return JsonSerializer.Serialize(new { ok = false, error = "resource service not registered" });

                await resource.ReloadAsync(bundleId, ct).ConfigureAwait(false);
                log.LogInformation("resource.reload_bundle: reloaded '{BundleId}'.", bundleId);
                return JsonSerializer.Serialize(new { ok = true, bundleId });
            });

        var health = orchestration.HealthAsync().GetAwaiter().GetResult();
        log.LogInformation($"[Host] registered: Command (orchestration {(health.Ok ? "healthy" : "degraded")}, {health.Commands} commands)");
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

    private static string? ParseBundleId(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;
        try
        {
            var node = JsonNode.Parse(payloadJson);
            return node?["bundleId"]?.GetValue<string>() ?? node?["id"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }
}
