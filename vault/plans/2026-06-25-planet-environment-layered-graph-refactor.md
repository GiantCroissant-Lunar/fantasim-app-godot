# Plan: Planet in Environment + layered graph runtime

> **AUDIT (2026-07-06, code-verified):** COMPLETED (core: environment.tscn Planet/LayerMounts, PlanetPresentationBinder is the live presenter) — 'DRAFT' header stale; worker-manifest/cache sections unverified. _(See the authority index in `vault/README.md`.)_


**Status:** DRAFT (2026-06-25). This supersedes the stopped GPU smoke attempt. It keeps the
direction aligned with [service-scope-ownership.md](../architecture/service-scope-ownership.md),
[hot-reloadable-ui-runtime-and-scoped-bindings.md](../architecture/hot-reloadable-ui-runtime-and-scoped-bindings.md),
[node-graph-paradigm.md](../architecture/node-graph-paradigm.md), and
[iii-graph-runtime.md](../architecture/iii-graph-runtime.md).

## Non-negotiables

- No smoke/demo/fake runtime code. Follow
  [no-smoke-or-fake-production-code.md](../../.agent/rules/no-smoke-or-fake-production-code.md).
- `Environment` is a plain Stage subscene, not a DI scope.
- `Planet` is a real rendered object under `Environment`, not a demo scene or host preload.
- `World` owns generation data and stage-scoped services; the Environment scene owns no services.
- UI and world presentation should converge on data-driven documents plus binding sessions, not one
  Godot-derived seam per feature surface.
- Real verification must run in the exported windowed app and exercise a real runtime surface.

## Current repo facts

- `project/bundles/stage/scenes/stage_entry.tscn` is currently only:

  ```text
  [node name="StageEntry" type="Node"]
  ```

  There is no `Environment` subscene and no `Planet` mount yet.

- `WorldPlugin` is a pure C# data-bundle entry. Its comment explicitly says globe/view composition
  is dormant until the Environment scene-tree handoff exists.
- `App.World.Seam/GlobeView.cs` still contains the real globe shader inline and is not referenced by
  `complete-app.csproj`, so extracting shader data alone cannot prove visible hot reload.
- `NodeGraphViewSource` already supports comment boundaries and subgraph navigation. It exposes
  `Annotations`, `Subgraphs`, `ADD COMMENT`, `open-subgraph:*`, and round-trips `GraphFrame` bounds.
- `WorldGenerationGraphFamilySource` already models an active graph selected by regime/tick,
  graph-scoped overrides, and subgraph bindings.
- `WorldGenerationNodeCatalog` already has a `Layer Scope` node and projects VPlanet external-tool
  manifests into world node schemas, but ComfyUI, Blender, shader, and compute manifests are not yet
  first-class world authoring providers.
- `IiiFunctionProvider` already routes `comfy.*`, `blender.*`, `asset.*`, and `vplanet.*` through
  `IIiiInvoker`; `test.echo` is only a dev/test harness path.
- `GodotComputeBackend` uses `RDShaderFile` and caches shader/pipeline RIDs by
  `shaderId|shaderPath|version`. It already has `ClearPipelineCache()`, but no reload event calls it.

## Target topology

```text
App.Common (resident)
  Resource, SceneFlow, Command, Activity, UiRuntime, generic render/presentation adapters

  Stage scene/scope (collectible)
    StageEntry
      Environment          plain scene, no DI binding
        PlanetMount
          Planet           real runtime node tree/material/mesh output

    Stage-owned services:
      World, Camera, NodeGraph, Timeline, field projection, planet presenter

    Assist / Timeline child scopes as needed
```

The important split:

- `World` computes and publishes typed products.
- `PlanetPresenter` observes world products and presentation documents.
- `PlanetRenderer` is the resident or stage-owned Godot adapter that mounts/rebinds nodes under
  `Environment/PlanetMount`.
