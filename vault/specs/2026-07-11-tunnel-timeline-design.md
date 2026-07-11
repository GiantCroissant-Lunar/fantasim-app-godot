# Tunnel Timeline — design spec (2026-07-11)

**STATUS: DRAFT for user adjustment round (2026-07-11). Synthesized from the user's
claude-design export + locked corrections; decision points flagged inline.**

The user wants a 3D "tunnel timeline": depth into a pipe = canonical tick, a globe at the
center shows the world at the current tick, and timeline tracks become typed radial corridors
on the tunnel wall. Their own exploration — three HTML wireframes plus screenshots, made with
claude-design — lives read-only at `ref-projects/fantasim-app-godot/`. This spec follows the
wireframes where they align with the locked corrections and departs from them explicitly,
inline, wherever they don't.

Two preconditions this design leans on hard, both shipped 2026-07-11 on the same day this spec
was drafted: the **layer→track registry** (`2026-07-10-layer-track-registry-design.md`) gives
every track a schema-first descriptor instead of hardcoded lane strings, and the **TimelineFace
split** (`2026-07-11-timelineface-split-plan.md`) separated the 2D face's *behavior spine*
(scrub coalescing, playback, filmstrip fetch) from its *view* (Godot `Control` layout). The
tunnel is a second view over that same behavior spine — not a rewrite of it.

---

## 1. Concept selection

The wireframe offers three tabs over the same data model (`TRACKS`, `CLIPS`, `S.tick`): **A
Concentric Tunnel**, **B Time Helix**, **C Track Corridor**. All three share the causality-scope
panel, the amber current-tick ring, and the transport bar; they differ only in how depth is
projected.

| | A — Concentric Tunnel | B — Time Helix | C — Track Corridor |
|---|---|---|---|
| Depth projection | Concentric rings shrinking toward a throat (`ringR(z) = 560·4/(4+z)`) | A coiling spiral (`helixPt`, `p = 5.4/(5.4+z·0.92)`) | A rectangular perspective corridor converging to a vanishing point (`gP(z) = 6/(6+z)`) |
| Track placement | 6 angular sectors, 60° each, radial strips on the cylinder wall | Clips ride the coil surface with small radial offsets | 6 rails (2 floor, 2 per wall) converging to the VP |
| World/globe | Center, fixed scale independent of depth | Rides the near loop, moves with `t` | Sits inside the current "gate" cross-section |
| Camera framing read | Reads as "inside a pipe looking down its axis" — matches "tunnel" literally | Reads as looking down a spring/DNA-strand — track identity harder to place spatially at a glance | Reads as "inside a hallway of rails" — very legible near-field, but the vanishing-point convergence makes far tracks visually merge |

**Recommendation: A — Concentric Tunnel is primary.**

Reasoning:
1. It is literally what "tunnel timeline" names — the other two are worthwhile *alternate
   camera framings* of the same data, not better fits for the name.
2. It is the only concept the user's own reference-image round graduated to a photoreal
   mockup for (`ref-projects/fantasim-app-godot/uploads/3d-timeline-tunnel-spiral-hero.png`):
   globe at center, colored radial track bands wrapping the tunnel wall like orbital rings, one
   amber current-tick ring. That image is the aspirational look target and it is unambiguously
   Concept A's composition, not B's or C's.
2. It composes cleanly with the **ring control** the user asked for (binding correction #2):
   Concept A's amber current-tick ring (the wireframe's `jogRing`, visible close-up in
   `screenshots/02-bc.png` with the "drag to scrub" callout) is *already* a literal ring widget
   — just scoped to fine per-tick scrub, not rung-scale navigation. §2 extends this same ring
   idiom to rung-scale navigation instead of introducing an unrelated control.
