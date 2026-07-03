# Planet evolution arc — design

> status: concept-lock 2026-07-02 · approved in-session (brainstorming dialogue)
> repo: fantasim-app-godot · builds on: `2026-06-22-tscn-timeline-time-advancement-design.md`
> (sub-project B executes that spec; this doc does not restate it)

## 1. Why

Watching the planet evolve is the product. Three gaps break the experience today:

1. **Crust features don't read.** With crust products flowing (`productLayers=1`), elevation
   displaces the mesh but renders as undifferentiated lumps: no ranges along convergent
   boundaries, no volcano forms, no height tinting. Feature types exist in the data
   (`CrustFeatureKind`: Mountain / VolcanicArc / Trench / Ridge / Fault, each with magnitude)
   but have no visual identity. Elevation also re-opens cross-plate seams (thin dark slivers):
   the watertight proof held at flat-zero elevation only.
2. **The timeline is not a timeline.** HSlider + hand-written tick accumulator; the
   AnimationPlayer/AnimationTree transport is a trackless shell (see the 2026-06-22 spec §1).
3. **Regime transitions are hard cuts.** Crossing stagnant-lid → mobile-plate is an instant
   remount + material swap. The lid should visibly fracture into plates.

## 2. Locked decisions (from the design dialogue)

- **One arc, three ordered sub-projects: A → B → C.** Each gets its own plan and windowed
  verification; later ones build on earlier ones.
- **Transition = data-driven emergence, presentation-side.** The fracture animation derives
  from real engine data (boundary network + convection seeds) but is reconstructed in the
  app over a transition window around onset. Engine-side gradual fracture (truth-stream
  crack-propagation events) is a recorded future direction this design must not preclude —
  the emergence renderer consumes "fracture progress" as an input, so a future engine can
  supply it instead of the reconstruction.
- **Crust look = hypsometric + accents.** Elevation drives a height-tinted surface (ocean
  depth → shelf → plains → mountain → snow); typed accents on top (volcano = emissive vent,
  trench = dark groove).
- **AMENDED 2026-07-02 (post-A review): layer focus selects the VIEW.** The focused timeline
  track determines what the globe renders (track = layer doctrine): `geosphere.plate` focused
  → the PLATE view: per-plate identity caps (flat relief) + the COMPLETE typed boundary
  network (closed loops enclosing every plate; convergent/divergent/transform in high-contrast
  styling). `geosphere.crust` focused → the hypsometric terrain view above. The original
  "retire per-plate albedo at mobile-plate" applies only to the crust-focused view — the A2
  implementation removed it globally, which made the plate view read as a hydrosphere costume
  (a sphere that does not exist yet). Follow-up packets P1 (view switching), P2 (complete
  boundary network — fragments today), P3 (boundary-type legibility).
- **Timeline = the 2026-06-22 tscn spec, plus two requirements it lacked:** the emergence
  window drawn as a distinct zone with auto-slow playback through it, and crust snapshots
  pre-cached along the track for fluid scrubbing. All four outcomes are required: smooth
  continuous playback, visible regime sections, transition-zone awareness, scrub
  responsiveness.
- **Time-varying crust.** `CrustPipeline.RunAsync(startTick, endTick, snapshotTicks)` and the
  accumulative `CellCrustState` semantics already support terrain that grows with the
  playhead; the app requests snapshots across the mobile-plate span and presents the
  snapshot at ≤ playhead tick.

## 3. Sub-project A — crust surface truth (this arc's first build)

Three work packets, parallel-safe by ownership:

**A1 — watertight under elevation (correctness).** Cross-plate boundary vertices must sample
IDENTICAL elevations from both plates so displaced shared corners still coincide. Root cause
of the slivers: per-plate elevation sampling diverges at shared cells. Proof: extend the
existing coincidence test to non-zero, realistic elevation fields; assert exact match at
shared boundary vertices. Owner: mesh path (`GlobePlateSurfaces` usage in
`PlanetPresentationBinder`, App.World globe wrapper). Cartography repo stays read-only; API
gaps get reported, not patched cross-repo.

