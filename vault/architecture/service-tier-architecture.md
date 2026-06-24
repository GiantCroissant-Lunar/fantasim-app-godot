# Service tier architecture with Akka.NET (T1-T4)

**Status:** PROPOSED. Distilled from the ref-projects `lunar-horse-002/ref-projects/fantasim-app-godot` architecture (confirmed 2026-06-10) combined with the Akka.NET integration discussion (2026-06-19), and extended for the iii-graph runtime as a peer orchestration axis (2026-06-19). Supersedes the plain-class-only T3 model where actor benefits are warranted.

> **Orthogonal axis:** tier (T1–T4) is the *vertical* axis (Godot-coupling). The *horizontal* axis — which scope (resident `App.Common` vs collectible Stage/Assist/Timeline) owns each service's lifetime, and the resident↔collectible reference rules — lives in [service-scope-ownership.md](service-scope-ownership.md). Every service has one answer on each axis.

## Three-layer model

The app is understood as three orthogonal layers (see [node-graph-paradigm.md](node-graph-paradigm.md) for the full treatment):

- **Paradigms** — general app-level UI/execution shapes any domain can populate: node graph (`App.NodeGraph`), timeline (`App.Timeline`).
- **Orchestration axes** — where work actually runs: Akka axis (dormant: World/Ecs), iii axis (active: bridge + workers).
- **UI seams** — the only place Godot types live (`*.Seam` projects).

A concrete behavior sits at an intersection (e.g. "a node graph whose nodes are iii functions"). The paradigm doesn't know the axis; the axis doesn't know the paradigm; they meet at function-registration time.

## Two orchestration axes

The app has **two peer orchestration axes**, each covering what the other cannot. This replaces any earlier framing of a single "orchestration seat above Akka/ECS."

| Axis | Covers | Backed by | Status |
|------|--------|-----------|--------|
| **Akka axis** | Internal actor supervision: concurrent stateful entities, ECS worlds, retry/supervision, in-process simulation | Akka.NET `ActorSystem` (resident) | Present, dormant (`App.World` / `App.Ecs`) |
| **iii axis** | Orchestration crossing the process/agent boundary: dataflow DAGs over external capability workers, agent-driven commands, out-of-process pipelines | Rust gdext bridge + iii-sdk + Python capability workers | Active (`App.Iii`) |

`App.Command.IService` is the **router** between the two axes — not a seat above either. Each axis plugin registers its own command family (`world.*` -> Akka, `pipeline.*`/`iii.*`/`graph.*` -> iii) via `IService.Register`; `ExecuteAsync` dispatches by command-id lookup. The per-axis seams (`IWorldOrchestration`, `IIiiOrchestration`) exist for subsystem identity + health, not as a competing dispatch path. See `iii-graph-runtime.md` for the full iii-axis design.

## Why tiers

The four-tier split keeps **Godot quarantined** in a thin seam layer and makes the core engine-agnostic. Every service follows the same shape, whether or not its T3 is backed by an Akka actor:

- The **contract** (T1) is a pure C# interface in a `net8.0`/`netstandard2.1` assembly marked `[PluginSharedContract]`, safe to share across the ALC boundary.
- The **proxy** (T2) is a source-generated locator in the same contract assembly; callers use it without knowing who implements the service.
- The **orchestrator** (T3) is pure C# with zero Godot usings; it holds the service logic and delegates engine work to a provider interface. T3 may be a plain class or an Akka actor adapter -- a per-service decision.
- The **seam** (T4) is the *only* tier allowed to touch Godot types; it implements the T3 provider interface. T4 is always a plain class, never an actor.

This keeps collectible bundles free of Godot-derived types (the ALC unloads cleanly) and allows the same T3 logic to run under a different engine by swapping the T4 seam.

---

## The tiers

### T1 -- Contract

**Role:** Engine-agnostic service interface + shared DTOs/messages.

**Location:** `project/contracts/App.X/`

**Required attributes and conventions:**