- `Environment` is just the scene-tree address where the render result lives.

## Data model direction

Add a data-first planet presentation document rather than a feature seam per visual:

```text
PlanetPresentationDocument
  planetId
  sourceWorldId
  layers[]
    layerId
    role
    material/channel bindings
    regimes[]
      regimeId
      graphId
      activeRange/tick policy
      output products
```

This mirrors the UI runtime idea:

- presentation data can be JSON, `.tscn`, `.tres`, shader resources, or bundle-relative assets;
- state/action binding stays scope-owned;
- generic render adapters mount/rebind from data;
- changing data should trigger rebind, not require a new feature-specific seam assembly.

## Layer/regime graph model

Use the existing graph family machinery instead of adding a second graph system:

- keep `WorldGenerationGraphFamilyDocument` as the authored graph family;
- add explicit planet/layer/regime metadata that references graph ids;
- use `WorldGenerationGraphFamilySource.ForRegime(...)` for editor navigation;
- use graph-scoped overrides for phase/regime/tick-specific edits;
- use `WorldGenerationProductAddress` for generated product identity.

Each layer/regime graph can mix providers:

- `world.*` nodes from `App.World` and app-side adapters over fantasim-world;
- `vplanet.*`, `comfy.*`, `blender.*`, and `asset.*` nodes through iii workers;
- `shader.*` nodes for high-level/material shader generation;
- `gpu.*` / compute nodes through `App.GpuShader` + `App.GpuCompute`.

Keep fantasim-world lean: nodes that exist only for app authoring, iii, Godot presentation, shader
packing, or external tools belong in `fantasim-app-godot`, not in fantasim-world.

## Worker bundle direction

Real iii workers should become bundle resources or worker descriptors, not host assumptions:

```text
worker manifest
  workerId
  functions[]
  runtime
  entrypoint
  bundle-relative files
  restart policy
```

Reload behavior:

1. bundle reload replaces worker files/descriptors;
2. worker supervisor stops the old worker process;
3. new worker process starts from the reloaded bundle path;
4. function registry updates provider metadata;
5. Activity records worker stop/start and function availability.

The Rust gdextension bridge remains resident and restart-only. Python capability workers are
resource/process reloadable.

## Shader and compute hot reload

Spatial/material shader data:

- load shader resources from bundle-relative paths;
- on resource bundle reload, rebuild or reassign the material under the live Planet node;
- do not rely on a previously instantiated `ShaderMaterial` mutating itself.

Compute shader data:

- `RDShaderFile` can be loaded after PCK replacement, but `GodotComputeBackend` caches RIDs;
- resource reload must call a public compute cache invalidation hook or bump the shader version key;
- verification must dispatch a real compute node and show changed readback or changed visible planet
  output, not a standalone compute smoke command.

Godot references:

- `ProjectSettings.load_resource_pack()` loads a `.pck` into `res://` and replacement files win on
  later loads when `replace_files` is true.
- `ResourceLoader.CacheMode.ReplaceDeep` refreshes cached resources and dependencies where possible,
  but existing node/material/pipeline instances still need explicit rebind or cache invalidation.
- `PackedScene.instantiate()` creates a node tree from the packed resource; freeing/remounting a branch
  is the reliable way to release old scene nodes.

## Implementation phases

### Phase 0 - clean base and guardrails

- Done: stopped GPU smoke attempt reverted.
- Done: repo-local no-smoke/fake production rule added.
- Optional: mirror the rule into the workspace root if this should govern all lunar-horse repos.

### Phase 1 - real Stage scene tree

Files:

- `project/bundles/stage/scenes/stage_entry.tscn`
- new `project/bundles/stage/scenes/environment.tscn`
- `project/bundles/stage/manifest.json`
- `project/hosts/content-app/export_presets.cfg`

Work:

- make `StageEntry` instance or contain `Environment`;
- add `Environment/PlanetMount`;
- keep both nodes scriptless or generic; no DI binding in Environment;
- verify stage bundle reload unmounts/remounts Environment and old ALC collection still passes.

### Phase 2 - Planet presenter handoff

Files:

- `project/contracts/App.World/...` for planet presentation DTOs if needed;
- `project/plugins/App.Stage/Bootstrap.cs` or a stage presenter composition file;
- possibly a new generic presentation/render adapter under existing UI/render infrastructure.

Work:

- resolve world service from the stage scope/kernel;
- create a `PlanetPresenter` binding session that mounts under `Environment/PlanetMount`;
- consume `WorldRenderSnapshot` or typed generation products;
- do not resurrect `App.World.Seam` as a direct host dependency;
- if `GlobeView` is reused temporarily, move it through the Stage/Environment handoff and remove inline
  shader coupling as part of this phase.

### Phase 3 - layered/regime graph product flow

Files:

- `project/contracts/App.World/GenerationGraph/WorldGenerationGraph.cs`
- `project/plugins/App.World/GenerationGraph/*`
- `project/plugins/App.World/WorldFunctionProvider.cs`

Work:

- add layer/regime metadata around existing graph families;
- route layer/regime graph execution to typed products;
- expose products to the Planet presenter through a product catalog/address;
- keep graph editor support on existing `NodeGraphViewSource`.

### Phase 4 - external manifests as provider registry

Files:

- `project/contracts/App.NodeGraph/ExternalTools/ExternalToolManifest.cs`
- `project/plugins/App.World/GenerationGraph/ExternalToolNodeSchemaProjector.cs`
- new manifests for ComfyUI, Blender, shader, compute, and app-local world adapters.

Work:

- generalize from hard-coded `VplanetExternalToolManifest.Build()` to registered manifests;
- project manifests into world node schemas through one projector/visitor path;
- keep provider metadata visible in node graph UI.

### Phase 5 - real worker bundle lifecycle

Files:

- `project/workers/*`
- `project/plugins/App.Iii*`
- bundle manifests / resource extraction visitors.

Work:

- package worker descriptors/files as bundle data;
- add worker supervisor stop/start on reload;
- update iii provider/function registry from descriptors;
- verify with a real worker function, not `test.echo`.

### Phase 6 - shader/compute resource reload

Files:

- `project/plugins/App.GpuShader/*`
- `project/plugins/App.GpuCompute.Seam/GodotComputeBackend.cs`
- real planet shader resources under a world/render bundle path.

Work:

- move real planet shader source out of inline `GlobeView.ShaderCode`;
- rebind material from bundle resource changes;
- expose compute cache invalidation and call it on relevant resource reload;
- verify by changing a real shader/compute-backed planet output in the exported window.

## External-agent packets

Use these only after the lead session stages a clean prompt under `.agent/run/dispatch/`.

- `agy`: review Godot scene/scope correctness for `StageEntry -> Environment -> PlanetMount` and the
  SceneFlow/bundle reload lifecycle.
- `kimi`: implement narrow file edits for Stage scene files and manifest/export-preset packaging once
  the lead locks names and paths.
- `opencode`: review or implement manifest-registry projection for external tools and ensure no
  smoke/fake runtime paths are introduced.

Every packet must cite the no-smoke rule, expected touched paths, and verifier. The lead session owns
diff review and windowed verification.

## Verification gates

- `git status --short` clean before dispatch or implementation.
- Build via the repo's `task`/UnifyBuild flow, not ad hoc output paths.
- Exported windowed app shows:
  - Activity surface;
  - Stage scene with `Environment`;
  - real `Planet` under `PlanetMount`;
  - bundle reload events;
  - worker stop/start or function availability events where relevant;
  - `Hot-reload: old ALC collected` for collectible bundle reloads.
- For resource-only shader/presentation changes, expected result is visible rebind in the same
  exported process without claiming ALC collection if no assembly was unloaded.