**A2 — hypsometric tint + typed accents (the look).** Per-vertex color from the same
elevation data that displaces the mesh: ramp deep-ocean → shelf → plains → mountain →
snow (colors chosen from the existing regime-material palette family). Typed accents from
`CrustFeature` cells: VolcanicArc → small emissive vent (magnitude → intensity), Trench →
darkened groove band, Ridge → subtle bright seam (the boundary polylines already color by
type; accents must complement, not duplicate them). Mobile-plate regime only; magma/lid
materials unchanged. Owner: material/color path in the binder + gdshader.

**A3 — time-varying snapshots (evolution).** The crust trigger requests snapshots across the
mobile-plate span (reuse the existing 5M-tick window as snapshot spacing); products carry
per-snapshot addresses; the presentation selects the snapshot at ≤ playhead tick and
re-tints/re-displaces on snapshot change (not per tick). Scrubbing within one snapshot
window causes no re-fetch. Owner: `CrustGenerationTrigger` / `WorldPlugin` / `Service`
product flow.

## 4. Sub-project B — native timeline foundation (concept here, plan later)

Execute the 2026-06-22 tscn timeline spec as written (AnimationPlayer master CT value-track =
continuous playhead; AnimationTree transport; multi-lane sphere/layer/regime read-out;
odometer-ladder labels; retire HSlider + boom-hud face), extended with:

- **Emergence-window zone**: the transition window (§5) renders as a distinct marked zone in
  the geosphere lane; playback auto-slows through it (configurable factor, default 4×).
- **Snapshot cache strip**: A3's snapshot set is surfaced along the track so scrub hitches
  are visible as "not yet generated" rather than silent stalls; pre-generation runs in
  playback order.

## 5. Sub-project C — emergence transition (concept here, plan later)

Presentation-side reconstruction over a transition window W around plate onset (default:
W = [onset − 2M ticks, onset + 3M ticks], tunable):

- Before W: stagnant-lid basalt surface (unchanged).
- Inside W: crack polylines grow along the REAL onset boundary network, propagating outward
  from the real convection seed/plume points (growth parameter = normalized playhead
  progress through W); crack glow = magma material bleeding through; near W's end the
  fragments take on plate identity (boundary polylines fade in, hypsometric tint fades in).
