using Akka.Actor;
using FantaSim.App.Ecs.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FantaSim.App.Ecs.Tests;

/// <summary>
/// Behavioral coverage through the public <see cref="IService"/> surface.
/// Each test spins a private <see cref="ActorSystem"/> + <see cref="Service"/>
/// so the supervisor and world actors are exercised end-to-end with no shared
/// state between tests. No sleeps: all assertions use synchronous Service calls
/// or bounded Ask.
/// </summary>
public class EcsServiceTests : IDisposable
{
    private readonly ActorSystem _system;
    private readonly Service _service;

    public EcsServiceTests()
    {
        _system = ActorSystem.Create(
            $"ecs-svc-tests-{Guid.NewGuid():N}");
        _service = new Service(
            _system, NullLoggerFactory.Instance, bus: null);
    }

    public void Dispose()
    {
        _service.Dispose();
        _system.Terminate().Wait(TimeSpan.FromSeconds(5));
        _system.Dispose();
    }

    private static EcsWorldSpec Spec(string id) =>
        new(id, EcsBackendKind.Arch, id, 16, false);

    // ---------------------------------------------------------------------
    // Behavior 1: CreateWorld returns a snapshot describing the new world
    // and the world is then listable via ListWorlds.
    // ---------------------------------------------------------------------
    [Fact]
    public void CreateWorld_returns_snapshot_and_world_appears_in_list()
    {
        // Given a fresh service
        // When a world is created
        var info = _service.CreateWorld(Spec("svc-a"));

        // Then the snapshot reports the spec's identity and pre-init state
        Assert.Equal("svc-a", info.WorldId);
        Assert.Equal(EcsBackendKind.Arch, info.Backend);
        Assert.Equal("svc-a", info.DisplayName);
        Assert.False(info.Initialized);

        // And ListWorlds contains exactly that world
        var list = _service.ListWorlds();
        Assert.Single(list);
        Assert.Equal("svc-a", list[0].WorldId);
    }

    // ---------------------------------------------------------------------
    // Behavior 2: CreateWorld is idempotent for an existing id and returns
    // the same world's snapshot (no duplicate entry in ListWorlds).
    // ---------------------------------------------------------------------
    [Fact]
    public void CreateWorld_is_idempotent_for_existing_id()
    {
        // Given a service with one world
        _service.CreateWorld(Spec("svc-dup"));

        // When the same id is created again
        var second = _service.CreateWorld(Spec("svc-dup"));

        // Then the snapshot is for the same world and ListWorlds has one entry
        Assert.Equal("svc-dup", second.WorldId);
        Assert.Single(_service.ListWorlds());
    }

    // ---------------------------------------------------------------------
    // Behavior 3: GetWorld returns the snapshot for an existing id; the
    // contract is that GetWorld after CreateWorld round-trips identity.
    // ---------------------------------------------------------------------
    [Fact]
    public void GetWorld_returns_snapshot_for_existing_world()
    {
        // Given a service with one world
        _service.CreateWorld(Spec("svc-get"));

        // When GetWorld is called
        var info = _service.GetWorld("svc-get");

        // Then the snapshot reports the same id and backend
        Assert.Equal("svc-get", info.WorldId);
        Assert.Equal(EcsBackendKind.Arch, info.Backend);
    }

    // ---------------------------------------------------------------------
    // Behavior 4: DestroyWorld returns true for an existing world and the
    // world is no longer present in ListWorlds.
    // ---------------------------------------------------------------------
    [Fact]
    public void DestroyWorld_removes_world_from_list()
    {
        // Given a service with one world
        _service.CreateWorld(Spec("svc-destroy"));
        Assert.Single(_service.ListWorlds());

        // When DestroyWorld is called
        var result = _service.DestroyWorld("svc-destroy");

        // Then the service reports true and the world is gone from the list
        Assert.True(result);
        Assert.Empty(_service.ListWorlds());
    }

    // ---------------------------------------------------------------------
    // Behavior 5: DestroyWorld returns false for an unknown id without
    // throwing or altering the existing worlds.
    // ---------------------------------------------------------------------
    [Fact]
    public void DestroyWorld_returns_false_for_unknown_id()
    {
        // Given a service with one world
        _service.CreateWorld(Spec("svc-keep"));
        var before = _service.ListWorlds().Count;

        // When DestroyWorld is called for an unknown id
        var result = _service.DestroyWorld("svc-ghost");

        // Then the service reports false and the existing world is untouched
        Assert.False(result);
        Assert.Equal(before, _service.ListWorlds().Count);
    }

    // ---------------------------------------------------------------------
    // Behavior 6: ListWorlds returns every created world in creation order
    // and reflects multiple worlds.
    // ---------------------------------------------------------------------
    [Fact]
    public void ListWorlds_returns_all_created_worlds()
    {
        // Given a service with three worlds
        _service.CreateWorld(Spec("svc-list-1"));
        _service.CreateWorld(Spec("svc-list-2"));
        _service.CreateWorld(Spec("svc-list-3"));

        // When ListWorlds is called
        var list = _service.ListWorlds();

        // Then all three worlds are present with their ids
        Assert.Equal(3, list.Count);
        Assert.Contains(list, w => w.WorldId == "svc-list-1");
        Assert.Contains(list, w => w.WorldId == "svc-list-2");
        Assert.Contains(list, w => w.WorldId == "svc-list-3");
    }

