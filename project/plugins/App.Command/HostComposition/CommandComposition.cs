using FantaSim.App.Common;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;
using System;
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

                // Drop the resident ViewHost reference to this bundle's IViewSource BEFORE unloading,
                // so the old collectible ALC can be GC-collected. The command handler runs ON the
                // Godot main thread (RemoteBridgeNode._Process dequeues + fire-and-forget runs the
                // async handler, so awaits yield back to the frame loop). UnmountNow is a SYNCHRONOUS
                // main-thread call -- the previous UnmountAndWaitAsync used a deferred-TCS unmount with
                // RunContinuationsAsynchronously + ConfigureAwait(false), which hopped the rest of the
                // handler (SceneFlow Exit->Enter -> RemoveChild) onto a threadpool thread and tripped
                // "Removing children only allowed from the main thread." Keeping the unmount synchronous
                // keeps the whole reload on the main thread. Keyed off the ViewHost's own mounted set,
                // so it covers both IService.ShowAsync-mounted views (e.g. "activity") and views mounted
                // directly via IViewHost.Mount (e.g. "timeline", whose TimelinePlugin mounts it without
                // going through IService).
                var viewHost = registry.TryGet<FantaSim.App.Ui.Providers.IViewHost>();
                var wasMounted = viewHost is not null && viewHost.UnmountNow(bundleId);

                var sceneFlow = registry.TryGet<FantaSim.App.SceneFlow.IService>();
                var target = sceneFlow?.ActiveScenes.FirstOrDefault(s => s.SceneId == bundleId);
                if (sceneFlow is not null && target is not null)
                {
                    // Scene-tier reload via Exit->Enter so the old ALC's SceneFlow pin is released.
                    // ExitAsync cascades to descendant scenes, so capture the subtree (root + all
                    // transitive descendants, in ActiveScenes entry order) and re-Enter it after.
                    // No ConfigureAwait(false): the handler is on the Godot main thread and scene-tree
                    // mutation downstream (RemoveChild) must stay there.
                    var subtree = CollectSubtree(sceneFlow.ActiveScenes, bundleId);
                    await sceneFlow.ExitAsync(bundleId, ct);
                    foreach (var node in subtree)
                        await sceneFlow.EnterAsync(new FantaSim.App.SceneFlow.SceneRequest(node.SceneId, node.ParentSceneId), ct);
                }
                else
                {
                    await resource.ReloadAsync(bundleId, ct); // plugin-only bundle, or no SceneFlow

                    // Scene-tier reloads re-mount the view via the re-entered scene's plugin; a
                    // plugin-only reload has nothing to do so. Restore it here only in that case.
                    // Mount is deferred and re-resolves the freshly-registered IViewSource.
                    if (wasMounted)
                        viewHost!.Mount(bundleId);
                }

                foreach (var hook in registry.GetAll<FantaSim.App.Command.IBundleReloadHook>())
                    await hook.AfterReloadAsync(bundleId, ct);

                log.LogInformation("resource.reload_bundle: reloaded '{BundleId}'.", bundleId);
                return JsonSerializer.Serialize(new { ok = true, bundleId });
            });

        // A3 (agent-UI pilot): emission path. An agent publishes an activity entry carrying an
        // A2UI detail document (through the remote ingress = the AG-UI transport); the activity card
        // renders it via A2uiPresentationNormalizer as the entry's expanded detail. Publishing on the
        // bus records it in the ledger (App.Activity subscribes) AND refreshes the view (the source
        // subscribes) in one call.
        commands.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: "activity.emit_detail",
                Title: "Emit activity detail",
                Description: "Publishes an activity entry carrying an agent-authored A2UI detail document. Payload: {\"name\":\"...\",\"category\":\"...\",\"outcome\":\"...\",\"detail\":{A2UI adjacency-list document}}.",
                Category: "activity"),
            (payloadJson, ct) =>
            {
                JsonObject? payload;
                try
                {
                    payload = JsonNode.Parse(payloadJson ?? string.Empty) as JsonObject;
                }
                catch (JsonException)
                {
                    payload = null;
                }

                if (payload?["detail"] is not { } detail)
                    return Task.FromResult(JsonSerializer.Serialize(new { ok = false, error = "missing 'detail' A2UI document" }));

                var bus = registry.TryGet<CrosscutFoundation.Messaging.IMessageBus>();
                if (bus is null)
                    return Task.FromResult(JsonSerializer.Serialize(new { ok = false, error = "message bus not registered" }));

                var name = payload["name"]?.GetValue<string>() ?? "agent.detail";
                bus.Publish(new FantaSim.App.Activity.ActivityEntry(
                    EntryId: Guid.NewGuid().ToString("N"),
                    Kind: FantaSim.App.Activity.ActivityEntryKind.UiOperation,
                    Timestamp: DateTimeOffset.UtcNow,
                    Actor: new FantaSim.App.Activity.ActivityActor("agent", payload["actorId"]?.GetValue<string>() ?? "assistant"),
                    Name: name,
                    Category: payload["category"]?.GetValue<string>() ?? "agent",
                    Outcome: payload["outcome"]?.GetValue<string>(),
                    DetailDocumentJson: detail.ToJsonString()));

                log.LogInformation("activity.emit_detail: published entry '{Name}' with agent detail.", name);
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, name }));
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
