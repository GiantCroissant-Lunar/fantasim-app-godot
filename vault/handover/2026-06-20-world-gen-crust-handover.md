# Handover — world-gen / crust, next steps (2026-06-20)

Read first: `2026-06-20-world-gen-foundation-crust-session-record.md` (what was built, in detail).
This doc = current state + how to continue.

## TL;DR
A correct, reproducible **plate-motion + evolving-crust** pipeline now exists end-to-end, on a
canonical foundation (scale ladder / time / cosmology) + an extensible JSON-schema field system, all
on the preserved hash-chained truth-stream. It runs **inside the app's node graph** (Phase 1, C#,
tested). **Not done: the Godot visual render (Phase 2).**

## Current state (all green)
| Piece | Repo | Status |
|---|---|---|
| `world-stage` reconstruction kernel (USD/GPlates) | yokan-projects/world-stage | committed `3ca65dd`, 78 tests |
| reconstruction proof + cartography render + crust.json | yokan-projects/world-stage-proof | committed `243f085`/`754a255` |
| cartography projection (curated) | yokan-projects/fantasim-cartography | committed `6128970`, 11 tests |
| deterministic event ids | fantasim-world | committed `1794982`, 16 tests |
| canonical foundation + field system + reconstruction | fantasim-world | committed `f8fc580`, 319-test suite |
| plate topology + evolving crust | fantasim-world | committed `4979cd9` (Topology 15, Crust 24 locally after canonical-time fixes) |
| world-gen→cartography design doc | fantasim-app-godot | committed `6627d8b` |
| **WorldFunctionProvider + crust recipe (Phase 1)** | fantasim-app-godot | **see commit note below** |

**Feed:** app now consumes `GiantCroissant.FantaSim.* @ 0.1.3` for the current crust/world-lib slice;
`GiantCroissant.WorldStage 0.1.0`;
managed `UnifyMaths*`/`UnifyGeometry.*`/`UnifyCell.* @ 1.0.0`.

## NEXT: Phase 2 — Godot visual render (the focusable crust layer)
Goal: render the crust globe in the actual app and watch it evolve. The C# data is ready
(`WorldFunctionProvider.crust.generate` → cells/boundaries/features; `crust.json` is a reference of
the exact shape).
- **Create `App.World.Seam`** (T4, Godot) building an `ArrayMesh` for the geodesic crust sphere:
  cells colored by crust (continental/oceanic), boundaries by type (convergent/divergent/transform),
  features (mountains/trenches/ridges) with **exaggerated relief** (crust is a sliver of the radius —
  ignore true scale). Reconstruct cell positions per tick via `WorldStage Motion.ReconstructPoint`.
- **Focusable layer**: clicking the crust layer focuses it; cells pickable (raycast) → emit a world
  command. Mount via a scene/bundle or a deferred demo in `Host.cs` (simplest first).
- **Open steer for the user:** 3D globe (recommended for a "focusable Geosphere layer") vs an in-app
  2D map first; standalone demo scene vs into the existing stage/scene-flow.
- **Verification is the user's** — Godot render can't be seen in the headless agent env; verify by
  running the windowed/exported app + console logs (the established workflow).

## After Phase 2 (deeper geology / breadth)
- Real continent shapes (continental-fraction is plate-seeded, not shaped) + erosion (cross-layer:
  atmosphere `temperature`/`precipitation` fields → crust erosion via the field system).
- Crust **mass conservation** (divergent gaps → new oceanic crust; convergent overlaps → subduction)
  — the full Lagrangian dynamics; currently accumulation-only.
- **Voronoi** cells (swap `GeodesicSphereTessellation` → `SphericalVoronoiTessellation` via the
  `ITessellation` seam; needs the native Geogram kernel — do the kernel spike on macOS then).
- More spheres (atmosphere/hydrosphere/mythosphere) as field-catalog modules (catalogs already ported).
- **Calibrated time, Phase 2:** `crust.generate` now accepts `durationMegaAnnum`/`durationMa`
  or explicit `canonicalTick`/`targetTick`, defaults to 8 Ma = 800,000 canonical ticks, and reports
  the active tick scale (`100,000 ticks/Ma`). The world-lib crust pipeline snapshot-folds canonical
  ranges, so Ma-scale app runs no longer emit one event per tick. Follow-up review fixed the integrated
  divergent ridge reset, so ridge cells stay young at Ma-scale snapshots. Remaining work is the Godot
  visual layer: scrub/render using the same canonical tick/profile instead of a local display-time artifact.
