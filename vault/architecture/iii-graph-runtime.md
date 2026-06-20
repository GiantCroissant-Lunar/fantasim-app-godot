# iii-graph runtime

**Status:** ACTIVE. The iii axis is the complementary orchestration axis to Akka.NET. This doc covers iii as an orchestration fabric + function provider.

> **Graph data placement superseded.** `GraphDocument`/`GraphNode`/`GraphWire`/`GraphExecutor` have been promoted out of iii into a general **`App.NodeGraph`** service (see [node-graph-paradigm.md](node-graph-paradigm.md)). iii is now a **node-function provider** (`comfy.*`, `blender.*`, `asset.*`) over that general graph paradigm, not a graph engine. Read this doc for the iii axis (bridge fabric, bidirectional model, worker roles); read `node-graph-paradigm.md` for the graph paradigm itself.

---

## 1. Purpose: iii as the complementary orchestration axis to Akka.NET

The app has **two peer orchestration axes**, each covering what the other cannot:

| Axis | Covers | Backed by | Status |
|------|--------|-----------|--------|
| **Akka axis** | Internal actor supervision: concurrent stateful entities, ECS worlds, retry/supervision, in-process simulation | Akka.NET `ActorSystem` (resident) | Present, **dormant** (`App.World` / `App.Ecs`) |
| **iii axis** | Orchestration that crosses the process/agent boundary: dataflow DAGs over external capability workers, agent-driven commands, heterogeneous out-of-process pipelines, long-running externally-triggered jobs | Rust gdext bridge + iii-sdk engine + Python capability workers | **Active** (`App.Iii`) |

`App.Command.IService` is the **router** between the two axes — not a seat above either. It exposes `Register(CommandDescriptor, CommandHandler)` + `ExecuteAsync(CommandRequest)`. Each axis plugin registers its own command family; dispatch is by command-id lookup. The per-axis seams (`IWorldOrchestration`, `IIiiOrchestration`) exist for **subsystem identity + health**, not as a competing dispatch path.

This replaces any earlier framing of "iii sits above Akka/ECS as a single orchestration seat." iii does not sit above World/ECS — it sits beside them, covering a different shape of work.

---

## 2. The bidirectional iii axis

The iii axis is **bidirectional** over one fabric (Rust bridge -> iii-sdk engine -> WebSocket -> workers):

| | Outbound (app-driven) | Inbound (agent-driven) |
|---|---|---|
| **Who orchestrates** | The app | External Hermes agent |
| **iii functions flow** | app -> workers (`comfy.generate`, `blender.refine`, ...) | agent -> app (`fantasim.command.execute`, `fantasim.bundle.list`, ...) |
| **Expression** | `GraphExecutor` (dataflow DAG) | tools-role workers exposing `fantasim.*` |
| **Backed by** | `IIiiOrchestration` | `App.Command` / UI capabilities re-exposed as iii functions |

This doc owns the **outbound** story. The inbound/Hermes story (the `project/workers/AGENTS.md` model) is the inbound counterpart and is cross-linked, not folded in — they share the axis, not the mechanism.

---

## 3. Tier mapping (canonical)

| Concern | Tier | Assembly | Project path | Namespace |
|---|---|---|---|---|
| Router (dispatch entry, handler registry) | T1 | `FantaSim.App.Command.Contracts.dll` | `project/contracts/App.Command/Services/IService.cs` | `FantaSim.App.Command` |
| World-axis seam (**dormant**) | T1 | `FantaSim.App.Command.Contracts.dll` | `project/contracts/App.Command/Orchestration/IWorldOrchestration.cs` | `FantaSim.App.Command.Orchestration` |
| **Iii-axis seam (NEW)** | T1 | `FantaSim.App.Command.Contracts.dll` | `project/contracts/App.Command/Orchestration/IIiiOrchestration.cs` | `FantaSim.App.Command.Orchestration` |
| Graph data model + executor + provider interfaces (GENERAL — shared by all axes) | T1 + T3 | `FantaSim.App.NodeGraph.Contracts.dll` / `FantaSim.App.NodeGraph.dll` | `project/contracts/App.NodeGraph/` + `project/plugins/App.NodeGraph/` | `FantaSim.App.NodeGraph` |
| `IiiOrchestrator` (implements `IIiiOrchestration`), `IiiFunctionProvider` (registers `comfy.*`/`blender.*`/`asset.*`), `Recipes/` | T3 | `FantaSim.App.Iii.dll` | `project/plugins/App.Iii/` | `FantaSim.App.Iii` |
| `IiiBridge : Node, IIiiInvoker` | T4 (Node-backed exception) | `FantaSim.App.Iii.Seam.dll` | `project/plugins/App.Iii.Seam/` | `FantaSim.App.Iii.Seam` |
| `IiiGraphViewSource` (a node-graph view over an iii recipe) | T3 (UI view source) | `FantaSim.App.Ui.IiiGraph.dll` | `project/plugins/App.Ui.IiiGraph/` | `FantaSim.App.Ui.IiiGraph` |
| Rust cdylib `IiiClient` | Native (no tier) | gdextension `.dylib/.so/.dll` | `project/native/iii-bridge/` + `.gdextension` in Godot project | n/a (engine-loaded) |

