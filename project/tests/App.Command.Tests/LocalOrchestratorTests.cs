using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using FantaSim.App.Activity;
using FantaSim.App.Command.Clients;
using FantaSim.App.Command.Orchestration;
using FantaSim.App.Command.Providers;
using FantaSim.App.Command.Services;
using FantaSim.App.Ecs;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using Xunit;
using EcsService = FantaSim.App.Ecs.IService;
using WorldService = FantaSim.App.World.IService;

namespace FantaSim.App.Command.Tests;

/// <summary>
/// Focused coverage for the App.Command T3 local orchestration slice:
/// <list type="bullet">
/// <item><see cref="LocalOrchestrator"/> drives mock App.World + App.Ecs and returns success.</item>
/// <item><see cref="IiiBridgeOrchestrator"/> throws a documented <see cref="NotImplementedException"/>.</item>
/// <item>Missing App.World registration returns a clear typed error instead of throwing.</item>
/// <item>The trimmed <see cref="Service"/> routes registered commands and the built-in
/// <c>world.orchestrate</c> command through the active orchestrator.</item>
/// </list>
/// </summary>
public class LocalOrchestratorTests
{
    private static IRegistry NewRegistry() => new ServiceRegistry();

    // ---------------------------------------------------------------------
    // Behavior 1: world.generate drives App.World RunGeneration and then
    // fans an ECS UpdateAll(0f). The orchestrator returns Ok=true with the
    // generation result serialized as JSON.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Generate_drives_world_generation_and_ecs_update_then_returns_success()
    {
        // Given a registry with mock App.World + App.Ecs
        var registry = NewRegistry();
        var world = new FakeWorldService();
        var ecs = new FakeEcsService();
        registry.Register<WorldService>(world, new ServiceRegistration { Tags = new[] { "world" } });
        registry.Register<EcsService>(ecs, new ServiceRegistration { Tags = new[] { "ecs" } });
        var orchestrator = new LocalOrchestrator(registry, NullLoggerFactory.Instance);

        // When a world.generate command is triggered
        var result = await orchestrator.TriggerAsync(new CommandRequest(
            Command: LocalOrchestrator.WellKnownCommands.Generate,
            ActorId: "w-test"));

        // Then the result is Ok, the world saw the generation request, and the ECS saw a 0-delta update
        Assert.True(result.Ok);
        Assert.Equal(1, world.GenerateCalls);
        Assert.Contains("\"success\":true", result.ResultJson);
        Assert.Equal(1, ecs.UpdateAllCalls);
        Assert.Equal(0f, ecs.LastDelta);
    }

    // ---------------------------------------------------------------------
    // Behavior 2: world.tick forwards the delta to ECS UpdateAll and reports Ok.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Tick_forwards_delta_to_ecs_update_all()
    {
        // Given a registry with only App.Ecs (App.World not required for tick)
        var registry = NewRegistry();
        var ecs = new FakeEcsService();
        registry.Register<EcsService>(ecs, new ServiceRegistration { Tags = new[] { "ecs" } });
        var orchestrator = new LocalOrchestrator(registry, NullLoggerFactory.Instance);

        // When a world.tick command is triggered with a numeric payload
        var result = await orchestrator.TriggerAsync(new CommandRequest(
            Command: LocalOrchestrator.WellKnownCommands.Tick,
            PayloadJson: "0.25"));

        // Then ECS received UpdateAll(0.25) and the result is Ok
        Assert.True(result.Ok);
        Assert.Equal(1, ecs.UpdateAllCalls);
        Assert.Equal(0.25f, ecs.LastDelta);
        Assert.Contains("\"delta\":0.25", result.ResultJson);
    }

    [Fact]
    public async Task Tick_accepts_canonical_tick_json_payload_without_treating_it_as_delta()
    {
        var registry = NewRegistry();
        var ecs = new FakeEcsService();
        registry.Register<EcsService>(ecs, new ServiceRegistration { Tags = new[] { "ecs" } });
        var orchestrator = new LocalOrchestrator(registry, NullLoggerFactory.Instance);

        var result = await orchestrator.TriggerAsync(new CommandRequest(
            Command: LocalOrchestrator.WellKnownCommands.Tick,
            PayloadJson: "{\"tick\":123456}"));

        Assert.True(result.Ok);
        Assert.Equal(1, ecs.UpdateAllCalls);
        Assert.Equal(0f, ecs.LastDelta);
        Assert.Contains("\"tick\":123456", result.ResultJson);
        Assert.Contains("\"canonicalTick\":123456", result.ResultJson);
    }

