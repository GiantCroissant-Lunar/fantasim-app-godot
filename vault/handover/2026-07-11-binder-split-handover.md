# 2026-07-11 session handover — PlanetPresentationBinder split (pre-D8b)

**For the next session.** Read order: this doc →
[`../plans/2026-07-11-planet-presentation-binder-split-plan.md`](../plans/2026-07-11-planet-presentation-binder-split-plan.md)
(executed; contains the seam map and the partial-vs-collaborator design call) → prior context
[`2026-07-10-review-and-track-registry-slice1-handover.md`](2026-07-10-review-and-track-registry-slice1-handover.md).

## 1. What landed

| Commit | What |
|---|---|
| `4e13c27` | docs(vault): binder-split plan — 7 seams, partial-first, D8b landing zones |
| `5c9f619` | refactor(presentation): split PlanetPresentationBinder into core + 7 seams |

`PlanetPresentationBinder.cs` 2,636 → 749 lines. New files, all in
`project/plugins/App.Presentation/` (assembly unchanged — world collectible bundle):

- **Real classes:** `PlanetShaderLibrary` (6 GLSL constants + static Shader/Material caches),
  `PlateSurfaceMeshFactory` (pure static builders), `ScrubRefreshCoordinator`
  (**the D8b landing zone** — owns ScrubApplyScheduler + rest-flush CTS; injected
  requestHeavyRefresh / deferToMainThread / nowMs / delayAsync, Godot-free, 4 unit tests),
  `PlanetTimelineController.cs` (verbatim file move — D8b's `RegisterPlayback` onSeek
  widening happens here).
- **Partial-class file splits** (verbatim member moves, zero reference changes):
  `.PlateSurface.cs` (BindPlateSurface + `_last*` bind cache — D8b maps rungs onto its
  `AdaptiveSubdivisionOptions`), `.CutawayExploded.cs`, `.MantleViews.cs`, `.SceneFurniture.cs`.

Implemented by an in-house sonnet subagent (strict TDD, no git ops); lead reviewed, committed,
gated. Suite 1094 → **1102/1102** (lead re-run; +8 new tests). Verbatim-move audit: multiset
line-diff original-vs-new left only the plan's allowed substitutions (57 lines, all accounted).

## 2. Gate evidence (`../specs/evidence/2026-07-11-binder-split-gate/`)

1. **ALC ×2**: `old ALC collected for bundle world` on both hot-reload rounds
   (`task bundle:world && task bundle:install` against the running exported app).
2. **Visual**: World view + atmosphere rim (`gate-world.png`), cutaway wedge + cut faces
   (`gate-cutaway.png`), mantle x-ray on/off (`gate-mantle.png`/`gate-restored.png`),
   magma-ocean regime render — MagmaShader via the moved library (`gate-probe.png`).
3. **Scrub-origin**: discrimination test — one boundary-crossing ScrubPreview → **0 binds at
   +150 ms, exactly 1 at +1.15 s** (rest flush); tight 6-preview+1-commit sweep → exactly
   **1 cold `Crust generation triggered`** at the commit tick. Debounce intact through the
   new coordinator.
4. Suite green; diff scope = App.Presentation + its tests only.

## 3. Decisions this session

- **Partial-first split** (not collaborator extraction) for the Godot-node view clusters —
  zero reference changes in the just-stabilized reload paths; promote a partial to a
  collaborator only when an arc needs it.
- **`BuildCellAppearance` stays in the core file**: the `ContinentProxyBanTests`
  architecture gate hard-codes an exact-path allowlist for the `ProvinceTint` call site.
  Widening an architecture-gate allowlist inside a refactor was rejected; revisit only as a
  deliberate standalone decision.

## 4. Gotchas NEW (G31+; G1–G30 stand)

- **G31 remote scrub-gate fixtures lie twice**: (a) per-command `python3` spawns (~400 ms) and
  even synchronous urllib round-trips can exceed the 300 ms rest window — a "burst" that
  isn't sub-300 ms *end-to-end at the app* makes every preview legitimately rest-flush, which
  looks like a broken debounce; (b) tick scale — this run's odometer is 1 kb = 100 M ticks
  (status label `4 ka` at tick 400,000), so sweeps at 100 k-tick steps scrub microscopic
  early-run ranges and cross nothing. Use the **discrimination test** (single
  boundary-crossing preview; count binds at +150 ms vs +1.15 s) instead of raw sweep counts,
  and vendor exact fixture ticks with any future gate proof.
- **G32 out-of-range seeks pollute the crust snapshot series**: seeks far past the generated
  span materialize snapshots at those ticks (`snapshots=5` grew by every cold seek); later
  sweeps then cross a boundary per step. Windowed scrub gates should use a fresh boot or
  in-span ticks only.
- The exception wave during multi-pck reload (`ObjectDisposedException` ×3 in
  `TimelineFace.ApplyFilmstripPreview`, TimelineFace.cs:1349) is **teardown noise, not a pin**
  — ALCs still collect. Chip spawned (`task_d8945845`) to make the late deferred apply exit
  silently.

## 5. Follow-ups (ranked)

1. **D8b progressive-resolution scrub** — runway now fully clear: rung ladder in
   `ScrubRefreshCoordinator`, rung → `AdaptiveSubdivisionOptions` in `.PlateSurface.cs`
   `BindPlateSurface`, `RegisterPlayback` onSeek widening in `PlanetTimelineController.cs`.
   Spec: D8b directives in
   `../specs/2026-07-07-layer-presentation-input-parity-canonical-units-directives.md` §D8b.
2. Dead-code sweep flagged by the implementer (verify then delete):
   `PlanetShaderLibrary.HypsoPlateMaterial` (static variant, zero call sites),
   `PlateSurfaceMeshFactory.ToColor(RampColor)` + `.ToV3(CartesianPoint3)` (orphans).
3. Cross-partial scale coupling now physically split across files:
   `BuildExplodedSolidCrust` (`.CutawayExploded.cs`) bakes a ×2 house scale that
   `RebuildMantleLayer` (`.MantleViews.cs`) halves when reusing slabs — do not change either
   scale convention without the other (documented in-place, windowed-gate comment 2026-07-08).
4. Test-namespace drift noted by implementer: new test files use
   `FantaSim.App.Presentation.Tests`, 13 pre-existing files use `App.Presentation.Tests` —
   cosmetic; align in a formatter pass if desired.
5. Carried from 07-10 (unchanged): D4.2 unit sweep, polarity flip, TimelineFace split,
   compose-json (locked, build at first consumer), SurrealDB slice, vault README index,
   0.02-sibling decision + Service.cs mixed-frame intent (both still awaiting user).

## 6. State at session end

App main: plan + split commits (see §1) ready to push / pushed per session log. Working tree
clean (AGENT-SUMMARY.md reviewed and deleted after folding into this doc). Exported app
STILL RUNNING (leave it open — bundle-runtime-verification rule): PID 77934, log
`/private/tmp/claude-501/-Users-apprenticegc-Work-lunar-horse/b1b3986a-a1fa-4296-abe6-93539b98478e/scratchpad/app-run7.log`,
ingress :19292, last evidence = the gate above. Snapshot series polluted per G32 — prefer a
fresh export/boot before the next eye-judged look session.