> The graph paradigm (`GraphDocument`, `GraphExecutor`, `INodeFunctionProvider`, `IGraphSource`) lives in `App.NodeGraph` and is shared by every axis. iii contributes an `IiiFunctionProvider` (registers iii capability functions) — it does not own the graph engine. See [node-graph-paradigm.md](node-graph-paradigm.md).

The contract shapes:

```csharp
// NEW — peer to IWorldOrchestration, same assembly, same namespace.
public interface IIiiOrchestration
{
    Task<CommandResult> TriggerAsync(CommandRequest request, CancellationToken ct = default);
    Task<CommandHealth> HealthAsync(CancellationToken ct = default);
    // health reports: bridge up? engine reachable? in-flight jobs?
}
```

`IWorldOrchestration` is **unchanged** — it is honestly named for its axis; it is simply dormant right now.

---

## 4. The Node-backed seam exception (`IiiBridge`)

`IiiBridge` is the one place where the standard "T4 is always a plain class, never a Node" rule breaks, and the break is justified.

**Why it must be a `Godot.Node`:** the gdext `IiiClient` child runs a tokio runtime off-thread and pushes results through an mpsc channel that is drained in its `_Process(float)` loop on the main thread, where it is safe to build `GString` and emit the `response` signal. A plain class has no `_Process`; without the drain loop, results never cross back to the main thread.

**Why it is still T4, not T3:** it touches Godot types (`Node`, `GString`, signals, `CallDeferred`), so by the tier rule it cannot be T3. It is a seam that exposes a pure-C# contract upward.

