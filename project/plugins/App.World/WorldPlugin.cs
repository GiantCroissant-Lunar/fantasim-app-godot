using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Common;
using FantaSim.App.World.Composition;
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
    private const long CrustGenerationWindowTicks = 5_000_000L;

    private IDisposable? _worldCompositionHandle;
    private CrustGenerationTrigger? _crustTrigger;
    private IRegistry? _registry;
    private ILoggerFactory? _loggerFactory;
    private ILogger? _log;
    private IDisposable? _lateArmSubscription;
    private bool _crustArmed;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        var registry = context.Services.GetRequiredService<IRegistry>();
        var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
        _registry = registry;
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger("WorldPlugin");

        var ctx = new HostCompositionContext(registry, loggerFactory);
        _worldCompositionHandle = WorldComposition.ComposeWorld(ctx);

        RegisterWorldCommand(registry);
        InstallCrustTrigger();

        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _log?.LogInformation("WorldPlugin: shutdown started.");

        // Unregister the world command so the resident command service drops the handler
        // delegate. The handler closes over the registry and resolves bundle-typed
        // INodeFunctionProvider instances; keeping it registered pins the old bundle's ALC.
        if (_registry is not null)
        {
            _registry.TryGet<FantaSim.App.Command.IService>()?.Unregister(RunWorldGenerationGraphCommand);
            _log?.LogInformation("WorldPlugin: unregistered {Command}", RunWorldGenerationGraphCommand);
        }

        _lateArmSubscription?.Dispose();
        _lateArmSubscription = null;

        _crustTrigger?.Dispose();
        _crustTrigger = null;
        _crustArmed = false;

        _worldCompositionHandle?.Dispose();
        _worldCompositionHandle = null;
        _registry = null;
        _loggerFactory = null;
        _log?.LogInformation("WorldPlugin: shutdown completed.");
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
                // The host binder may register ITimelineController after this plugin initialized;
                // re-try arming on each world command invocation (one-shot once armed).
                TryArmCrustTrigger();

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

    private void InstallCrustTrigger()
    {
        if (TryArmCrustTrigger())
            return;

        // The timeline controller was not registered at init time. The resident host binder
        // registers it later in the startup sequence. IRegistry exposes no change notification,
        // so attach a one-shot late-arm hook to the world service's generation-changed event --
        // the first generation run can only happen after the host has wired the controller.
        var world = _registry?.TryGet<FantaSim.App.World.IService>();
        if (world is null)
        {
            _log?.LogWarning("WorldPlugin: no ITimelineController registered and no world service to arm via; automatic crust generation disabled.");
            return;
        }

        _lateArmSubscription = world.SubscribeGenerationChanged(_ => TryArmCrustTriggerLate());
        _log?.LogInformation("WorldPlugin: no ITimelineController registered at init; deferred crust-trigger arming to first world generation event.");
    }

    private bool TryArmCrustTrigger()
    {
        if (_crustArmed || _registry is null)
            return _crustArmed;

        var controller = _registry.TryGet<ITimelineController>();
        if (controller is null)
            return false;

        var family = WorldGenerationGraphDefaults.BuildFamily();
        var policy = new CrustGenerationTriggerPolicy(CrustGenerationWindowTicks);
        _crustTrigger = new CrustGenerationTrigger(
            controller,
            policy,
            family.Revision,
            (canonicalTick, graphRevision, ct) => ExecuteCrustGenerationAsync(_registry, family, canonicalTick, graphRevision, ct),
            _loggerFactory);
        _crustTrigger.Start();
        _crustArmed = true;

        _log?.LogInformation(
            "WorldPlugin: automatic crust generation armed (window={WindowTicks:N0}, graphRevision={Revision})",
            CrustGenerationWindowTicks,
            family.Revision);
        return true;
    }

    private bool TryArmCrustTriggerLate()
    {
        if (TryArmCrustTrigger())
        {
            _lateArmSubscription?.Dispose();
            _lateArmSubscription = null;
            return true;
        }

        return false;
    }

    private static async Task ExecuteCrustGenerationAsync(
        IRegistry registry,
        WorldGenerationGraphFamilyDocument family,
        long canonicalTick,
        int graphRevision,
        CancellationToken cancellationToken)
    {
        var world = registry.TryGet<FantaSim.App.World.IService>();
        if (world is null)
            return;

        var providers = registry.GetAll<FantaSim.App.NodeGraph.INodeFunctionProvider>().ToArray();
        if (providers.Length == 0)
            return;

        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            canonicalTick,
            WorldGenerationGraphDefaults.GeosphereSphereId);

        var scopeKey = source.TryBuildExecutionScopeKey("base", "main", scheduleRevision: 1);
        var sharedParams = new JsonObject
        {
            ["canonicalTick"] = canonicalTick,
            ["graphRevision"] = graphRevision,
        };

        var runner = new WorldGenerationGraphRunner(providers);
        var run = await runner.RunAsync(
            source.CompileForExecution().Document,
            sharedParams,
            scopeKey,
            cancellationToken).ConfigureAwait(false);

        var generation = world.RunGenerationAsync(WorldGenerationGraphRunner.ToGenerationRequest(run, worldId: "default"));
        if (generation.Success)
            registry.TryGet<FantaSim.App.Ecs.IService>()?.UpdateAll(0f);
    }
}