    // ---------------------------------------------------------------------
    // Behavior 7: InitializeWorld flips the Initialized flag on an existing
    // world and returns a snapshot reflecting the initialized state.
    // ---------------------------------------------------------------------
    [Fact]
    public void InitializeWorld_flips_initialized_flag_for_existing_world()
    {
        // Given a service with one created (uninitialized) world
        _service.CreateWorld(Spec("svc-init"));
        Assert.False(_service.GetWorld("svc-init").Initialized);

        // When InitializeWorld is called
        var info = _service.InitializeWorld("svc-init");

        // Then the snapshot reports initialized
        Assert.Equal("svc-init", info.WorldId);
        Assert.True(info.Initialized);
    }

    // ---------------------------------------------------------------------
    // Behavior 8: InitializeWorld is idempotent: calling it again on an
    // already-initialized world returns a snapshot that still reports
    // Initialized=true without throwing.
    // ---------------------------------------------------------------------
    [Fact]
    public void InitializeWorld_is_idempotent_for_already_initialized_world()
    {
        // Given a service with one initialized world
        _service.CreateWorld(Spec("svc-init-idem"));
        _service.InitializeWorld("svc-init-idem");

        // When InitializeWorld is called again
        var info = _service.InitializeWorld("svc-init-idem");

        // Then the snapshot still reports initialized (no reset, no throw)
        Assert.True(info.Initialized);
        Assert.Equal("svc-init-idem", info.WorldId);
    }

    // ---------------------------------------------------------------------
    // Behavior 9: InitializeWorld for an unknown id throws
    // InvalidOperationException rather than hanging or silently succeeding.
    // ---------------------------------------------------------------------
    [Fact]
    public void InitializeWorld_throws_for_unknown_world_id()
    {
        // Given a service with no worlds
        // When InitializeWorld is called for an unknown id
        // Then it throws InvalidOperationException
        Assert.Throws<InvalidOperationException>(
            () => _service.InitializeWorld("svc-ghost-init"));
    }

    // ---------------------------------------------------------------------
    // Behavior 10: RegisterSystem followed by InitializeWorld and UpdateAll
    // ticks the registered CountingEntitySystem, observable through
    // EntityCount growth and the system's tick counter.
    // ---------------------------------------------------------------------
    [Fact]
    public void RegisterSystem_then_InitializeWorld_then_UpdateAll_ticks_system()
    {
        // Given a service with a world, a registered counting system, then initialized
        _service.CreateWorld(Spec("svc-reg"));
        var sys = new CountingEntitySystem();
        _service.RegisterSystem("svc-reg", sys);
        _service.InitializeWorld("svc-reg");
        Assert.Equal(0, sys.Ticks);

        // When UpdateAll is sent with two ticks
        _service.UpdateAll(0.016f);
        _service.UpdateAll(0.016f);

        // Then the system is ticked twice and EntityCount reaches 2
        EcsWorldInfo WaitEntityCount(int expected)
        {
            for (var i = 0; i < 40; i++)
            {
                var s = _service.GetWorld("svc-reg");
                if (s.EntityCount == expected) return s;
            }
            throw new Xunit.Sdk.XunitException(
                $"entity count never reached {expected}");
        }

        var snap = WaitEntityCount(2);
        Assert.Equal(2, snap.EntityCount);
        Assert.Equal(2, sys.Ticks);
    }

    // ---------------------------------------------------------------------
    // Behavior 11: UpdateAll on an initialized but empty world (no systems
    // registered) is safe: EntityCount stays at zero and no exception is
    // thrown.
    // ---------------------------------------------------------------------
    [Fact]
    public void UpdateAll_on_initialized_empty_world_is_safe()
    {
        // Given a service with an initialized world and no registered systems
        _service.CreateWorld(Spec("svc-empty"));
        _service.InitializeWorld("svc-empty");

        // When UpdateAll is sent
        _service.UpdateAll(0.016f);
        _service.UpdateAll(0.016f);

        // Then EntityCount stays zero and no exception propagated
        var info = _service.GetWorld("svc-empty");
        Assert.True(info.Initialized);
        Assert.Equal(0, info.EntityCount);
    }

    // ---------------------------------------------------------------------
    // Behavior 12: the host bootstrap sequence at the service boundary is
    // safe for the default "main" world: create, initialize, then tick.
    // ---------------------------------------------------------------------
    [Fact]
    public void Create_main_initialize_main_update_all_is_safe()
    {
        _service.CreateWorld(Spec("main"));
        var initialized = _service.InitializeWorld("main");

        _service.UpdateAll(0.016f);
        _service.UpdateAll(0.016f);

        var info = _service.GetWorld("main");
        Assert.True(initialized.Initialized);
        Assert.True(info.Initialized);
        Assert.Equal(0, info.EntityCount);
    }
}