| File | Contents | Conventions |
|------|----------|-------------|
| `Services/IService.cs` | Interface with `[ServiceContract]` + `[SelectionStrategy(SelectionMode.HighestPriority)]` | Pure C# (BCL types + shared-foundation types only); namespace `FantaSim.App.X` |
| `Services/Service.cs` | T2 proxy partial class with `[RealizeService(typeof(IService))]` | Namespace `FantaSim.App.X.Services.Proxy`; constructor takes `IRegistry` |
| `AssemblyInfo.cs` | `[assembly: PluginSharedContract]` | Marks the assembly as shared across the ALC boundary |
| Root-level types | Non-service shared types: messages, DTOs, records | e.g. `EcsModel.cs`, `ViewMessages.cs` |

**TFM:** `netstandard2.1` (older contracts) or `net8.0` (newer ones). Pure C# -- no Godot package reference, no Akka types.

**Constraint:** No Akka types (`IActorRef`, `ActorSystem`, `Props`) leak into T1. The contract stays method-based (`Task ShowAsync(...)`, `EcsWorldInfo CreateWorld(...)`) regardless of whether T3 is actor-backed. Callers must not know or care about the implementation.

### T2 -- Proxy

**Role:** Service-locator proxy. ServiceArchi.SourceGen emits the forwarding partial that implements `IService` by resolving the active T3 from the registry using the selection strategy declared on the contract.

**Location:** Same project as T1 (`project/contracts/App.X/Services/Service.cs`).

```csharp
namespace FantaSim.App.X.Services.Proxy;

[RealizeService(typeof(IService))]
public sealed partial class Service
{
    private readonly IRegistry _registry;
    public Service(IRegistry registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));
}
```

No hand-written method bodies. Unchanged by Akka adoption.

### T3 -- Orchestrator

**Role:** Engine-agnostic service implementation. Zero Godot usings. Owns the service logic, tracks state, subscribes to bus messages, delegates engine work to provider seam interfaces.

**Location:** `project/plugins/App.X/Services/Service.cs`

**Two T3 shapes:**

#### Plain class T3 (unchanged from ref-projects)

For services with simple concurrency (thin orchestration, `ConcurrentDictionary` suffices, no supervision need):

```csharp
public sealed class Service : IService, IDisposable
{
    private readonly IViewHost _viewHost;
    private readonly ConcurrentDictionary<string, byte> _active = new();
    // direct method bodies, no actor mailbox
}
```

Services that fit this shape: `App.Ui`, `App.SceneFlow`, `App.Camera`, `App.Activity`.

#### Actor adapter T3 (new with Akka adoption)

For services with actor-shaped problems (concurrent stateful entities, long-running async workflows, supervision/retry needs):

```csharp
// T3 adapter: implements IService, delegates to actor ref
public sealed class Service : IService, IDisposable
{
    private readonly IActorRef _actor;
    public Service(IActorRef actor) => _actor = actor;

    public Task<CommandResult> ExecuteAsync(CommandRequest req, CancellationToken ct = default)
        => _actor.Ask<CommandResult>(req, ct);
}
```

The actor itself is an implementation detail in `Actors/`:

```csharp
internal sealed class AgentSessionActor : ReceiveActor
{
    private readonly ILlmProvider _llm;
    // single-threaded state, no locks
    ReceiveAsync<AskRequest>(async msg => { ... });
}
```

Services that fit this shape: `App.Agent` (multiple concurrent LLM sessions), `App.Remote` (command dispatch with timeout/retry), `App.Ecs` (multiple worlds with per-world isolation -- see `akka-ecs-integration.md`).

**T3 conventions (both shapes):**

- Constructor receives providers, shared services (`IMessageBus`, `IRegistry`, `ILoggerFactory`, `ActorSystem`), and other service contracts.
- For actor-backed T3: the `ActorSystem` is injected as shared infrastructure (composed in `Bootstrap.cs`). The service creates its child actors within it under a named path (e.g. `/user/app-agent`, `/user/app-ecs`).
- Registers into the kernel `IRegistry` with `ServiceRegistration` tags at composition time (done in `Host.cs`).

### T4 -- Seam