    // ---------------------------------------------------------------------
    // Behavior 3: world.refresh returns the overview as JSON without
    // touching ECS.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Refresh_reports_overview_without_ecs_update()
    {
        // Given a registry with only App.World
        var registry = NewRegistry();
        var world = new FakeWorldService();
        registry.Register<WorldService>(world, new ServiceRegistration { Tags = new[] { "world" } });
        var orchestrator = new LocalOrchestrator(registry, NullLoggerFactory.Instance);

        // When a world.refresh command is triggered
        var result = await orchestrator.TriggerAsync(new CommandRequest(
            Command: LocalOrchestrator.WellKnownCommands.Refresh));

        // Then the result is Ok and carries the overview JSON; ECS was never registered
        Assert.True(result.Ok);
        Assert.Contains("\"worldId\":\"fake-world\"", result.ResultJson);
        Assert.Contains("\"fieldCount\":2", result.ResultJson);
    }

    [Fact]
    public async Task Generation_products_reports_product_snapshot_without_ecs_update()
    {
        var registry = NewRegistry();
        var world = new FakeWorldService
        {
            ProductsView = new WorldGenerationProductsView(
                7,
                new[] { "/base/main/formation/body-set@1234" },
                1_234L),
        };
        registry.Register<WorldService>(world, new ServiceRegistration { Tags = new[] { "world" } });
        var orchestrator = new LocalOrchestrator(registry, NullLoggerFactory.Instance);

        var result = await orchestrator.TriggerAsync(new CommandRequest(
            Command: LocalOrchestrator.WellKnownCommands.GenerationProducts));

        Assert.True(result.Ok);
        Assert.Equal(1, world.ProductsCalls);
        Assert.Contains("\"graphRevision\":7", result.ResultJson);
        Assert.Contains("\"products\":[\"/base/main/formation/body-set@1234\"]", result.ResultJson);
        Assert.Contains("\"referenceTick\":1234", result.ResultJson);
    }

    // ---------------------------------------------------------------------
    // Behavior 4: missing App.World registration yields a typed
    // missing-service error rather than a NullReferenceException.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Generate_without_world_registered_returns_missing_service_error()
    {
        // Given a registry with only App.Ecs (no App.World)
        var registry = NewRegistry();
        registry.Register<EcsService>(new FakeEcsService(), new ServiceRegistration { Tags = new[] { "ecs" } });
        var orchestrator = new LocalOrchestrator(registry, NullLoggerFactory.Instance);

        // When a world.generate command is triggered
        var result = await orchestrator.TriggerAsync(new CommandRequest(
            Command: LocalOrchestrator.WellKnownCommands.Generate));

        // Then the result reports a typed missing-service error naming App.World.IService
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Equal("missing-service", result.Error!.Type);
        Assert.Contains("FantaSim.App.World.IService", result.Error.Message);
    }

    // ---------------------------------------------------------------------
    // Behavior 5: unknown command returns a typed unknown-command error.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Unknown_command_returns_typed_error()
    {
        // Given a fully wired orchestrator
        var registry = NewRegistry();
        registry.Register<WorldService>(new FakeWorldService(), new ServiceRegistration { Tags = new[] { "world" } });
        registry.Register<EcsService>(new FakeEcsService(), new ServiceRegistration { Tags = new[] { "ecs" } });
        var orchestrator = new LocalOrchestrator(registry, NullLoggerFactory.Instance);

        // When an unknown command is triggered
        var result = await orchestrator.TriggerAsync(new CommandRequest(Command: "world.bogus"));

        // Then the result reports a typed unknown-command error
        Assert.False(result.Ok);
        Assert.Equal("unknown-command", result.Error!.Type);
    }

    // ---------------------------------------------------------------------
    // Behavior 6: HealthAsync reports Ok=true only when both services
    // are registered.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task Health_reports_ok_only_when_both_services_registered()
    {
        // Given a registry with only App.World
        var registry = NewRegistry();
        registry.Register<WorldService>(new FakeWorldService(), new ServiceRegistration { Tags = new[] { "world" } });
        var orchestrator = new LocalOrchestrator(registry, NullLoggerFactory.Instance);

        // When health is checked
        var partial = await orchestrator.HealthAsync();

        // Then Ok is false because App.Ecs is missing
        Assert.False(partial.Ok);

        // When App.Ecs is also registered
        registry.Register<EcsService>(new FakeEcsService(), new ServiceRegistration { Tags = new[] { "ecs" } });
        var full = await orchestrator.HealthAsync();

        // Then Ok is true and the command count matches the trimmed surface
        Assert.True(full.Ok);
        Assert.Equal(4, full.Commands);
    }
}

