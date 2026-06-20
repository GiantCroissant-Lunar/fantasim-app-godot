# Node-graph paradigm: a general app service

**Status:** DECIDED (2026-06-19). Node graph is promoted from a domain-bound view (the `ref-projects` pattern) to a general app-level service. This doc is the canonical reference for the graph paradigm, the function-provider pattern, and the three-layer model. It supersedes the graph-data placement in `iii-graph-runtime.md` and extends `service-tier-architecture.md`.

---

## 1. Decision

Node graph becomes a **general service** (`App.NodeGraph`) — shared data model + executor + view — that every domain populates by registering **node functions**. Domains (World, iii, a future VisualScript) no longer own their own graph engines; they provide node handlers keyed by `function_id`.

This replaces the `ref-projects` pattern where each domain reimplemented the graph (World's `WorldGenerationGraph` + `RunGenerationGraphAsync`, iii's `GraphDocument` + `GraphExecutor`, ComfyUI's `ComfyWorkflow`). Timeline was already a general service in the ref; node graph is now promoted to match.

## 2. Why (grounded)

1. **Duplication already exists and is growing.** The MVVM records (`NodeItem`/`PortItem`/`WireItem`) are byte-identical across the ref's `App.Ui.NodeGraph`, the ref's `App.Ui.ComfyGraph`, and the pivot's `IiiGraphViewSource`. The pivot's `GraphExecutor` reimplements the same topological walk the ref's `World.RunGenerationGraphAsync` performs. At N=2 graph consumers the duplication is real; the project intends N=many.
2. **Execution is domain-agnostic.** Topo-sort + wire-thread + invoke-by-function-id is identical across domains; only the handlers differ. Timeline is general for the same reason (domains implement `ITimelineSource`). The ref's asymmetry (Timeline general, NodeGraph domain-bound) was accidental, not principled.
3. **Resolves the iii-vs-graph conflation.** With the graph paradigm general, iii stops being "a graph engine" and becomes a **function provider** (`comfy.*`, `blender.*`) reachable through its bridge fabric. World is another provider (`world.*`, `geosphere.*`). A node graph is a document whose nodes resolve to functions from any provider. Node graph = the *shape*; iii/World = *function sources*.
4. **Matches stated intent.** "Node graph is a primary UI, a lot of behavior can use" only holds if the paradigm is shared.

## 3. The three-layer model

The app is now understood as three orthogonal layers:

| Layer | What it is | Examples |
|---|---|---|
| **Paradigms** | General app-level UI/execution shapes any domain can populate | Node graph (`App.NodeGraph`), Timeline (`App.Timeline`) |
| **Orchestration axes** | Where work actually runs | Akka axis (dormant: World/Ecs), iii axis (active: bridge + workers) |
| **UI seams** | The only place Godot types live | `App.Ui.Seam`, `App.Iii.Seam`, `App.Timeline.Seam` |

A concrete behavior sits at an intersection: "a node graph whose nodes are iii functions" = paradigm (node graph) × axis (iii). "A node graph whose nodes are world-generation ops" = paradigm (node graph) × axis (Akka/World). The paradigm doesn't know the axis; the axis doesn't know the paradigm; they meet at function-registration time.

## 4. Structure

| Concern | Tier | Assembly | Project path | Godot? |
|---|---|---|---|---|
| Graph data model + provider/source interfaces | T1 | `FantaSim.App.NodeGraph.Contracts.dll` | `project/contracts/App.NodeGraph/` | no |
| General executor + editing + run-context hooks | T3 | `FantaSim.App.NodeGraph.dll` | `project/plugins/App.NodeGraph/` | no |
| General nodeGraph view (shared MVVM + surface builder) | T3 (view) | `FantaSim.App.Ui.NodeGraph.dll` | `project/plugins/App.Ui.NodeGraph/` | no |
| Godot rendering (GraphEdit binder/enhancer/layout) | T4 | `FantaSim.App.Ui.Seam.dll` | `project/plugins/App.Ui.Seam/` (exists) | yes |
| World function provider | T3 | `FantaSim.App.World.dll` | `project/plugins/App.World/` | no |
| iii function provider | T3 | `FantaSim.App.Iii.dll` | `project/plugins/App.Iii/` | no |
| iii Godot bridge | T4 | `FantaSim.App.Iii.Seam.dll` | `project/plugins/App.Iii.Seam/` | yes |

**No `App.NodeGraph.Seam`.** Node graph has no Godot concerns of its own — it renders through the resident `App.Ui.Seam` binder. The seam rule (any Godot-touching project is a `.Seam`) is satisfied: `App.NodeGraph` (T1+T3) and `App.Ui.NodeGraph` (T3 view) stay pure.

### Contract shapes (T1)

```csharp
namespace FantaSim.App.NodeGraph;

public sealed record GraphNode(string Id, string FunctionId, JsonObject Params);
public sealed record GraphWire(string FromNode, string FromPort, string ToNode, string ToPort);
public sealed record GraphDocument(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphWire> Wires, string SinkNodeId);

// A domain capability that a node invokes. Returns a JSON object of output fields.
public interface INodeFunctionProvider
{
    bool Supports(string functionId);
    Task<JsonObject> InvokeAsync(string functionId, JsonObject payload, CancellationToken ct = default);
}

// A live, editable graph instance a view binds to (analogous to ITimelineSource).
public interface IGraphSource
{
    string SourceId { get; }
    GraphDocument Document { get; }
    event Action? Changed;
    Task ApplyEditAsync(GraphEdit edit, CancellationToken ct = default);
}
```

### Composition

The executor is registry-driven: at composition, the `App.NodeGraph` T3 collects every registered `INodeFunctionProvider` (World, iii, ...). A graph run resolves each node's `FunctionId` to the provider that `Supports` it and invokes it. The executor owns the topological walk, wire-threading, and sink result; providers own their domain semantics.

## 5. The function-provider pattern

Domains register as `INodeFunctionProvider`s:

- **World** registers `world.*` / `geosphere.*` / `truthstream.*` / `timeline.*` handlers. World's existing generation-graph semantics live *inside its handlers* — truth-stream commits, materialization, cache writes.
- **iii** registers `comfy.*` / `blender.*` / `asset.*` handlers. Each handler calls through `IIiiInvoker` to the bridge.
- A future **VisualScript** provider registers script-op handlers (see §7).

The executor stays domain-agnostic; adding a new graph capability = registering a new provider. No executor changes.

## 6. Run-context hooks (the World transaction mitigation)

World's graph run today is a cohesive transaction with cross-node invariants:
- Truth-stream events commit in visit order.
- Cache invalidation when a source param (e.g. `Seed`) changes (`timeline-node-graph-integration.md` Decision 6).
- `timeline.frame-at` must finish before `GenerationChanged` fires (Decision 7).

A naive executor calling independent handlers would break these. The executor therefore supports a **run context** with hooks domains install:

```csharp
public sealed class RunContext
{
    public Func<GraphDocument, Task>? BeforeRun { get; init; }   // e.g. World: snapshot params for invalidation
    public Func<GraphNode, JsonObject, Task>? BeforeNode { get; init; }
    public Func<GraphNode, JsonObject, JsonObject, Task>? AfterNode { get; init; }  // node, input, output
    public Func<GraphDocument, Task>? AfterRun { get; init; }    // e.g. World: fire GenerationChanged
}
```

World registers `BeforeRun` (detect source-param change → invalidate cache), `AfterNode` (commit truth drafts in order), and `AfterRun` (raise `GenerationChanged`). The executor still owns the topo walk and invokes hooks in deterministic order. This preserves World's invariants without World owning the executor.

## 7. VisualScript — definition and scope

**Definition.** VisualScript is a node graph whose catalog is **general-purpose programming constructs** — control flow (branch/sequence/loop), variables, math, events — making the graph itself a program (Unreal Blueprints, Unity Visual Scripting). The iii `GraphExecutor` is *not* VisualScript: it is a pure dataflow DAG (no control flow, no variables, no events) whose nodes are external capabilities.

**Scope: out of scope for this pivot, but design `App.NodeGraph` to not preclude it.** Concretely:
- Model `GraphWire` as typed (data vs control) from the start.
- Leave room in the `GraphNode` shape for variable/event nodes.
- A future `App.NodeGraph.VisualScript` function-provider (registering script-op handlers + control-flow semantics) slots in without breaking structural dataflow graphs.

Do not build the script-op catalog or control-flow execution now. iii pipelines and World recipes are pure dataflow and do not need it.

## 8. App.Ui.Timeline (future, symmetry)

`App.Ui.Timeline` is the view-source layer over `App.Timeline.ITimelineSource`, parallel to `App.Ui.NodeGraph` over `IGraphSource` — producing a BoomHud `timeline` surface document, with scrubbing/rendering in `App.Timeline.Seam` / `App.Ui.Seam`. Timeline is already a general service, so this is a view split, not a paradigm shift. Lower priority than the node-graph generalization; does not block the iii pivot.

## 9. Seam discipline (reaffirmed)

Every project that references Godot types is a `.Seam` (T4). All other csproj are Godot-free. For node graph specifically:
- `App.NodeGraph` contract + plugin: pure C#.
- `App.Ui.NodeGraph` view plugin: pure C# (`IViewSource` + `RuntimeSurfaceDocument`, like the ref's `App.Ui.NodeGraph`).
- Godot rendering of the graph: `App.Ui.Seam` (resident binder/enhancer/layout — already exists).

No new seam is introduced for node graph.

## 10. Migration path

1. **Foundation:** create `App.NodeGraph` contract + plugin (general executor, run-context hooks); create `App.Ui.NodeGraph` (shared MVVM). Solution builds, no behavior change yet.
2. **iii migration:** move `GraphDocument`/`GraphExecutor` out of `hosts/complete-app/Iii/` into `App.NodeGraph`; create `App.Iii` plugin (function provider over `IIiiInvoker`); add `IIiiOrchestration` seam + `IiiOrchestrator`; rewire `Host.cs` (`ComposeIii`, demos → commands); delete the `IiiBridgeOrchestrator` stub + `OrchestratorFactory` + `Mode`.
3. **World migration (highest risk):** expose World's generation graph as an `IGraphSource` + register World as `INodeFunctionProvider` with run-context hooks for truth/cache invariants. Preserve existing `world.*` semantics inside the handlers/hooks.
4. **Tests + verification:** `dotnet build`, `dotnet test`, `task verify`, `task build:godot:desktop`, exported-app smoke.

## 11. References

- `vault/architecture/iii-graph-runtime.md` — iii as orchestration axis + function provider (revised by this doc)
- `vault/architecture/service-tier-architecture.md` — tier model, extended by the three-layer framing
- `vault/architecture/cross-alc-rules.md` — ALC rules (node-graph contracts are shared-resident via `FantaSim.App.` prefix)
- `lunar-horse/ref-projects/fantasim-app-godot/vault/architecture/timeline-node-graph-integration.md` — the World-graph + Timeline integration invariants this design must preserve
- `lunar-horse/ref-projects/fantasim-app-godot/project/contracts/App.Timeline/` — the general-service precedent (ITimelineSource pattern) this follows