**Role:** The ONLY tier that references Godot types. Implements the T3 provider interface.

**Location:** `project/plugins/App.X.Seam/`

**T4 is always a plain class, never an actor.** Godot APIs are not thread-safe -- every call must land on the main thread via `Callable.From(...).CallDeferred()`. An Akka actor runs on its own dispatcher thread, not the Godot main thread. Making T4 an actor would add a mailbox hop + thread switch only to reach the same `CallDeferred` call that was one method invocation away. T4 is nearly stateless; there is no concurrency problem to solve with actors.

```
T3 actor -> T4 method call -> CallDeferred -> Godot main thread   (correct)
T3 actor -> T4 actor mailbox -> T4 actor thread -> CallDeferred    (wrong: redundant hop)
```

**Key conventions:**

- References T3 (to implement its provider interface), the contract, and Godot packages.
- Marshals Godot main-thread work via `Callable.From(...).CallDeferred()`.
- Registered at composition (in `Host.cs`) by being constructed and handed to the T3 ctor -- NOT registered into the kernel `IRegistry` as a service.

### T4 Node-backed seam (exception)

`IiiBridge` (`App.Iii.Seam`) is the one place where "T4 is always a plain class, never a Node" breaks, and the break is justified. It extends `Godot.Node` because the gdext `IiiClient` child runs a tokio runtime off-thread and pushes results through an mpsc channel drained in its `_Process(float)` on the main thread (where it is safe to build `GString` and emit `response`). A plain class has no `_Process`; without the drain loop, results never return to the main thread.