public class CommandServiceTests
{
    // ---------------------------------------------------------------------
    // Behavior 8: the trimmed Service routes a registered command to its
    // handler and returns the handler's JSON result.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ExecuteAsync_routes_registered_command_to_handler()
    {
        // Given a Service with one registered command
        var registry = new ServiceRegistry();
        var service = new Service(
            new ImmediateMainThreadDispatcher(),
            registry,
            NullLoggerFactory.Instance);
        service.Register(
            new CommandDescriptor("ping", "Ping", "Pings back", "test"),
            (payload, ct) => Task.FromResult<string?>("{\"pong\":true}"));

        // When the command is executed
        var result = await service.ExecuteAsync(new CommandRequest(Command: "ping"));

        // Then the result is Ok and carries the handler JSON
        Assert.True(result.Ok);
        Assert.Contains("\"pong\":true", result.ResultJson);
    }

    // ---------------------------------------------------------------------
    // Behavior 9: unknown command returns a typed error without throwing.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ExecuteAsync_unknown_command_returns_typed_error()
    {
        var registry = new ServiceRegistry();
        var service = new Service(
            new ImmediateMainThreadDispatcher(),
            registry,
            NullLoggerFactory.Instance);

        var result = await service.ExecuteAsync(new CommandRequest(Command: "nope"));

        Assert.False(result.Ok);
        Assert.Equal("unknown-command", result.Error!.Type);
    }

    // ---------------------------------------------------------------------
    // Behavior 10: the built-in world.orchestrate command delegates to the
    // active IWorldOrchestration (LocalOrchestrator with registered services).
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ExecuteAsync_world_orchestrate_delegates_to_local_orchestrator()
    {
        // Given a registry with App.World + App.Ecs and a Service
        var registry = new ServiceRegistry();
        var world = new FakeWorldService();
        registry.Register<WorldService>(world, new ServiceRegistration { Tags = new[] { "world" } });
        registry.Register<EcsService>(new FakeEcsService(), new ServiceRegistration { Tags = new[] { "ecs" } });
        var service = new Service(
            new ImmediateMainThreadDispatcher(),
            registry,
            NullLoggerFactory.Instance);

        // When the world.orchestrate command is executed with an inner world.refresh request
        var innerJson = System.Text.Json.JsonSerializer.Serialize(new CommandRequest(Command: "world.refresh"));
        var result = await service.ExecuteAsync(new CommandRequest(
            Command: Service.OrchestrateCommandId,
            PayloadJson: innerJson));

        // Then the result is Ok and the inner refresh reached the world service
        Assert.True(result.Ok);
        Assert.Equal(1, world.OverviewCalls);
    }

    // ---------------------------------------------------------------------
    // Behavior 10b: world.orchestrate PROPAGATES the inner result instead of
    // nesting it — an inner failure (unknown command / missing service) must
    // surface as outer Ok=false with the inner error, and an inner success
    // must pass the inner ResultJson through un-nested. Before 2026-07-03 the
    // built-in serialized the whole inner CommandResult as the ResultJson of
    // a SUCCESSFUL outer result, so automation reading the transport-level Ok
    // saw success on inner failure.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ExecuteAsync_world_orchestrate_propagates_inner_failure()
    {
        // Given a registry with NO world service registered
        var registry = new ServiceRegistry();
        var service = new Service(
            new ImmediateMainThreadDispatcher(),
            registry,
            NullLoggerFactory.Instance);

        // When the orchestrate command wraps an inner command that must fail
        var innerJson = System.Text.Json.JsonSerializer.Serialize(new CommandRequest(Command: "world.refresh"));
        var result = await service.ExecuteAsync(new CommandRequest(
            Command: Service.OrchestrateCommandId,
            PayloadJson: innerJson));

        // Then the OUTER result reports the failure with the inner error attached
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_world_orchestrate_passes_inner_result_json_through_unnested()
    {
        var registry = new ServiceRegistry();
        var world = new FakeWorldService();
        registry.Register<WorldService>(world, new ServiceRegistration { Tags = new[] { "world" } });
        registry.Register<EcsService>(new FakeEcsService(), new ServiceRegistration { Tags = new[] { "ecs" } });
        var service = new Service(
            new ImmediateMainThreadDispatcher(),
            registry,
            NullLoggerFactory.Instance);

        var innerJson = System.Text.Json.JsonSerializer.Serialize(new CommandRequest(Command: "world.refresh"));
        var result = await service.ExecuteAsync(new CommandRequest(
            Command: Service.OrchestrateCommandId,
            PayloadJson: innerJson));

        Assert.True(result.Ok);
        // No double-nesting: the ResultJson must NOT itself be a serialized CommandResult
        // (the pre-fix shape had Ok/CommandId fields at the top level of ResultJson).
        if (!string.IsNullOrEmpty(result.ResultJson))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(result.ResultJson);
            Assert.False(
                doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("Ok", out _)
                && doc.RootElement.TryGetProperty("CommandId", out _),
                "world.orchestrate ResultJson is still a nested CommandResult");
        }
    }

