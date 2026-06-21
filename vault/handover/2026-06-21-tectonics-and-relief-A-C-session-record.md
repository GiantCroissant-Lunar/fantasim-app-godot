# Session Record — Tectonics fix + Relief render (A–C)

> **Date:** 2026-06-21 · **Repos:** `fantasim-app-godot` (app) + `fantasim-world` (engine) · **Result:** the globe now does the **full tectonic vocabulary** (convergent/divergent/transform; mountain/trench/arc/ridge/fault) and renders as **3D lit terrain** instead of a flat strip-painted "rubber ball". App `main @ cb204d5` (85 tests green), engine `main @ 522c829` (382 green, packed **0.1.5**), both clean.

## TL;DR — the arc of the session

Started from a user question: *"is the plate doing convergent / divergent / transform? I don't see mountain, trench."* That unfolded into:
1. **Diagnosis** — the globe showed only convergent+divergent (no transform) and no trench. Root causes: (a) a **geometrically degenerate seed** (3 coaxial-+Z plates → only head-on motion, one continent–continent convergent boundary → mountains, never subduction), and (b) the **boundary classifier used an absolute epsilon** (`|normal| < 1e-14`) so any *moving* boundary's discrete normal (~1e-8) always beat it — **transform was unreachable**.
2. **Fixes** — relative classifier in the engine + a 4-plate seed → all three boundary types + all five feature kinds.
3. **Then the user pivoted to the render:** *"this looks like a rubber ball with strips."* We reframed it against the user's own world-gen model (fields→geometry; coarse truth + seeded detail on zoom) and built the **relief-render foundation** (sub-projects A→C of a 6-part decomposition).

## Commit trail

**App `fantasim-app-godot` (main):**
| Commit | What |
|---|---|
| `fe7b9db` | **Ma cleanup** — DTO `TicksPerMegaAnnum`→`TicksPerAnchor`; stop "Ma" leaking past the authoring boundary (tick-native + OdometerLadder) |
| `06c571c` | **Gate** — engine pins 0.1.4→**0.1.5** (relative classifier); 66 green on new engine, old seed |
| `db476ca` | **4-plate full-vocabulary seed** — Mtn/Trench/Arc/Ridge/Fault + conv/div/transform all show |
| `5fb0c6a` | **Spec** — sub-project A (GPU foundation port) |
| `a629da3` | **A.1** — port `App.GpuCompute` (compute-shader capability, real RenderingDevice) |
| `2fc25aa` | **A.2** — port `App.GpuShader` (shader-graph authoring/validation) |
| `c18bc33` | **B** — ECS cell model + fields→elevation derivation |
| `cb204d5` | **C.1** — relief render (compute displacement + lighting + biome coloring) |

**Engine `fantasim-world` (main):**
| Commit | What |
|---|---|
| `522c829` | **Relative boundary classifier** — Transform = `|normal| < |tangential|` (shear-dominated), not absolute `<1e-14`; 382 green. Packed **0.1.5** to the local feed |

## What shipped (by workstream)

### 1. Tectonics: classifier + seed (the original question, fully resolved)
- **Engine (`RigidBoundaryClassifier.ClassifyRates`)**: now compares normal vs tangential **relatively**. Both below `eps` → Inactive; `|normal| < |tangential|` → **Transform**; else normal-sign → Divergent/Convergent. This is the correct reading of the classifier's own "Transform = small normal" spec, and it stops a moving transform from spuriously flipping as plates drift. Updated the per-tick reclassification crust test to its truer (~90 Ma, not ~1 Ma) flip point.
- **App seed (`GlobeReconstructor.DefaultPlates`)**: a **4-plate** arrangement — plate 0 (continental, +Z) collides into still continental plate 1 (**Mountain**) and overrides oceanic plates 2,3 (**Trench + Arc**); plates 2,3 (oceanic, ±Y) spread (**Ridge** at 2|3) with boundary-parallel shear (**Transform/Fault** at 1|2,1|3). Recipe `Continental(0,1)` unchanged.

