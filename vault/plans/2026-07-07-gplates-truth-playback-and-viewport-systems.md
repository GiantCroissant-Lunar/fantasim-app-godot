# GPlates truth playback, conditioned convection, and viewport-systems repair

**Status: ACTIVE (2026-07-07). Supersedes nothing — extends the attempt-8 roadmap
(`2026-07-06-attempt8-recovery-roadmap.md`) with a data-first track and repairs.**

## Why (context, one paragraph)

After P2's motion gates passed, the user's verdict stood: "a ball with strips." Frame
analysis confirmed the gap is *structure*, not color: five binary patches on a coarse grid,
one flat fill, sawtooth coasts, and the styled World view disconnected from the moving
Continents view. Reference target: the GPlates rendering of Cao et al. 2024 ("Earth's
tectonic and plate boundary evolution over 1.8 billion years", Geoscience Frontiers 15(6)
101922; data: Zenodo record 13340841) — hundreds of crisp polygons rotated by a `.rot`
model, plus mantle-flow imagery produced by *imposing* those plate motions on a convection
model (Cao et al. 2021, G³, 10.1029/2020GC009244). **Key architectural fact: our plate
truth stream is author-agnostic.** The `.rot` importer writes the same event vocabulary a
future emergent generator would. Building playback + conditioned convection against
imported real data is the same downstream that emergence will feed later.

## Data policy

- Real dataset: Zenodo 13340841 (rotation file + continental polygons GPML + coastlines).
  Dev-time reference and calibration only for now; check license (CC-BY expected,
  attribution required) before shipping any derived asset.
- **Fixture-first:** every packet must be green on small deterministic fixtures (hand-written
  `.rot` / tiny GPML) checked into the repo tests. Real-data download is optional, behind a
  `tools/` script, never required by tests.

## Packets

| # | Name | Repo | Model pool | Scope anchor paths |
|---|---|---|---|---|
| P1 | gplates-import | fantasim-world | zai glm-5.2 | `project/plugins/Geosphere.Plate.Rotation.Stream/**`, `project/plugins/Geosphere.Plate.Reconstruction/**`, new `Geosphere.Plate.Polygon.Import` |
| P2 | convection-conditioned | fantasim-world | zai glm-5.2 | `project/plugins/Geosphere.Asthenosphere.Convection/**` (new `PlateHistoryForcingSource`) |
| P3 | gplates-playback-app | fantasim-app-godot | zai glm-5.2 | `project/plugins/App.World/Crust/**`, `project/plugins/App.Presentation/**` (Continents view / `RefreshContinentsMembership`) |
| P4 | nodegraph-repair | fantasim-app-godot | ollama glm-5.2 | `project/contracts/App.NodeGraph/**`, `project/plugins/App.NodeGraph/**`, editor view (env-gated `FANTASIM_SHOW_WORLD_GRAPH=1`) |
| P5 | timeline-animplayer | fantasim-app-godot | ollama glm-5.2 | timeline bundle sources + `vault/plans/2026-06-23-tscn-timeline.md` reconcile; Play button |
| P6 | camera-phantom | fantasim-app-godot | ollama glm-5.2 | `project/contracts/App.Camera/**`, `project/plugins/App.Camera*/**`, vendored `project/hosts/complete-app/addons/phantom_camera/**` |
| P7 | adaptive-subdiv | fantasim-cartography | first free slot | `project/contracts/Cartography.Globe/Adaptive*`, `project/plugins/Cartography.Globe.Core/AdaptiveGlobeSurfaceBuilder.cs` |

### P1 — imported plate truth pipeline (engine)

Wire the existing, tested `RotParser` (PLATES4, `Geosphere.Plate.Rotation.Stream`) through
`PlateRotationDraft`/payload/codec into the truth stream so a parsed rotation model becomes
tick-addressed per-plate rotations consumable by reconstruction (the 2026-07-02 follow-up
"importer→drafts→stream"). Add a continental-polygon importer: GPML (XML) → spherical
polygons (UnifyGeometry `SphericalPoint`/great-circle ops — NEVER hand-rolled math) → a
point-in-spherical-polygon rasterizer producing per-cell `ContinentalFraction` at a
reference tick plus plate-id assignment (nearest/containing static polygon). Success: unit
tests green on fixtures (a 4-plate hand-written `.rot` + a 2-polygon GPML); a test proves a
polygon rasterized at t0 and rotated via the stream matches GPlates-style expected positions
within cell quantization.

