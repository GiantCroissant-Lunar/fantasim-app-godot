# Service scope ownership — which scope (resident vs collectible) owns each service

**Status:** PROPOSED (2026-06-25). Companion to [service-tier-architecture.md](service-tier-architecture.md)
(the vertical T1–T4 axis) and [multi-scene-di-scoping-review.md](multi-scene-di-scoping-review.md)
(the scope *mechanism*). This doc owns the **horizontal axis**: for each service, *which scope owns
its lifetime*, and *which direction references may cross the resident↔collectible boundary*.

## Why this axis exists

Tier (T1–T4) and scope are **orthogonal**. Tier answers "is this Godot-coupled?" (vertical, keeps
Godot quarantined in T4). Scope answers "when is this created and destroyed, and can it unload?"
(horizontal, decides ALC collection). A service has exactly one answer on each axis. The tier doc
already carries a per-service table; this doc adds the scope answer and the rule that produces it.

Getting scope wrong is **not** cosmetic. It has two concrete failure modes:

- **Too broad** (resident when it should be scene-owned): the service leaks across scene
  transitions, and — if it lives in a collectible bundle — its strong references *pin the ALC and
  break hot-reload collection* (the `old ALC collected` gate never fires).
- **Too narrow** (scene-owned when a resident/sibling consumer needs it): missing-dependency
  resolution failures, or duplicate instances across scopes.

## The two scopes

| Scope | Backed by | Lifetime | Can unload? |
|-------|-----------|----------|-------------|
| **Resident** (`App.Common`) | the app kernel + shared-resident assemblies | entire app session | **No** — pinned until process exit |
| **Collectible** (`App.Stage`, `App.Assist`, `App.Timeline`) | PCK bundles in collectible `AssemblyLoadContext`s, entered via `SceneFlow.EnterAsync` | scene enter → exit | **Yes** — unload/reload without restart |

`App.Common` is the **resident scene**: it bootstraps the kernel and never unloads. Stage/Assist/
Timeline are **collectible scenes**: child scopes (assist and timeline nest under stage) that load
and unload as bundles. This is the operative distinction — "which scope owns this service" reduces
to **"should this service ever unload without restarting the app?"**

## The decision rule

Place a service in the **resident** scope (`App.Common`) **iff both**:

1. its lifetime spans the whole session (no reload needed), **and**
2. it is shared by multiple sibling scenes or required by the kernel itself.

Place it in a **collectible** scope (Stage/Assist/Timeline) **iff either**:

1. you want to unload/reload it without restarting (world regen, bundle hot-reload), **or**
2. it depends on / holds references to other collectible (scene-tier) services.

The single sharpest test: **"when I exit this scene, should this service be torn down?"**
Yes → that scene's scope. Must survive → a parent scope or resident.

## The reference-direction invariant (the load-bearing rule)

Placement alone is insufficient. What actually decides whether a collectible ALC *collects* is which
way references flow across the boundary:

- **collectible → resident**: ✅ safe. A Stage/Assist service may freely depend on `IRegistry`,
  `IMessageBus`, `ActorSystem`, `ILoggerFactory`, config — resident outlives it.
- **resident → collectible**: ⛔ forbidden as a *strong* reference. Any resident object holding a
  strong ref to a collectible-scene type pins that ALC; the bundle never unloads. If a resident
  service must observe a collectible one, go through a weak reference, an event it unsubscribes on
  scene exit, or an indirection — never a stored strong field. This is the `WeakReference`
  collection-gate concern; see [cross-alc-rules.md](cross-alc-rules.md) and the
  [bundle-hot-reload handover](../handover/2026-06-24-bundle-hot-reload-di-scoping.md).

**Working example already in the tree:** the Timeline split. `ITimelineController` is resident
(composed by `WorldViewComposition`), while the timeline *view* lives in the collectible `timeline`
bundle. The view (collectible) → controller (resident) reference is the safe direction. The reverse
— controller storing the view — would pin the timeline ALC.

## "Scope ownership" is three layers, not one

This is the crux, and the reason "move World to Stage" is not a one-line change. Owning a service in
a collectible scope (in the strong, *unloadable* sense) requires **all three** layers to agree:

1. **Registration site / call order** — where the `Compose*` runs. Today every domain service is
   composed in the resident `Host.cs` sequence ([Host.cs:75-89](../../project/hosts/complete-app/Host.cs:75)),
   not in a scene activator.
2. **DI scope / lifetime** — the service lives in the shared kernel `IRegistry` (one instance, same
   hash across scopes — resident lifetime) vs. a scene's child scope that disposes on exit. Note the
   two DI systems: domain services register into ServiceArchi `IRegistry` (shared/resident); the
   scene activators build a MEDI child `IServiceProvider` (`SceneActivatorBase`) used only for the
   scene's own objects. **True scene-scope-owned singletons are a KNOWN UNIMPLEMENTED GAP** — see
   Issue 1 of [multi-scene-di-scoping-review.md](multi-scene-di-scoping-review.md) (manual kernel
   forwarding, not real child containers) and the
   [child-scope-singletons follow-up spec](../specs/2026-06-24-dependency-archi-child-scope-singletons-followup.md).
