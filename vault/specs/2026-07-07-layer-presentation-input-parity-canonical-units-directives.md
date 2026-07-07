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