3. It composes cleanly with **dual time base** (binding correction #4): the sibling wireframe
   "Sphere Tracks & Dual Time Base" (`screenshots/atmo.png`) is explicitly built as concentric
   nested rings around a Concept-A-shaped tunnel (outer ring = coarse base, inner ring = working
   fidelity, drag any ring to scrub at its own base) — it is a *variant of Concept A*, not of B
   or C. Adopting A as primary means the dual-time-base design in §4 is a direct extension, not
   a bolt-on.
4. Track corridors as angular wedges around a cylinder read naturally as "one lane per sphere,
   subdivided into per-layer wedges" — the exact grouping `TrackLaneViewModelBuilder.BuildLanes`
   already produces (one `TrackLaneViewModel` per `SphereId`, containing `TrackRowViewModel`s).
   Concept C's 6-rail-to-a-vanishing-point layout hard-codes exactly 6 rails in the wireframe
   (`railsNear` array) and does not obviously generalize to add/remove — the addition of a 7th
   track has nowhere natural to go without either shrinking every existing rail's angle or adding
   a new wall (both change the corridor topology under the user's feet mid-session, which fights
   binding correction #3, "layers appear/disappear dynamically").

**What to salvage from B (Time Helix):** the depth-compression falloff curve concept — as the
run grows toward the full 2 kb span (`MaxTick`), a straight concentric-ring falloff crowds an
enormous number of rings near the throat. The helix's `p = 5.4/(5.4+z·0.92)` curve is tuned
differently but the *idea* — a more generous compression curve for very long spans — is worth
carrying into Concept A's `ringR(z)` tuning once slice 1 is eye-judged against the real 2 kb
`MaxTick` span (today's wireframe constant `S.max = 24` ticks is a toy value, not calibrated to
canonical ticks at all — see §7 and Decision Point 11).

**What to salvage from C (Track Corridor):** the rectangular-gate cross-section reads as a very
legible "you are here" marker (the `cur` gate in `gP`/gate-rect code) — more legible than a bare
ring for a *coarse* rung boundary specifically. Consider it as a candidate look for the OUTER
(rung-select) ring in §2's two-ring control, rather than reviving Concept C as a whole
camera mode. Flagged as Decision Point 3.

---

## 2. Canonical-units mapping: depth ↔ ticks ↔ odometer rungs

### 2.1 The axis is canonical ticks, full stop

The wireframes' own caption is already correct doctrine and should ship verbatim as the
in-app caption: *"depth into the pipe = canonical tick (CU) · the shared causal axis · clips
ride their track · real time (Ma) is only a display mapping per causality scope."* The one
thing to strike from the wireframes: every on-screen label reading `Ma`/`CU x.x` must become a
ladder-vocabulary label. There is no "CU" unit in the shipped codebase — ticks are ticks, and
display uses `TimelineTimeFormatter`/`CanonicalDisplayFormatter` against the
`GeospherePlateTime`/`GeospherePlateTimeV1` profile, which never emits Ma (guarded by
`OdometerLabelTests`).

Concretely, per `TimelineTimeFormatter.ForTick` (`project/contracts/App.Timeline/TimelineTimeFormatter.cs`):

```
anchorAmount = tick / UnitConverter.TicksPerMegaAnnum
label = CanonicalDisplayFormatter.Format(anchorAmount, GeospherePlateTimeV1, ...)
```

Evidenced rungs (from `OdometerLabelTests.cs`, `project/tests/App.Timeline.Tests/`):

| ticks | label |
|---|---|
| 0 | `0 jv` (finest ladder rung — floor) |
| 500,000 | `5 ka` |
| 100,000,000 | `1 kb` (the ka→kb rollover, ×1000) |
| 150,000,000 | `1.50 kb` |

So `1 ka` = 100,000 ticks (matches the `100k ticks = 1 Ma` import-bridge equivalence already
locked in memory), and `1 kb` = 1000 ka = 100,000,000 ticks. `MaxTick` today is ~200,000,000
ticks = **2 kb** (post-D4.2 rescale, `8f5f4d3`). Below `ka` the ladder runs finer rungs (`jv` is
the evidenced floor symbol; the full jv…ka chain and its ratios were not directly read this
session — flagged as a verification item, see Sources).

**Tunnel depth is a direct, monotonic function of tick — never of Ma.** The wireframe's
`ringR(z)`/`gP(z)`/`helixPt` falloff functions operate on `z = tick - currentTick` in whatever
unit the caller passes; the real implementation passes canonical ticks (or a fixed-point rung
count) into that same falloff shape, nothing else changes structurally.

### 2.2 Rung labels reuse `TimelineModel`, not new math

`TimelineModel` (`project/contracts/App.Timeline/TimelineModel.cs`) already has everything the
tunnel's depth-ring labeling needs:

- `TimelineModel.GetLadderRungs()` — the ordered rung list (finest → coarsest).
- `TimelineModel.SelectRungForSpan(viewSpanTicks)` — "the coarsest rung whose unit still fits
  inside the view span" — this is exactly the rule that should pick which rung's rings get
  drawn/labeled at any zoom level.
- `TimelineModel.Ruler(viewStartTick, viewEndTick, rung)` — produces tick+fraction+label marks
  locked to one rung. The 2D ruler already calls this; the tunnel's depth rings are the SAME
  call, with `fraction` mapped through `ringR(z)`/similar instead of through pixel-X.
- `TimelineModel.TryGetFinerRung` / `TryGetCoarserRung` — the step function for rung-scale
  zoom, already wired to the 2D face's zoom buttons and wheel-zoom
  (`TimelineFace.OnZoomInPressed`/`OnZoomOutPressed`, `TryHandleTimelineWheelZoom`).

No new domain math is required for "which rings get drawn at which depth, labeled how." The
tunnel's job is purely a new *rendering* of `TimelineModel.Ruler`'s output onto ring geometry
instead of onto a flat ruler strip.

### 2.3 Ring control (binding correction #2)

The wireframes never actually built the control the user is asking for. What exists in the
wireframes is two *different* things that need to be fused:

- **"Time Scale Loupe"** (`Time Scale Loupe.html`) has the right SEMANTIC (outer ring = a
  fixed/coarse "base" scale, inner ring = the current "working fidelity" scale, `nFold` scales
  folded in between) but the wrong WIDGET: the scale-picker itself is a vertical **bar** (`#rail`,
  a linear list of stops with a draggable "loupe window" bracket) — this is precisely the
  control the user rejected.
- **Concept A / Sphere Tracks** have the right WIDGET (a literal ring — `jogRing`, an amber
  circular dial with a fixed zero mark, a sweep arc, and a drag handle) but today it is scoped
  ONLY to fine per-tick scrub within the current rung — there is no ring for jumping BETWEEN
  rungs (ka → kb → …), only the Loupe's rejected bar does that.

**Proposed fusion — a two-ring control, both literal rings, nested concentrically at the
tunnel's throat, replacing the Loupe's rail entirely:**

- **Outer ring = rung select.** Discrete detents, one per `TimelineModel.GetLadderRungs()`
  entry (or a windowed subset around the current rung — full-ladder-always-visible is a UI
  question, not a data question). Dragging past a detent calls `TryGetCoarserRung`/
  `TryGetFinerRung` exactly like today's wheel-zoom, then `ZoomToSpanAroundCurrentTick`
  (`TimelineModel.SpanTicksForRung`) — reusing the 2D face's existing zoom math verbatim, only
  the input surface (ring drag vs. button/wheel) is new.
- **Inner ring = fine scrub within the current rung**, i.e. exactly the existing Concept-A
  jog dial (`jogRing`/`jogBy`), unchanged, driving the SAME `TimelineScrubCoalescer` →
  `ApplyScrubAction` → `_ctl.PushTick(tick, origin)` path `TimelineFace.Input.cs` already uses.
- The Loupe's "N scales folded" dashed link between the two rings is a nice legibility touch
  worth keeping: it visually answers "how many rung-steps am I skipping between the ring I'm
  navigating with and the ring I'm scrubbing on."
- Readouts (both rings' current position, "N ka into 1 kb", etc.) should call
  `TimelineTimeFormatter.ForViewRange`/`ForTick` — never format ladder numbers by hand in the
  tunnel view.

This resolves the letter of binding correction #2 (a ring, not a bar) while reusing 100% of the
existing rung-stepping/zoom-window contracts. The exact detent geometry (how many rung-stops are
visible at once, snap-vs-continuous drag, whether the outer ring is always full-ladder or
windowed) is intentionally left open — see Decision Point 4.

### 2.4 Loupe/zoom behavior

"Zoom" in the tunnel has two independent meanings that must not be conflated (the wireframes
conflate them in Concept A: `#zoomctl`'s "world zoom" slider changes the CENTER GLOBE's render
scale, unrelated to time):

1. **Depth/time zoom** — §2.3's outer ring; changes `_viewStartTick`/`_viewEndTick` (or the
   tunnel-equivalent state), i.e. how many ticks of depth are visible between the camera and the
   throat. Reuses `TimelineModel`/`TimelineScrubMapper.ZoomWindowAroundFraction`.
2. **Camera/world zoom** — a literal camera dolly or globe-scale control, orthogonal to time.
   Kept as a separate control (candidate: reuse `GlobeOrbitControls`' wheel/pinch-to-zoom
   pattern for camera dolly instead of inventing a third slider). See §6 (camera) and Decision
   Point 10.

---

## 3. Dual time base

This is the genuinely novel part, and the wireframe's literal design (`Sphere Tracks & Dual Time
Base.html`) cannot ship as-is: it gives every sphere its OWN independent nested unit ladder
(Geosphere: `10 Ma → 1 Ma → 100 ka → 10 ka`; Hydrosphere: `1 kyr → 100 yr → 10 yr → 1 yr`;
Atmosphere: `100 yr → 10 yr → 1 yr → 1 day`; Biosphere: `10 kyr → 1 kyr → 100 yr → 1 yr`), each
with its own `COARSE_N` division count and its own `bases[i].per` subdivision ratios. That is
literally a parallel unit system per sphere — directly against D4's lock ("canonical units are
the vocabulary... Ma/Ga is structurally excluded... every time field in every json is canonical
ticks + rung labels"). It also isn't grounded in anything the codebase has: there is exactly ONE
canonical ladder profile (`BaselineScaleProfiles.GeospherePlateTime`/`...V1`) referenced anywhere
in this repo — no per-sphere profile exists or is implied by the registry schema.

### 3.1 Reinterpretation: one axis, per-track native cadence

**The depth axis stays canonical ticks for every sphere, always — never a per-sphere unit.**
What legitimately varies per layer/sphere, and what the wireframe's spinning-rings visual is
*actually* gesturing at, is two fields the registry schema **already carries** and that the 2D
view does not yet read:

- `LayerTrackDescriptor.TimeDomain.Rung` (`project/contracts/App.World/Composition/LayerTrackDescriptor.cs`)
  — "the odometer-ladder display rung (e.g. `ka`)... never Ma/Ga" — a per-track NATIVE rung.
- `LayerTrackDescriptor.Content.CadenceTicks` — how often, in ticks, that track's content
  actually changes/updates (already populated in the v1 sketch's example, `5,000,000` ticks for
  a crust track).

This is exactly the shape the current mixed-frame gap in the codebase names: geosphere.plate
evolves at roughly `ka` cadence; a future `hydrosphere.rivers`/weather layer would evolve at a
much finer, `jw`-scale cadence (per the memory ledger, "mixed-frame DEFERRED (asymmetric
per-layer time: ka plates vs jw rivers)"). **This design explicitly does NOT implement that
simulation-side asymmetry** — the underlying world still steps on one canonical tick clock. What
this design proposes is the presentation-side scaffolding so that when/if that simulation slice
lands, the tunnel already has somewhere correct to show it. See Decision Point 5 to confirm this
scoping is what the user means by "first-class."

### 3.2 What "dual base" becomes in the tunnel

Each corridor (or corridor group, i.e. sphere lane) gets its OWN instance of §2.3's ring-select
control, scoped to that track's/sphere's declared native rung and cadence:

- A **global** outer/inner ring pair at the tunnel throat navigates the WHOLE tunnel's depth —
  exactly §2.3, unchanged.
- Each sphere GROUP, when focused (see below), exposes its OWN inner ring — reading and
  scrubbing at that sphere's dominant `TimeDomain.Rung`, and using `Content.CadenceTicks` to
  decide how densely to draw ring-tick marks / filmstrip frame markers on that corridor's own
  wall (a `ka`-cadence corridor draws sparse markers many ticks apart on the shared axis; a
  hypothetical finer-cadence corridor draws many closely-packed markers over the same physical
  depth span) — this reproduces the VISUAL effect the wireframe's "rings spinning at different
  rates" was going for, without a second unit system: it's the same `TimelineModel.Ruler(start,
  end, rung)` call, just invoked once per corridor with THAT corridor's own rung instead of once
  globally for the whole ruler.
- The "spin a sphere to the bottom slot to focus it" gesture from the Sphere Tracks wireframe
  (`bottomSphere()`/`setBottom(key)`) is a reasonable adoption for *which* sphere's local ring is
  currently exposed/interactive, since showing every sphere's own ring-pair simultaneously and
  permanently would be extremely visually busy. Flagged as Decision Point 6 — this is a real UX
  commitment, not a data question.
- `TimeDomain.StartTick`/`EndTick` (already on the descriptor) bound where a track's corridor
  visually begins/ends/dims along the shared depth axis — reusing the SAME "declared-always,
  dimmed pre-onset" affordance `TrackRowViewModel.IsDimmed` already encodes for the 2D view
  (`TrackLaneViewModelBuilder.BuildLanes`), just rendered as a dimmed wedge segment instead of a
  dimmed 2D strip.

### 3.3 Verification gap

I did not find any current CONSUMER of `LayerTrackDescriptor.TimeDomain.Rung` besides its own
JSON round-trip — it is populated (`"rung": "ka"` in the v1 sketch, default `"ka"` per the
stream-discovery plan) but nothing reads it to change behavior yet. Confirm this before building
§3.2 (verification item, see Sources) — if it turns out unused, populating it correctly becomes
part of slice 1's own scope, not a given.

---

## 4. Track corridors

### 4.1 Registry-driven mapping

The mapping from registry to tunnel geometry is direct, reusing the exact grouping the 2D face
already computes:

- `TrackLaneViewModelBuilder.BuildLanes(snapshot)` → one `TrackLaneViewModel` per `SphereId` →
  one **sphere sector** (angular wedge group) around the tunnel wall, matching Concept A's
  6-sector wireframe layout generalized to N spheres instead of a hardcoded 6.
- Each `TrackRowViewModel` inside a lane → one **corridor** (a longitudinal wall panel running
  the full visible depth), matching the wireframe's per-track colored strip
  (`TRACKS[ti].color`/`col(ti)`).
- `TrackLaneViewModelBuilder.ResolvePresenterKind(descriptor.Content.Type)` decides the
  corridor's content rendering — reused verbatim, not reimplemented for 3D (see 4.2).

### 4.2 Add/remove behavior

Fully reuses the shipped registry mechanics — no new lifecycle needed:

- `ILayerTrackRegistry.Changed` already fires on archive/restore/reload
  (`ILayerTrackRegistry.cs`); `TimelineFace.OnLayerTrackRegistryChanged` already schedules a
  rebuild (`ScheduleViewRebuild` → `BuildLanes` → `UpdateLayout`) on the main thread with the
  correct threading discipline (`OS.GetThreadCallerId()` check + `CallDeferred` for off-thread
  callers, since `SetArchived`/`Reload` may be invoked from a command handler). The tunnel's
  corridor rebuild subscribes to the SAME event and applies the SAME threading rule.
- `LayerTrackStates.Declared`/`Discovered`/`Archived` are unchanged: an archived track drops out
  of `BuildLanes`'s output entirely (current code literally skips it), so a corridor disappearing
  is "rebuild the wedge list from the new snapshot" — no bespoke fade-out state machine required
  for correctness, though a fade transition is a reasonable slice-2 polish item.
- The declared-always contract (a layer with no content yet still gets a track, dimmed) carries
  straight over: a pre-onset corridor renders as a dim/empty wedge rather than not existing,
  exactly matching `geosphere.mantle`/`atmosphere.bulk`/`atmosphere.coupled` in the shipped
  `declared-layers.json`.

### 4.3 Content presenter surfaces in the tunnel

`TrackContentPresenterKind` has exactly three values today (`Filmstrip`, `Graph`, `Generic`),
resolved from `LayerTrackContentTypes` strings. Mapping each into 3D:

- **Filmstrip → reused verbatim.** `FilmstripPreviewController.RequestTexture` already returns
  an `ImageTexture`; the only change is the caller — instead of assigning it to a 2D
  `TextureRect`, blit it onto a 3D quad's `StandardMaterial3D.AlbedoTexture` (or a
  `MultiMeshInstance3D` frame strip along the corridor). The cache/queue/ALC-discipline
  machinery (execution-time provider resolve, `CancellationToken`-honoring renders,
  `FilmstripCacheLedger` eviction) is untouched — this is purely a different sink for the same
  texture.
- **Graph → needs a real decision.** The 2D expand affordance mounts a live Godot `GraphEdit`
  (`TimelineFace.BuildExpandedGraph` → `EmbeddedNodeGraphRenderer.TryBindReadOnly`). A
  `GraphEdit` is a `Control`; painting it onto a curved 3D wall needs either (a) a
  `SubViewport`→texture bridge (render the `GraphEdit` off-screen, texture the result onto the
  corridor — real Godot capability, but its exact interaction/readability at oblique 3D angles
  is unverified in this codebase) or (b) a flat 2D pop-out/overlay panel triggered by
  selecting a graph corridor (reusing `BuildExpandedGraph` completely unchanged, just NOT
  embedded in the 3D wall). (b) is far cheaper and lower-risk for slice 1. Flagged as Decision
  Point 7.
- **Generic (unknown/unregistered content types) → the Unity round-trip guarantee, carried
  into 3D.** `LayerTrackContentTypes.WorldContext`, `Series`, `Observations`, `Events`, and
  `DeclaredEmpty` all currently fall through to `Generic` even in the 2D view (no presenter is
  registered for them yet) — the tunnel inherits exactly this degradation, not a gap it
  introduces: a corridor with no dedicated 3D presenter is a plain labeled/dimmed wedge, never
  invisible, never a crash. This is the same wireframe vocabulary (`world context`, `series`,
  `observations`, `events` all appear in the `TRACKS` legend) already seeded as
  `LayerTrackContentTypes` constants, awaiting real presenters on their own schedule.

---

## 5. Interaction contract

### 5.1 Scrub

Depth-axis dragging must map onto the exact same tick-changing pipeline the 2D face uses —
nothing new at the domain layer:

- `TimelineScrubCoalescer` (per-frame press/motion/release coalescing, already Godot-free and
  reused as-is) → `TimelineFace.ApplyScrubAction` → `EchoSeekTo` (immediate local echo) +
  `_ctl.PushTick(tick, origin)` with `TimelineTickOrigin.ScrubPreview`/`ScrubCommit`.
- D8b's progressive-resolution ladder (`ScrubRefreshCoordinator`, `LowRung`=2 → `MidRung`=3 →
  full) is entirely on the world/presentation side of this contract — the tunnel gets it for
  free by pushing the SAME origin-carrying ticks; it does not need its own resolution-rung logic.
- **Which gesture fires a scrub is a real design choice in 3D that the 2D face didn't have to
  make**, because a 2D face has one obvious drag axis (X) and the tunnel has several plausible
  ones. Proposed split, following Concept A's own wireframe interaction code
  (`el.addEventListener('pointerdown', ...)` in `Tunnel Timeline Wireframes.html`): dragging
  the **inner/current-tick ring** (§2.3) is a scrub; dragging elsewhere on the tunnel wall is a
  camera/wall-spin gesture with NO tick side effect (so idly looking around the tunnel never
  moves the playhead). This mirrors the wireframe's own `mode = Math.abs(r-Rj)<48 ? 'time' :
  'wall'` radius-based dispatch.

### 5.2 Ring control

Covered fully in §2.3. Interaction summary: outer ring drag/detent = rung-scale zoom (reuses
`TimelineModel.TryGetCoarserRung`/`TryGetFinerRung` + `ZoomToSpanAroundCurrentTick`); inner ring
drag = fine scrub (§5.1); per-corridor local rings (§3.2) are the same widget, scoped.

### 5.3 Layer toggle

Reuses `ITimelineController.ToggleLayer(sphereId, layerId)` / the `timeline.toggle_layer`
command exactly as `TimelineFace.OnTrackPressed` does today — including its command-vs-local
fallback (dispatch through `IClient` when available; fall back to `_ctl.ToggleLayer` directly
otherwise) and its authorization check (`IsLayerActive` gates the toggle attempt). A corridor's
toggle affordance (a button on the wedge header, a tap/click on the wedge itself) calls the
identical entry point, so `ActiveLayers` stays one source of truth read by both views —
`TimelineFace.UpdateUI`'s selected/active-but-inactive styling logic is exactly what should
drive the tunnel corridor's own highlight state (dim inactive, outline selected).

### 5.4 Camera

Must satisfy D2 (input-parity doctrine) from the directives spec: *"the agent should operate
like a normal user does"* and *"any gate that claims 'the user can X' must be exercised through
real input events... not only ingress commands."* Concretely:

- The existing `GlobeOrbitControls` (`project/plugins/App.Camera.Seam/GlobeOrbitControls.cs`)
  is the house pattern for real-mouse camera control: drag-orbit (yaw/pitch), wheel/pinch zoom,
  clamped pitch, a `LazyBindOnce<PhantomCameraHost>` bind-when-available pattern for the
  host-may-not-exist-yet race. The tunnel camera should follow this SAME pattern (either a new
  `TunnelCameraControls` node or an extension of the existing one) — not reinvent input capture.
- Two DIFFERENT drags must coexist without fighting: orbiting/spinning the tunnel to look at a
  different sphere sector (a pure camera/view gesture) vs. scrubbing the ring (a tick-changing
  gesture, §5.1). The radius-gated dispatch in §5.1 is the proposed disambiguator.
- Every tunnel-specific capability (ring drag, corridor toggle, wall spin, layer expand) needs a
  UI path exercised by REAL mouse input in the windowed app before it can be claimed to work —
  same doctrine `verify-windowed`'s skill and D2 already impose on the 2D face.

---

## 6. Architecture sketch

### 6.1 Where the code lives — the central open question

Two real placements exist in this codebase today, both grounded in actual precedent, and they
trade off very differently:

**Option A — resident T4 seam, sibling to `TimelineFace`.** A new class (e.g.
`TunnelTimelineFace : Node3D`) inside `project/plugins/App.Timeline.Seam/`, binding through the
exact same `ITimelineFaceContext` (`ResidentRegistry.TryGet<ITimelineFaceContext>()`,
`RebindResidentContext`, the same rebind-safe subscription pattern
`BindLayerTrackRegistry`/`UnsubscribeLayerTrackRegistry` already codifies). This is the cheapest
path to a working prototype: it is a mechanical copy of a pattern that already works, end to
end, today. The cost: `App.Timeline.Seam` is a resident T4 seam project — per the
`verify-windowed` skill's decision table, T4 seam changes are NOT hot-reloadable; every
iteration on the tunnel's Godot code needs a full `task build:godot:desktop` → `task
run:exported` cycle. For what will likely be heavy mesh/shader/material iteration, this is a
real cost.

**Option B — bundle-local Godot content, following the bundle-maximalism phase-1 precedent
(Presentation→world.pck, 2026-07-08; reinforced by the 2026-07-11 polarity flip making
collectible the default).**
`App.Presentation` (`project/plugins/App.Presentation/`) is itself a `Godot.NET.Sdk` T4-tagged
project — its own csproj comment says *"RESIDENT for now; the file layout is bundle-ready —
flipping this project into a collectible presentation bundle later needs only the mount-protocol
contracts and a manifest"* — and per the shipped `collectible-bundles.json`, App.Presentation's
build output is now actually listed under the **collectible `world` bundle**, loaded through
Godot's `IsolatedComponentLoadContext` (per the bundle-oriented-maximalism memory ledger, this
flip already shipped — "DynamicData prefix... now bundle-local in world.pck"). That is,
Godot-typed 3D presentation code (mesh builders, materials, binders) already lives inside a
collectible, hot-reloadable bundle in this codebase — the tunnel's corridor/ring/wall-material
code is architecturally the same KIND of thing `PlanetPresentationBinder` already is. Placing it
there gets `task bundle:world` (or a new `timeline`-adjacent bundle) + `task bundle:install` +
`old ALC collected` hot-reload iteration, matching the rest of active 3D presentation work and
the project's stated "everything collectible except the loading floor" trajectory
(`2026-07-08-bundle-oriented-maximalism.md`). The cost: real ALC discipline overhead —
execution-time provider resolution, no anonymous-type STJ serialization, no cross-ALC statics,
`CancellationToken`-honored renders — all the hard-won rules `FilmstripPreviewController`/
`PlanetPresentationBinder`/`TimelinePlugin` now encode, and getting them right for NEW code is
real, proven-necessary work (see the seven pin classes in the ALC-shared-type-identity memory).

**This spec recommends Option B for the tunnel's rendering/geometry code**, with a thin resident
mount point (a `SubViewport` or `Node3D` slot the Stage-owned scene already parents, exactly the
shape `App.Presentation`'s own doc comment describes) — consistent with where the codebase's
other active 3D presentation work already lives, and the project's declared bundle-maximalism
direction. This is flagged as Decision Point 1 — it is the single highest-leverage choice in this
document and should be confirmed explicitly, not inferred.

### 6.2 2D face coexistence

`TimelineFace` (the 2D `Control`-based face) is UNTOUCHED by this design. The tunnel is an
ADDITIONAL view over the same `ITimelineController`/`ILayerTrackRegistry`/`ITimelineFaceContext`
— both views only ever READ controller state and PUSH ticks through `SeekTo`/`PushTick(tick,
origin)`, so they can coexist live (2D HUD overlay + 3D tunnel background, or a mode toggle)
with zero new synchronization contract: whichever view last called `PushTick` wins, exactly like
the ingress path and the face already coexist today (`TimelinePlugin.RegisterTimelineCommands`'s
`timeline.seek` handler and `TimelineFace.SeekTo` already both drive the one controller). Whether
the two views are simultaneously visible, mutually exclusive tabs, or one replaces the other over
time is a product decision, not an architecture one — flagged as Decision Point 9.

### 6.3 Contract/registry extensions needed

**None required for slice 1.** `LayerTrackDescriptor` already carries every field this design
uses: `TimeDomain.{StartTick,EndTick,Rung}`, `Content.{Type,Source,CadenceTicks}`,
`Capabilities`, `State`. `TimelineModel.Ruler(start,end,rung)` already accepts an explicit rung
per call, so per-corridor cadence-driven ring density (§3.2) needs no new contract — just a
render loop that calls it once per corridor with that corridor's own rung instead of once
globally. The one open question is whether anything needs to start CONSUMING
`TimeDomain.Rung` for the first time (§3.3) — that is new *behavior*, not a new *contract*.

### 6.4 What the tunnel needs from compose-json — and what it doesn't

The registry design doc (`2026-07-10-layer-track-registry-design.md`) names "the tunnel skin" as
one of two possible first real consumers of compose-json (`geometry-stack`, `coloring-priority`,
`exaggeration-ratio`, `visibility-weight`), alongside the D5/D7b compose-node arc. Assessment
after reading both docs closely: **the tunnel's center globe does not need compose-json.** The
center globe is the SAME `PlanetPresentationDocument`/`LayerCompositionDecision` the 2D face's
bound world view already renders (driven by the same `ITimelineController` and world service) —
composition (which layer owns surface coloring, whether mantle-interior mounts, etc.) is already
resolved upstream of anything the tunnel adds. Compose-json would become relevant only if
individual CORRIDOR WALLS want their own isolated mini-preview of "this layer's geometry
contribution alone" (as opposed to the composed whole) — a real, plausible future feature, but
NOT required for slice 1's filmstrip/generic corridors, which just display existing filmstrip
frames or labels. **This design does not trigger compose-json implementation.** If the user
wants per-corridor isolated-layer previews, that becomes the real first consumer and deserves its
own schema pass at that time, per the registry doc's own "no schema work before a real consumer
arrives" rule. Flagged as Decision Point 8 to confirm this scoping.

### 6.5 Verification

Whichever placement (§6.1) is chosen, the tunnel must clear the SAME gates every other feature
in this codebase clears:
- Option A (T4 seam): full `task build:godot:desktop` → `task run:exported` per change.
- Option B (bundle-local): `task bundle:<tier>` → `task bundle:install` → `old ALC collected` log
  line, per the `verify-windowed` skill.
- Either way: real-mouse interaction proof in the windowed app (D2 doctrine) before any "the
  ring works" / "corridors toggle" claim — ingress commands alone are insufficient evidence for
  user-facing interaction claims, exactly as already stated for the 2D face.

---

## 7. Phasing

### Slice 1 — the smallest eye-judgeable tunnel

**In scope:**
- Concept A (Concentric Tunnel) rendering only. Camera: a fixed or minimally-orbitable oblique
  view is sufficient for the first eye-judgment — full orbit parity with `GlobeOrbitControls` is
  NOT required to judge "does this read as a tunnel."
- Center globe: reuse the SAME bound `PlanetPresentationDocument`/geometry the Stage/world scene
  already renders at the current tick — do not stand up a second, independently-bound copy for
  slice 1 unless investigation (Decision Point 2) shows the shared-node approach is impractical.
- Corridors: one wedge per non-archived `LayerTrackDescriptor` from `ILayerTrackRegistry.Current`,
  grouped into sphere sectors via `TrackLaneViewModelBuilder.BuildLanes` (reused, not
  reimplemented). Filmstrip presenter only (`FilmstripPreviewController` reused verbatim,
  new sink = 3D quad material instead of `TextureRect`). Graph/Generic content types render as a
  plain labeled/dimmed wedge (no in-3D graph, no pop-out yet).
- Depth rings: driven by the EXISTING zoom controls (buttons + wheel-zoom, i.e. the same
  `_viewStartTick`/`_viewEndTick` state and `TimelineModel.Ruler` call the 2D face already makes)
  rendered as ring spacing/labels instead of ruler ticks. The full two-ring §2.3 WIDGET (drag a
  literal ring to change rungs) is a stretch goal for slice 1, not a hard requirement — see
  Decision Point 11.
- Scrub: real-mouse drag on the current-tick ring emits `ScrubPreview`/`ScrubCommit` through the
  unchanged pipeline (§5.1); D8b progressive-resolution reused verbatim, no new resolution logic.
- Add/remove: `ILayerTrackRegistry.Changed` wired to a corridor rebuild — wedges appear/disappear
  live under `timeline.set_track_archived`, mirroring the shipped registry gate.
- 2D `TimelineFace`: untouched; can be shown, hidden, or toggled alongside the tunnel.

**Out of scope (explicitly, not by omission):**
- Time Helix / Track Corridor as selectable camera modes (salvage only, §1).
- The full ring-select-for-rungs widget, if not reached within slice 1 (falls back to existing
  buttons/wheel-zoom).
- Per-sphere dual-time-base local rings and the "spin to focus" gesture (§3.2) — depends on
  confirming Decision Point 5/6 and on `TimeDomain.Rung` actually being populated meaningfully
  (§3.3).
- In-3D graph presenter (SubViewport bridge) — Decision Point 7; pop-out 2D overlay is the
  cheap fallback if attempted at all in slice 1.
- Camera orbit/zoom parity with `GlobeOrbitControls`.
- compose-json consumption of any kind (§6.4).
- A second independently-bound globe (only if Decision Point 2 forces it).

---

## 8. DECISION POINTS FOR THE USER

This is the most important section — flag every place a real alternative was chosen or the
wireframes were ambiguous, for the adjustment round.

1. **Bundle placement (§6.1).** Resident T4 seam (cheap, simple, but every iteration needs a
   full rebuild+relaunch) vs. bundle-local collectible content following the phase-1
   Presentation→world.pck precedent (hot-reloadable, matches where the rest of 3D presentation
   work lives, but real ALC
   discipline overhead). This spec recommends bundle-local (Option B). Confirm or override.
2. **Center-globe reuse.** Does the tunnel VIEW the same bound globe node the Stage/world scene
   already builds (shared node, re-parented/re-viewed from a new camera), or does it need its own
   independently-bound `PlanetPresentationDocument`? Shared is cheaper and keeps one source of
   truth; independent is more view-flexible but doubles binder work. Needs a quick spike to
   confirm feasibility either way.
3. **Concept confirmation.** This spec recommends Concept A (Concentric Tunnel) as primary, with
   B's depth-compression curve and C's gate-cross-section framing salvaged as tuning/alternate-
   camera ideas rather than full concepts. Confirm, or say if B or C should get a real prototype
   instead/also.
4. **Ring control literal shape (§2.3).** This spec's synthesis — an outer rung-select ring +
   an inner fine-scrub ring, nested — is a NEW composition; neither wireframe drew this exact
   pair (the Loupe used a bar for scale-select; Concept A's ring only did fine scrub). Confirm
   this is what "ring, not bar" means, and weigh in on: always-visible full ladder on the outer
   ring vs. a windowed subset; snap-to-detent vs. continuous drag.
5. **Dual time base semantics (§3.1–3.2).** Confirm this design's reinterpretation — ONE
   canonical tick axis always; per-track native rung/cadence (existing `TimeDomain.Rung`/
   `Content.CadenceTicks` fields) drives corridor ring/marker DENSITY, not a second unit system
   — is what "first-class" dual time base means, as opposed to the wireframe's literal per-sphere
   independent unit ladders (which would violate D4). Also confirm this explicitly does NOT
   imply simulating spheres at different step cadences (that's the separately-deferred
   "mixed-frame" item) — this design only makes the PRESENTATION ready for that, if/when it lands.
6. **Sphere-group focus interaction (§3.2).** Adopt the "spin a sphere to the bottom slot to
   focus/drill its own local rung ring" gesture from the Sphere Tracks wireframe, or keep every
   sphere's corridors uniformly visible/navigable without a focus mode? This is a real UX
   commitment with real geometry implications (focus mode needs a wall-spin-to-angle mapping;
   no-focus-mode needs a way to show N local rings without clutter).
7. **Graph-track 3D presentation (§4.3).** Pop-out 2D overlay (cheap, reuses
   `EmbeddedNodeGraphRenderer` unchanged) vs. `SubViewport`-textured onto the curved corridor
   wall (visually integrated, but unverified in this codebase — needs a source-driven-development
   pass against Godot 4.7's SubViewport-to-texture docs before committing). Recommend pop-out for
   slice 1 regardless; confirm whether the textured-wall version is worth prototyping later.
8. **compose-json scoping (§6.4).** Confirm this design correctly keeps compose-json OUT of
   scope (center globe reuses existing composition; no per-corridor isolated-layer previews in
   slice 1). If per-corridor isolated previews ARE wanted, that changes slice-1 scope materially
   and should trigger the registry doc's "real consumer arrived" schema pass.
9. **2D/3D coexistence horizon (§6.2).** Confirm the assumption that `TimelineFace` (2D) and the
   tunnel (3D) coexist INDEFINITELY as two views over one behavior spine (matching the split's
   stated precondition), rather than the tunnel eventually replacing the 2D face, or the two
   being mutually-exclusive modes from day one.
10. **Camera control ownership (§5.4).** Extend `GlobeOrbitControls` to also drive tunnel
    wall-spin (shared input plumbing, different semantics — orbit-around-a-point vs.
    spin-around-an-axis), or build a separate `TunnelCameraControls`? Either is viable; this
    needs a call before implementation starts to avoid rework.
11. **Slice-1 scope on the ring control.** Is the literal ring WIDGET (§2.3) essential to even
    the FIRST eye-judgeable cut (since it's an explicit binding correction, not a nice-to-have),
    or is it acceptable for slice 1 to reuse the existing zoom buttons/wheel-zoom for rung
    navigation and land the ring widget itself as slice 2? This spec's §7 phasing treats the ring
    as a stretch goal for slice 1 — confirm or push back.
12. **Depth-falloff tuning against the real 200M-tick `MaxTick`.** The wireframes' falloff
    constants (`ringR`'s `f=4.0`, `S.max=24`) are toy values calibrated to a 24-unit demo range,
    not to the real ~2 kb (200,000,000-tick) span. The actual curve needs eye-tuning once slice 1
    renders against real data — flag this explicitly so it isn't mistaken for a solved parameter
    carried over from the wireframe.
13. **Tunnel activation mechanism.** How does a user enter the tunnel view — a persistent mode
    toggle, a keybind, a menu item, always-on as a background behind the 2D face? Distinct from
    Decision Point 9 (which is about the 2D face's long-term fate); this is about the tunnel's
    own entry point for slice 1's eye-gate session.

---

## 9. Sources

**Wireframes and screenshots (read-only, `ref-projects/fantasim-app-godot/`):**
- `Tunnel Timeline Wireframes.html` — the three concepts (A/B/C) in full: `ringR`/`renderA`
  (concentric tunnel + jog dial + track strips), `helixPt`/`renderB` (time helix coil), `gP`/
  `renderC` (track corridor rails/gates), shared `TRACKS`/`CLIPS` data model, transport bar,
  causality-scope panel, wall-spin/jog pointer-event dispatch (`mode = ... 'time' : 'wall'`).
- `Time Scale Loupe.html` — the `LADDER` array (jv…1sec-shaped rung ladder with per-step
  ratios), outer/inner two-ring info-panel semantics (base vs. working fidelity, "N scales
  folded"), and the rejected bar/rail widget (`#rail`, `#window`) that motivated binding
  correction #2.
- `Sphere Tracks & Dual Time Base.html` — per-sphere `bases[]` nested ladders, `jogRing`
  composition, `bottomSphere()`/`setBottom()` spin-to-focus mechanic, `ringFrac`/`ringVal`/
  `cumPer` chain math — source for §3's dual-time-base reinterpretation.
- `screenshots/tunnelA.png` — Concept C corridor/gate framing (salvage reference, §1).
- `screenshots/01-bc.png`, `screenshots/02-bc.png` — Concept A rendered, including the amber
  jog-dial close-up with the "drag to scrub" callout (§2.3's starting point).
- `screenshots/atmo.png` — Sphere Tracks & Dual Time Base nested-ring ladder rendered (§3's
  visual source).
- `screenshots/jog.png`, `screenshots/zoomtest.png` — additional Concept A renders (world-zoom
  panel visible); largely redundant with 01/02-bc for spec purposes.
- `uploads/3d-timeline-tunnel-spiral-hero.png` — the aspirational photoreal mockup; primary
  evidence for recommending Concept A (§1) and the north-star look reference.

**Vault docs:**
- `vault/specs/2026-07-07-layer-presentation-input-parity-canonical-units-directives.md` — the
  full D1–D8c directive arc. D2 (input parity/real-mouse doctrine, §5.4/§6.5), D4 (canonical
  units lock, §2.1/§3.1), D5 (stacked active layers, §5.3), D6 (timeline slide/zoom precedent for
  ring control), D7/D7c (per-track node-graph content, §4.3), D8/D8b/D8c (scrub coalescing,
  progressive-resolution rungs, filmstrip-as-frames — all reused verbatim in §5.1).
- `vault/specs/2026-07-10-layer-track-registry-design.md` — registry architecture, the v1 track
  descriptor sketch, the Unity round-trip degradation guarantee (§4.3), compose-json status and
  the explicit "tunnel skin" candidacy (§6.4), and its own already-flagged open question "ring
  control (vs bar) for huge time-scale navigation... implementation-time detail" (directly
  answered by §2.3 of this spec).
- `vault/plans/2026-07-10-layer-track-registry-slice1-plan.md`,
  `vault/plans/2026-07-10-layer-track-registry-slice2-plan.md` — slice scope, gate results,
  `laneOrder`/stream-discovery mechanics (source for the `world.truth-events` / `events` content
  type example in §4.3).
- `vault/plans/2026-07-11-timelineface-split-plan.md` — the split's shape and its explicit
  framing as the tunnel's precondition (quoted in this spec's intro).
- `vault/handover/2026-07-11-parallel-packets-handover.md` — confirms the split shipped (core
  774 lines), the D4.2 `MaxTick` rescale to ~200M ticks / 2 kb (§2.1), and names the tunnel/
  D5-D7b compose arc as the next design conversation.
- `vault/plans/2026-07-11-d8b-progressive-resolution-slice1-plan.md` — `ScrubRefreshCoordinator`
  rung policy (`LowRung`=2, `MidRung`=3, full), `TimelineTickOrigin` threading through
  `SeekTo`/`PushTick` — reused verbatim in §5.1.
- `vault/README.md` — taxonomy confirmation (specs/plans/handover), authority index.
- `vault/specs/2026-07-08-bundle-oriented-maximalism.md` — "everything collectible except the
  loading floor" doctrine, cited in §6.1's Option B reasoning (title/phase-ladder framing; full
  phase detail not re-read this session beyond what the memory ledger and `collectible-bundles.json`
  already evidence).

