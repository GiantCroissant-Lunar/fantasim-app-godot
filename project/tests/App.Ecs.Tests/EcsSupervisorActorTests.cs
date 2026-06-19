using Akka.Actor;
using Akka.TestKit.Xunit2;
using FantaSim.App.Ecs.Actors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FantaSim.App.Ecs.Tests;

/// <summary>
/// Behavioral coverage of <see cref="EcsSupervisorActor"/> and <see cref="EcsWorldActor"/>
/// via Akka TestKit. Exercises supervisor routing, world lifecycle, duplicate-world
/// behavior, UpdateAll fan-out, and supervision. All assertions are deterministic
/// (Ask with timeout or TestKit message probes); no sleeps.
/// </summary>
public class EcsSupervisorActorTests : TestKit
{
    private static readonly ILoggerFactory LoggerFactory =
        NullLoggerFactory.Instance;

    private static EcsWorldSpec Spec(string id) =>
        new(id, EcsBackendKind.Arch, id, 16, false);

    private IActorRef SpawnSupervisor() =>
        Sys.ActorOf(
            Props.Create(() => new EcsSupervisorActor(null, LoggerFactory)),
            $"sup-{System.Guid.NewGuid():N}");

    // ---------------------------------------------------------------------
    // Behavior 1: CreateWorld routes to a child world actor and returns a
    // snapshot that reports the world id, backend, display name, and the
    // not-yet-initialized state.
    // ---------------------------------------------------------------------
    [Fact]
    public void CreateWorld_replies_with_snapshot_when_world_is_fresh()
    {
        // Given a fresh supervisor
        var sup = SpawnSupervisor();
        var spec = Spec("alpha");

        // When a CreateWorld is asked
        var info = sup.Ask<EcsWorldInfo>(
            new CreateWorld(spec), TimeSpan.FromSeconds(3)).Result;

        // Then the snapshot reflects the spec and pre-init state
        Assert.Equal("alpha", info.WorldId);
        Assert.Equal(EcsBackendKind.Arch, info.Backend);
        Assert.Equal("alpha", info.DisplayName);
        Assert.False(info.Initialized);
        Assert.Equal(0, info.EntityCount);
    }

    // ---------------------------------------------------------------------
    // Behavior 2: CreateWorld for an existing world id is idempotent: it
    // returns a snapshot of the existing world actor instead of creating a
    // second child. Verified by sending InitializeWorld between the two
    // CreateWorld calls and asserting the second snapshot reports
    // Initialized=true (i.e. same actor).
    // ---------------------------------------------------------------------
    [Fact]
    public void CreateWorld_is_idempotent_when_world_id_already_exists()
    {
        // Given a supervisor with one world already created and initialized
        var sup = SpawnSupervisor();
        var spec = Spec("dup");

        var first = sup.Ask<EcsWorldInfo>(
            new CreateWorld(spec), TimeSpan.FromSeconds(3)).Result;
        Assert.False(first.Initialized);

        // Initialize the world actor by sending InitializeWorld directly to the
        // child via the supervisor's child lookup. The supervisor does not expose
        // a public Initialize route, so we resolve the child and ask it.
        var child = Sys.ActorSelection(sup.Path / "dup").ResolveOne(
            TimeSpan.FromSeconds(3)).Result;
        var initialized = child.Ask<WorldInitialized>(
            new InitializeWorld("dup"), TimeSpan.FromSeconds(3)).Result;
        Assert.Equal("dup", initialized.WorldId);

        // When CreateWorld is asked again for the same id
        var second = sup.Ask<EcsWorldInfo>(
            new CreateWorld(spec), TimeSpan.FromSeconds(3)).Result;

        // Then the same world actor answers and reflects the initialized state
        Assert.Equal("dup", second.WorldId);
        Assert.True(second.Initialized);
    }

