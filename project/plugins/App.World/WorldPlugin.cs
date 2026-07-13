using System;
using System.Collections.Generic;
using System.IO;
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
    internal const string ReloadRegistryCommandId = "registry.reload";
    // Single source of truth for crust-snapshot spacing: the trigger window, the service's snapshot
    // tick states, and product selection must all agree or playheads select never-generated ticks.
    private const long CrustGenerationWindowTicks = CrustSnapshotTickSeries.DefaultSpacingTicks;

    private IDisposable? _worldCompositionHandle;
    private CrustGenerationTrigger? _crustTrigger;
    private IRegistry? _registry;
    private ILoggerFactory? _loggerFactory;
    private ILogger? _log;
    private IDisposable? _lateArmSubscription;
    private Services.Service? _presentationArmSource;
    private LayerTrackRegistryService? _layerTrackRegistry;
    private IDisposable? _layerTrackRegistryRegistration;
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

        // Layer-track registry ownership lives HERE, not in the timeline bundle: the concrete
        // LayerTrackRegistryService is an App.World.Composition type, and that assembly already
        // ships in the world bundle's collectible ALC (collectible-bundles.json assemblyNames).
        // Referencing it from the timeline bundle dual-copied 8 Unify assemblies across the two
        // ALCs (stage_bundle --check-dual, 2026-07-10) -- the type-identity-split incident class.
        // Timeline consumes the shared ILayerTrackRegistry contract via registry lookup only.
        ComposeLayerTrackRegistry(registry);

        RegisterWorldCommand(registry);
        InstallCrustTrigger();

        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _log?.LogInformation("WorldPlugin: shutdown started.");

        // Unregister the world commands so the resident command service drops the handler
        // delegates. The handlers close over the registry and resolve bundle-typed instances
        // (INodeFunctionProvider, LayerTrackRegistryService); keeping either registered pins the
        // old bundle's ALC.
        if (_registry is not null)
        {
            var commandService = _registry.TryGet<FantaSim.App.Command.IService>();
            commandService?.Unregister(RunWorldGenerationGraphCommand);
            commandService?.Unregister(ReloadRegistryCommandId);
            _log?.LogInformation("WorldPlugin: unregistered {Command} and {ReloadCommand}", RunWorldGenerationGraphCommand, ReloadRegistryCommandId);
        }

        _lateArmSubscription?.Dispose();
        _lateArmSubscription = null;
        DetachPresentationArmHook();

        _crustTrigger?.Dispose();
        _crustTrigger = null;
        _crustArmed = false;

        _layerTrackRegistryRegistration?.Dispose();
        _layerTrackRegistryRegistration = null;

        // The registration dispose above only removes it from the registry; disposing the
        // instance clears its Changed delegate, dropping any subscriber (the resident timeline
        // face) that would otherwise pin this bundle's ALC across a hot-reload.
        _layerTrackRegistry?.Dispose();
        _layerTrackRegistry = null;

        _worldCompositionHandle?.Dispose();
        _worldCompositionHandle = null;
        _registry = null;
        _loggerFactory = null;
        _log?.LogInformation("WorldPlugin: shutdown completed.");
        _log = null;
        return ValueTask.CompletedTask;
    }

    private void ComposeLayerTrackRegistry(IRegistry registry)
    {
        var layerTrackRegistry = CreateLayerTrackRegistry();
        _layerTrackRegistry = layerTrackRegistry;
        _layerTrackRegistryRegistration = registry.RegisterOwned<ILayerTrackRegistry>(
            layerTrackRegistry,
            new ServiceRegistration
            {
                OwnerId = "app.world",
                Tags = new[] { "world", "layer-track-registry" },
                Description = "Layer-track registry (world bundle, slice 1)"
            });
        _log?.LogInformation("WorldPlugin: layer-track registry registered.");
    }

    private LayerTrackRegistryService CreateLayerTrackRegistry()
    {
        var assetRoot = ResolveTrackAssetRoot();
        var configDir = Path.Combine(assetRoot, "config");
        var dataDir = Path.Combine(assetRoot, "data");
        return new LayerTrackRegistryService(
            // LayerGraphBindings are tick-invariant declared structure (which layers exist), not
            // per-tick generation state, so a fixed reference tick is correct here -- the compose-
            // json arc (deferred past slice 1) is what will vary per-tick presentation, not this.
            () => _registry?.TryGet<FantaSim.App.World.IService>()?.GetPlanetPresentationAsync(0L).GenerationGraphFamily,
            Path.Combine(configDir, "track-pipeline.json"),
            Path.Combine(configDir, "declared-layers.json"),
            Path.Combine(dataDir, "layer-track-archive.json"),
            _loggerFactory,
            DiscoverTruthStreamTracks);
    }

    /// <summary>
    /// The stream-discovery provider seam (vault/plans/2026-07-10-layer-track-registry-slice2-
    /// plan.md Task 2): the engine's <c>ITruthEventStore</c> has no stream-enumeration API, so
    /// discovery is app code that already knows which streams exist, not an engine query. The one
    /// real discoverable today is the world truth stream <c>WorldHistoryCoordinator</c> writes, surfaced via
    /// <see cref="FantaSim.App.World.IService.GetOverviewAsync"/>: <see cref="WorldOverview.IsDirty"/>
    /// is true once the stream has a head (i.e. at least one event has been appended), and
    /// <see cref="WorldOverview.WorldId"/> is that stream's key. This is real data (the append-only
    /// truth stream itself), not a demo asset -- no other record is ever invented here.
    /// </summary>
    private IReadOnlyList<DiscoveredTrackRecord> DiscoverTruthStreamTracks()
    {
        var world = _registry?.TryGet<FantaSim.App.World.IService>();
        if (world is null)
            return Array.Empty<DiscoveredTrackRecord>();

        var overview = world.GetOverviewAsync();
        if (!overview.IsDirty)
            return Array.Empty<DiscoveredTrackRecord>();

        return new[]
        {
            new DiscoveredTrackRecord(
                SphereId: "world",
                LayerId: "world.truth-events",
                StreamId: new LayerTrackStreamId("app", "main", "L0", "world", "default"),
                DisplayName: "Truth Events",
                ContentType: LayerTrackContentTypes.Events,
                ContentSource: overview.WorldId,
                CadenceTicks: null),
        };
    }

    private static string ResolveTrackAssetRoot()
    {
        // In a Godot macOS export AppContext.BaseDirectory is the per-arch data dir
        // (Contents/Resources/data_*), which carries no config/; the provisioned config/
        // lives next to the executable (Contents/MacOS, where the export also installs
        // the common-resident catalog). Probe both so tests/dev and exports resolve the
        // same shipped assets.
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        foreach (var root in new[] { AppContext.BaseDirectory, exeDir })
        {
            if (!string.IsNullOrEmpty(root)
                && File.Exists(Path.Combine(root, "config", "track-pipeline.json")))
                return root;
        }
        return string.IsNullOrEmpty(exeDir) ? AppContext.BaseDirectory : exeDir;
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

        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: ReloadRegistryCommandId,
                Title: "Reload layer-track registry",
                Description: "Re-reads the track-pipeline and declared-layers json assets and rebuilds the layer-track registry, preserving the archive overlay.",
                Category: "world"),
            (_, _) =>
            {
                var layerTrackRegistry = _layerTrackRegistry
                    ?? throw new InvalidOperationException("Layer-track registry not composed.");
                layerTrackRegistry.Reload();
                var snapshot = layerTrackRegistry.Current;
                // JsonObject, NOT JsonSerializer.Serialize(anonymous): anonymous types compile
                // into this collectible assembly, and the resident serializer's per-Type cache
                // roots their LoaderAllocator -- the ALC never collects (gcroot-proven 2026-07-08).
                return Task.FromResult<string?>(new JsonObject
                {
                    ["ok"] = true,
                    ["revision"] = snapshot.Revision,
                    ["trackCount"] = snapshot.Tracks.Count,
                }.ToJsonString());
            });

        _log?.LogInformation("WorldPlugin: registered {Command} and {ReloadCommand}", RunWorldGenerationGraphCommand, ReloadRegistryCommandId);
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

        // Generation-changed alone is a chicken-and-egg: in the exported app no generation runs
        // before the trigger itself would start one. The host binder fetches the presentation
        // right after registering ITimelineController, so a presentation fetch is the earliest
        // organic arm signal.
        if (world is Services.Service concrete)
        {
            _presentationArmSource = concrete;
            concrete.PresentationRequested += OnPresentationRequestedArm;
        }

        _log?.LogInformation("WorldPlugin: no ITimelineController registered at init; deferred crust-trigger arming to first presentation fetch or world generation event.");
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
            (decision, ct) => ExecuteCrustGenerationAsync(_registry, family, decision, ct),
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
            DetachPresentationArmHook();
            return true;
        }

        return false;
    }

    private void OnPresentationRequestedArm() => TryArmCrustTriggerLate();

    private void DetachPresentationArmHook()
    {
        if (_presentationArmSource is not null)
        {
            _presentationArmSource.PresentationRequested -= OnPresentationRequestedArm;
            _presentationArmSource = null;
        }
    }

    private static async Task ExecuteCrustGenerationAsync(
        IRegistry registry,
        WorldGenerationGraphFamilyDocument family,
        CrustGenerationTriggerDecision decision,
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
            decision.CanonicalTick,
            WorldGenerationGraphDefaults.GeosphereSphereId);

        var scopeKey = source.TryBuildExecutionScopeKey("base", "main", scheduleRevision: 1);
        var snapshotTicks = decision.SnapshotTicks?.SnapshotTicks ?? new[] { decision.CanonicalTick };
        var snapshotTickArray = new JsonArray();
        foreach (var t in snapshotTicks)
            snapshotTickArray.Add(t);

        var sharedParams = new JsonObject
        {
            ["canonicalTick"] = decision.CanonicalTick,
            ["graphRevision"] = decision.Key?.GraphRevision ?? family.Revision,
            ["snapshotTicks"] = snapshotTickArray,
            // Snapshot ticks are absolute world-timeline ticks; plates sit at their seed orientation
            // at plate onset, so boundary classification must rotate by (tick - onset) — the same
            // convention GlobeReconstructor renders with.
            ["rotationReferenceTick"] = SphereRegimeScheduleDefaults.PlateOnsetTick,
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
