# Session record — Plan 4 (app regimes + hydration-derived onset + timeline) MERGED + windowed-verified

> **Date:** 2026-06-22 · **Repos:** `fantasim-app-godot` (Plan 4) + `fantasim-world` (sub-plan 4.0) · **Result:**
> emergent plate tectonics now render in the app — **plates are born at the plate-onset tick** (not Genesis),
> derived from an atmosphere hydration curve, scrubable on an AnimationPlayer timeline.
> App `main` @ **`03bd394`** (119/119 app tests green) · engine `main` @ **`4c1c69c`** (sub-plan 4.0 merged).

## TL;DR — the arc
This session took the design from "Plan 4 is one bullet in the umbrella spec" to a **verified, merged, cross-repo
feature**. Brainstorming surfaced that the user wanted the *ambitious* cut (atmosphere coupling IN, a new
composition plugin, a timeline face), so the work **decomposed into two plans**: an engine **sub-plan 4.0**
(curated-port the atmosphere genesis library) and the app **Plan 4** (regimes + onset wiring + timeline).
Both were executed via subagent-driven development, then **verified in the EXPORTED WINDOWED app** — which caught
four runtime bugs the code reviews could not. The windowed scrub now shows magma/lid → **plate caps appear at onset**.

## What shipped

### Sub-plan 4.0 — engine (`fantasim-world` `main`, merged `f1b2e09..4c1c69c`)
Curated bulk-state-only port from `ref-projects/fantasim-world` (2 NEW projects, **no `World.Shared` dep** —
deliberately dropped the climate seams + generated params):
- `contracts/Atmosphere` (`GiantCroissant.FantaSim.Atmosphere.Contracts`) — `AtmosphereState`, `IAtmosphereStateSolver`.
- `plugins/Atmosphere.Genesis.Core` (`…Atmosphere.Genesis.Core`) — `AtmosphereForcing`, `PrimordialAtmosphereSolver`
  (deterministic hydration curve; threshold `0.99` → default onset `1e8` ticks).
- Tests (determinism + onset-threshold curve); added to `build.config.json` pack lists; packed to the lunar-horse feed.

### Plan 4 — app (`fantasim-app-godot` `main`, merged `832720b..03bd394`)
Additive + **one new approved plugin** `App.World.Composition`:
- **Task 1** — engine-pin reconcile: projectref-for-dev to the post-Plan-1–3 surface (`…Topology.Generation`,
  `Asthenosphere.Convection`, materializer) + the new atmosphere projects; branch default `UseProjectReferences=true`.
- **Task 2** — `App.World.Composition`: regime DTOs (`SphereRegime`/`SphereRegimeSchedule.RegimeAt`), 3 geosphere
  layers (magma/lid/plate) + synthetic crust, 2 atmosphere layers, catalogs, `SphereRegimeScheduleDefaults`
  (`GeosphereFor`/`AtmosphereFor`/`PlateOnsetTickFor`). **Right-sized**: body-formation, geology-tagging, and
  JSON/manifest loaders deliberately deferred.
- **Task 3 / 3b** — onset wiring: `OnsetRoster` calls `LidFractureAtOnset.Fracture` and folds via the materializer;
  `GlobeReconstructor.FromOnsetRoster` + `BuildGlobeAt(tick)` gate the roster (empty before onset, N after);
  `RunCrustEvolution` short-circuits pre-onset.
- **Task 4** — `RegimeTimelineTransport`: `AnimationPlayer`/`AnimationTree` state machine (idle/playing/scrub) drives
  `SetTick` + `SetRegime` across the regime sections; `GlobeView.SetRegime` toggles cap visibility + color-by.
- **Task 5** — windowed verify (below).

### Decisions locked this session
- **Atmosphere is engine-side** — curated-ported into the live engine (like Plan 2's Asthenosphere), packed to the
  feed; app consumes it. (The 0.3.10 ref packages were absent from this workspace; the source was in
  `ref-projects/fantasim-world`.)
- **Onset is causal** — derived from the hydration curve via `PrimordialAtmosphereSolver`, not a hardcoded constant.
- **Timeline face = focused AnimationPlayer transport** (the locked vocabulary), not the ref's full 3D clip-tunnel.
- **Engine consumption = projectref-for-dev**; packaging the full engine surface + package-pin bump = release-gate follow-up.

## ⚠️ Durable finding — the windowed-verify earned its keep
The exported run caught **four runtime bugs the per-task code reviews passed** (reviews can't run Godot):
1. `ComposeCellElevation` called the guarded legacy `BuildGlobe()` on an onset-aware reconstructor → *"Cell elevation
   model failed"*. Fixed: route through `BuildGlobeAt` (`164caaf`).
2. `AnimationMixer` errors — scale tracks targeted a non-`Node3D`. Fixed: trackless placeholder animations (`164caaf`).
3. Scrubber `MaxValue` didn't cover the full transport range → the scrub couldn't reach onset. Fixed (`962dd51`).
4. Teardown race — `GlobeView.UpdateCaps` touched a disposed `ArchSystemRunner` during `CoordinatedShutdown`. Fixed:
   `_ExitTree` stops the transport + `GlobeView` swallows the dispose (`03bd394`).
**Lesson (recorded):** "verify in the exported windowed app" is not ceremony — it is the only gate that exercises the
Godot seam + teardown. Read the app's own rich `[Host]`/`[GlobeView]` logs (they report `plates`, `onset`, cap counts).

## ✅ Verification
- **Engine 4.0:** green, merged. **App Plan 4:** 119/119 app tests across 11 projects (Composition 8, World 40, Ecs 32…).
- **Windowed (exported `complete-app.app`, autoplay loop sampled):** pre-onset frames = plain dark lid sphere (no caps);
  **post-onset frames = the blue 6-plate-cap globe** (log: `globe mounted (… 6 plates … onset=100,000,000)`); wrap
  returns to the lid. The `magma/lid → onset → mobile-plate` story renders. (The `ka→kb` label is the odometer ladder
  rolling over at onset, not a bug.)

## Plan-5 follow-ups (deferred polish — NOT bugs in the core)
- **Boundary-type routing** → real terrain + boundary lines (today the surface is uniform `[-500,-500]` because
  placeholder poles classify every boundary Inactive; this is the single biggest visual upgrade).
- **Thermal magma glow** for the magma-ocean regime (pre-onset sphere is plain dark).
- **Autoplay tuning** — magma-ocean is ~0.2 s at 5M ticks/s (geologically 1% of pre-onset); consider non-uniform speed
  or pause-on-regime.
- **Odometer label** cosmetics (ka→kb rollover reads oddly mid-scrub).
- **Field-system dedupe** — the ported `FieldComposer`/`FieldValueResolver` parallels the engine `World.Fields`.
- **Release path** — pack the full engine surface + bump the app's package-mode pins (currently projectref-for-dev).

## NEXT
**Plan 5 (render polish)** is the natural follow-up — boundary-type routing first (turns the flat caps into real
continents + boundary lines), then magma glow. The emergent-tectonics spine (Plans 1–4) is done and on `main` in both repos.

## Pointers
- App plan: [`vault/plans/2026-06-22-app-regimes-onset-and-timeline.md`](../plans/2026-06-22-app-regimes-onset-and-timeline.md)
- Engine sub-plan 4.0: `fantasim-world/vault/plans/2026-06-22-engine-atmosphere-genesis-port.md`
- Umbrella spec (4-plan ladder + decomposition): `fantasim-world/vault/architecture/event-sourced-plate-topology-port.md` §8
- SDD ledger (per-task commits + the 4 windowed-verify findings): `.git/sdd/progress.md`