The hard constraint: `IiiBridge` exposes **only** `IIiiInvoker` (pure C#, in `FantaSim.App.Iii.Contracts`) upward. No Godot type crosses to T1 or T3. Collectible bundles reference `IIiiInvoker`, never `IiiBridge`. See `iii-graph-runtime.md` §4.

---

## When to use an actor for T3

The actor model earns its complexity cost when a service has one or more of:

- **Concurrent stateful entities** that need isolation (multiple agent sessions, parallel ECS worlds)
- **Long-running async workflows** with retry/supervision (LLM calls, pipeline orchestration)
- **Request-response with timeout/retry** (remote calls, external API integration)
- **Natural message-flow semantics** (event sourcing, command dispatch)

| Service | Actor-shaped? | Why |
|---------|--------------|-----|
| App.Resource | Marginal | Sequential per-bundle lifecycle, SemaphoreSlim gate works. Actor would simplify but isn't required. |
| App.Ui | No | Thin orchestration, `ConcurrentDictionary` handles it. |
| App.SceneFlow | No | Simple scene entry/exit state. |
| App.Camera | No | Minimal concurrency. |
| App.Activity | No | Append-only event log. |
| App.Agent | Yes | Multiple concurrent LLM sessions, each with lifecycle, retry, streaming. |
| App.Remote | Yes | Command dispatch, concurrent handlers, timeout/retry. |
| App.Ecs | Yes | Multiple independent worlds, per-world isolation, parallel updates. See `akka-ecs-integration.md`. |
| App.World | Maybe | World simulation could be an actor system (one actor per region/entity). Currently dormant. |
| App.Timeline | Maybe | Playback state machine, currently simple. |
| App.Iii (`IiiOrchestrator`) | No | Dataflow DAG executor; pure async over external iii functions. Plain class + `ConcurrentDictionary` for in-flight jobs. Becomes an actor only if retry/supervision needs grow. |

**Rule of thumb:** start with a plain class T3. Add an actor only when you hit a real concurrency, supervision, or distribution need. Forcing every service through a mailbox when a method call would do is premature complexity.

---

## Composition -- how Host.cs wires tiers together

`project/hosts/complete-app/Host.cs` is the Godot autoload entry point. Every service follows the same composition pattern, illustrated with `ComposeUi` (plain class T3) and `ComposeAgent` (actor T3):

```
ComposeUi(composition)
  |
  +-- Create T4 seam instance (ViewHost -- receives Godot scene tree artifacts)
  |
  +-- Create T3 orchestrator (App.Ui.Services.Service)
  |     constructor: (IViewHost seam, Resource.IService, IMessageBus, ILoggerFactory)
  |
  +-- Register T3 into kernel IRegistry

ComposeAgent(composition)
  |
  +-- Create T3 actor (AgentSessionActor) within the shared ActorSystem
  |     actor path: /user/app-agent
  |
  +-- Create T3 adapter (App.Agent.Services.Service wrapping IActorRef)
  |     constructor: (IActorRef actor, IMessageBus, ILoggerFactory)
  |
  +-- Register T3 adapter into kernel IRegistry
```

The pattern is identical in structure. The only difference is whether T3 constructs a plain class or creates an actor in the `ActorSystem` and wraps it. Composition order matters: services that others depend on must be composed first.

### ComposeIii (iii-axis composition)

The iii axis self-registers its command family into the router. Ordered after `ComposeCommand` (the router must exist):

```
ComposeIii(composition)
  |
  +-- Create T4 seam IiiBridge (Node-backed; AddChild so _Process drains the bridge)
  |     register IIiiInvoker into kernel IRegistry (as the pure contract, not the Node)
  |
  +-- Create T3 orchestrator (App.Iii.IiiOrchestrator wrapping IIiiInvoker + GraphExecutor)
  |     constructor: (IIiiInvoker invoker, ILoggerFactory)
  |
  +-- Register IIiiOrchestration into kernel IRegistry
  |
  +-- orchestrator.Register(...) its command family (pipeline.*, iii.*, graph.*) into App.Command.IService
```

`ComposeWorld` stays composed but is dormant infrastructure (see `iii-graph-runtime.md` §10). The iii axis does not reference `App.World`'s outputs.

### Bootstrap and shared infrastructure

`project/plugins/App.Common/Bootstrap.cs` sets up the kernel infrastructure *before* any service is composed:

1. **ServiceArchi registry** (`ServiceRegistry`) -- the kernel `IRegistry`.
2. **Structured logging** -- `ILoggerFactory`.
3. **Crosscut services** -- `IMessageBus`, config, resilience.
4. **ActorSystem** -- shared Akka actor system (new with Akka adoption). Composed alongside `IMessageBus` and `IRegistry`. Services that need actors receive the `ActorSystem` by constructor injection.
5. **Plugin host** -- `IPluginHost` for collectible bundles.

---

## Naming conventions

- **Assembly prefix:** `FantaSim.App.*`. Contract assemblies carry a `.Contracts` suffix; T3 orchestrators are the bare name; seams are `FantaSim.App.X.Seam`.
- **Non-service contract assemblies** (shared DTOs + interfaces, no `IService`, no registry registration) follow the `Cross.Abstractions` precedent: e.g. `FantaSim.App.NodeGraph.Contracts` holds `GraphDocument`/`GraphNode`/`GraphWire`/`INodeFunctionProvider`/`IGraphSource`. `FantaSim.App.Iii.Contracts` (if needed) holds iii-specific shared types.
- **Project directories** omit the `FantaSim` prefix AND the `.Contracts` suffix: `project/contracts/App.Ui/`, `project/plugins/App.Camera.Seam/`.
- **Contract namespaces:** `FantaSim.App.X` for the service interface and root types; `FantaSim.App.X.Services.Proxy` for T2.
- **Provider namespaces:** `FantaSim.App.X.Providers` for seam interfaces.
- **Seam namespaces:** `FantaSim.App.X.Seam` for Godot implementations.
- **Actor namespaces:** `FantaSim.App.X.Actors` for internal actor implementations (never exposed in T1).
- **Actor messages:** `FantaSim.App.X.Actors.Messages` (internal DTOs between T3 adapter and actor; never cross the ALC boundary).

---

## References

- ref-projects service-tier-architecture: `lunar-horse-002/ref-projects/fantasim-app-godot/vault/architecture/service-tier-architecture.md`
- Cross-ALC rules: `vault/architecture/cross-alc-rules.md`
- Akka + ECS integration: `vault/architecture/akka-ecs-integration.md`