- **Deep-review finding:** current app `world.tick` was still an ECS delta command and ignored JSON
  `{"tick":...}` payloads; it now accepts `tick`/`canonicalTick` JSON without treating the value as a
  frame delta. It is still not the full ref playback surface: there is no `App.Timeline`, stage presenter,
  tick cache, or reconstructed globe seam in this current app checkout.
- **Deep-review finding:** current `CrustPipeline` classifies topology once from the seed plates and
  Euler rates. Canonical duration changes accumulated crust fields, but it does **not** reconstruct cell
  positions/boundaries at each target tick. Ref-projects has the richer timeline/reconstruction stack;
  restoring or re-integrating that is the next architectural fix for "plates look odd".

## Constraints / decisions to honor (don't relitigate)
- Truth-stream 5-axis stream id + SHA-256 hash chain are authoritative & preserved. Resolution is NOT
  a stream-id axis.
- Properties = JSON-schema fields, not enums; scalar values, schema-driven definitions, reduce
  cross-layer. Add fields without code changes.
- Crust ≠ plate; crust rides plates (Lagrangian); evolving/accumulative features.
- **≥3 plates** for active boundaries (2-plate closed ring → net-zero normal rate → no features).
- Cartography ships **parts, not assembly**; assembly (recipes, node graph, "producing a world") is
  fantasim-app-godot. World↔cartography bridge is read-only.
- ref-projects is **read-only** → curated-port.
- USD = technical substrate; GPlates = plate-motion domain model; OSM = later feature/edit model.
  Science-first, tunable toward fantasy/wuxia.

## Gotchas
- **Feed versioning:** FantaSim packages are now **0.1.3** for this current local-feed slice. The app's
  `Directory.Packages.props` was bumped to 0.1.3 + new pins added (Phase 1b + canonical-time fixes).
  The unify packages
  emit a benign **NU1603** (their source floors UnifyMaths 0.1.x but feed has 1.0.0) — suppressed via
  NoWarn on the touched csprojs; a proper fix is to repack the unify libs against the 1.0.0 line. The
  unify-cell/unify-geometry **source lock files** still pin 0.1.x (a locked restore there would fail
  until regenerated).
- **Concurrent agent in `fantasim-app-godot`** (branch `feat/iii-node-graph`): it owns `.omo/`, some
  `vault/handover/*`, `AGENTS.md`, and the iii node-graph work. **Stay path-scoped**; coordinate on
  `Host.cs` (currently clean, but shared).
- **`build/build.config.json` + `AGENTS.md` in fantasim-world** show as modified/untracked but are
  another session's — leave them; our packing didn't touch them. Our new plugins are NOT yet in
  build.config.json's unify-build pack list (we packed via `dotnet pack` directly).
- App headless build works (Godot.NET.Sdk 4.6.3 builds under `dotnet`), but **Godot rendering must be
  run** to verify.

## Where things live
- Design: `fantasim-world/vault/architecture/{canonical-foundation,crust-geology}.md`,
  `fantasim-app-godot/vault/architecture/world-generation-cartography-flow.md`.
- Kernel: `world-stage/WorldStage/`. Proofs + viz data: `world-stage-proof/` (`crust.json`,
  `reconstruction.json/.map.json`).
- World domain: `fantasim-world/project/plugins/{Geosphere.Plate.Topology,Geosphere.Crust,
  Geosphere.Plate.Reconstruction,Geosphere.Plate.Rotation.Stream,World.Fields.Catalog,
  World.Fields.Stream}` + contracts `{World.Shared,World.Export,Mythosphere,Mythosphere.Cosmology}`.
- App provider: `fantasim-app-godot/project/plugins/App.World/{WorldFunctionProvider.cs,
  Recipes/CrustGenerationGraph.cs}`, registered in `project/hosts/complete-app/Host.cs` (ComposeWorld).
- Ref algorithms to lift later: `ref-projects/fantasim-world/project/plugins/{Geosphere.Discrete.Topology,
  Geosphere.Plate.Velocity.Core,Geosphere.Crust.Classification,Geosphere.Plate.Geology.Classifier}`.

## How to pick up
1. Decide Phase-2 visual form (3D globe vs 2D map) + mount point (demo scene vs scene-flow).
2. Build `App.World.Seam` (ArrayMesh from `crust.generate` output; `crust.json` is the shape ref).
3. Run the app (windowed/exported) to verify the crust layer renders + scrubs over time.