    // ---------------------------------------------------------------------
    // Behavior 11: ExecuteAsync records structured command-card payloads for
    // both the request and the result entry, including descriptor, actor,
    // correlation/causation, and payload/result JSON.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ExecuteAsync_records_enriched_command_activity_payloads()
    {
        var registry = new ServiceRegistry();
        var ledger = new RecordingLedger();
        registry.Register<FantaSim.App.Activity.IService>(ledger, new ServiceRegistration { Tags = new[] { "activity" } });
        var service = new Service(
            new ImmediateMainThreadDispatcher(),
            registry,
            NullLoggerFactory.Instance);
        service.Register(
            new CommandDescriptor("ping", "Ping", "Pings back", "test"),
            (payload, ct) => Task.FromResult<string?>("{\"pong\":true}"));

        await service.ExecuteAsync(new CommandRequest(
            Command: "ping",
            PayloadJson: "{\"hello\":1}",
            ActorKind: "user",
            ActorId: "godot"));

        Assert.Equal(2, ledger.Entries.Count);
        var requestEntry = ledger.Entries[0];
        var resultEntry = ledger.Entries[1];
        Assert.Equal(ActivityEntryKind.DomainCommand, requestEntry.Kind);
        Assert.Equal(ActivityEntryKind.CommandResult, resultEntry.Kind);

        var requestPayload = JsonNode.Parse(requestEntry.PayloadJson!)!.AsObject();
        Assert.Equal("ping", requestPayload["command"]!.GetValue<string>());
        Assert.Equal("Ping", requestPayload["descriptorTitle"]!.GetValue<string>());
        Assert.Equal("Pings back", requestPayload["descriptorDescription"]!.GetValue<string>());
        Assert.Equal("test", requestPayload["category"]!.GetValue<string>());
        Assert.Equal("user", requestPayload["actorKind"]!.GetValue<string>());
        Assert.Equal("godot", requestPayload["actorId"]!.GetValue<string>());
        Assert.Equal("{\"hello\":1}", requestPayload["payloadJson"]!.GetValue<string>());

        var resultPayload = JsonNode.Parse(resultEntry.PayloadJson!)!.AsObject();
        Assert.Equal("ping", resultPayload["command"]!.GetValue<string>());
        Assert.True(resultPayload["ok"]!.GetValue<bool>());
        Assert.Equal("{\"pong\":true}", resultPayload["resultJson"]!.GetValue<string>());
        Assert.Equal(requestEntry.EntryId, resultEntry.CausationId);
        Assert.Equal(requestEntry.CorrelationId, resultEntry.CorrelationId);
    }

    // ---------------------------------------------------------------------
    // Behavior 12: an unknown command records a structured failure payload
    // with ok=false and the typed errorType/errorMessage.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ExecuteAsync_unknown_command_records_enriched_failure_payload()
    {
        var registry = new ServiceRegistry();
        var ledger = new RecordingLedger();
        registry.Register<FantaSim.App.Activity.IService>(ledger, new ServiceRegistration { Tags = new[] { "activity" } });
        var service = new Service(
            new ImmediateMainThreadDispatcher(),
            registry,
            NullLoggerFactory.Instance);

        await service.ExecuteAsync(new CommandRequest(Command: "nope", ActorKind: "cli", ActorId: "tester"));

        var single = Assert.Single(ledger.Entries);
        Assert.Equal(ActivityEntryKind.CommandResult, single.Kind);
        var payload = JsonNode.Parse(single.PayloadJson!)!.AsObject();
        Assert.Equal("nope", payload["command"]!.GetValue<string>());
        Assert.False(payload["ok"]!.GetValue<bool>());
        Assert.Equal("unknown-command", payload["errorType"]!.GetValue<string>());
        Assert.Contains("Unknown command", payload["errorMessage"]!.GetValue<string>());
        Assert.Equal("cli", payload["actorKind"]!.GetValue<string>());
        Assert.Equal("tester", payload["actorId"]!.GetValue<string>());
    }

