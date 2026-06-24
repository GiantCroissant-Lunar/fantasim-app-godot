# Plan: World → stage scope (dedicated `world` bundle)

**Status:** PLANNED (2026-06-25). Reconciled from two independent cross-model investigations
([glm](../../.agent/run/dispatch/world-to-stage-plan.glm.md), [kimi](../../.agent/run/dispatch/world-to-stage-plan.kimi.md)),
the lead-session cross-check, and the live-code resolution of the `RegisterOwned` question.
Implements the target topology in [service-scope-ownership.md](../architecture/service-scope-ownership.md).

## Locked decisions

- **Topology:** a dedicated **`world` collectible bundle entered under `stage`** (NOT folded into
  `stage.pck`). This is the template Camera / NodeGraph / Timeline follow later; **Environment** is the
  shared plain mount scene. (User, 2026-06-25.)
- **Lifetime mechanism:** `IRegistry.RegisterOwned<T>(…)` + dispose the handle in `ShutdownAsync` —
  the **same proven pattern** as `StagePlugin.cs:25`, `AssistPlugin.cs:25`, `TimelinePlugin.cs:20`,
  `App.Ui.Activity/Plugin.cs:31`. `IRegistry` also exposes `UnregisterOwner(ownerId)`. This resolves
  the GLM-vs-Kimi divergence in Kimi's favour.
- **Phasing is by RISK, not weak-vs-strong:** Phase 1 wires World as a bundle + severs every
  resident→World-impl reference, assemblies still resident-shared (no collection). Phase 2 flips on
  collection (pack `world.pck`, shared-policy exclusion, engine-DLL handling) and is gated by a
  `doubt-driven-development` review + the windowed `old ALC collected` gate.

### Why `RegisterOwned` reshapes the plan

The existing collectible bundles (timeline/assist/activity) already achieve collectible **lifetime +
collection** through `RegisterOwned` + `ShutdownAsync` disposal + shared-assembly exclusion — **not**
through the unbuilt dependency-archi child-scope container. So World follows `TimelinePlugin` verbatim,
and the "layer-2 child-scope gap" both models flagged is **not** a blocker for bundle-style ownership
(it only matters for nested, parent-visible *scoped* services, which World does not need).

---

## Phase 1 — wire World as a bundle + sever resident refs (no collection yet)

**Deliverable:** World is composed by a `WorldPlugin`, `RegisterOwned` into the kernel, entered under
stage; **nothing resident strong-references World impl.** Assemblies stay resident-shared. Reversible.

### 1. New `WorldPlugin` (mirror `TimelinePlugin`)
- New `project/plugins/App.World/WorldPlugin.cs`: `[Plugin] ILifecyclePlugin`. `InitializeAsync` runs
  the World composition and `RegisterOwned`s each service, storing the handles; `ShutdownAsync` disposes
  them (or `registry.UnregisterOwner`).
- Change `WorldComposition` (`WorldComposition.cs:19,28,37`) and `CellElevationComposition` from
  `ctx.Registry.Register<…>` to `RegisterOwned<…>` returning the disposables.

### 2. Remove World from the resident host
- `Host.cs`: drop usings `:20-24`; the `_cellElevation` field `:43` (+ dispose `:561`); the world-graph
  slots `:47-53`; the `ComposeWorld`/`ComposeCellElevation`/`ComposeWorldView` calls `:78-85`; the
  `ShowWorldGraph`/`RunWorldGraphTest` demos + helpers `:270-360,:430-466`; their deferrals `:100,:102`.
- `complete-app.csproj`: remove `ProjectReference`s to `App.World`, `App.World.FieldView`,
  `App.World.Seam` (Kimi A.1).

### 3. Move the `world.run_generation_graph` handler out of resident Command — THE blocker
- `CommandComposition.cs`: remove `using FantaSim.App.World.GenerationGraph` `:2`; remove the command
  descriptor + handler `:38-92` that does `new WorldGenerationGraphRunner(providers)` `:49` and
  `PublishWorldGenerationGraphRun` `:98-111`.
- Re-register that command family **from the world bundle** via `App.Command.IService.Register(...)` —
  the same self-registration the iii axis uses for `pipeline.*`/`iii.*`
  ([service-tier-architecture.md:222-237](../architecture/service-tier-architecture.md)). Resident
  Command keeps **only** the router. `LocalOrchestrator` stays (contract-only; already returns
  `MissingService` when World is absent — `LocalOrchestrator.cs:85,142,164`).

### 4. Godot SceneTree handoff → **Environment**
- `WorldViewComposition` needs `GetTree()` and adds `GlobeView` to the tree
  (`App.World.Seam/HostComposition/WorldViewComposition.cs:71,83`). The world bundle's entry — the
  **Environment** plain scene — is the mount: its script receives the scene `IServiceProvider` and hands
  `GetTree()` to the seam; `GlobeView` is parented under **Environment** so it leaves the tree on exit.
  This is the concrete realization of "planet generated in Environment."
- Mechanism: static handoff now, or the `SceneScopeNode` bridge (review Issue 3) — small decision (Q2).

### 5. Sever the Timeline static + controller pin — Kimi catch #1
- `App.Timeline.Seam/HostComposition/TimelineComposition.cs`: `TimelineFace.ResidentController =
  controller` `:22-23` is a **resident static** holding the controller; `TryGet<ITimelineController>()`
  `:13`. Clear the static on exit and/or move the registration so no resident static pins a stage/world
  object; null `TimelineFace` fields on `_ExitTree` (`TimelineFace.cs:14,52`). Depends on Q1.

