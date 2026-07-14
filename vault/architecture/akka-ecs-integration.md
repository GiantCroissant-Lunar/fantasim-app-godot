# Akka.NET + UnifyECS multi-world integration

> **AUDIT (2026-07-14, code-verified — supersedes the 2026-07-06 note):** IMPLEMENTED, but not
> "as designed." The shape is real (`App.Ecs/Actors/{EcsSupervisorActor,EcsWorldActor}` exist,
> Akka 1.5.69 per `Directory.Packages.props`, `EcsComposition.ComposeEcs` wired at `Host.cs`), but
> this doc's three load-bearing design decisions were **not built**: **no `PinnedDispatcher`**
> anywhere (`EcsSupervisorActor.cs` creates world-actor children with plain `Props.Create`, no
> `.WithDispatcher(...)`); **no `FrameLocked`/Option-C dispatch** (`UpdateAll` is a plain `Tell`
> fan-out to every child, not the per-world Ask/Tell split described below); **supervision is
> narrower** than documented (`EcsSupervisorActor.SupervisorStrategy` restarts only on
> `ObjectDisposedException`, not the added `InvalidOperationException` case shown in the
> "Supervision strategy" section below); and `GetWorldSnapshot` hardcodes
> `RegisteredSystemCount` to `0` (`EcsWorldActor.MakeSnapshot`) rather than reporting the runner's
> real count. Treat "The threading decision," "The update timing decision," and the expanded
> "Supervision strategy" section below as **design-only** until these land. _(See the authority
> index in `vault/README.md`.)_


**Status:** PROPOSED (2026-06-19). Based on analysis of UnifyECS source (`plate-projects/unify-ecs`), the ref-projects `App.Ecs` service, and the Akka.NET actor model discussion.

## The problem

UnifyECS provides a backend-agnostic ECS abstraction (`IWorld`, `ISystemRunner`, `WorldFactory`) with Arch, Flecs, and Friflo backends. The multi-world support (`ArchMultiWorldRunner`) uses plain `Dictionary<string, ArchWorld>` and a sequential `foreach` update loop -- no threading model, no concurrency safety, no lifecycle supervision.

The ref-projects `App.Ecs` service added its own synchronization on top: `lock(_gate)` on every `EcsWorldContext` method, `ConcurrentDictionary` in `Service`. This is correct but blunt -- a long-running system update blocks a status query, and a crashed system leaves the world in an unknown state.

## The insight: different layers, complementary

UnifyECS is the right abstraction for **what** a world is (entities, components, systems, queries). Akka is the right abstraction for **who** owns the world, **how** it's accessed, and **what happens when it fails**.

```
Akka (concurrency, lifecycle, supervision, messaging)
  |-- owns
      UnifyECS (entities, components, systems, queries)
        |-- delegates to
            Arch.Core (high-performance archetype storage)
```

---

## The mapping: one actor per world

Each `EcsWorldActor` owns one `ArchWorld` + `ArchSystemRunner` as private state. Because an actor processes one message at a time on a single thread, the lock disappears entirely -- the actor IS the synchronization.

```
EcsSupervisorActor (T3 adapter target, manages all worlds)
  |-- child: EcsWorldActor("plate-tectonics")   owns ArchWorld + runner
  |-- child: EcsWorldActor("atmosphere")         owns ArchWorld + runner
  +-- child: EcsWorldActor("biome")              owns ArchWorld + runner
```

### EcsWorldActor

Owns one `ArchWorld` + `ArchSystemRunner`. No locks -- the mailbox serializes all access.

