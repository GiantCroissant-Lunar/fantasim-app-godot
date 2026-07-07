# User directives 2026-07-07 (session start): layer presentation, input parity, crust thickness, canonical units

**Status: DIRECTIVE-LOCK (2026-07-07, user-stated at session start).** Four design corrections,
each grounded against the code as audited the same day. **D1 AMENDS the M-A presentation in
`vault/specs/2026-07-07-mantle-xray-exploded-crust-references.md`** (the volumetric FIELD method
stays locked; the x-ray/ghost PRESENTATION is superseded). Sections marked *proposed translation*
are the lead's design mapping and are open to plan review; the **user intent** lines are not.

---

## D1 — Mantle is a LAYER of the world stack, not an x-ray

**User intent (verbatim spirit):** the mantle part of the geosphere should not be presented as
x-ray. We have a layer-presentation design: when one layer is focused (active), we show the
detail of that layer. Mantle convection is one layer of the world stack — when it is active, we
see it. Following this concept, when mantle is active the crust should be SEPARATED (as the
Sketchfab exploded-plates reference shows): the crust pieces still form a sphere, they just look
de-attached. When plates move (or, the other way around, when mantle convection behaves), we see
the corresponding motion.

**Code grounding (2026-07-07 audit):**
- The layer system already exists and already drives view modes:
  `GlobeViewModeResolver.Resolve` maps the selected timeline layer → `GlobeViewMode`
  (`contracts/App.World/Composition/GlobeViewMode.cs:86-105`); layer ids in
  `contracts/App.World/PresentationLayers.cs`. There is **no mantle layer id**.
- `render.mantle` today is a look-dev command outside the layer system. It HIDES the crust
  surface entirely (`PlanetPresentationBinder.cs:405-406`) and substitutes a 10%-alpha ghost
  shell (`BuildGhostShell` :836-849) — i.e. exactly the x-ray presentation being rejected.
- M-A (mantle interior) and M-B (`render.exploded` plate solids with real thickness + side
  walls, `PlateSolidBuilder.cs`) have **never been composed**, though the references spec
  anticipated it ("the exploded crust parts to reveal this interior").

**Proposed translation:**
- Add a `geosphere.mantle` layer. Selecting it (track button / `timeline.select_layer`) resolves
  to a new view mode (e.g. `MantleInterior`) whose presentation = M-A interior (core sphere +
  four isosurfaces, field method unchanged) **composed with M-B separated crust** at a modest
  explode factor: discrete thick slabs, radially detached, still reading as a sphere. The
  separated slabs — not a ghost shell — are the surface reference frame.
