using Akka.Actor;
using Akka.TestKit.Xunit2;
using FantaSim.App.Ecs.Actors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FantaSim.App.Ecs.Tests;

/// <summary>
/// Behavioral coverage of <see cref="EcsWorldActor"/> lifecycle: snapshot
/// content, InitializeWorld idempotency, UpdateWorld ticking a registered
/// system, RegisterSystem-after-init rejection, and DestroyWorld lifecycle.
/// Drives the actor directly via TestKit so the assertions isolate world-actor
/// behavior from supervisor routing.
/// </summary>
public class EcsWorldActorTests : TestKit
{
    private static readonly ILoggerFactory LoggerFactory =
        NullLoggerFactory.Instance;

    private static EcsWorldSpec Spec(string id) =>
        new(id, EcsBackendKind.Arch, id, 16, false);

    private IActorRef SpawnWorld(EcsWorldSpec spec) =>
        Sys.ActorOf(
            Props.Create(() => new EcsWorldActor(spec, LoggerFactory)),
            $"world-{Guid.NewGuid():N}");

    // ---------------------------------------------------------------------
    // Behavior 1: GetWorldSnapshot before initialize reports the spec's
    // identity and the not-initialized state with zero entities.
    // ---------------------------------------------------------------------
    [Fact]
    public void Snapshot_before_initialize_reports_identity_and_uninitialized_state()
    {
        // Given a freshly spawned world actor
        var spec = Spec("snap-pre");
        var world = SpawnWorld(spec);

        // When a snapshot is asked
        var info = world.Ask<EcsWorldInfo>(
            new GetWorldSnapshot(spec.WorldId), TimeSpan.FromSeconds(3)).Result;

        // Then the snapshot reflects the spec and pre-init state
        Assert.Equal("snap-pre", info.WorldId);
        Assert.Equal(EcsBackendKind.Arch, info.Backend);
        Assert.Equal("snap-pre", info.DisplayName);
        Assert.False(info.Initialized);
        Assert.Equal(0, info.EntityCount);
    }

    // ---------------------------------------------------------------------
    // Behavior 2: InitializeWorld replies WorldInitialized and flips the
    // snapshot's Initialized flag; a second InitializeWorld is a no-op
    // (the actor guards with _initialized and does not reply, so we verify
    // state via a snapshot instead of awaiting a second reply).
    // ---------------------------------------------------------------------
    [Fact]
    public async Task InitializeWorld_flips_initialized_flag_and_second_call_is_noop()
    {
        // Given a freshly spawned world actor
        var spec = Spec("init");
        var world = SpawnWorld(spec);

        // When InitializeWorld is sent once
        var first = await world.Ask<WorldInitialized>(
            new InitializeWorld(spec.WorldId), TimeSpan.FromSeconds(3));

        // Then it acknowledges and the snapshot reports initialized
        Assert.Equal("init", first.WorldId);
        var snapAfterFirst = await world.Ask<EcsWorldInfo>(
            new GetWorldSnapshot(spec.WorldId), TimeSpan.FromSeconds(3));
        Assert.True(snapAfterFirst.Initialized);

        // When InitializeWorld is sent again (fire-and-forget; actor no-ops)
        world.Tell(new InitializeWorld(spec.WorldId));

        // Then the snapshot still reports initialized (idempotent, no reset)
        var snapAfterSecond = await world.Ask<EcsWorldInfo>(
            new GetWorldSnapshot(spec.WorldId), TimeSpan.FromSeconds(3));
        Assert.True(snapAfterSecond.Initialized);
    }

    // ---------------------------------------------------------------------
    // Behavior 3: RegisterSystem after InitializeWorld is rejected: the
    // late-registered system is never ticked by UpdateWorld, so EntityCount
    // stays at zero. This pins the "already initialized" guard without
    // depending on a specific supervisor strategy.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task RegisterSystem_after_initialize_is_rejected_and_never_ticks()
    {
        // Given an initialized world actor
        var spec = Spec("late-reg");
        var world = SpawnWorld(spec);
        await world.Ask<WorldInitialized>(
            new InitializeWorld(spec.WorldId), TimeSpan.FromSeconds(3));

        // When a system is registered after initialization, then the world is updated
        var sys = new CountingEntitySystem();
        world.Tell(new RegisterSystem(spec.WorldId, sys));
        world.Tell(new UpdateWorld(spec.WorldId, 0.016f));
        world.Tell(new UpdateWorld(spec.WorldId, 0.016f));

        // Then the late-registered system is never ticked and EntityCount stays zero
        // (the guard rejected the registration instead of silently accepting it).
        EcsWorldInfo Snap() => world.Ask<EcsWorldInfo>(
            new GetWorldSnapshot(spec.WorldId), TimeSpan.FromSeconds(1)).Result;

        EcsWorldInfo WaitForStableZero()
        {
            var last = -1;
            for (var i = 0; i < 10; i++)
            {
                var s = Snap();
                if (s.EntityCount == 0 && s.EntityCount == last) return s;
                last = s.EntityCount;
            }
            return Snap();
        }

        var snap = WaitForStableZero();
        Assert.Equal(0, snap.EntityCount);
        Assert.Equal(0, sys.Ticks);
    }

    // ---------------------------------------------------------------------
    // Behavior 4: UpdateWorld ticks a registered, initialized system and
    // EntityCount grows by one per update.
    // ---------------------------------------------------------------------
    [Fact]
    public void UpdateWorld_ticks_registered_system_and_grows_entity_count()
    {
        // Given an initialized world actor with a CountingEntitySystem
        var spec = Spec("tick");
        var world = SpawnWorld(spec);
        var sys = new CountingEntitySystem();
        world.Tell(new RegisterSystem(spec.WorldId, sys));
        world.Ask<WorldInitialized>(
            new InitializeWorld(spec.WorldId), TimeSpan.FromSeconds(3)).Wait();
        Assert.Equal(0, sys.Ticks);

        // When UpdateWorld is sent twice (fire-and-forget, then snapshot probe)
        world.Tell(new UpdateWorld(spec.WorldId, 0.016f));
        world.Tell(new UpdateWorld(spec.WorldId, 0.016f));

        // Then the system is ticked twice and EntityCount reaches 2
        EcsWorldInfo Snap() => world.Ask<EcsWorldInfo>(
            new GetWorldSnapshot(spec.WorldId), TimeSpan.FromSeconds(1)).Result;

        EcsWorldInfo WaitEntityCount(int expected)
        {
            for (var i = 0; i < 40; i++)
            {
                var s = Snap();
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
    // Behavior 5: DestroyWorld replies true and stops the actor.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task DestroyWorld_replies_true_and_stops_actor()
    {
        // Given a spawned world actor
        var spec = Spec("destroy");
        var world = SpawnWorld(spec);
        Watch(world);

        // When DestroyWorld is asked
        var result = await world.Ask<bool>(
            new DestroyWorld(spec.WorldId), TimeSpan.FromSeconds(3));

        // Then the actor replies true and terminates
        Assert.True(result);
        ExpectTerminated(world, TimeSpan.FromSeconds(3));
    }
}