```csharp
internal sealed class EcsWorldActor : ReceiveActor
{
    private readonly EcsWorldSpec _spec;
    private readonly ArchWorld _world;
    private readonly ArchSystemRunner _runner;
    private bool _initialized;

    public EcsWorldActor(EcsWorldSpec spec)
    {
        _spec = spec;
        _world = (ArchWorld)WorldFactory.Create(EcsBackend.Arch, new WorldConfig
        {
            Name = spec.DisplayName ?? spec.WorldId,
            InitialEntityCapacity = spec.InitialEntityCapacity,
        });
        _runner = new ArchSystemRunner(_world);

        Receive<RegisterSystem>(m =>
        {
            if (_initialized) throw new InvalidOperationException("Already initialized.");
            _runner.Register(m.System);
        });

        Receive<InitializeWorld>(_ =>
        {
            if (_initialized) return;
            _runner.Initialize();
            _initialized = true;
            Sender.Tell(new WorldInitialized(_spec.WorldId));
        });

        Receive<UpdateWorld>(m => _runner.Update(m.DeltaTime));

        Receive<GetWorldSnapshot>(_ =>
            Sender.Tell(new EcsWorldInfo(
                _spec.WorldId, _spec.Backend,
                _spec.DisplayName ?? _spec.WorldId,
                _world.EntityCount, _runner.RegisteredSystemCount, // DESIGN-ONLY: shipped code hardcodes 0 here (EcsWorldActor.MakeSnapshot), not _runner.RegisteredSystemCount
                _initialized)));

        Receive<DestroyWorld>(_ =>
        {
            _runner.Dispose();
            _world.Dispose();
            Sender.Tell(new WorldDestroyed(_spec.WorldId));
            Context.Stop(Self);
        });
    }

    protected override void PostStop()
    {
        _runner.Dispose();
        _world.Dispose();
    }
}
```

### EcsSupervisorActor

Manages world actors. Creates children, routes messages by world id, aggregates queries, handles supervision.

```csharp
internal sealed class EcsSupervisorActor : ReceiveActor
{
    private readonly Dictionary<string, IActorRef> _worldActors = new();
    private readonly IMessageBus? _bus;

    public EcsSupervisorActor(IMessageBus? bus, ILoggerFactory loggerFactory)
    {
        _bus = bus;

        Receive<CreateWorld>(m => { /* create child actor, publish event */ });
        Receive<DestroyWorld>(m => { /* remove child, publish event */ });
        Receive<UpdateWorld>(m => { /* forward to child by id */ });
        Receive<UpdateAll>(m => { /* Tell all children */ });
        Receive<ListWorlds>(_ => { /* Ask all children, aggregate */ });
    }

    protected override SupervisorStrategy SupervisorStrategy()
        => new OneForOneStrategy(
            maxNrOfRetries: 3,
            withinTimeRange: TimeSpan.FromSeconds(30),
            localOnlyDecider: ex =>
            {
                if (ex is ObjectDisposedException)
                    return Directive.Restart;  // world disposed mid-update, recreate
                return Directive.Escalate;
            });
}
```

### T3 adapter (Service.cs)

Implements `IService`, delegates to the supervisor actor. The T1 contract is unchanged.

```csharp
public sealed class Service : IService, IDisposable
{
    private readonly IActorRef _supervisor;

    public Service(ActorSystem system, ILoggerFactory loggerFactory, IMessageBus? bus = null)
    {
        _supervisor = system.ActorOf(
            Props.Create(() => new EcsSupervisorActor(bus, loggerFactory)),
            "ecs-supervisor");
    }

    public EcsWorldInfo CreateWorld(EcsWorldSpec spec)
        => _supervisor.Ask<EcsWorldInfo>(new CreateWorld(spec), TimeSpan.FromSeconds(5))
            .GetAwaiter().GetResult();

    public void UpdateWorld(string worldId, float dt)
        => _supervisor.Tell(new UpdateWorld(worldId, dt));

    public void UpdateAll(float dt)
        => _supervisor.Tell(new UpdateAll(dt));

    public IReadOnlyList<EcsWorldInfo> ListWorlds()
        => _supervisor.Ask<ListWorldsResult>(ListWorlds.Instance, TimeSpan.FromSeconds(5))
            .GetAwaiter().GetResult().Worlds;

    public bool DestroyWorld(string worldId)
        => _supervisor.Ask<bool>(new DestroyWorld(worldId), TimeSpan.FromSeconds(5))
            .GetAwaiter().GetResult();

    public void Dispose()
        => _supervisor.GracefulStop(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
}
```

---

## The threading decision: pinned dispatcher