- Eye-gate amendment: criterion 4 of the north-star gate ("ghosted surface reads as reference
  frame") is superseded by "separated crust slabs read as the reference frame; viewer can tell
  where a slab hangs relative to the plate above it". Criteria 1–3, 5, 6 stand.
- `render.mantle`/`render.exploded` remain as agent look-dev knobs, but the user-reachable path
  is layer selection.
- Motion correspondence (plates ↔ convection actually coupled) remains M-C, its own gated slice;
  what D1 requires NOW is that scrubbing time with the mantle layer active shows both the slabs
  moving (plate motion) and the interior evolving (field conditioning) in the same view.

## D2 — The agent must be able to operate the app like a normal user

**User intent:** the timeline could not be adjusted; the planet cannot be zoomed or rotated. The
agent should operate like a normal user does — not only through the remote ingress.

**Code grounding:** the user-input code EXISTS and converges on the same machinery as the ingress
commands; it fails or is invisible at runtime:
- Rotate/zoom: `GlobeOrbitControls._UnhandledInput` (`App.Camera.Seam/GlobeOrbitControls.cs:149`)
  handles drag-orbit, wheel and pinch zoom, feeding the same `CameraOrbitState` as `camera.orbit`
  — but early-returns while `_host is null` (:151). The PhantomCameraHost never binds because it
  is parented on the rig root instead of being a **child of the Camera3D it drives**
  (`CameraRig.EnsureViewportRig`) — the SAME defect as handover next-action 2. One fix restores
  both the ingress command and real mouse control.
- Timeline: `TimelineFace` lane drag-scrub exists (`TimelineFace.cs:332-360`, → `_ctl.SeekTo`)
  but is undiscoverable: no slider/handle anywhere, the visually obvious Ruler is click-through
  (`MouseFilter = Ignore`, :670-693), and regime/track buttons eat clicks on most of the lane
  area. A user reasonably concludes the timeline cannot be adjusted.

**Directives:**
1. Fix the camera host parenting (standing diagnosed action) — then verify by REAL input:
   mouse-drag rotates, wheel/pinch zooms, in the windowed app.
2. Give the timeline a visible, draggable playhead handle and a clickable ruler.
3. Verification doctrine addition: any gate that claims "the user can X" must be exercised
   through real input events (mouse/keyboard on the windowed app — e.g. computer-use), not only
   ingress commands. Ingress remains the fast agent path; it is no longer sufficient evidence
   for user-facing interaction claims.
4. Every user-relevant capability needs a UI path (D1's layer selection covers the mantle view;
   `render.screenshot`/`cutaway` stay agent-only look-dev).

## D3 — Crust has THICKNESS, with its own exaggeration, ratio-locked to the mantle's

**User intent:** the crust should have thickness (stated repeatedly over months). Thickness gets
its OWN scaling so it is amplified — while PRESERVING the ratio to mantle thickness, which has a
DIFFERENT scaling.

**Code grounding:**
- The default crust view is a zero-thickness displaced shell (`GlobeSurfaceBuilder.cs:71-86`).
- Only M-B solids have thickness (top relief + bottom offset + side walls,
  `PlateSolidBuilder.cs:229-304`), but the thickness reuses the SURFACE relief exaggeration
  (`Build(caps, thickness, _lastExaggeration)`, binder :614; comment :96-97 makes the coupling
  explicit). At `VerticalExaggeration = 0.00003`, 30 km of crust ≈ 0.0009R — visually nothing.
- No crust↔mantle thickness ratio exists anywhere; the mantle core sits at a hardcoded 0.55R.
  The "thickness-exaggeration knob" was already an open ledger item.

**Proposed translation:** a single **radial section profile** as source of truth: real metres for
crust thickness (per-cell `CellCrustThickness`, default 30 km), lithosphere lid (90 km), mantle
depth (CMB at 0.55R). Two knobs — `crustThicknessExaggeration` (amplifies crust so it reads) and
a mantle depth scale — constrained so the DISPLAYED crust:mantle proportion equals a declared
target ratio rather than falling out by accident. M-B solids, cutaway strata, the D1 composed
view, and the core-sphere radius all read this one profile. Crust thickness stays data-true in
shape (continental keels thicker than ocean floor); exaggeration scales it uniformly.

## D4 — Canonical units are the vocabulary, and not only for time

**User intent:** despite the scaling odometer (jy, jz, ka, kb, kc …), Ma/Ga still appear — this
confuses both agents and developers. And scaling should apply not only to time but to weight,
length, etc. Canonical units were forgotten after months of work.

**Code grounding (2026-07-07 audit):**
- The ladder itself is healthy: `OdometerLadder`/`CanonicalDisplayFormatter` never emit Ma
  (guard tests assert it); timeline rung labels come from the ladder symbols.
- ONE genuine user-facing leak: `App.World/WorldFunctionProvider.cs:411-416` emits JSON with
  `"unit": "Ma"` and `durationMegaAnnum`/`ticksPerMegaAnnum` keys in command output.
- The vocabulary leak that steers agents wrong: identifier `UnitConverter.TicksPerMegaAnnum`
  used pervasively, ~25 engine comment lines and ~92 test strings say Ma/Ga, `UnitIds.cs`
  carries `Megaannum="Ma"` etc. Agents reading this code adopt Ma vocabulary.
- Beyond time the doctrine is defined-but-DEAD: CLU/CMU/temperature/angle/rate profiles exist
  (`BaselineScaleProfiles.cs`, `CanonicalUnitFamilyIds`), `CanonicalScale` conversion boundary
  exists — but `CanonicalQuantityMapper.ToCanonicalQuantity` has ZERO production callers; every
  spatial value is a raw double in metres/kg/°C, and the display formatter is only ever invoked
  for time.

**Directives:**
1. Fix the `WorldFunctionProvider` JSON leak: canonical fields (ticks + ladder-labelled
   duration); physical anchors only under explicitly-named `physical*` metadata if needed.
2. Vocabulary sweep (own packet, mechanical): rename ladder-hostile identifiers
   (`TicksPerMegaAnnum` → anchor-rung naming), convert comments in touched files; Ma/Ga are
   permitted ONLY at import/export bridges (GPlates `.rot` provenance, `PhysicalUnitId`
   anchors) where they describe EXTERNAL data.
3. Wire the non-time canonical path: spatial quantities that reach user-facing surfaces
   (readouts, inspector, cutaway/section labels, D3's section profile) flow through
   `CanonicalQuantity` + the CLU/CMU profiles and display in ladder vocabulary. Rendering
   internals may keep raw doubles; the boundary is what a user or agent SEES.
4. Standing rule for new code: user-facing strings and new identifiers use ladder vocabulary.

---

## Proposed order (for the plan)

1. **Camera host re-parent** (D2.1 = handover next-action 2; unblocks user rotate/zoom AND the
   edge-on eye-gate verdict).
2. **Rate calibration constant** (standing next-action 1; independent, tiny).
3. **D4.1 leak fix** (small, immediate).
4. **D1 mantle layer + composed separated-crust view** (builds on M-A + M-B; needs D3's profile
   for believable slab thickness → do D3 profile + knob first or together).
5. **D2.2 timeline handle/ruler UX**, then **D2.3** verification-doctrine adoption.
6. **D4.2/D4.3** as their own packets.

---

## ADDENDUM 2026-07-08 (user, after wave-5 verification): D5–D7

## D5 — Layers are STACKED, not exclusive

**User intent (verbatim spirit):** the layer is not exclusive; layers are stacked — several of
them can be active at the same time.

**Code grounding:** the current system is single-select: `ITimelineController.SelectedLayer` is
ONE `TimelineLayerSelection`; `GlobeViewModeResolver.Resolve` maps one selection to one exclusive
`GlobeViewMode`; the binder swaps whole presentations on transition (wave-5's MantleInterior
included). Track buttons behave as radio buttons.

**Design translation (proposed):** selection becomes an ACTIVE SET (per-sphere list of layer
ids); track buttons toggle membership; the presentation composes every active layer's
contribution (e.g. Mantle active alone = interior + separated slabs; Mantle+Crust = interior +
slabs carrying full terrain tops; Plate+Crust = identity coloring over terrain relief). The
GlobeViewMode enum dissolves into per-layer presentation contributions with declared composition
rules (which layer owns the surface coloring, which adds geometry, which hides what). This is a
real architecture change to the resolver/binder — spec + plan before code.

## D6 — Timeline: the indicator must slide everywhere; zoom scales time

**User intent:** could not slide the timeline indication; timeline zoom in/out should scale
time in/out.

**Code grounding:** wave-5 made the ruler band click/drag-scrubbable (real-mouse verified), but
the playhead INDICATOR (the line crossing the lanes) is not grabbable along its length — the
regime/track Buttons cover most lane area and eat presses, so grabbing the line where users
naturally reach for it (in the lanes) fails. Zoom exists only as +/-/Fit buttons
(`OnZoomIn/OutPressed` change the view span — that IS time scaling) — no mouse-wheel zoom, not
cursor-centered.

**Directives:** (a) the playhead line (plus handle) is grabbable anywhere along its height —
a grab zone around the line takes precedence over the band/track buttons; (b) mouse wheel over
the timeline zooms the TIME scale, centered on the cursor's tick; pinch likewise; buttons stay.

## D7 — Layer composition should be node-graph-driven (and the graph must be visible)

**User intent:** "I don't see node graph — is layer being composed by node graph? If not, we
should work on that."

**Code grounding:** two separate facts. (1) The world-generation node-graph PANEL is gated off
by default (`world:showGraph`, default false, env `world__showGraph=true` — Host.cs:697-699),
which is why nothing is visible. (2) GENERATION flows through graph nodes (P4b regime
layer-generation nodes delegate to composition), but layer PRESENTATION composition — which
layers render, how they compose, the wave-5 MantleInterior assembly — is binder/composer C#
code, not graph nodes.

**Directives:** (a) make the graph discoverable (config default or a UI toggle instead of
env-only); (b) extend the graph vocabulary so LAYER COMPOSITION is expressed as nodes
(per-layer presentation nodes feeding a compose node; D5's stacking rules become graph wiring,
inspectable and editable in the panel). This pairs naturally with D5 — the stacked-layer
composition rules should live in the graph, not in binder branches.

**Sequencing note:** D5+D7b are one architecture arc (stacked layers expressed as graph
composition); D6 and D7a are bounded UX/config packets that can ship independently.

### D7c — refinement (user, 2026-07-08): per-track dropdown detail in the timeline

**User intent (verbatim spirit):** the Godot timeline (AnimationPlayer) can actually have node-graph
content: create a HEADER for each track with a dropdown UI, so expanding a track shows the node
graph — or other detail — of that layer.

**Design translation (proposed):** the timeline is the single home of the layer stack. Each track
row = one layer, with a header carrying (a) the D5 active-toggle and (b) an expand/dropdown
control. Expanding grows the track row into a detail pane hosting that layer's node-graph subview
— the existing graph rendering, FILTERED to the layer's subgraph (the P4b per-regime layer-gen
nodes are the natural anchor; D7b's composition nodes join it) — or other per-layer detail
(parameters, field stats). This replaces the floating full-graph panel as the primary
discoverability path (D7a's toggle remains for the whole-graph view). Track content strips stay
time-domain (bands/keys); detail panes are untimed UI below the header. Feasibility: the graph
view already exists as a mountable view; embedding = mounting it inside an expandable track
container with a layer filter. One arc with D5: toggle-stack + inspect-detail are the two halves
of the same track-header UI.

### D7c CORRECTED (user, 2026-07-08 + Godot docs research): the layer graph IS an AnimationTree-style blend graph

The earlier "dropdown shows the world-generation graph" translation was WRONG. The reference model
is Godot's own animation architecture (docs: tutorials/animation/animation_tree):
- **AnimationPlayer** = the library + track/keyframe timeline (tracks with headers over time).
- **AnimationTree** = a NODE GRAPH (AnimationNodeBlendTree: 2D graph with one Output; Animation
  nodes pull clips FROM the player; Blend2/Add2/TimeScale nodes compose them; per-node FILTERS
  select which tracks a blend affects).
- Composition lives in the GRAPH; the timeline holds the raw tracks; graph nodes reference them.

**Translation for FantaSim:** each LAYER track is an Animation-node-like INPUT; layer composition
(D5's stacking rules — who owns surface coloring, what adds geometry) is a blend-tree-shaped graph
of compose nodes with per-layer filters, feeding ONE Output = the rendered planet view. The
timeline face already drives playback through a real AnimationPlayer + AnimationTree
(SetupAnimationSystem) — the layer-composition graph should live THERE (the tree's graph, or a
parallel graph of the same shape), viewable/editable like Godot's AnimationTree editor panel, not
as a separate world-generation-graph dropdown. D5's active-set toggles become enable/weight
parameters ON graph nodes. The wave-6 LayerCompositionDecision table is the interim hardcoding of
what this graph will express.

### D7c FINAL (user, verbatim lock): "each track has node graph as its content"

The track's CONTENT AREA — the strip that in Godot's AnimationPlayer would hold keyframes — holds
that layer's NODE GRAPH. Track = layer; content = the layer's graph (its generation/composition
pipeline); the timeline stacks these tracks and the stack composes (D5). The AnimationTree
research above stays as the composition-semantics reference (inputs → compose nodes w/ filters →
one Output), but the PRESENTATION is per-track embedded graphs, not a single separate graph
panel. Design round starts from THIS sentence.

## D8 (user, 2026-07-08 late): smooth timeline sliding + the world IS the animation preview

**User intent:** the timeline cannot slide smoothly — resolve it. And the AnimationPlayer/Tree
should show the GENERATED WORLD of that layer the way an animation preview shows an animated
character: scrub/play a layer's track and the planet animates that layer's product.

**Known roots (measured this session):** every scrub motion event fires a full seek →
presentation rebuild → CrustGenerationTrigger (heavy; the seek→visible-rebind latency was
measured at ≥4s during sweeps — the standing "triple-rebind perf" item). Dragging fires many
seeks per second. The mantle layer additionally never resamples per tick (composed root rebuilds
only on layer transitions).

**Directives:** (1) scrub seeks COALESCE — at most one applied tick per frame (latest wins),
intermediate ticks dropped; (2) the per-tick apply SPLITS: the light path (fraction/rotation
updates — exists since P8) runs on every applied tick so the planet visibly animates under the
drag like a character preview; the heavy path (crust snapshot regen and anything logging
"Crust snapshot transition") DEBOUNCES until the scrub rests (~300 ms); (3) active layers each
animate their product per applied tick — mantle per-tick field resample is its own follow-up
slice (grid cost), but the slabs/surface must ride the light path now.

### D8b (user, 2026-07-08): PROGRESSIVE RESOLUTION is the scrub mechanism; low-res track previews

**User intent (verbatim spirit):** timeline sliding adjustment should be on the PLANET RESOLUTION —
like a web image loading low-resolution first and increasing gradually. The generated planet must
never slow the sliding. And the track preview (as a FRAME concept) should also be low resolution.

**Grounding:** the cost that fights the drag is world GENERATION at full tessellation frequency
(Default freq 4; crust snapshot regen). Generation at low frequency is drastically cheaper, and
the adaptive-subdivision machinery already exists (logs show subdivision=fixed|adaptive; the
2026-07-04 LOD roadmap). The D8 coalesce/debounce shipped 2026-07-08 is the interim mechanism.

**Directives:**
1. While scrubbing: the planet regenerates/renders at a LOW resolution rung (e.g. freq 2-3, cheap
   enough to follow the hand per applied tick — replacing "freeze heavies until rest").
2. At rest: resolution steps UP progressively through the rung ladder to full (each rung replaces
   the last visibly, web-image style; cancel the climb if a new scrub starts).
3. Track content previews are a FRAME concept (filmstrip thumbnails of the layer's world along
   the time axis) rendered at LOW resolution; they join the D7c track content design.
4. The resolution rungs should reuse the existing tessellation-frequency / adaptive-subdivision
   ladder, not invent a parallel LOD system (reuse-Unify doctrine applies).