    private sealed class RecordingLedger : FantaSim.App.Activity.IService
    {
        public List<ActivityEntry> Entries { get; } = new();

        public void Append(ActivityEntry entry) => Entries.Add(entry);

        public IReadOnlyList<ActivityEntry> QueryLatest(int count)
            => Entries.TakeLast(count).Reverse().ToArray();

        public IReadOnlyList<ActivityEntry> QueryByCorrelationId(string correlationId)
            => Entries.Where(e => e.CorrelationId == correlationId).ToArray();
    }
}

/// <summary>Minimal fake App.World.IService for orchestration tests.</summary>
internal sealed class FakeWorldService : WorldService
{
    public FantaSim.App.World.LayerFilmstripPreviewMap? GetLayerFilmstripPreview(FantaSim.App.World.LayerFilmstripPreviewRequest request, System.Threading.CancellationToken cancellationToken = default) => null;

    public int GenerateCalls;
    public int OverviewCalls;
    public int ProductsCalls;
    public WorldGenerationProductsView ProductsView = new(0, Array.Empty<string>(), 0L);

    public WorldOverview GetOverviewAsync()
    {
        OverviewCalls++;
        return new WorldOverview("fake-world", "Fake", EntityCount: 0, FieldCount: 2, IsDirty: false);
    }

    public WorldFieldValues GetFieldValuesAsync(WorldFieldValuesRequest request)
        => new(new Dictionary<string, object>(0));

    public WorldScalarFieldValues GetScalarFieldValuesAsync(WorldScalarFieldValuesRequest request)
        => new(new Dictionary<string, float>(0));

    public WorldRenderSnapshot GetRenderSnapshotAsync()
        => new(0, Array.Empty<RenderEntityDto>());

    public WorldGenerationProductsView GetGenerationProductsAsync()
    {
        ProductsCalls++;
        return ProductsView;
    }

    public PlanetPresentationDocument GetPlanetPresentationAsync()
        => new(
            PlanetId: "fake-world",
            SourceWorldId: "fake-world",
            ReferenceTick: ProductsView.ReferenceTick,
            Revision: ProductsView.GraphRevision,
            Layers: Array.Empty<PlanetPresentationLayer>(),
            RenderEntities: Array.Empty<RenderEntityDto>());

    public PlanetPresentationDocument GetPlanetPresentationAsync(long referenceTick)
        => GetPlanetPresentationAsync();

    public WorldGlobeSnapshot GetGlobeSnapshotAt(long tick)
        => throw new NotSupportedException();

    public byte[] GetGlobeBoundaryCellsAt(long tick)
        => throw new NotSupportedException();

    public IReadOnlyDictionary<int, double> GetContinentalFractionByCellAt(long tick)
        => throw new NotSupportedException();

    public MantleIsosurfaceSet GetMantleIsosurfacesAsync(long tick)
        => MantleIsosurfaceSet.Empty;

    public WorldGenerationResult RunGenerationAsync(WorldGenerationRequest request)
    {
        GenerateCalls++;
        return new WorldGenerationResult(true, "", "fake-world");
    }

    public IDisposable SubscribeGenerationChanged(Action<WorldGenerationChangedEvent> callback)
        => NoopDisposable.Instance;

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly IDisposable Instance = new NoopDisposable();

        public void Dispose()
        {
        }
    }
}

/// <summary>Minimal fake App.Ecs.IService for orchestration tests.</summary>
internal sealed class FakeEcsService : EcsService
{
    public int UpdateAllCalls;
    public float LastDelta;

    public EcsWorldInfo CreateWorld(EcsWorldSpec spec)
        => new(spec.WorldId, spec.Backend, spec.DisplayName ?? "fake", 0, 0, false);

    public bool DestroyWorld(string worldId) => true;
    public EcsWorldInfo GetWorld(string worldId)
        => new(worldId, EcsBackendKind.Arch, "fake", 0, 0, false);
    public IReadOnlyList<EcsWorldInfo> ListWorlds() => Array.Empty<EcsWorldInfo>();
    public EcsWorldInfo InitializeWorld(string worldId)
        => new(worldId, EcsBackendKind.Arch, "fake", 0, 0, true);
    public void UpdateWorld(string worldId, float deltaTime) { }
    public void UpdateAll(float deltaTime)
    {
        UpdateAllCalls++;
        LastDelta = deltaTime;
    }
}
