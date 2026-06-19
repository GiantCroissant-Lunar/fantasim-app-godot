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
        Assert.Equal(3, full.Commands);
    }
}

public class IiiBridgeOrchestratorTests
{
    // ---------------------------------------------------------------------
    // Behavior 7: the deferred iii bridge stub throws a documented
    // NotImplementedException naming the native bridge as not-built.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task TriggerAsync_throws_documented_NotImplementedException()
    {
        var orchestrator = new IiiBridgeOrchestrator(NullLoggerFactory.Instance);

        var ex = await Assert.ThrowsAsync<NotImplementedException>(
            () => orchestrator.TriggerAsync(new CommandRequest(Command: "world.generate")));

        Assert.Contains("native iii bridge", ex.Message);
        Assert.Contains("not built", ex.Message);
    }

    [Fact]
    public async Task HealthAsync_throws_documented_NotImplementedException()
    {
        var orchestrator = new IiiBridgeOrchestrator(NullLoggerFactory.Instance);

        var ex = await Assert.ThrowsAsync<NotImplementedException>(
            () => orchestrator.HealthAsync());

        Assert.Contains("native iii bridge", ex.Message);
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
}

/// <summary>Minimal fake App.World.IService for orchestration tests.</summary>
internal sealed class FakeWorldService : WorldService
{
    public int GenerateCalls;
    public int OverviewCalls;

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

    public WorldGenerationResult RunGenerationAsync(WorldGenerationRequest request)
    {
        GenerateCalls++;
        return new WorldGenerationResult(true, "fake-generation", request.WorldId);
    }

    public void SubscribeGenerationChanged(Action<WorldGenerationChangedEvent> callback) { }
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
    public void UpdateWorld(string worldId, float deltaTime) { }
    public void UpdateAll(float deltaTime)
    {
        UpdateAllCalls++;
        LastDelta = deltaTime;
    }
}