# Session record — Planet evolution arc: plate correctness, world view, doctrine

> **Date:** 2026-07-02 → 2026-07-03 (one long session) · **Repos:** `fantasim-world` (engine),
> `fantasim-app-godot` (app, most work) · **Result:** the planet render went from
> "flat colored ball with cracks" to a doctrine-grounded multi-view system with a composed
> WORLD VIEW — but **the world view still does not match the user's expectation**; that is
> the entry point for the next session (§2).

## 1. TL;DR

~15 opencode dispatches (worktree-isolated, GLM Ollama/Z.AI; Kimi failed once with a
`doom_loop` invalid tool call — prefer GLM), 3 genuine design corrections all caught by the
exported-windowed gate, 2 doctrine documents locked, and an evidence pipeline
(`render.screenshot`) so the user finally SEES verification captures. App main tip:
`WorldViewContentGate` doc-fix commit after `38a7969`; 584 tests green. Engine main tip:
`a71234f` (+ later ALC/MessagePack fix `6b0a27a` in app repo).

## 2. THE OPEN ITEM — world view ≠ user expectation

**The user's words: "the world view still not what I expect, we will address this in next
session."** What we know about the expectation (assembled over many rounds — read ALL of
this before proposing anything):

- **References (the canonical set):** USGS Vigil cross-section (`vault/reference/`, with
  element→coverage map in its README); kenny.wtf world-synth post (~41k noise-jittered
  regions, NOAA ramp, grid invisible); two stylized game planets (swirly oceans/forests —
  but see next point); three textbook cross-sections (subduction/collision mechanics with
  strata thickness).
- **"Waterless worlds are worlds"** (spec §5c, user-locked): the missing quality is TERRAIN
  LEGIBILITY, not water. A Mars-like bare-rock planet must read as a planet.
- **Cross-section is an interaction of the world view** (zoom / x-ray / cutaway mask), not
  a separate mode. Mask first (W3, not built).
- **Current state vs expectation:** magma-ocean world view is a genuine showpiece (molten
  + amber steam rim — user saw the capture). Mobile-plate world view FAILS the read:
  (a) atmosphere rim fresnel too broad → blue additive cast over the whole disk;
  (b) terrain face too dark — the near-black lowland stop dominates; the rust/ochre
  mid-tones that make the Mars read almost never appear (percentile normalization parks
  most cells at the ramp bottom);
  (c) suspected deeper issue: 350 m WorldPeaks noise + tint grain buried the mesh read,
  but the terrain still has no LANDFORM STORY face-on — no recognizable regions,
  silhouettes, or albedo provinces (kenny.wtf gets this from continent shapes; we have
  ~no continents yet — see A4/maturity in §5).
- **Suggested next-session opening:** fix (a)+(b) as quick parameter tunes, capture, then
  have a look-dev conversation ON CAPTURES (render.screenshot makes this cheap) rather
  than in the abstract — the user reasons visually; ComfyUI (100.79.159.89:8188) is
  sanctioned for STYLE reference only, never correctness.

## 3. What landed (chronological, all merged to main, all windowed-verified unless noted)

**Engine (fantasim-world):** GPlates `.rot` importer + PLATES4 format fix (`a71234f`);
source-generated topology JSON codec. Follow-ups recorded in memory: wire importer→drafts→
stream; source-gen sibling codecs.

**App (fantasim-app-godot), in merge order:**
1. Watertight globe via Cartography.Globe (`20f074e`) + regime shaders (`5e928cb`) +
   typed boundary polylines from topology truth (`97cdf19`) + crust auto-generation
   trigger (`e30458c`).
2. Wiring fixes: regime-change presentation refresh (`2ddcfd5`), crust trigger late-arm
   (`8771c42` + presentation-fetch arm `97d4f2b`). ALC MessagePack type-identity fix
   (`6b0a27a`, separate session).
3. Sub-project A (crust surface truth): seams-under-elevation via global vertex envelope
   (`24c9332`); hypsometric tint + typed accents, CellElevations/CellFeatures plumbed —
   they had NEVER reached the document (`698ecd2`); time-varying crust snapshots +
   `CrustSnapshotTicks` contract (`2fc518b`); lead fixes: displacement wired to document
   elevations, exaggeration 0.00012→1e-5, snapshot-crossing refresh (`5c1f8c7`).
4. Plate-view correction (user reframe): complete boundary network — 2 single-sample
   transform boundaries recovered (`dc89231`); layer-focused views + legible boundary
   styling (`56a5d0e`).
