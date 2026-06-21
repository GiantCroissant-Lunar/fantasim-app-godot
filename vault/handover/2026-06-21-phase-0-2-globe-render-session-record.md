# Session Record — Globe Render: Phase 0 → 2 (heartbeat, boundaries, features)

> **Date:** 2026-06-21 · **Repo:** `fantasim-app-godot` · **Result:** Phases 0–2 + the consumption gate + a scrubber-unit fix, all merged to `main @ a221573`. Package mode (engine 0.1.4), **66 tests green**, working tree clean, Godot **4.7**.

## TL;DR — the engine is now visible

The Godot app renders a geodesic globe whose **plates move** over canonical time, whose **boundaries reclassify** per tick, and on which **crust features accumulate** — mountains grow at continental collisions, trenches + volcanic arcs at subduction, ridges at spreading. A bottom time-scrubber drives a canonical tick (OdometerLadder display, never Ma).

**The recurring pattern:** the engine (`fantasim-world`) already had every per-tick capability, tested — `CellReconstructor.ReconstructCellCenters`, `PlateTopologyBuilder.ClassifyBoundariesAt`, `CrustPipeline.RunAsync` + `CrustFeatures.Derive`. So each phase was **app-side rendering**, not engine work. Don't rebuild engine capabilities — check first.

## What shipped (commit trail on `main`)

| Commit | What |
|---|---|
| `2b33f4e` | (pre-session WIP) canonical-tick-aware WorldFunctionProvider; pins → 0.1.3 |
| `75cfd64` | Consume engine `main` via **project refs** + reconcile spin rad/Ma→rad/tick |
| `aca2a0e` | **T3 `GlobeReconstructor`** — seeded plate-globe snapshot (Godot-free) |
| `be39af6` | **T4 `App.World.Seam` `GlobeView`** — GPU-shaded geodesic globe (Phase 0) |
| `fd8a005` | **T0.5** scrubber + mantle — plates move over canonical time |
| `3b18b11` | Align all Godot projects to **4.7.0** |
| `081c3a2` | **Phase 0 gate** — engine repacked → 0.1.4, app on packages |
| `52d88ad` | **Fix** — scrubber drives ticks + OdometerLadder (drop Ma) |
| `03bb47e` | **Phase 1** — boundaries reclassify per tick + colored |
| `a221573` | **Phase 2** — crust features accumulate on moving cells (mountains/trenches) |

## Architecture (the render stack)

Honors the **4-tier service architecture** (Godot only in T4) + the render constraints (shader-driven, bundle-safe).

- **T3 `App.World/Globe/GlobeReconstructor.cs`** (Godot-free, stateful — holds tessellation + 3-plate seed + topology):
  - `BuildGlobe()` → `WorldGlobeSnapshot` (per-cell base triangle corners + plate id + per-plate Euler axis/rate + `TicksPerMegaAnnum`).
  - `ClassifyCellsAt(tick)` → `byte[]` per-cell boundary type (Phase 1; via `ClassifyBoundariesAt` + cell adjacency).
  - `RunCrustFeatures(snapshotTicks)` → per-snapshot `byte[]` per-cell feature kind (Phase 2; one `CrustPipeline.RunAsync`).
  - `App.World/Globe/CanonicalTimeLabel.cs` → ladder string via `CanonicalDisplayFormatter` (`geosphere.plate.time.v1`).
- **T4 `App.World.Seam/GlobeView.cs`** (the only Godot tier): one `ArrayMesh` (base corners, `UV.x`=plateId, `UV.y`=cell data-texture U). A spatial **vertex shader** rotates each cell by its plate's Euler quaternion from `u_tick`; a per-cell **data texture** (`u_cell_types`, nearest-filtered, updated on scrub) carries the per-tick code; the **fragment** colors by it. A mantle `SphereMesh` fills divergent gaps. Bottom-bar HSlider scrubber.
- **Composition `hosts/complete-app/Host.cs` → `ComposeWorldView`**: builds the model, precomputes ~21 feature snapshots once, passes `snapshot` + `formatTick` + `featuresAt` (nearest-snapshot lookup) to the seam.