**Code (`project/`):**
- `plugins/App.Timeline.Seam/TimelineFace.cs` — resident-context bind/rebind/clear
  (`BindResidentContext`, `ClearResidentContext`, `BindLayerTrackRegistry`), animation/transport
  plumbing, `SeekTo`/`EchoSeekTo`/`ApplyView` — the exact contract a new tunnel face would bind
  through identically (§6.1/§6.2).
- `plugins/App.Timeline.Seam/TimelineFace.Input.cs` — `_Input` drag capture, wheel/magnify zoom
  dispatch, `HandleScrubPress`/`QueueScrubMotion`/`HandleScrubRelease`/`ApplyScrubAction` — the
  scrub pipeline reused verbatim in §5.1.
- `plugins/App.Timeline.Seam/TimelineFace.Lanes.cs` — `BuildLanes`/`BuildLane`/`BuildLaneTracks`,
  `RenderTrackContent`/presenter dispatch, `OnTrackPressed`/`OnTrackExpandPressed` — the
  registry-to-view pipeline reused in §4.
- `plugins/App.Timeline.Seam/FilmstripPreviewController.cs` — texture cache/request/queue/
  ALC-safety pattern, reused as the corridor filmstrip sink in §4.3.
- `plugins/App.Timeline.Seam/TimelineScrubMapper.cs` — pure tick↔fraction and zoom-window math,
  reused for ring math in §2.3/§5.1.
