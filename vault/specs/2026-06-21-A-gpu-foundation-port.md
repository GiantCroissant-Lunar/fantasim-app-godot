# Sub-project A — GPU Foundation (port + adjust)

> **AUDIT (2026-07-06, code-verified):** SHIPPED — App.GpuCompute/App.GpuShader + seams exist and are host-composed; context sections narrate the dead GlobeView pipeline. _(See the authority index in `vault/README.md`.)_


> **Date:** 2026-06-21 · **Status:** spec (pre-implementation) · **Part of:** Phase-3 relief decomposition (A→F).
> **Goal in one line:** bring compute-shader + GPU node-graph capability into the working app by **porting and adjusting** `ref-projects`' `App.GpuCompute` + `App.GpuShader` (+ seams), so sub-projects **C** (relief render) and **E** (VisualShader bridge) have their enabler.

## Why
The relief render (C) needs a **compute shader** (radial displacement + normals) and a **shader graph** (the lit, biome-coloured material). Both are *greenfield in this app* but *mature in `ref-projects`*. We reuse, not reinvent (workspace rule 2). This sub-project delivers **only the capability**, verified by a smoke dispatch — no relief yet.

## Non-goals (explicitly later sub-projects)
- B: ECS cell model · C: relief render · D: adaptive LOD · E: VisualShader `.tres` parser · F: seeded noise detail.
- Do **not** touch `GlobeView`/`GlobeReconstructor` beyond what wiring requires.

## What to port (source → target)
All under `…/fantasim-app-godot/project/`. Source = `ref-projects/`, target = `yokan-projects/` (the working app). **Port + adjust, do not copy verbatim.**

| Source (ref-projects) | Tier | Target |
|---|---|---|
| `contracts/App.GpuCompute` | T3 (contracts) | `contracts/App.GpuCompute` |
| `plugins/App.GpuCompute` (`Services/Service`, dispatch types) | T3 | `plugins/App.GpuCompute` |
| `plugins/App.GpuCompute.Seam` (`GodotComputeBackend`: `RenderingDevice`, `RDShaderFile`, storage buffers, dispatch/submit/sync/readback) | T4 (Godot) | `plugins/App.GpuCompute.Seam` |
| `contracts/App.GpuShader` | T3 | `contracts/App.GpuShader` |
| `plugins/App.GpuShader` (node-graph for GPU pipelines: `shader.compute`, `buffer.storage`, `dispatch.groups`, `gpu.dispatch`, validation) | T3 | `plugins/App.GpuShader` |
| `plugins/App.GpuShader.Seam` (`ShaderGraphBackend`) | T4 (Godot) | `plugins/App.GpuShader.Seam` |
| `tests/App.GpuCompute.Tests`, `tests/App.GpuShader.Tests` | tests | same |
| `bundles/gpu-demo/shaders/compute_double.glsl` (+`.import`), `tint.gdshader`, `manifest.json` | resource | a bundle/resource the app can load for the smoke test |

## Adjustments required (the "adjust")
1. **Tiers:** each csproj declares `<ServiceArchiTier>T3</ServiceArchiTier>` (services/contracts) or `T4` (seams) + the `CompilerVisibleProperty` line, matching `App.NodeGraph`/`App.Ecs`.
2. **CPM:** this app uses Central Package Management (`project/Directory.Packages.props`). Any package the ref plugins reference must be pinned there (add versions; no inline versions in csproj). Watch for engine-package overlap (already at 0.1.5).
3. **Solution:** add all new projects to `project/FantaSim.sln` (mirror how `App.NodeGraph` is added).
4. **Composition:** add a `ComposeGpu(composition)` to `project/hosts/complete-app/Host.cs` that registers the GpuCompute + GpuShader services into the registry (mirror `ComposeWorld`/`ComposeIii` pattern). Call it from `_Ready`.
5. **Node-graph reconciliation — CORRECTED during A.2 (this premise was wrong).** `App.GpuShader` is NOT a GPU executor and does NOT plug into `App.NodeGraph`. Investigation found it is a **shader authoring/validation `IService`** with its OWN DTO vocabulary (`GpuShaderGraphView`/Node/Wire/Edit) that it edits + validates; its only Godot touch is the seam's `InspectShaderAsync` (load a `Shader`, report its mode). The ref has no `App.NodeGraph`. So the three are distinct: `App.NodeGraph` executes **function-pipelines** (iii/crust), `App.GpuShader` **describes shaders** (a graph rendered in GraphEdit, parsed-into by E, materialised by C), `App.GpuCompute` **runs compute**. A.2 composes `App.GpuShader` as a plain registry service that COEXISTS with `App.NodeGraph` (mirrors `ComposeGpu`); it drops the ref's `Ops/` + `App.Remote` dep (the app has neither). The "graph drives the material" wiring belongs to C (relief material) + E (VisualShader bridge), not here.
6. **Shader resources:** `.glsl` compute shaders load via `RDShaderFile` from a `res://` path. Wire the gpu-demo shaders as a loadable resource/bundle so the seam can `ResourceLoader.Load<RDShaderFile>()` them in the exported app (respect the bundle-oriented resource model).
7. **`UseProjectReferences`:** if the ref plugins depend on engine types, gate them the same hybrid way (`USE_PROJECT_REFERENCES`) as `App.World`/`App.Ecs`. Likely GPU plugins are engine-agnostic (pure Godot/DTO) — verify.

## Verification (evidence before "done")
1. **T3 unit tests** (ported `App.GpuCompute.Tests` + `App.GpuShader.Tests`) green under `dotnet test` (engine-agnostic, no Godot).
2. **GPU smoke (windowed Godot):** an env-guarded hook (mirror `FANTASIM_GLOBE_CAPTURE`) dispatches `compute_double.glsl` over a small storage buffer via the ported `App.GpuCompute`, reads back, asserts every element doubled, logs PASS, quits. Proves the real `RenderingDevice` path works in the exported app.
3. **Full app suite** stays green (currently 67); **both consumption modes** build (package default + project-refs).
4. Handover gotcha guard: `grep -c App.World.Seam complete-app.csproj == 1` after wiring.

## Staging (subagent execution)
- **A.1 — Compute path:** contracts/plugin/seam/tests for `App.GpuCompute` + gpu-demo shader + `ComposeGpu` + smoke hook. Verify (1)(2)(3). Checkpoint.
- **A.2 — Shader-graph path:** contracts/plugin/seam/tests for `App.GpuShader` (authoring/validation service), composed as a registry service COEXISTING with `App.NodeGraph` (see corrected adjustment #5). Verify (1)(3) + an inspect smoke (load a real `.gdshader`, assert `mode=spatial`) instead of a graph→GPU smoke (this service has no dispatch path).
- Shared files (`FantaSim.sln`, `Directory.Packages.props`, `Host.cs`) are edited within each stage sequentially (no parallel races).

## Risks / watch-items
- `RenderingServer.CreateLocalRenderingDevice()` availability in the run mode we use (the ref's gpu-demo proves it works windowed; confirm headless if needed).
- CPM version conflicts when adding the ref plugins' deps.
- `App.GpuShader` vs existing `App.NodeGraph` overlap — must compose, not duplicate (adjustment #5).
- `ref-projects` is **read-only** — read as reference, create fresh in `yokan-projects` (never edit ref).