- After W: full mobile-plate presentation (A's surface + polylines).
- Mechanism: AnimationTree crossfade states (lid / fracturing / plates) per the locked
  vocabulary "state-machine = regime selector · blend = crossfade"; the fracture-progress
  input is a function of playhead tick — replaceable later by engine truth.

## 5b. P4 — boundary-profile topography (locked 2026-07-03)

The crust/terrain view expresses boundary types TOPOGRAPHICALLY, not (only) by symbology:
convergent = asymmetric trench + arc pair (trench on the subducting side, uplift set back on
the overriding side); divergent = symmetric swell with an axial rift notch, floor deepening
with crust age away from the axis (the engine's crust-age deepening already provides the
age term); transform = subtle narrow band of linear scarps. Implementation: a Godot-free
per-cell field (distance to nearest boundary, boundary type, side/polarity) in App.World;
per-type cross-boundary profile functions shape the elevation the crust view already
displaces and tints; bundled with a presentation LOD (tessellation frequency) bump so the
profiles have cells to live in.

**Fantasy-world principle (locked):** Earth is ONE world. Real-Earth references (USGS
cross-sections, GEBCO/ETOPO relief) calibrate the DEFAULT profile parameters only. The
profile shapes (trench depth, arc height and setback, rift width/depth, swell breadth,
scarp amplitude, symmetry factors) are world PARAMETERS in the control plane — JSON-schema
parameter fields addressed per truth-stream identity (variant, branch) like every other
world property — never constants in code. A different world legitimately has different
tectonic expression. ComfyUI is a later STYLE reference only, never a correctness source.

## 5c. The World View (locked 2026-07-03 — supersedes "which view is default")

**Principle: waterless worlds are worlds.** A world reads as a world through terrain
legibility (elevation-ramp variation, landform silhouettes, grid-hiding detail, lighting,
atmosphere rim) — NOT through water. Water renders only when the world's hydrosphere has
volume (a world parameter); a desert world is a world with an empty hydrosphere, never a
missing one. (References: Mars; kenny.wtf world-synth — believability from ~41k noise-
jittered regions + NOAA-style ramp, water incidental.)

- **GlobeViewMode.World is the DEFAULT view** (no layer focused): the composed product,
  stacking the contributions of every ACTIVE sphere — geosphere terrain (bare-rock ramp:
  dark basalt lowlands → rust/ochre plains → pale highlands; boundary landforms; volcanic
  glow; sub-cell detail noise to bury the cell grid), atmosphere limb glow gated on the
  real atmosphere state, hydrosphere water when it exists (future). Boundary ribbons stay
  OFF here — they are diagnostics.
- Layer diagnostic views (plate identity, crust) are reached by focusing tracks, as built.
- Lighting: warm/neutral key + ambient (the blue-grey ambient was re-costuming bare rock
  as ocean — lighting is part of the no-costume rule).
- **Cross-section is an INTERACTION of the world view, not a mode**: first mechanism = the
  cutaway mask (a clipped spherical wedge revealing strata — crust thickness field,
  lithosphere lid, asthenosphere, slab geometry at convergent boundaries — under the S1/S2
  exaggeration + indicator rules). Zoom-in is camera behavior on top; a flat slice panel
  (textbook style) is a later 2D readout of the same cut. Packets: W1 world view + terrain
  legibility; W2 atmosphere rim; W3 cutaway mask.

### 5c-i. Height lens + relief fabric (locked 2026-07-03, look-dev on captures)

- **Non-linear height lens (amends S1 for the world view).** The world view displaces
  vertices by `sign(h)·|h|^p · scale` (shipping p=0.5, scale=5e-4). Rationale: the truth
  elevation field is ~±1,400 m interiors under 21,000+ m unbounded orogenic extremes — a
  ratio NO linear factor can render (interiors invisible or peaks become spears). S1 still
  holds in spirit: the lens is a declared parameter and the S2 indicator NAMES the profile
  (`vertical h^0.5 x5e-4 units` — VerticalScaleLabel profile overload). **Diagnostic views
  (crust hypsometric) stay strictly LINEAR** — diagnostics must not bend scale. The lens
  relaxes back toward linear when truth-side erosion/orogenic saturation lands (A4).
- **Everywhere-relief fabric (user-locked from references).** An old waterless world is
  rough at every point — impact history, pre-onset orogenies, erosion — none of which the
  crust pipeline simulates yet. The WorldPeaks noise is the DECLARED stand-in for that
  unsimulated history, promoted from grid-hiding garnish to base fabric (freq 8, 6 octaves,
  nominal amplitude 17,000 → ~2,500 m-std relief ≈ 2.5% of radius through the lens), with
  tectonic contrast reserved on top (ranges ≈ 8%, trenches ≈ −5%). Calibration gotcha:
  NoiseRelief's `Amplitude` is a BOUND, not typical magnitude (std ≈ 0.15×A — documented +
  characterization-tested upstream in fantasim-cartography). Known limitation: the fabric
  is sphere-fixed (sampled on base positions), so it does not drift with plates; the
  truth-side replacement (roughness from crust age / impact fields) is the A4-adjacent
  roadmap item that will.

## 6. Verification

Every packet: unit tests for the Godot-free logic (TDD), full suite green, then the exported
windowed app is the gate (per verify-windowed): seams gone under elevation at mobile-plate;
hypsometric + accents visible; terrain visibly different at early vs late mobile-plate ticks;
(B) continuous playback + regime sections; (C) fracture animation through W with no hard cut.

## 6b. Vocabulary and scale rules (binding, 2026-07-03)

This spec follows the doctrine note `fantasim-world/vault/architecture/
terminology-strata-scale-resolution.md`: one term per concept (Sphere / Sub-domain /
Regime / Layer / Field / **Stratum** / Plate / Track / Lane — "layer" ONLY for the
composition atom); strata are Fields until the cutaway view; **no sphere-costume
rendering** (the crust view's low-elevation tint must stop reading as ocean — recolor in
the tuning pass); presentation exaggeration is a declared parameter (S1), indicated
on-screen in odometer-ladder rungs (S2/S3 — the ladder gains a spatial anchor per world);
R-adaptive cell subdivision is the roadmap answer to resolution, replacing global
frequency bumps.

## 7. Non-goals / recorded follow-ups

- Engine-side gradual fracture (truth-stream crack events) — future direction, kept pluggable.
- Boundary-polyline layer auto-select at mobile-plate — small UX decision, folded into B.
- Cell LOD above 1280 — revisit after A lands; polylines already decouple boundary look.