- `plugins/App.Timeline.Seam/TrackLaneViewModelBuilder.cs` — registry snapshot → lane/track
  view-model grouping and presenter-kind resolution, reused verbatim in §4.1/§4.3.
- `plugins/App.Timeline.Seam/TimelinePlayheadHandle.cs` — existing D2.2 playhead-handle
  precedent (visual-affordance-only Control, input handled by the face root).
- `contracts/App.World/Composition/LayerTrackDescriptor.cs` — the track schema (`StreamId`,
  `TimeDomain`, `Content`, `State`, `Capabilities`) this entire design maps onto; source of the
  §3.1 `TimeDomain.Rung`/`Content.CadenceTicks` reinterpretation.
- `contracts/App.World/Composition/LayerTrackRegistrySnapshot.cs`,
  `contracts/App.World/Composition/ILayerTrackRegistry.cs` — registry contract (§4.2).
- `contracts/App.World/Composition/ITimelineController.cs` — `Tick`/`MaxTick`/`ActiveLayers`/
  `ToggleLayer`/`PushTick(tick, origin)` surface the tunnel drives (§5).
- `contracts/App.World/Composition/TimelineTickOrigin.cs` — `Standard`/`ScrubPreview`/
  `ScrubCommit` (§5.1).
- `contracts/App.World/Composition/LayerCompositionDecision.cs`,
  `contracts/App.World/Composition/LayerActiveSet.cs`,
  `contracts/App.World/Composition/TimelineLayerSelection.cs` — D5 stacked-layer model and the
  surface-coloring decision the center globe already resolves (§6.4).