**The hard constraint:** `IiiBridge` exposes **only** `IIiiInvoker` (pure C#, in `FantaSim.App.Iii.Contracts`) to anything above it. No Godot type ever crosses to T1 or T3. Collectible bundles reference `IIiiInvoker`, never `IiiBridge`.

See `service-tier-architecture.md` §T4 "Node-backed seam (exception)" for the tier-rule treatment.

---

## 5. Command verbs and routing

Dispatch is handler-lookup, not prefix-switching. Each axis plugin self-registers its command family into `IService` at composition time:

| Command verb | Owner axis | Registered by |
|---|---|---|
| `world.generate`, `world.tick`, `world.refresh` | Akka (dormant) | `LocalOrchestrator` via `App.World`/`App.Ecs` |
| `world.orchestrate` | (router built-in) | `App.Command.Services.Service` |
| `iii.ping`, `pipeline.run`, `pipeline.run_text_to_3d`, `graph.*` | iii | `IiiOrchestrator` via `App.Iii` |

`IService.ExecuteAsync` resolves the command id against the registered handlers and routes. The seams are queried for health and subsystem identity, not for dispatch.

---

## 6. Execution model

The general `GraphExecutor` lives in `App.NodeGraph` (see [node-graph-paradigm.md](node-graph-paradigm.md) §5–6). iii's role at execution time is to be the **function provider**: when the executor visits a node with `FunctionId = "comfy.generate"`, it asks the registered `INodeFunctionProvider`s who `Supports("comfy.generate")`; the `IiiFunctionProvider` claims it and invokes the iii engine through `IIiiInvoker`. The executor owns topo-sort + wire-threading + run-context hooks; iii owns the iii capability call.

Because the executor depends only on `INodeFunctionProvider`, and `IiiFunctionProvider` depends only on `IIiiInvoker`, both are fully testable with fakes — no Godot, no Rust, no network.

---

## 7. The Rust gdext bridge

**Source:** `project/native/iii-bridge/` (`iii-bridge` crate, `godot = 0.5`, `iii-sdk`, tokio).

**Threading rule (load-bearing):**
- A process-wide multi-threaded tokio runtime drives `iii.trigger().await`. It **never touches Godot**.
- Results come back through a `std::sync::mpsc::channel` as **plain Rust `String`s** — never `GString`, because `GString` is a Godot type that must not be built off the main thread.
- The channel is drained in `process()` on the **main thread**, where `GString`s are constructed and the `response(id, payload)` signal is emitted.

**ALC invisibility (see `cross-alc-rules.md` §3b):** the cdylib is a native gdextension, engine-loaded via `.gdextension` at Godot startup. It **never enters the managed AssemblyLoadContext graph**. `SharedAssemblyPolicy` governs managed assemblies only, so the cdylib is not listed there. The C# side reaches it only through Godot `Variant` calls and signals (`ClassDB.Instantiate("IiiClient")`, `Call`, signal connection) — never direct native interop, never a managed wrapper type that bundle code references.

**Export:** cdylib + `.gdextension` sit in the Godot project; Godot's exporter includes registered gdextensions.

**Hot-reload:** bundles invoking pipelines hold only `IIiiInvoker` -> bundle reload never touches the bridge/cdylib (clean unload). The cdylib itself **cannot hot-reload** — updating the Rust bridge requires a Godot restart.

---

## 8. Worker roles after the pivot

| Worker | Status | Role |
|---|---|---|
| `pipeline-worker` (Python) | **REPLACED** | Its hard-coded DAG orchestration is now the C# `GraphExecutor` (data, not code) |
| `comfy-worker` (Python) | **RETAINED** | Implements the `comfy.generate` iii capability function |
| `blender-worker` (Python) | **RETAINED** | Implements `blender.refine` |
| `asset.to_gltf` | (function) | Presumably a worker or engine function; called as a graph node |
| `echo-worker` (Python) | **RETAINED** | Test harness (`test.echo`, `ping`) |

The split: the C# executor replaces the **orchestration** worker, not the **capability** workers. Capability workers remain the iii functions the executor invokes.

---

## 9. Recipes

A recipe is a named `GraphDocument` builder. `TextTo3dGraph.Build(prompt)` is the reference recipe: `comfy.generate -> blender.refine -> asset.to_gltf`, wiring `path` and `usd_path` between nodes.

**Authoring location:** recipes that ship with the app live in `App.Iii` (`Recipes/`). Collectible bundles author **their own** recipes against the `FantaSim.App.Iii.Contracts` data model — they construct `GraphDocument`/`GraphNode`/`GraphWire` directly and execute via the resolved `IIiiInvoker` (or via the `pipeline.*` command family). Bundle code never references `App.Iii` T3 internals.

---

## 10. App.World status: present-but-dormant

`App.World`, `App.World.Projection`, and the `world.*` command family remain in the codebase and stay composed in `Host.cs`, but they are **not the active center** of the architecture. The Akka/ECS/field-reduction spine (the `iii-runtime-spine` plan output) is backgrounded infrastructure that can reactivate independently.

Dormancy is a **framing/documentation state, not a runtime state**. There is no feature flag gating `ComposeWorld`; composing dormant services is cheap and keeps the app runnable. If World ever needs to be physically excluded, that is a build/config change (remove the project reference), not an architecture change.

The iii axis does **not** reference `App.World`'s outputs. It composes and runs even if the world axis is physically removed from the build.

---

## 11. Open questions

- **Inbound iii projection as a first-class contract.** Today `fantasim.command.execute` and the `fantasim.*` family are worker-side glue re-exposing `App.Command`/UI capabilities as iii functions. Formalizing this as an app-tier contract (so the inbound direction is typed, not just worker convention) is future work. See the inbound counterpart doc (TBD: `vault/architecture/agent-verification.md`, or elevate `project/workers/AGENTS.md`).
- **Recipe split at 250 LOC.** If the `Recipes/` directory grows past the pure-LOC ceiling, split per-domain (`Recipes/TextTo3d/`, `Recipes/ImageGen/`, ...).
- **`IiiOrchestrator` actor backing.** Currently a plain class with a `ConcurrentDictionary` for in-flight jobs. If retry/supervision/cancellation-propagation needs grow, it may become an Akka actor adapter — but only then. Do not force it through a mailbox prematurely.

---

## References

- `vault/architecture/service-tier-architecture.md` — tier model; the two-axis framing and the Node-backed seam exception
- `vault/architecture/cross-alc-rules.md` — ALC rules; §3b covers native gdextensions
- `vault/architecture/akka-ecs-integration.md` — the Akka axis (dormant)
- `project/native/iii-bridge/src/lib.rs` — the Rust bridge source
- `project/workers/AGENTS.md` — the inbound (agent-driven) iii direction
- `.omo/plans/iii-runtime-spine.md` — the backgrounded Akka/World/ECS plan
