# Handover — iii-graph pivot: direction locked, docs + memory written, code refactor pending

**Date:** 2026-06-19
**Repo:** `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot`
**Branch:** `feat/iii-node-graph`
**Status:** The direction drift is resolved. The iii-graph runtime is now the committed direction, framed as a peer orchestration axis to Akka.NET. Architecture docs and Supermemory are updated to match. The code itself still needs to be refactored to match the documented target — that is the next phase.

## 1. What this session resolved

The user asked for a review of code/docs/memory coherence. The review found that the last 4 commits (`299561e`..`998e24f`) introduced a genuine architecture pivot — Rust gdext bridge, Python workers, C# graph executor, BoomHud nodeGraph editor — that was **not documented anywhere** and **contradicted the `iii-runtime-spine` plan's own guardrails** ("no Hermes/Python", "no Rust iii bridge"). The new code also bypassed `App.Command` entirely via env-guarded `Host.cs` demos, creating a second orchestration path alongside the documented `IWorldOrchestration` seam.

The user chose to **commit to the iii-graph pivot** with two refinements:
- App.World is **not the current focus** — it stays composed but dormant.
- iii provides an orchestration capability that **Akka.NET cannot cover** — it is a complementary axis, not a layer above World/ECS.

## 2. The locked design (canonical)

**Two peer orchestration axes behind one command router.**

- **Akka axis** (dormant): internal actor supervision — `App.World` / `App.Ecs`, `IWorldOrchestration`, `world.*` commands.
- **iii axis** (active): orchestration crossing the process/agent boundary — dataflow DAGs, agent-driven commands, external pipelines. New seam `IIiiOrchestration`, new plugin `App.Iii`, new seam `App.Iii.Seam`.

`App.Command.IService` is the **router**, dispatching by command-id lookup. Each axis self-registers its command family.

**Bidirectional iii fabric:** outbound = app drives workers via `GraphExecutor` (the graph-runtime story); inbound = external Hermes agent drives the app via `fantasim.*` iii functions (the `project/workers/AGENTS.md` model). Same fabric, two directions.

Full canonical tier mapping, the Node-backed seam exception, worker roles, and execution model are in [vault/architecture/iii-graph-runtime.md](../architecture/iii-graph-runtime.md).

## 3. What was written this session

**New doc:**
- `vault/architecture/iii-graph-runtime.md` — the canonical iii-axis reference (11 sections).

**Edited docs:**
- `vault/architecture/service-tier-architecture.md` — added "Two orchestration axes" framing, the T4 Node-backed seam exception (`IiiBridge`), `App.Iii` / `App.Iii.Contracts` naming, `ComposeIii` composition pattern, and the `App.Iii` actor-table row.
- `vault/architecture/cross-alc-rules.md` — added §3b (native gdextensions and ALC), `FantaSim.App.Iii.Contracts` to MAY-cross, `IiiBridge` to Must-NOT-cross, and the iii-pipeline bundle hot-reload verification step.

**Supermemory** — 4 new project-scope architecture entries:
- Two-axis + router decision (the authoritative framing).
- Canonical tier mapping for the iii axis (the single source of truth).
- Bidirectional iii axis + worker status after pivot.
- App.World dormancy status.

(Oracle also reconciled a stale single-seat entry during consultation.)

## 4. What is NOT done yet — the code refactor

The docs now describe a target the code does not yet match. The iii code currently lives in `project/hosts/complete-app/Iii/` and is wired as env-guarded demos on `Host.cs`, bypassing `App.Command`. To bring the code in line with the docs:

| Refactor step | Effort |
|---|---|
| Extract `GraphDocument`/`GraphNode`/`GraphWire`/`IIiiInvoker` into new `project/contracts/App.Iii/` (`FantaSim.App.Iii.Contracts`) | 2h |
| Create `project/plugins/App.Iii/` (`FantaSim.App.Iii`); move `GraphExecutor` + recipes; add `IiiOrchestrator` implementing `IIiiOrchestration` | 3h |
| Create `project/plugins/App.Iii.Seam/` (`FantaSim.App.Iii.Seam`); move `IiiBridge : Node` there | 1h |
| Add `IIiiOrchestration` to `project/contracts/App.Command/Orchestration/`; delete `IiiBridgeOrchestrator` stub + `OrchestratorFactory` + `Mode` | 2h |
| Rewire `Host.cs`: add `ComposeIii` (one resident `IiiBridge`, register `IIiiInvoker` + `IIiiOrchestration`, self-register `pipeline.*`/`iii.*`); convert the 3 env-guarded demos into registered commands dispatched via `IClient.ExecuteAsync` | 3h |
| Move `IiiGraphViewSource` under `App.Ui/Views/` | 0.5h |
| Tests: fake-invoker `GraphExecutor` unit tests; `IiiOrchestrator` registration + dispatch test | 2h |

Roughly 1-2 days, no new dependencies, no Rust changes. Build gates: `dotnet build project/FantaSim.sln`, `dotnet test`, `task verify`, `task build:godot:desktop`, plus an exported-app smoke that logs the iii axis composed and a `pipeline.run_text_to_3d` round-trip.

## 5. Current working tree (uncommitted, separate from the pivot)

The uncommitted changes are on `App.Ecs` (`EcsSupervisorActor.cs`, `EcsWorldActor.cs`, field-reduction tests) — that is back on the `iii-runtime-spine` (dormant World) direction, not the iii-graph direction. These are legitimate in-progress test work and should be committed or stashed on their own merit, independent of the pivot.

Untracked: several `.omo/run-continuation/ses_*.json`, `TestResults/` dirs, and this handover doc. The root `AGENTS.md` is still empty (0 lines) — a top-level agent-guidance file is still missing.

## 6. Open questions deferred to the code phase

- **Inbound iii projection as a first-class contract.** `fantasim.command.execute` and the `fantasim.*` family are worker-side glue today. Formalizing the inbound direction as a typed app-tier contract is future work (a separate `agent-verification.md` doc, or elevating `project/workers/AGENTS.md`).
- **`IiiOrchestrator` actor backing.** Plain class for now. Becomes an Akka actor adapter only if retry/supervision/cancellation-propagation needs grow.
- **Recipe split at 250 LOC** if `Recipes/` grows past the pure-LOC ceiling.

## 7. Key files

- New: [vault/architecture/iii-graph-runtime.md](../architecture/iii-graph-runtime.md)
- Edited: [vault/architecture/service-tier-architecture.md](../architecture/service-tier-architecture.md), [vault/architecture/cross-alc-rules.md](../architecture/cross-alc-rules.md)
- Prior handover (superseded for direction, still valid for the restoration history): `vault/handover/2026-06-19-restoration-progress-and-next-steps.md`
- Backgrounded plan: `.omo/plans/iii-runtime-spine.md`