- `contracts/App.Timeline/TimelineModel.cs` — ladder rungs, `Ruler`, `SelectRungForSpan`,
  `TryGetFinerRung`/`TryGetCoarserRung`, `SpanTicksForRung` — the depth/rung math reused
  throughout §2 and §5.2.
- `contracts/App.Timeline/TimelineTimeFormatter.cs` — canonical display formatting, reused for
  all tunnel readouts (§2.1/§2.3).
- `contracts/App.Timeline/Providers/ITimelineFaceContext.cs` — the resident-context contract a
  new tunnel face binds through identically to `TimelineFace` (§6.1).
- `tests/App.Timeline.Tests/OdometerLabelTests.cs` — concrete evidenced rung symbols (`jv`,
  `ka`, `kb`) and the ka→kb rollover math (§2.1).
- `plugins/App.World/Globe/CanonicalTimeLabel.cs` — confirms the canonical-only, never-Ma display
  doctrine at another call site (§2.1).
- `hosts/complete-app/config/track-pipeline.json` — shipped pipeline (family-layers/
  declared-layers/stream-discovery → track-set, `laneOrder` param) (§4.1).
- `hosts/complete-app/config/declared-layers.json` — real declared-but-not-generating layers
  (`geosphere.mantle`, `atmosphere.bulk`, `atmosphere.coupled`) — concrete layer-id vocabulary
  used as examples throughout, and the declared-always dimming precedent (§4.2).