3. **Assembly collectibility** — whether the assembly is shared-resident or packaged into the
   scene's collectible bundle. The `SharedAssemblyPolicy` in
   [App.Common/Bootstrap.cs:110-136](../../project/plugins/App.Common/Bootstrap.cs:110) lists
   `FantaSim.App.` and `FantaSim.App.World.` as **shared-resident prefixes**; only
   `collectibleBundles.AssemblyNames` are excluded (→ collectible). So a service is collectible only
   if its assembly leaves the shared prefixes **and** is packed into the scene `.pck`.

A **weak** ("lifetime-scoped but not unloadable") ownership needs layers 1+2. A **strong**
("unloadable/reloadable") ownership needs all three. The user's "Stage can be unloaded/reloaded"
intent points at the strong form.

## Tier × scope: which tiers move, which stay

Scope ownership is applied **per tier** — "put service X in the stage scope" does not make X's whole
project tree collectible:

| Tier | Resident vs collectible |
|---|---|
| **T1 contract** (`contracts/App.X`) | **Always resident-shared.** Marked `[PluginSharedContract]`; the interface type must be *identical* on both sides of the ALC boundary, so the resident kernel and every collectible scene share one copy. A collectible contract assembly gives each ALC its own incompatible type and breaks resolution. **T1 never moves.** |
| **T2 proxy** | In the contract assembly → resident with T1. |
| **T3 orchestrator** (`plugins/App.X`) | **This is what "moves to stage."** Its registration/lifetime is owned by the stage scope; its assembly may be packed into `stage.pck` once the reference audit is clean. |
| **T4 seam** (`plugins/App.X.Seam`) | Goes where the scene's Godot content mounts (e.g. the Environment sub-scene); collectible with the scene. |

So "App.Camera / App.NodeGraph / App.Timeline in stage scope" means **their T3 lifetime + impl move to
stage; their T1 contracts stay resident.** Pointing at `contracts/App.X` names the subsystem, not the
assembly that relocates.

## Target scene/scope topology (proposed)

```
root  -- App.Common  (RESIDENT, never unloads)
|        kernel: IRegistry, IMessageBus, ActorSystem, Config, IPluginHost, ILoggerFactory
|        resident services that must outlive every scene: Resource, SceneFlow, Command
|
+-- stage  -- collectible DI scope
    |    stage-owned (T3 lifetime + impl): World, Camera, NodeGraph, Timeline,
    |    CellElevation, WorldView/render   (T1 contracts stay RESIDENT -- see tier x scope)
    |
    +-- Environment  -- PLAIN sub-scene, NO DI binding
    |      the planet renders here: a stage-scoped T4 seam mounts the planet's Godot nodes into
    |      this subtree. Environment owns no services; it reads from stage's scope.
    |
    +-- assist     -- collectible DI scope (child of stage)
    +-- timeline   -- collectible view bundle (child of stage)
```

Two decisions fixed here:

- **Camera / NodeGraph / Timeline join World as stage-owned**, on the same migration template (move
  registration out of `Host.cs`, contract stays resident, reference audit, optional collectible packaging).
- **Environment is a plain scene** (the third scene category in
  [multi-scene-di-scoping-review.md](multi-scene-di-scoping-review.md) — no `ISceneActivator`, no
  container). **Planet *generation* is a stage-scoped service (World); Environment is where its
  *rendered* result lives.** Data (DI-scoped) and render (Godot nodes, no DI) stay separate — the seam
  writes into Environment; Environment binds nothing.

> **Audit flags before any of these move:**
> - **NodeGraph + Timeline have resident consumers today** — `Host.cs` mounts the world-graph / iii-graph
>   demos via `App.NodeGraph` and `ITimelineController` ([Host.cs:244-336](../../project/hosts/complete-app/Host.cs:244)).
>   Relocating these services makes those resident references the forbidden resident→collectible
>   direction. The Host demos must move into stage, be retired, or the service stays resident — the
>   reference audit decides.
> - **Camera is not composed in `Host.cs` at all** — confirm where it is currently registered before
>   assuming it is resident; it may already be the easiest to make stage-owned.

## Current inventory (2026-06-25)

Everything domain-level is **resident** today; the collectible scenes own no domain services.

### Resident kernel (App.Common.Bootstrap) — foundational, correctly resident

`IRegistry` · `ILoggerFactory` · `CrosscutFoundation.Config.IService` · `IMessageBus` ·
`ActorSystem` (Akka "fantasim") · `IPluginHost`. These satisfy the resident rule (session lifetime +
kernel-shared) and must stay resident.

### Resident domain services (composed in Host.cs into the kernel IRegistry)

