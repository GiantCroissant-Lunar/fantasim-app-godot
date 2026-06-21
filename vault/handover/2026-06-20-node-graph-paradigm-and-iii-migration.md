# Handover — node-graph paradigm promoted, iii migrated to function provider

**Date:** 2026-06-20
**Repo:** `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot`
**Branch:** `feat/iii-node-graph`
**Status:** The node graph is now a general app-level service. iii is a function provider over it (not a graph engine). The two orchestration axes (Akka dormant / iii active) sit behind `App.Command` as router. Full solution builds clean (0 warnings, 0 errors) and all tests pass. This doc supersedes `2026-06-19-iii-graph-pivot-direction-locked.md`, which was written before implementation.

## 1. What was decided

Two architecture decisions, both grounded in the ref-projects (`lunar-horse/ref-projects/fantasim-app-godot`) and the user's intent:

- **iii is a peer orchestration axis to Akka.NET**, not a layer above World/ECS. Akka = internal actor supervision (dormant: World/Ecs). iii = orchestration crossing the process/agent boundary (active: bridge + workers). `App.Command.IService` is the **router** between them via `Register`/`ExecuteAsync` handler-lookup. Verbs: `world.*` -> Akka axis, `pipeline.*`/`iii.*` -> iii axis.
- **Node graph is a general paradigm**, promoted from the ref's domain-bound view pattern. `GraphDocument`/`GraphExecutor` live in a general `App.NodeGraph` service; domains (iii, World future) register as `INodeFunctionProvider`s. This eliminates the MVVM/executor duplication that existed across `App.Ui.NodeGraph` / `App.Ui.ComfyGraph` / `IiiGraphViewSource`.

**Three-layer model:** paradigms (node graph, timeline) × orchestration axes (Akka dormant, iii active) × UI seams (`.Seam` projects). A behavior sits at an intersection — "a node graph whose nodes are iii functions."

## 2. What was implemented

**New projects (all Godot-free except the seam):**

| Project | Tier | Purpose |
|---|---|---|
| `contracts/App.NodeGraph` | T1 | `GraphDocument`/`GraphNode`/`GraphWire` (typed `WireKind` for future VisualScript), `INodeFunctionProvider`, `IGraphSource`, `GraphEdit` |
| `plugins/App.NodeGraph` | T3 | general `GraphExecutor` (Kahn topo-sort + wire-threading + provider resolution), `RunContext` hooks (BeforeRun/BeforeNode/AfterNode/AfterRun), `ReadOnlyGraphSource` |
| `plugins/App.Ui.NodeGraph` | T3 (view) | shared MVVM records (`NodeItem`/`PortItem`/`WireItem`) + generic `NodeGraphViewSource` rendering any `IGraphSource` |
| `plugins/App.Iii` | T3 | `IiiFunctionProvider` (claims comfy/blender/asset over `IIiiInvoker`), `IiiOrchestrator` (IIiiOrchestration), `IIiiInvoker`, `Recipes/TextTo3dGraph` |
| `plugins/App.Iii.Seam` | T4 | `IiiBridge : Godot.Node` (the one Node-backed seam exception; gdext child needs `_Process` to drain the mpsc channel) |
| `tests/App.NodeGraph.Tests` | test | 10 tests: executor topo-sort/wire-thread/sink/cycle/provider-resolution/shared-params/hook-ordering + ReadOnlyGraphSource |

**Contract additions / changes:**
- `contracts/App.Command/Orchestration/IIiiOrchestration.cs` — new peer seam to `IWorldOrchestration`.

**Deletions (the old inline-iii code):**
- `hosts/complete-app/Iii/` (GraphExecutor, IiiGraph, IiiBridge, TextTo3dGraph, IiiGraphViewSource) — replaced by the proper-tier projects above.
- `plugins/App.Command/Orchestration/IiiBridgeOrchestrator.cs` + `OrchestratorFactory.cs` — the deferred stub is replaced by the real `IiiOrchestrator`.
- `App.Command.Tests/LocalOrchestratorTests.cs` — removed `IiiBridgeOrchestratorTests` (tested deleted code).

