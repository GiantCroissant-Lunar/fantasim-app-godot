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
    // and reflects multiple worlds. (UpdateAll stability is covered at the
    // actor level in EcsSupervisorActorTests because the public Service has
    // no Initialize path; exercising UpdateAll through Service on
    // uninitialized worlds would crash every child, which is not a
    // supported public behavior today.)
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
}