- `hosts/complete-app/config/collectible-bundles.json` — bundle registry; evidences that the
  `world` bundle already ships `App.Presentation`'s Godot-typed output collectibly (§6.1's
  central precedent), and that `timeline` (the T3 `App.Timeline` orchestrator) is a DIFFERENT
  project from `App.Timeline.Seam` (the resident T4 face).
- `plugins/App.Timeline/TimelinePlugin.cs` — the T3 orchestrator composing
  `ITimelineFaceContext`/`IService`/commands inside the collectible `timeline` bundle; its
  world-reload rebind lifecycle (`OnResourceRuntimeChanging`/`TryConsumePendingWorldRebind`) is
  the pattern any tunnel-side registry/controller consumption must respect.
- `plugins/App.Timeline/DeferredTimelineFace.cs` — proxy-pattern confirmation (`ITimelineFaceProxy`).
- `plugins/App.Camera.Seam/GlobeOrbitControls.cs` — real-mouse orbit/zoom precedent for the D2
  camera doctrine (§5.4), `LazyBindOnce<T>` host-may-not-exist-yet pattern.
- `plugins/App.Presentation/App.Presentation.csproj` — T4 `Godot.NET.Sdk` tag plus the explicit
  "bundle-ready... needs only the mount-protocol contracts and a manifest" comment central to
  §6.1's Option B argument.