**Wiring:**
- `Host.cs` gains `ComposeIii` (ordered after `ComposeCommand`): composes one resident `IiiBridge`, registers `IIiiInvoker` + `INodeFunctionProvider` + `IIiiOrchestration`, and self-registers `pipeline.run_text_to_3d` + `iii.ping` into the router. The three env-guarded demos (`FANTASIM_III_PING`/`GRAPH_TEST`/`SHOW_GRAPH`) now route through `App.Command.IClient` dispatch instead of inline bridge construction.
- `complete-app.csproj` references the four new projects; `FantaSim.sln` registers all five.

## 3. Verification

```bash
dotnet build project/FantaSim.sln      # 0 warnings, 0 errors
dotnet test project/FantaSim.sln       # all pass (10 new + existing; 0 failures)
```

Test results: `App.NodeGraph.Tests` 10, `App.Command.Tests` 9, `App.Ecs.Tests` 25, `App.World.Projection.Tests` 4, `App.Resource/SceneFlow/Ui.Tests` 1 each.

The Godot desktop export (`task build:godot:desktop`) and bundle export (`task bundle:stage`) were not re-run this session; they should be re-verified before claiming the exported app boots the iii axis. The build is green; the runtime composition (`ComposeIii` + the routed demos) is not yet smoke-tested in the windowed app.

## 4. Working tree notes

Changes from this session are uncommitted. **Pre-existing uncommitted work is also present and is NOT from this session:**
- `App.Ecs` changes (`EcsSupervisorActor`, `EcsWorldActor`, `EcsService`, `EcsServiceTests`, field-reduction tests) — the split-brain World-axis test work noted at session start. Commit or stash independently.
- `Directory.Packages.props` modification — pre-existing.
- `.omo/run-continuation/*.json`, `.omo/boulder.json` — agent-internal state.

This session's changes can be committed as one or two atomic commits: (a) the node-graph paradigm + tests, (b) the iii migration (App.Iii + App.Iii.Seam + IIiiOrchestration + Host rewire + deletions).

## 5. Deferred — World as a function provider

The original migration plan included exposing World's generation graph as an `IGraphSource` and registering World as an `INodeFunctionProvider` with `RunContext` hooks for truth/cache invariants. **This is deferred** because:
- World is dormant by the user's decision ("we are not going for App.World yet").
- This repo's `App.World` has no generation graph — only the field/truth-stream surface (`GetOverviewAsync`, `RunGenerationAsync`, etc.). The graph-authoring `WorldGenerationGraph`/`RunGenerationGraphAsync` lives in **ref-projects**, a different codebase.

The `RunContext` hooks already exist in `App.NodeGraph` for when World reactivates with a graph. Porting the ref-projects generation graph is its own slice, tied to World reactivation.

## 6. Next steps

1. **Smoke-test the exported app**: run `task build:godot:desktop`, then launch with `FANTASIM_III_PING=1` / `FANTASIM_GRAPH_TEST=1` / `FANTASIM_SHOW_GRAPH=1` to verify the iii axis composes and the routed commands work end-to-end through the bridge.
2. **Commit** this session's work in atomic commits (paradigm; iii migration).
3. **World reactivation** (future, separate slice): port the generation graph, expose as `IGraphSource`, register World as a provider with truth/cache `RunContext` hooks.
4. **App.Ui.Timeline** (future): the view-source layer over `App.Timeline.ITimelineSource`, parallel to `App.Ui.NodeGraph`. Lower priority; timeline is already a general service.
5. **VisualScript** (future, out of scope): a graph of general programming constructs. `App.NodeGraph` is designed to not preclude it (typed `WireKind`, room for variable/event nodes).

## 7. References

- `vault/architecture/node-graph-paradigm.md` — the canonical paradigm doc
- `vault/architecture/iii-graph-runtime.md` — iii as orchestration axis + function provider
- `vault/architecture/service-tier-architecture.md` — three-layer model + tier rules
- `vault/architecture/cross-alc-rules.md` — §3b native gdextensions + ALC rules
- `lunar-horse/ref-projects/fantasim-app-godot/project/contracts/App.Timeline/` — the general-service precedent (ITimelineSource) this follows
- Supermemory entries: node-graph generalization, three-layer model, function-provider pattern, bidirectional iii axis, World dormancy, seam discipline