### 2. Ma cleanup (tick-native discipline)
The app DTO field `TicksPerMegaAnnum` was the conduit leaking "Ma" into the composition root (a misnamed `ka` loop), the scrubber, and comments. Renamed `TicksPerAnchor` (matches `CanonicalTimeLabel`'s `ticksPerAnchor`); scrub range authored in OdometerLadder **anchors**. Engine's `UnitConverter.TicksPerMegaAnnum` stays as the single sanctioned Ma→tick authoring boundary. (The node-graph `WorldFunctionProvider` Ma JSON API was intentionally left.)

### 3. Relief render — the A–F decomposition (A, B, C.1 done)
Reframed against the user's model: **simulation produces fields; renderer turns fields into geometry; no mountain mesh is stored.** Cell count is **adaptive (auto-LOD)**, not a fixed frequency. "Shader graph" = the app's node-graph in GraphEdit (VisualShader editor can't run in the exported app; a `.tres` *parser* bridges them — that's E).

| # | Sub-project | Status | Delivers |
|---|---|---|---|
| **A** | GPU foundation (port `App.GpuCompute` + `App.GpuShader` from ref-projects) | ✅ done | compute dispatch + shader-graph authoring |
| **B** | ECS cell model + elevation-derivation system | ✅ done | per-cell elevation as ECS data |
| **C.1** | relief render (compute displaces mesh+normals; lit; biome/tectonic coloring) | ✅ done | **3D lit terrain** (the visible payoff) |
| **D** | adaptive LOD (`HierarchicalTessellation` subdivision) | ⬜ next | smooth, zoomable, detailed |
| **E** | VisualShader `.tres` → App.NodeGraph/GraphEdit bridge | ⬜ | author looks in-editor, run/edit in exported app |
| **F** | seeded ridged-noise detail (needs a noise lib — none in plate-projects) | ⬜ | infinite-zoom peaks |

## Architecture (the relief stack)

**Data flow:** crust `StateByTick` (engine) → **ECS cell entities** (B: `CellElevationModel` owns an Arch world; `CellElevationSystem.Derive` = continental base + orogenic uplift + volcanic + age-deepening) → `GetElevations()` → **GLSL compute** (A.1 `App.GpuCompute`, `relief_displace.glsl`: radial displace + face normals) → readback rebuilds the `ArrayMesh` → **spatial shader** keeps the motion-spine rotation (rotates normal too) + `DirectionalLight3D` sun + half-Lambert → **fragment** colors by elevation/biome ramp (default) or tectonic features (toggle).

**Three distinct graph roles** (a correction made this session — the spec's adjustment #5 was wrong): `App.NodeGraph` executes **function-pipelines** (iii/crust); `App.GpuShader` **describes shaders** (authoring/validation DTOs + `InspectShaderAsync`; rendered in GraphEdit, parsed-into by E, materialised by C); `App.GpuCompute` **runs compute**. They coexist as registry services; GpuShader is NOT plugged into NodeGraph.

**Tiers honored:** contracts T1, services T3 (Godot-free), seams T4 (the only Godot tier). Ports dropped the ref's `Ops/`+`App.Remote` (app has neither); seams opt out of CPM (Godot SDK manages its graph).

## Key learnings / gotchas (read before D)

1. **Godot capture build path:** `Godot --path` loads `.godot/mono/temp/bin/Debug`, NOT your `dotnet -c Release` output. Run `Godot --build-solutions` (or `dotnet build complete-app.csproj`) before a capture, or you'll render stale code.
2. **Metal compute gotcha:** Godot's macOS Metal backend rejects an unsized-array `.length()` in SPIR-V→MSL (pipeline-create fails). Pass element counts via a small **second storage buffer** (the compute ports do this). Also: GLSL reserved words (e.g. `centroid`) → "Failed parse"; rename.
3. **The ECS model owns its own Arch world** (via `ArchSystemRunner`), NOT the actor "main" heartbeat world — the `App.Ecs.IService` contract exposes no population/registration surface. If you want cells in "main", that needs contract additions (flagged, not done).
4. **Relief is coarse + subtle at freq-3** (1280 cells). Tunable single constants in `GlobeView`: `DisplacementExaggeration = 0.00012f`, the sun, the biome ramp. Dramatic detail is **D (adaptive LOD)** + **F (noise)**, not a bigger fixed frequency.
5. **Consumption is hybrid + gated**: `UseProjectReferences=false` (default, packages 0.1.5) / `true` (project-refs to engine `main`). Both modes verified green this session. The gate: `dotnet pack FantaSimWorld.sln -p:Version=X -o <feed>` packs the FULL closure; bump the 12 app pins together.
6. **Env-guarded hooks** (all inert normally, all self-quit): `FANTASIM_GLOBE_CAPTURE=<png>` (+ `FANTASIM_GLOBE_TICK`, `FANTASIM_GLOBE_COLORMODE`=0 biome/1 tectonic), `FANTASIM_GPU_SMOKE=1` (compute), `FANTASIM_GPUSHADER_SMOKE=1` (shader inspect).
7. **`ref-projects` is READ-ONLY** — read as reference, create fresh in `yokan-projects`. The GPU plugins came from `ref-projects/fantasim-app-godot/project/{plugins,contracts}/App.Gpu*`.

## State at close
- **App `main @ cb204d5`** — 85 tests green, clean, Godot 4.7, engine packages 0.1.5.
- **Engine `main @ 522c829`** — 382 green, clean, packed 0.1.5 to `/Users/apprenticegc/Work/lunar-horse/packages/nuget`.
- The globe renders moving plates with all 3 boundary types, all 5 crust features, displaced into lit 3D terrain with biome + tectonic color views, tick-scrubbable on the OdometerLadder.

## What's next
1. **Tune the look** (quick): push `DisplacementExaggeration`, refine the biome ramp + sun for a dramatic hero shot at the current resolution.
2. **D — adaptive LOD** (the real "looks like a planet" fix): view-dependent `HierarchicalTessellation` subdivision (more cells near camera). Likely the first thing the next session builds. The compute path (A.1) is the scaling backend for it.
3. **E — VisualShader bridge:** parse `.tres` → App.NodeGraph nodes → GraphEdit + drive the material.
4. **F — seeded ridged-noise detail** (needs a noise lib; none in plate-projects — source or add a small Unify lib, structural → ask first).
5. **Phase 4** — truth-stream fold/determinism (SurrealDB via unify-storage).

## Pointers
- Spec: `vault/specs/2026-06-21-A-gpu-foundation-port.md` (A; adjustment #5 corrected in-doc).
- Prior handover: `vault/handover/2026-06-21-phase-0-2-globe-render-session-record.md`.
- Build/run/capture: `task build` · `task test` · `task run` · `Godot --build-solutions` then a capture hook. Engine pack: `dotnet pack FantaSimWorld.sln -p:Version=0.1.6 -o <feed>` at the next gate.
- Memory: [[fantasim-app-engine-consumption]] (carries the A–F decomposition + per-sub-project status), [[fantasim-godot-render-constraints]], [[reuse-unify-not-reinvent]], [[fantasim-dev-workflow]], [[world-gen-direction-locked]].