5. Frame alignment: caps built at arc tick, motion-preview hack deleted (`1137f67`) —
   EXPOSED that rigid caps can't tile a drifted sphere → **cell reassignment** per tick
   (`9e36306`, the engine's own move→reclassify doctrine; 1.77 ms @5120 cells).
6. P4 boundary-profile topography: 14 world parameters through the node catalog, polarity
   from the crust pipeline's own trench-side decision, LOD freq 3→4 (`f6b9b2f`).
7. Tuning bundle: `render.screenshot` ingress command (`55f6d37`, new App.Render tier);
   bare-crust recolor + band contrast (`2094b65`); scale rules S1/S2 —
   VerticalExaggeration parameter + "vertical x1e-5 units" indicator (`580a858`; honest ×N
   blocked on the S3 world-radius parameter, upgrade path in `VerticalScaleLabel`).
8. World view (W1 `6cbe4fb` + W2 `38a7969`): `GlobeViewMode.World` default; bare-rock ramp
   + WorldPeaks sub-cell noise (Cartography `NoiseRelief` was already app-exposed via
   `GlobePlateSurfaces(noise:)`); warm lighting; honest atmosphere rim
   (`AtmosphereRimStateMapper`: steam 0.85 amber / co2 0.65 cream / coupled 0.50 blue /
   none → NO rim). `WorldViewContentGate` is deliberately regime-agnostic — do NOT
   delegate it to the resolver (mantle eras resolve Inactive; doc comment explains).

## 4. Doctrine locked (read before designing anything)

- `fantasim-world/vault/architecture/terminology-strata-scale-resolution.md` — ONE TERM PER
  CONCEPT (Sphere / Sub-domain / Regime / Layer / Field / **Stratum** / Plate / Track /
  Lane); strata are Fields until the cutaway; **no sphere-costume rendering**; scale rules
  S1 (exaggeration = parameter) / S2 (on-screen indicator) / S3 (odometer ladder retained
  for time + spatial anchor = world-radius parameter, NOT YET BUILT); R-adaptive cell
  subdivision = roadmap (T-junctions, parent/child cell ids, pipeline cost).
- `vault/specs/2026-07-02-planet-evolution-arc-design.md` — the arc spec, amended
  repeatedly; §5b P4 fantasy principle (Earth calibrates DEFAULTS; profile shapes are
  world parameters per truth-stream identity); §5c world view + waterless principle +
  cutaway-as-interaction; §6b binds to the terminology note.
- `vault/reference/README.md` — Vigil element→coverage map (island-arc variant,
  continental-rift variant, hotspot/plume expression, cutaway = backlog).

## 5. Open items and roadmap (priority order as of handover)

1. **World-view look (§2)** — next session's focus. Quick tunes (rim falloff, ramp
   distribution) then look-dev on captures.
2. **W3 cutaway mask** — the cross-section interaction (slab geometry from boundary +
   polarity; strata from thickness fields; S1/S2 rules apply). User's textbook refs.
3. **Sub-project B — native tscn timeline** (2026-06-22 spec + emergence-window zone +
   `CrustSnapshotTicks` cache strip). Where plate motion becomes continuous playback.
4. **Sub-project C — fracture emergence** (presentation-side, AnimationTree crossfade).
5. **A4 world maturity** — continents (ContinentalFraction growth rates / CrustInitRecipe)
   + the undiagnosed "terrain identical at 105M vs 119M" (rates vs pipeline span — the
   crust pipeline DOES accumulate, so suspect rate magnitudes or RunCrustSnapshot span).
6. **Hydrosphere lane** (parked mid-discussion): truth question open — scalar ocean volume
   with derived shorelines (my recommendation) vs per-cell water depth. Only after the
   waterless world reads correctly.
7. Vigil backlog: island-arc + continental-rift profile variants, hotspot/plume surface
   expression (plume centers exist in the convection engine, unrendered).
8. Smaller: boundary-layer auto-select at mobile-plate; per-world palette parameters;
   spatial ladder anchor parameter (unblocks honest ×N); binder shader comments refreshed;
   flaky `CellReassignmentTests` perf budget test (timing-sensitive); NU1603 warnings
   (UnifyGeometry.Kernel wants Numerics 0.1.4).
9. Engine repo follow-ups: rot importer → drafts → stream wiring; codec source-gen.

## 6. Operational knowledge (how to work on this)

- **Run/verify:** `task build` / `task test` (from repo root); export via
  `task build:godot:desktop` — **unify-build extracts to its OWN version dir** (grep the
  log for "Extracted runnable bundle ->", historically `_artifacts/0.1.2/`), while
  `task bundles` writes to the GitVersion dir (e.g. `0.1.48`) — copy PCKs into
  `<app>.app/Contents/MacOS/bundles/` manually (the dir is wiped each export).
- **Launch windowed:** `remote__enabled=true FANTASIM_REMOTE_ENABLED=1 <app-binary>`;
  drive via `python3 tools/fantasim-cmd.py` — `timeline.seek {"tick":N}`,
  `timeline.select_layer {"sphereId":"geosphere","layerId":"geosphere.plate|geosphere.crust"}`,
  `render.screenshot` (returns absolute PNG path; SendUserFile it — the user wants to SEE).
  Mobile-plate ticks: ~100M onset, maxTick 120M. Crust generates on regime entry (armed
  log line) — wait ~10s after first seek.
- **Delegation:** worktrees under `yokan-projects/.worktrees/` (12+ agent branches kept,
  `git worktree list`); prompts staged in `.agent/run/dispatch/` (gitignored); logs in
  `.agent/logs/opencode/`. GLM (ollama + zai) reliable; Kimi hit `doom_loop`. Ownership
  boundaries in prompts are what keep parallel merges clean; expect PlanetPresentationBinder
  to be the merge hotspot every time.
- **The windowed app is the only real gate.** Headless suites were green through every one
  of the failures this session caught (dead trigger, unrendered arcs, sphere gaps,
  frame misalignment, navy costume). Screenshot everything; send the user the files.

## 7. Next session entry point

1. Read §2. Look at the two world-view captures (user has them; re-capture in seconds via
   render.screenshot after relaunching).
2. Fix rim falloff + ramp distribution (parameters, likely no dispatch needed).
3. Re-capture, send, and START THE LOOK CONVERSATION from images, one change at a time.
4. Then W3 cutaway per spec §5c.