> **DESIGN-ONLY (not built as of 2026-07-14).** No `EcsWorldActor` is given a `PinnedDispatcher`
> today; children are created with plain `Props.Create` (`EcsSupervisorActor.cs`). The section
> below describes the intended design, not current behavior.

Arch is fastest single-threaded. Akka's `PinnedDispatcher` pins an actor to a single dedicated thread for its entire lifetime. Giving each `EcsWorldActor` a pinned dispatcher means:

- Each world's `Update` runs on its own dedicated thread.
- World A and world B update in parallel on separate threads.
- No lock needed inside `ArchWorld` or `ArchSystemRunner` -- the actor's single-threaded guarantee replaces all manual synchronization.
- No thread pool contention -- the pinned thread is not borrowed from the pool.

```csharp
var child = Context.ActorOf(
    Props.Create(() => new EcsWorldActor(m.Spec))
        .WithDispatcher("akka.actor.pinned-dispatcher"),
    m.Spec.WorldId);
```

This is the Arch-recommended pattern: single-threaded ECS update, no contention, and the pinned dispatcher guarantees it.

---

## The update timing decision

> **DESIGN-ONLY (not built as of 2026-07-14).** The shipped `UpdateAll` is a plain `Tell` fan-out
> to every child (Option A below) -- there is no `FrameLocked` flag on `EcsWorldSpec` and no
> Option-C per-world Ask/Tell split.

The Godot `_Process` loop calls `IService.UpdateAll(dt)` once per frame. Two options:

### Option A: Tell (fire-and-forget)

```csharp
public void UpdateAll(float dt) => _supervisor.Tell(new UpdateAll(dt));
```

