using FantaSim.App.Common;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;
using System.Collections.Generic;
using System.Linq;
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

                var sceneFlow = registry.TryGet<FantaSim.App.SceneFlow.IService>();
                var target = sceneFlow?.ActiveScenes.FirstOrDefault(s => s.SceneId == bundleId);
                if (sceneFlow is not null && target is not null)
                {
                    // Scene-tier reload via Exit->Enter so the old ALC's SceneFlow pin is released.
                    // ExitAsync cascades to descendant scenes, so capture the subtree (root + all
                    // transitive descendants, in ActiveScenes entry order) and re-Enter it after.
                    var subtree = CollectSubtree(sceneFlow.ActiveScenes, bundleId);
                    await sceneFlow.ExitAsync(bundleId, ct).ConfigureAwait(false);
                    foreach (var node in subtree)
                        await sceneFlow.EnterAsync(new FantaSim.App.SceneFlow.SceneRequest(node.SceneId, node.ParentSceneId), ct).ConfigureAwait(false);
                }
                else
                {
                    await resource.ReloadAsync(bundleId, ct).ConfigureAwait(false); // plugin-only bundle, or no SceneFlow
                }
                log.LogInformation("resource.reload_bundle: reloaded '{BundleId}'.", bundleId);
                return JsonSerializer.Serialize(new { ok = true, bundleId });
            });

        var health = orchestration.HealthAsync().GetAwaiter().GetResult();
        log.LogInformation($"[Host] registered: Command (orchestration {(health.Ok ? "healthy" : "degraded")}, {health.Commands} commands)");
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

    private static List<FantaSim.App.SceneFlow.SceneSession> CollectSubtree(
        IReadOnlyList<FantaSim.App.SceneFlow.SceneSession> active,
        string rootSceneId)
    {
        var reachable = new HashSet<string> { rootSceneId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var s in active)
            {
                if (reachable.Contains(s.SceneId))
                    continue;
                if (s.ParentSceneId is not null && reachable.Contains(s.ParentSceneId))
                {
                    reachable.Add(s.SceneId);
                    changed = true;
                }
            }
        }

        var result = new List<FantaSim.App.SceneFlow.SceneSession>(reachable.Count);
        foreach (var s in active)
        {
            if (reachable.Contains(s.SceneId))
                result.Add(s);
        }
        return result;
    }
}