### P2 — plate-history-conditioned convection (engine)

New `IAsthenosphereForcingSource` implementation (`PlateHistoryForcingSource` or similar)
alongside the existing stylized `ConvectionFieldGenerator`: given plate boundary history
(boundary segments typed convergent/divergent + time-since-subduction), produce downwelling
sheets under convergent boundaries (depth grows with subduction duration × convergence rate,
~45° dip), upwelling curtains under divergent ridges, plume seeds in regions distant from
recent downwelling. Deterministic, pure function of (history, tick, position), tick-addressed.
Success: unit tests — a synthetic 2-plate history yields cold anomaly under its trench and
hot under its ridge; determinism test; no changes to the existing generator's behavior.

### P3 — app-side playback + smooth light path (app)

(a) A rotation-source selection seam: `world.options` payload chooses imported-rotation truth
vs generated truth (mirror the `continentalPatches` recipe pattern from
`WorldCrustRunSpec.ReadRecipe`); when imported, `PlateFrameSampler` consumes per-plate Euler
rotations materialized from the imported model (engine package `Geosphere.Plate.Rotation.Stream`
0.1.8 already ships `RotParser`; do NOT block on P1 — parse directly and adapt locally if the
stream wiring isn't packaged yet). (b) **Per-tick fraction sampling in the light path**:
`RefreshContinentsMembership` currently only updates at 5 M-tick crust snapshots → visible
stepping; re-sample fractions per seek tick from cached sampler state without crust
re-materialization. Success: unit tests for the option seam; sampler test proving fraction
field at tick t+1k differs smoothly from t (no 5M plateau); app `task test` green.

### P4 — node graph repair (app)

Symptom: node graph "seems broken" (user report; the 2026-06-23 windowed pass never
happened). Systematic-debugging applies: reproduce headlessly/by test FIRST, find root
cause, then fix. Known context: feature is env-gated (`FANTASIM_SHOW_WORLD_GRAPH=1`); prior
arc fixed 14 review findings (landed `c05470e`); open items were magma glow, ruler guards,
perf (#5), ALC-collection (#6); the graph panel participates in the viewport-overlap defect
(panel + activity ledger crowd the globe — layout contract per
`2026-07-04-globe-surface-next-steps-roadmap.md`). Success: root-cause note in summary; graph
opens/executes without exceptions in headless tests; panel layout no longer overlaps the
globe viewport region (honor the roadmap's layout-contract direction); `task test` green.

### P4b — regime layer-generation nodes (app; user scope clarification 2026-07-07)

P4's gate/layout fix is necessary but not the whole ask: the node graph must also contain
**nodes that generate the layer for each regime**. Current state: only the mobile-plate
regime is graph-driven (`WorldGenerationGraphDefaults` defines
`geosphere.mobile-plate.layers` with `plate_layer`/`crust_layer` subgraph bindings;
`WorldFunctionProvider` handles `world.layer-scope/-source/-normalize`, `crust.generate`);
the magma-ocean and stagnant-lid layers are hard-composed in `App.World.Composition`
(`GeosphereMagmaOceanLayer`, `GeosphereStagnantLidLayer`, schedule in
`SphereRegimeScheduleDefaults`) with no generating nodes. Deliver: per-regime generation
subgraphs/nodes (`geosphere.magma-ocean.layer`, `geosphere.stagnant-lid.layer`, following
the existing graph-id/product-kind conventions) whose node functions **delegate to the same
composition layer builders** (no duplicated generation logic), subgraph bindings selected
by the regime schedule, and product-parity tests (graph-generated layer ≡ composition-
generated layer for fixed seed/tick). Runs in the same worktree on top of P4's changes.

### P5 — timeline / AnimationPlayer (app)

Reconcile `vault/plans/2026-06-23-tscn-timeline.md` (native .tscn AnimationPlayer CT
tick-track, multi-lane stream-addressed, odometer-ladder labels ka/kb) against what actually
exists in the timeline bundle today; implement the gap. Fix **Play**: the Play button has
never been exercised (attempt-8 handover P4 note) — continuous advance through canonical
ticks must drive the same seek path the scrubber uses. Success: reconciliation table in
summary (plan item → exists/missing/divergent); Play advances ticks in a headless-verifiable
way (unit/integration test on the controller, not only in-app); `task test` green.

### P6 — camera via phantom_camera (app)

The addon is already vendored (`project/hosts/complete-app/addons/phantom_camera/`) and an
App.Camera chain exists (contract + plugin + `App.Camera.Seam/CameraRig.cs`). Symptom:
camera "seems broken". Diagnose the wiring (is the seam registered? does CameraRig
instantiate a PhantomCamera3D? is the addon enabled in project.godot?), then deliver: orbit
+ zoom around the globe driven through the App.Camera service (T4-seam rules — Godot types
only in the seam; service/contract stay engine-free). Success: root-cause note; unit tests on
the service; seam compiles into complete-app; document the manual verification recipe for
the lead's windowed pass.

### P7 — adaptive subdivision repair (cartography)

Symptom: adaptive subdivision "seems broken" in the app. The arc was reviewed clean
2026-07-04 (`AdaptiveGlobeSurfaceBuilder`, resample-at-midpoints landed `03aa7b8`), so the
break is likely in consumption or a regression since. Diagnose with tests in
`Cartography.Globe.Core.Tests` first; respect the **S2-indexing-only lock** (no grid-provider
abstraction). Fix within the 5-slice roadmap's boundaries
(`2026-07-04-recursive-feature-aware-adaptive-subdivision.md`,
`2026-07-04-globe-surface-next-steps-roadmap.md`). Success: failing test that reproduces the
defect, then green; summary states root cause and which roadmap slice the fix belongs to.

## Standing locks (all packets)

Unify* math only (no hand-rolled Vec3/Quat/spherical) · continents = `ContinentalFraction`
only (C4) · waterless default look stands · S2 indexing only · no smoke/fake code in
production composition · determinism (no wall-clock/random in domain paths) · no new
repos/packages/top-level projects · engine types stay out of the app seam (C1–C5
architecture tests must stay green).

## Dispatch protocol (this plan's execution)

- Worktrees per packet under `yokan-projects/.worktrees/` (branch `wt/2026-07-07-<packet>`);
  agents do NOT commit or push — leave changes in the working tree and write
  `AGENT-SUMMARY.md` at the worktree root (root cause, files touched, tests added/run,
  blockers). Lead reviews every diff, path-scope-applies to main, runs `task test` and the
  windowed gate before claiming anything works.
- Models: P1–P3 `zai-coding-plan/glm-5.2`; P4–P6 `ollama/glm-5.2:cloud`; P7 first free slot.
  Caps: 3 per pool. Never a Claude/Anthropic model via opencode; never a gemini model via
  the ollama provider.
- Logs under `fantasim-app-godot/.agent/logs/opencode/`; prompts staged under
  `fantasim-app-godot/.agent/run/dispatch/` (gitignored, session-local).

## Wave 2 (dispatched 2026-07-07, all zai glm-5.2; references in
`vault/specs/2026-07-07-mantle-xray-exploded-crust-references.md`)

### P8 — windowed-defect fixes (app)

The 2026-07-07 windowed gate left two precise defects: (a) **per-tick light path inert in the
export** — `PlanetPresentationBinder.RefreshContinentsMembership` early-returns silently
(headless green; 0.0% pixel change across a 0.2 Ma seek in the exported app); suspects: the
`_registry.TryGet<FantaSim.App.World.IService>()` cross-ALC resolution, `_currentDocument`
null in the resident binder, or the `_boundContinentsTick` guard. Every early-return must gain
a permanent debug-level log line as part of the fix. (b) **camera orbit inert** —
`CameraComposition.MountDefaultGlobeCamera` logs "no PhantomCameraHost on viewport 'main'":
the rig registers the pcam via a deferred callable, so the mount's `GetHost` races it; bind
lazily (controls fetch the host on first frames until available) rather than ordering hacks.

### M-A — x-ray mantle view (app)

Per reference 2: sample the engine's `PlateHistoryForcingSource` (package
`Geosphere.Asthenosphere.Convection` 0.1.9, pinned; adapter builds `BoundarySegmentHistory`
from the presentation document's typed `BoundaryArcs` at the tick) on a spherical-shell grid →
CPU marching-cubes isosurfaces at ± anomaly thresholds (cold=blue slabs, warm plumes) →
meshes in the render seam; ghost the crust shell (~20% opacity) with boundary wireframe.
Toggle via a new ingress command mirroring `render.cutaway` (e.g. `render.mantle`).

### M-B — exploded solid crust (app)

Per reference 3: extrude each per-plate cap into a closed solid — top = existing relief
surface, bottom = radial offset by `CellCrustThickness`, side walls along boundary arcs;
exploded mode = per-plate radial translation about the plate centroid, factor via a new
ingress command mirroring `render.cutaway` (e.g. `render.exploded {"factor":0.3}`).
Thickness must be data-true (continental keels vs thin ocean floor).

## Integration order

P1/P2 (engine) → pack 0.1.9 → re-pin app. P3 merges after its option seam is reviewed against
P1's actual stream shape (P3 deliberately does not wait on P1). P4–P7 are independent;
integrate as each passes lead review + `task test`. Final gate for the track: windowed app,
imported 4-plate fixture (or real Cao data if downloaded), Continents view drifting smoothly
per-tick, cutaway showing conditioned convection consistent with the imposed boundaries —
judged by the user's eye per the session-goal contract.

## Wave 3 — canonical world-history adoption (design accepted; revised written review pending)

Authority: `vault/specs/2026-07-13-canonical-world-history-and-dry-crust-design.md`.

### P9a — app imported-history truth adoption

Replace the app's direct raw-text `ImportedRotationProvider` construction with the established
engine path (`RotParser` -> `RotationStreamImporter.ToDrafts` -> actor-mediated truth append ->
`RotationModelMaterializer`) while preserving onset-relative rotation behavior through explicit
parity tests. Rename the misleading `WorldRuntime` seam to `WorldHistoryCoordinator` only while
landing the real import/materialize/query responsibility described by the spec. Do not expand the
top-level truth-event envelope or promote `CrustProductCacheRecord` into truth.

Delete the no-op runtime path: both project-reference and package-reference builds compile and test
the same coordinator/writer/materializer behavior. Add the recoverable prepared -> CAS plate append
-> bound protocol, lead-owned quaternion/time-direction parity oracle, exported-runtime append proof,
and the contract-owned replacement for the anonymous field-descriptor value. Gate: focused TDD,
both build modes, SurrealDB actor serialization, staged bundle symbol/dependency evidence, exported
runtime append/materialization logs, and successful ALC collection.

### P9b — signed dry-crust geometry + visual proof

Independently make crust relief visibly and testably derive from `CellElevations` and signed
`CellFeatures`: mountain/volcanic/ridge positive, trench negative, noise secondary. Gate: focused
directional geometry tests, full app build/tests, fresh exported-app screenshots with dry crust and
runtime feature/displacement diagnostics, and successful ALC collection after reload. The screenshot
also proves independent larger planet zoom and thinner tunnel rings. P9b cannot substitute for P9a,
and P9a cannot substitute for the user-eye P9b gate.

### P10 — checkpoint/branch/manifest foundation

Follow P9a/P9b with the spec's Phase B: content-addressed canonical artifacts, bounded cursor replay and
hash verification, parent-cursor branch ancestry, manifest visibility, and durable local offline
storage. This is a separate falsifiable goal; P9a/P9b must not claim it landed.

### P11 — emergent mantle -> plate -> crust events + causal tunnel

Event-source the generated-world producer chain and make the tunnel read exact world-history causal
cursor edges. Imported histories show plate forcing -> conditioned mantle/crust; generated histories
show accepted mantle -> plate -> crust, with feedback only across later ticks.