Worlds update asynchronously. The caller doesn't wait. Good for simulation worlds that aren't frame-locked (plate tectonics doesn't care about 16ms precision). Bad for worlds that drive rendering (camera movement must be frame-locked).

### Option B: Ask (blocking with timeout)

```csharp
public void UpdateAll(float dt)
    => _supervisor.Ask<UpdateAllDone>(new UpdateAll(dt), TimeSpan.FromSeconds(1))
        .GetAwaiter().GetResult();
```

The Godot `_Process` call blocks until all world actors finish their update. Guarantees the update happened this frame. Slower (mailbox overhead) but frame-precise.

### Option C: Per-world decision (recommended)

Add `FrameLocked: bool` to `EcsWorldSpec`. The supervisor uses `Ask` for frame-locked worlds and `Tell` for simulation worlds:

```csharp
Receive<UpdateAll>(m =>
{
    foreach (var (id, child) in _worldActors)
    {
        if (_frameLockedWorlds.Contains(id))
            _pendingAcks.Add(child.Ask<UpdateAck>(new UpdateWorld(id, m.DeltaTime)));
        else
            child.Tell(new UpdateWorld(id, m.DeltaTime));
    }
});
```

For frame-locked worlds, the supervisor aggregates `Ask` results and replies to the original `UpdateAll` sender once all frame-locked worlds confirm. Simulation worlds fire-and-forget.

---

## What this replaces from the ref-projects

| Concern | ref-projects (locks) | Akka + UnifyECS |
|---|---|---|
| World access safety | `lock(_gate)` on every method | Actor mailbox (zero locks) |
| Cross-world parallelism | Sequential `foreach` | Parallel (each world on pinned thread) |
| World crash recovery | Exception propagates, world left broken | Supervisor restarts world actor |
| Lifecycle events | Manual `_bus.Publish` | Actor `PreStart`/`PostStop` + bus publish |
| Create world mid-tick | Race with `ConcurrentDictionary` + gate | Message queues behind update, no race |
| Query world mid-tick | Blocks on `lock(_gate)` behind update | Queues behind update, replies when done |
| Testing | Manual lock reasoning, `ConcurrentDictionary` mocks | Akka `TestKit`, message-based, deterministic |

---

## What stays the same

- **T1 contract** (`IService`) -- unchanged, method-based, no Akka types.
- **T2 proxy** -- unchanged, source-generated.
- **`UnifyEcs.Core` / `UnifyEcs.Runtime.Arch`** -- consumed as-is, no modifications. The actor wraps them.
- **`IWorld`, `ISystemRunner`, `WorldFactory`** -- used directly inside the actor, same API.
- **T4 seam** -- N/A. ECS has no Godot seam (same as ref-projects). If rendering reads from ECS, it goes through a separate `App.Renderer.Seam` that resolves worlds via `IEcsRuntime`, not through actors.
- **Bundle safety** -- the `ActorSystem` is resident infrastructure (shared like `IMessageBus`). Bundle code talks to `IService` (method-based), never to actors directly.

---

## Project layout

```
project/contracts/App.Ecs/
    App.Ecs.csproj              # T1: net8.0, [PluginSharedContract]
    AssemblyInfo.cs             # [assembly: PluginSharedContract]
    EcsModel.cs                 # EcsWorldSpec, EcsWorldInfo, EcsWorldChangedMessage
    Services/IService.cs        # [ServiceContract] + [SelectionStrategy]
    Services/Service.cs         # T2 proxy: [RealizeService(typeof(IService))]

project/plugins/App.Ecs/
    App.Ecs.csproj              # T3: net8.0, references contract + UnifyEcs + Akka
    Services/Service.cs         # T3 adapter: wraps IActorRef, implements IService
    Actors/EcsSupervisorActor.cs # Supervisor: creates/destroys/routes world actors
    Actors/EcsWorldActor.cs     # Per-world: owns ArchWorld + runner, no locks
    Actors/EcsMessages.cs       # Internal messages (CreateWorld, UpdateWorld, etc.)
    Runtime/IEcsRuntime.cs      # Resident-only surface for T3 services needing world access
```

---

## Actor messages (internal, never in T1)

```csharp
namespace FantaSim.App.Ecs.Actors;

internal sealed record CreateWorld(EcsWorldSpec Spec);
internal sealed record DestroyWorld(string WorldId);
internal sealed record UpdateWorld(string WorldId, float DeltaTime);
internal sealed record UpdateAll(float DeltaTime);
internal sealed record RegisterSystem(string WorldId, IUnifySystem System);
internal sealed record InitializeWorld(string WorldId);
internal sealed record GetWorldSnapshot(string WorldId);
internal sealed record ListWorlds();
internal sealed record ListWorldsResult(IReadOnlyList<EcsWorldInfo> Worlds);
internal sealed record WorldInitialized(string WorldId);
internal sealed record WorldDestroyed(string WorldId);
internal sealed record UpdateAllDone();
internal sealed record UpdateAck(string WorldId);
```

These live in the T3 orchestrator assembly (`FantaSim.App.Ecs`), not in the contract. They are resident-only and never cross the ALC boundary.

---

## Supervision strategy

> **DESIGN-ONLY (not built as of 2026-07-14).** The shipped `EcsSupervisorActor.SupervisorStrategy`
> restarts only on `ObjectDisposedException` (matching the narrower sample earlier in "The
> mapping" section above). The `InvalidOperationException` branch below is not built.

```csharp
protected override SupervisorStrategy SupervisorStrategy()
    => new OneForOneStrategy(
        maxNrOfRetries: 3,
        withinTimeRange: TimeSpan.FromSeconds(30),
        localOnlyDecider: ex =>
        {
            if (ex is ObjectDisposedException)
                return Directive.Restart;   // world disposed mid-update, recreate
            if (ex is InvalidOperationException)
                return Directive.Restart;   // state corruption, recreate
            return Directive.Escalate;      // unknown failure, let parent decide
        });
```

On `Directive.Restart`, the `EcsWorldActor`'s `PostStop` disposes the old `ArchWorld`/`ArchSystemRunner`, then `PreStart` creates a fresh one with the same `EcsWorldSpec`. The bus publishes an `EcsWorldChangedMessage` with `ChangeKind = Destroyed` then `Created`, so listeners know the world was reset.

---

## References

- UnifyECS source: `plate-projects/unify-ecs/dotnet/src/`
- ref-projects App.Ecs: `lunar-horse-002/ref-projects/fantasim-app-godot/project/contracts/App.Ecs/`, `project/plugins/App.Ecs/`
- Service tier architecture: `vault/architecture/service-tier-architecture.md`
- Cross-ALC rules: `vault/architecture/cross-alc-rules.md`