- `contracts/App.World/PresentationLayers.cs` — `PlanetPresentationDocument` shape
  (`GlobeSnapshot`, `LayerProjectionProfiles`, `VerticalExaggeration`, `GeosphereSchedule`, etc.)
  — what the tunnel's center globe already receives if reused (§7's slice-1 assumption); also
  `WorldLayerDescriptor`/`WorldLayerState`/`WorldPresentationProfile`.
- `.agent/skills/04-tooling/verify-windowed/SKILL.md` — the hot-reload-vs-full-rebuild decision
  table cited throughout §6 and §7.

**Verification items (not confirmed this session — mark before relying on them):**
- The full jv…ka rung chain and ratios (only `jv`, `ka`, `kb` and the ka→kb ×1000 rollover were
  directly evidenced from tests/code this session; `BaselineScaleProfiles` itself lives in a
  referenced package, not this repo's source tree, and was not opened).
- Whether `GeospherePlateTime` (used by `TimelineModel.BuildLadderRungs`) and
  `GeospherePlateTimeV1` (used by `TimelineTimeFormatter`/`CanonicalTimeLabel`/
  `OdometerLabelTests`) are the same profile or genuinely different profiles.
- Whether any code currently reads `LayerTrackDescriptor.TimeDomain.Rung` for behavior (§3.3) —
  only its JSON population was confirmed, not a consumer.
- Godot 4.7's `SubViewport`-to-texture behavior on a curved/non-planar mesh at oblique angles
  (§4.3, Decision Point 7) — not verified against Godot docs this session; needs a
  source-driven-development pass before committing to that approach.
- Exact feasibility of "the tunnel views the SAME bound globe node the Stage scene already
  builds" from a second camera (§6.1/Decision Point 2) — not spiked this session.

## 10. Screenshots worth vendoring into the vault (paths only — not copied by this session)

Recommend vendoring these as key frames (per `no-smoke-or-fake-production-code`/general vault
hygiene, only actually copy on user request, not as part of this drafting pass):

- `ref-projects/fantasim-app-godot/uploads/3d-timeline-tunnel-spiral-hero.png` — the north-star
  look target; single most important frame to vendor.
- `ref-projects/fantasim-app-godot/screenshots/02-bc.png` — Concept A with the jog-dial "drag to
  scrub" callout visible; best functional reference for §2.3's ring control starting point.
- `ref-projects/fantasim-app-godot/screenshots/atmo.png` — the Sphere Tracks & Dual Time Base
  nested-ring ladder; best visual reference for §3's dual-time-base design.
- `ref-projects/fantasim-app-godot/screenshots/tunnelA.png` — Concept C's corridor/gate framing;
  reference for the salvaged gate-cross-section look (§1).

Lower priority / likely skippable as redundant with the above: `01-bc.png`, `jog.png`,
`zoomtest.png`.