| Service (Compose*) | Current scope | Reload candidate? | Notes |
|---|---|---|---|
| Resource | resident | no | loads the bundles; must outlive them |
| SceneFlow | resident | no | owns scene enter/exit; the resident arbiter |
| Command | resident | no | the axis router; resident by design |
| Ecs | resident | maybe | per-world isolation; actor-backed (resident ActorSystem) |
| **World** | **resident** | **yes (proposed → stage)** | the trigger for this doc; see below |
| CellElevation | resident | with World | derived from World; same scope as World |
| WorldView | resident | with World | registers `ITimelineController`; render seam |
| Gpu / GpuShader | resident | maybe | smoke-checked from assist today |
| Iii | resident | no | external-axis bridge; Node-backed seam, resident |
| Timeline (Compose) | resident | yes (proposed → stage) | service → stage; T1 contract stays resident; the *view* is the collectible `timeline` bundle |
| Camera | not in Host.cs (verify) | yes (proposed → stage) | not composed in Host.cs — find current registration; may already be easiest to stage-own |
| NodeGraph | resident (contracts + view) | proposed → stage (audit) | largely shared contracts + App.Ui.NodeGraph view; resident Host demos consume it — see audit flags |
| Activity | resident | no | append-only log |
| Ui | resident | no | resident view host |
| RemoteIngress | resident | no | HTTP command ingress |

### Collectible scenes (entered via SceneFlow, loaded as PCK into collectible ALCs)

`stage` (StageActivator) · `assist` (under stage) · `timeline` (under stage). Each registers only its
own `Bootstrap`; assist additionally runs GPU smoke checks. **No domain service is scene-owned yet.**

## Worked example — World: resident → stage

Moving World to the stage scope is the first proposed delta. The honest scope of work, by layer:

- **Layer 1 (registration):** move `WorldComposition.ComposeWorld` (and `CellElevation`,
  `WorldView`) out of the resident `Host.cs` sequence into the stage activation path.
- **Layer 2 (DI scope):** requires the child-scope-owned-singleton capability that is **specced but
  not built** (see references). Until then, "stage-owned World" can only be approximated by manual
  forwarding — document the limitation, don't pretend it's clean.
- **Layer 3 (collectibility):** remove `FantaSim.App.World.` (and audit `FantaSim.App.`) from the
  shared prefixes, add World assemblies to `collectible-bundles.json`, and pack them into `stage.pck`.
- **Reference audit:** find every resident → World strong reference (e.g. `Host` fields like
  `_cellElevation`, the world-graph view slots) and sever/weaken them, or the stage ALC will not
  collect.
- **Akka gotcha:** `ActorSystem` is resident ([App.Common/Bootstrap.cs:64](../../project/plugins/App.Common/Bootstrap.cs:64)).
  If a collectible World spawns actors there, those actors hold collectible World types in resident
  state → the ALC never collects. A collectible World must either stay a plain class or explicitly
  stop + detach its actors on scene exit.

**Recommended phasing.** Phase 1 = lifetime-scope World to stage (layers 1+2, assembly stays
resident-shared) — reversible, no ALC risk, delivers "stage owns World's lifetime." Phase 2 (only if
true unload/reload is wanted) = make World collectible (layer 3 + reference audit + actor teardown),
gated by a `doubt-driven-development` review and a windowed `old ALC collected` verification per
[verify-windowed](../../.claude/skills/verify-windowed/SKILL.md).

**Decided (2026-06-25):** dedicated `world` bundle entered under stage (per-subsystem topology — the
template Camera/NodeGraph/Timeline follow); lifetime via `IRegistry.RegisterOwned<T>` + `ShutdownAsync`
disposal — the proven timeline/assist/activity bundle pattern, **not** the unbuilt child-scope work, so
Layer 2 is not a blocker for bundle-style ownership. Full step-by-step:
[plans/2026-06-25-world-to-stage-scope.md](../plans/2026-06-25-world-to-stage-scope.md).

## Adding a new service — apply the rule

1. Does it need to unload/reload, or depend on collectible services? → collectible scene scope.
   Else → resident.
2. If collectible: confirm only **collectible → resident** references exist; weaken any reverse edge.
3. If collectible and actor-backed: ensure actors stop on scene exit (no resident ActorSystem pin).
4. Record the answer in the inventory table above and in the service's tier-doc row.

## References

- [service-tier-architecture.md](service-tier-architecture.md) — the orthogonal T1–T4 axis
- [multi-scene-di-scoping-review.md](multi-scene-di-scoping-review.md) — scope mechanism; Issue 1 = the child-scope gap
- [cross-alc-rules.md](cross-alc-rules.md) — reference-direction / collection invariants
- [dependency-archi-child-scope-singletons-followup spec](../specs/2026-06-24-dependency-archi-child-scope-singletons-followup.md) — the unbuilt layer-2 capability
- [bundle-hot-reload-di-scoping handover](../handover/2026-06-24-bundle-hot-reload-di-scoping.md) — ALC-collection gate context