**The key idea for Phases 1–3:** per-tick CPU state (classification, features, later elevation) → a per-cell **data texture** the GPU samples, while the vertex shader keeps rotation on the GPU.

## Key learnings / gotchas (read before the next phase)

1. **Consumption is hybrid (locked, user-approved).** `UseProjectReferences` in `project/Directory.Build.props`: `false` (default on `main`) = packages (engine **0.1.4**, rad/tick); `true` (on a branch) = project refs to `fantasim-world` `main` for engine co-dev. See [[fantasim-app-engine-consumption]].
2. **Gate process (proven):** `dotnet pack project/FantaSimWorld.sln -c Release -p:Version=X -o <feed>` packs the FULL closure (the engine's `unify-build PackProjects` config is incomplete — misses Crust/Topology/etc.). Then bump the 12 app pins, flip default false, verify BOTH modes green, merge. Phases with no engine change need NO gate.
3. **A linter/Godot-export sometimes drops the `App.World.Seam` `<ProjectReference>`** from `complete-app.csproj` in the working tree (committed file stays correct) → `git checkout -- project/hosts/complete-app/complete-app.csproj`. Check `grep -c App.World.Seam complete-app.csproj == 1` after merges.
4. **Time display = canonical ticks + OdometerLadder, NEVER "Ma".** (Caught this session — a scrubber was labeled in Ma.) See [[fantasim-godot-render-constraints]] rule 5.
5. **Bash CWD persists between tool calls** — a stray `cd` to a plate-project left later `dotnet test` failing with MSB1009. Always `cd` to the app first.
6. **Verification is windowed** via the env-guarded `FANTASIM_GLOBE_CAPTURE=<png>` (+ `FANTASIM_GLOBE_TICK=<ticks>`) hook in `GlobeView` — renders, captures the viewport, quits; inert in normal runs. (Could be removed before a release.)

## What's next

- **Phase 3 — relief.** Map fields → elevation (continental-fraction base, orogenic uplift, crust-age ocean depth, volcanic cones) and **displace the mesh radially** so mountains rise + trenches sink. Currently features are flat color. `StateByTick` (per-cell `CellCrustState`, e.g. `OrogenicPressure`) is available from `CrustPipeline.RunAsync` — extend `RunCrustFeatures` to also surface magnitude/elevation per cell, push as a second texture channel, displace in the vertex shader.
- **Phase 4 — truth-stream.** Emit plate-rotation + field-contribution events, fold from the hash-chained stream, assert deterministic head hash.

### Deferred (explicit user calls)
- **Richer seed for boundary type-*flips*:** this 3-plate seed's types are stable over the range (engine confirms 0|1 stays convergent), so bands move but don't flip. A multi-moving-plate / off-pole-axis seed would show convergent↔divergent transitions.
- **Cell abstraction:** staying on **icosphere** (`GeodesicSphereTessellation`) through these phases; lift the engine's `CellReconstructor`/`PlateTopologyBuilder` onto `ITessellation<TCoord>` toward **Voronoi plates** later (slice-1 design intent).
- **App-side ECS (flag):** not load-bearing yet — the engine crust pipeline IS the simulation; the app leaned on the **shader** (per the "ECS, shader, and/or compute" constraint). If app-side cell entities/systems in UnifyEcs are wanted, that's a clean next step.
- **SurrealDB (unify-storage):** for truth-stream persistence (Phase 4), replacing `InMemoryTruthEventStore`.

## Pointers
- Plan: `fantasim-world/vault/plans/2026-06-21-motion-spine-and-features.md` (Phases 0–4).
- Render design: `fantasim-app-godot/vault/architecture/rendering-and-lod.md`; tiers: `service-tier-architecture.md`.
- Build/run: `task build` · `task test` (package mode) · `task run` (windowed) · `task build:godot` (export). Engine pack: `dotnet pack FantaSimWorld.sln -p:Version=0.1.5 -o <feed>` at the next gate.
- Memory: [[fantasim-app-engine-consumption]], [[fantasim-godot-render-constraints]], [[fantasim-dev-workflow]], [[world-gen-direction-locked]].