### 6. Build + verify Phase 1
- `task build` + `task test` green (fix, don't delete, any tests that constructed World via the host).
- Windowed run: stage enters → World composes → globe mounts under Environment → `world.*` commands
  resolve while stage is active. **No collection claim yet.**

---

## Phase 2 — make `world.pck` actually collect (doubt-driven gated)

### 7. Shared policy + multi-assembly bundle
- `Bootstrap.cs`: remove the `"FantaSim.App.World."` prefix `:131`; **keep** `"FantaSim.App."` (so
  `FantaSim.App.World.Contracts` stays shared) and `"FantaSim.World."` (engine libs stay shared —
  consumed by resident `App.Ecs` + collectible `App.Timeline`).
- Extend `CollectibleBundles` (`CollectibleBundles.cs:49-70`) to allow **multiple** assemblies per
  bundle (`assemblyNames` array). Add the `world` bundle with the four impl assemblies — `FantaSim.App.World`,
  `App.World.Composition` (**naming hazard:** no `FantaSim.` prefix), `FantaSim.App.World.FieldView`,
  `FantaSim.App.World.Seam` — **never** `FantaSim.App.World.Contracts`.

### 8. Engine-DLL availability — Kimi catch #2
- With the host no longer referencing `App.World`, ensure the shared-resident `FantaSim.World.*` /
  `Cartography.*` DLLs still land in the resident output dir (add direct host refs to those engine libs,
  or a build copy step) — else the world ALC throws `FileNotFoundException` resolving them from the parent.

### 9. Packaging
- `Taskfile.yml`: `bundle:world:build` (build App.World + companions, copy the four DLLs into
  `project/bundles/world/`), `bundle:world` (export `world.pck`), a `bundle:install` line, and a
  `bundles:` dep. Add a `"world PCK"` content-app export preset and `project/bundles/world/manifest.json`.

### 10. Enter under stage + verify collection
- `EnterInitialScenes`: `EnterAsync(new SceneRequest("world", "stage"))`.
- Gate ([verify-windowed](../../.claude/skills/verify-windowed/SKILL.md)): reload the world bundle →
  `Hot-reload: old ALC collected …`. Actor path (SurrealDb): `TruthEventWriterActor` `PostStop` logs and
  `ActorSystem.WhenTerminated` is **not** called. `world.run_generation_graph` still dispatches.

---

## Reference audit (union of both models)

| Resident holder | File:line | Ref | Severance |
|---|---|---|---|
| Host `_cellElevation` | `Host.cs:43,85,561` | `CellElevationModel` (impl) | relocate to bundle; drop field + dispose |
| Host world-graph demo | `Host.cs:47-53,270-360,430-466` | `.GenerationGraph/.Globe/.Seam` impl | move into world bundle or retire (Q3) |
| Command world handler | `CommandComposition.cs:2,38-92,98-111` | `WorldGenerationGraphRunner` (impl) | re-register from bundle; router only |
| LocalOrchestrator | `LocalOrchestrator.cs:3-8,85,142,164` | contract only | keep; tolerates null |
| Timeline.Seam static | `TimelineComposition.cs:13,22-23,29-34` | `ITimelineController` via resident static | clear on exit / move registration (Q1) |
| TimelineFace fields | `TimelineFace.cs:14,52` | controller fields | null on `_ExitTree` |
| App.Ecs | `FieldComponents.cs:3`, `ReduceFieldsSystem.cs:5` | `FantaSim.World.Fields` (shared) | none (safe, shared-resident) |

## Akka actor gate (both models agree)
World spawns `TruthEventWriterActor` in the **resident** `ActorSystem` **only** under the SurrealDb
backend (`Service.cs:44,53-57,65-68`; default `inmemory` → no actor). `Service.Dispose` →
`ActorTruthEventWriter.Dispose` → `GracefulStop` (`ActorTruthEventWriter.cs:51-57`). Phase-1 ships
actor-free on `inmemory`; Phase-2 SurrealDb path must guarantee `Service.Dispose` runs on scene exit.

## Open decisions
- **Q1 — `ITimelineController` ownership:** resident (split the controller from `GlobeView`) vs
  stage/world-owned (move `TimelineComposition` into the timeline bundle). The cross-scope knot.
- **Q2 — Environment handoff:** static field vs `SceneScopeNode` bridge (review Issue 3).
- **Q3 — world-graph demo:** move into the world bundle vs delete + rebuild later.
- **Q4 — `assemblyNames` schema** shape for multi-assembly bundles.

## Template generalization
**Camera** (not composed in `Host.cs` — likely the easiest), **NodeGraph**, **Timeline** each follow the
same `WorldPlugin` / `RegisterOwned` / own-bundle-under-stage template; **Environment** is the shared
mount scene. Do them after World validates the template.

## Prerequisites
- **Dirty tree:** the repo has uncommitted changes (`Host.cs`, several `.csproj`, World gen-graph files).
  This refactor edits `Host.cs` (already modified) — commit/stash to a clean, scoped base first
  (per the commit-often rule) before executing.
- Phase 2 is `doubt-driven-development`-gated and verified only by the windowed `old ALC collected` gate.