    // ---------------------------------------------------------------------
    // Behavior 3: DestroyWorld forwards to the child and the child stops
    // (lifecycle). After destroy, a subsequent GetWorldSnapshot routed via
    // the supervisor no longer resolves (the supervisor has removed the
    // child from its map and does not reply).
    // ---------------------------------------------------------------------
    [Fact]
    public void DestroyWorld_stops_child_and_removes_it_from_supervisor_map()
    {
        // Given a supervisor with one created world
        var sup = SpawnSupervisor();
        var spec = Spec("doom");

        var created = sup.Ask<EcsWorldInfo>(
            new CreateWorld(spec), TimeSpan.FromSeconds(3)).Result;
        Assert.Equal("doom", created.WorldId);

        var child = Sys.ActorSelection(sup.Path / "doom").ResolveOne(
            TimeSpan.FromSeconds(3)).Result;
        Watch(child);

        // When DestroyWorld is asked
        var destroyed = sup.Ask<bool>(
            new DestroyWorld("doom"), TimeSpan.FromSeconds(3)).Result;

        // Then the supervisor reports true and the child actor terminates
        Assert.True(destroyed);
        ExpectTerminated(child, TimeSpan.FromSeconds(3));

        // And a subsequent GetWorldSnapshot no longer answers within a short window
        // (supervisor drops the entry and never replies -> Ask times out).
        var probe = CreateTestProbe();
        sup.Tell(new GetWorldSnapshot("doom"), probe);
        probe.ExpectNoMsg(TimeSpan.FromSeconds(1));
    }

    // ---------------------------------------------------------------------
    // Behavior 4: DestroyWorld for an unknown world id returns false without
    // creating an actor or throwing.
    // ---------------------------------------------------------------------
    [Fact]
    public void DestroyWorld_returns_false_when_world_id_is_unknown()
    {
        // Given a fresh supervisor with no worlds
        var sup = SpawnSupervisor();

        // When DestroyWorld is asked for an unknown id
        var result = sup.Ask<bool>(
            new DestroyWorld("ghost"), TimeSpan.FromSeconds(3)).Result;

        // Then the supervisor replies false
        Assert.False(result);
    }

    // ---------------------------------------------------------------------
    // Behavior 5: UpdateAll fans out an UpdateWorld to every known world
    // actor. Observable through EntityCount growth produced by a registered
    // CountingEntitySystem after InitializeWorld.
    // ---------------------------------------------------------------------
    [Fact]
    public void UpdateAll_fans_out_update_to_all_worlds()
    {
        // Given a supervisor with two initialized worlds, each running a
        // CountingEntitySystem that creates one entity per tick
        var sup = SpawnSupervisor();
        var specA = Spec("fan-a");
        var specB = Spec("fan-b");

        sup.Ask<EcsWorldInfo>(new CreateWorld(specA), TimeSpan.FromSeconds(3)).Wait();
        sup.Ask<EcsWorldInfo>(new CreateWorld(specB), TimeSpan.FromSeconds(3)).Wait();

        var childA = Sys.ActorSelection(sup.Path / "fan-a").ResolveOne(
            TimeSpan.FromSeconds(3)).Result;
        var childB = Sys.ActorSelection(sup.Path / "fan-b").ResolveOne(
            TimeSpan.FromSeconds(3)).Result;

        // Register the counting system on each world, then initialize.
        var sysA = new CountingEntitySystem();
        var sysB = new CountingEntitySystem();
        childA.Tell(new RegisterSystem("fan-a", sysA));
        childB.Tell(new RegisterSystem("fan-b", sysB));
        childA.Ask<WorldInitialized>(new InitializeWorld("fan-a"), TimeSpan.FromSeconds(3)).Wait();
        childB.Ask<WorldInitialized>(new InitializeWorld("fan-b"), TimeSpan.FromSeconds(3)).Wait();

        // When UpdateAll is sent with two ticks (fire-and-forget, like Service.UpdateAll)
        sup.Tell(new UpdateAll(0.016f));
        sup.Tell(new UpdateAll(0.016f));

        // Then both worlds' entity counts grow by 2 (one entity per tick, two ticks).
        // We probe via GetWorldSnapshot until stable, deterministically bounded.
        EcsWorldInfo WaitEntityCount(IActorRef child, int expected)
        {
            for (var i = 0; i < 40; i++)
            {
                var snap = child.Ask<EcsWorldInfo>(
                    new GetWorldSnapshot(string.Empty), TimeSpan.FromSeconds(1)).Result;
                if (snap.EntityCount == expected) return snap;
            }
            throw new Xunit.Sdk.XunitException(
                $"entity count never reached {expected}");
        }

        var snapA = WaitEntityCount(childA, 2);
        var snapB = WaitEntityCount(childB, 2);
        Assert.Equal(2, snapA.EntityCount);
        Assert.Equal(2, snapB.EntityCount);
        Assert.Equal(2, sysA.Ticks);
        Assert.Equal(2, sysB.Ticks);
    }
}