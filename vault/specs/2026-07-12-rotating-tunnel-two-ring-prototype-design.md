# Rotating tunnel two-ring prototype design

**Status:** READY FOR USER REVIEW — three internal adversarial cycles reconciled; cross-model review
skipped during automatic goal continuation  
**Date:** 2026-07-12  
**Supersedes for this prototype:** the flat-annulus rendering and single current-ring interaction in
`2026-07-11-tunnel-timeline-design.md` / `2026-07-11-tunnel-slice1-plan.md`

## 1. Goal and evidence gate

Replace the flat dartboard render with one eye-judgeable, interactive prototype of a real hollow
timeline cylinder. The prototype has a bounded window of track corridors and exactly two physical
time controls:

1. an outer coarse-time ring; and
2. one inner ring bound to the registry track occupying the bottom-center focus slot.

The goal closes only when the exported app provides all of this evidence:

- an oblique screenshot visibly proving non-zero cylinder depth;
- a real-mouse wall rotation that moves another track into the bottom-center focus slot;
- the inner ring changing owner label when focus changes and resolving the new descriptor's real
  rung; its numeric scale changes only when the real descriptors differ;
- exact Godot-free mapping proof that accumulated `+360°` changes the canonical tick by exactly
  `+1 kb` and `-360°` produces exactly `-1 kb`, plus a real-mouse outer-ring gesture whose logged
  angle and tick delta satisfy that same mapping away from timeline bounds;
- a real-mouse inner-ring rotation that moves the focused track's magnified axial fine cursor and
  signed readout without mutating authoritative world time;
- no simultaneous globe-orbit gesture from any accepted tunnel gesture; and
- a live world-bundle reload while the tunnel is enabled, followed by the exact old-ALC-collected
  evidence line.

Build success alone is not the gate.

## 2. Established diagnosis

The first prototype failed to read as an inside-cylinder tunnel because its shared annulus builder
placed every ring and corridor vertex at local `z = 0`. The later face-on camera made that planar
choice more visible; it did not cause the missing depth. This replacement is therefore a geometry
and interaction correction, not another camera-framing round.

## 3. Product contract

### 3.1 Exactly two rings

- **Outer ring:** the base/coarse time control. One accumulated clockwise revolution advances time by
  one `kb`; one accumulated counter-clockwise revolution moves backward by one `kb`.
- Outer target mapping is
  `pressTick + RoundAwayFromZero(accumulatedDegrees / 360 × kb.UnitTicks)`, followed by clamping to
  `[0, MaxTick]`. `kb` is resolved from `TimelineModel.GetLadderRungs()`. Rounding occurs once at the
  target boundary, before clamping, and is symmetric for positive and negative angles.
- **Inner ring:** the focused-track fine control. There is only one inner ring. Its owner is always
  the registry track currently occupying the bottom-center focus slot. It accepts adjustment only
  when that layer is active at the current tick; otherwise it remains bound and visibly inert.
- Track focus changes automatically rebind the inner ring's label and scale. The implementation
  resolves `TimeDomain.Rung` through `TunnelCorridorLayout.ResolveCorridorRung`, using the current
  global rung as the established fallback for a null or unknown symbol. One full revolution
  represents one `TimelineLadderRung.UnitTicks` quantity. Conversion comes from `TimelineModel`;
  the presentation must not reconstruct ratios, symbols, or labels.
- A computed fine delta that is a whole number of ticks can be represented as an integral
  canonical-tick preview. Any non-whole delta, including a rung with `UnitTicks < 1`, is a fractional
  presentation quantity only because the authoritative controller tick is a `long`. It must never be
  rounded up and claimed as a real world-tick mutation.
- Current production track sources assign `Rung: "ka"` to every descriptor. This prototype
  implements and tests descriptor-driven rebinding but does not invent heterogeneous production
  metadata merely to make the runtime label change. Runtime evidence records the all-`ka` input
  honestly; meaningful per-layer rung assignment is a subsequent domain-data decision.
- No ladder ring, current-tick ring, per-track ring, or third physical dial is added in this
  prototype. Text readouts may explain the two controls without becoming additional controls.

### 3.2 Track window and focus

- The prototype exposes five angular track slots. `VisibleTrackSlots = 5` is a presentation constant
  for the first eye judgment, not a domain constraint.
- Source tracks are every real non-archived descriptor in `ILayerTrackRegistry.Current.Tracks`, in
  the stable order already produced by `TrackSetNodeHandler` (lane rank, sphere id, then layer id).
  Active state is joined from the current sphere schedule for styling and inner-ring enablement; it
  does not filter the registry list. No demo tracks or fabricated frames are permitted.
- The first registry descriptor is the initial bottom-center focus. With `N > 1`, dragging the
  cylinder wall rotates focus even when every track already fits. Clockwise wall rotation advances
  the focus index; counter-clockwise rotation decrements it.
- The five fixed relative slots are `-2, -1, 0, +1, +2`, where `0` is bottom-center. A track is
  mounted at most once. Populate the focused track at `0`, then the previous track at `-1`, next at
  `+1`, second previous at `-2`, and second next at `+2`, skipping any candidate identity already
  mounted. Thus one track occupies only `0`; two occupy `-1, 0`; three occupy `-1, 0, +1`; four
  occupy `-2, -1, 0, +1`; and five occupy all slots. For `N > 5`, the same rule produces the
  five-track window around focus.
- Slot centers are separated by `TrackSlotPitchDegrees = 30°` across the visible interior wall.
  During a wall drag, tracks interpolate continuously by the accumulated pointer angle. On release,
  `stepDelta = RoundAwayFromZero(accumulatedDegrees / TrackSlotPitchDegrees)`; the cyclic focus index
  changes by `stepDelta`, so the snap threshold is exactly `15°` and multi-slot drags are defined.
  The snapped wall angle is `stepDelta × TrackSlotPitchDegrees` relative to the press pose.
- Clockwise wall rotation scrolls forward/down through track order; counter-clockwise rotation
  scrolls backward/up.
- The carousel is cyclic for this prototype because the surface is a cylinder. Release snaps the
  nearest track to the fixed bottom-center focus slot.
- The bottom-center slot is visually distinguished. It alone owns the inner ring.
- With zero non-archived registry tracks, the cylinder remains visible, the inner ring is inert, and
  its label says `No track`. If tracks exist but the focused one is inactive, its identity/rung remain
  visible with an `inactive` state and its inner-ring gesture has no effect. The outer ring continues
  to work in both cases.

### 3.3 Time display

- The outer readout shows the canonical coarse/base time.
- The inner readout shows the focused track name, rung, and signed fine delta.
- A small fine delta must remain legible even when it is negligible at the outer `kb` scale. This is
  presented as text plus one axial fine cursor embedded in the focused corridor, not as a third
  scale ring and not by arbitrarily resampling or shifting real content.
- Clockwise is positive/forward for both rings. Counter-clockwise is negative/backward.
- Outer logical angle accumulates across revolutions while its visual rotation wraps at `360°`;
  inner preview angle is deliberately clamped to one revolution in either direction (§6).
- During a real gesture, the debug readout and structured log expose accumulated signed degrees,
  resolved unit symbol, raw tick quantity, rounded target tick, and clamped target tick. Runtime
  evidence compares the observed tick delta with the specified mapping for the logged angle; an
  exact full revolution is proved by the Godot-free mapping test rather than inferred from identical
  start/end ring orientation.

## 4. Real 3D cylinder

- The tunnel axis is local depth (`Z`). The mouth, throat, and every corridor use distinct non-zero
  `Z` positions.
- The mouth plane is `Z = 0`; the far throat is `Z = -TunnelDepth`. Positive/clockwise time points
  toward the throat (negative `Z`). For the coarse display window, base time maps to the mouth and
  `base + 1 kb` maps to the throat; out-of-window content is clipped rather than flattened.
- Each track is a curved interior-wall sector extending along the visible depth, not a flat XY
  annulus sector.
- Track content is positioned along the common axial timeline relative to the outer base time.
- The existing real globe remains at the far throat and is not copied into a second independently
  bound world.
- The round-one camera is mostly axial but deliberately oblique enough that the mouth, interior wall,
  and far throat visibly separate in the evidence screenshot.
- Geometry must remain readable from that oblique view without relying on disabled depth testing or
  coplanar draw order.

## 5. Interaction state machine

The tunnel owns one gesture at a time:

| Hit region | Gesture | Domain effect |
|---|---|---|
| Outer ring | Coarse dial rotation | Shared canonical tick preview/commit |
| Inner ring | Fine dial rotation | Focused-track presentation preview only |
| Cylinder wall | Track carousel rotation | Focus index only |
| Elsewhere | None | Event remains available to normal app input |

Accepted tunnel presses are marked handled. Owned motion and release continue through the strong
input path even when the pointer crosses a HUD `Control`. Disable, focus loss, controller loss,
bundle teardown, and disposal cancel the gesture without a stale commit. Key-repeat events do not
repeat the F9 toggle.

Every accepted press emits a structured ownership record containing gesture kind, pointer/button,
and `handled=true`. The runtime gate records the resident globe-orbit pose before the tunnel gesture,
after release, and after a subsequent no-button mouse move. Camera identity, orbit target, yaw/pitch,
and distance/transform must remain exactly unchanged across all three samples, proving both that the
gesture did not orbit the globe and that no stale orbit drag survived release.

The outer ring uses the existing origin-carrying scrub pipeline and per-frame coalescer. The
implementation must actually consume queued motion each frame, echo the action's requested tick,
and emit exactly one commit on release. Release computes its commit from the latest accumulated
pointer angle (or flushes that pending value first); it must not reuse a stale last-applied tick and
discard the final motion event.

## 6. Deliberately provisional inner-ring semantics

The user cannot yet decide whether a focused layer's fine adjustment should change the shared world
tick, establish a layer-local time offset, or remain a view-only inspection. The running visual is
the decision instrument.

For this prototype only:

- inner-ring motion resolves the focused descriptor's real ladder rung and computes its signed
  `UnitTicks` presentation delta;
- the focused corridor owns a fixed-length axial fine rail centered at a fixed mid-depth point between
  mouth and throat;
  the rail represents `-1` to `+1` focused-rung units at presentation magnification, independent of
  the global `kb` depth scale;
- inner accumulated angle is clamped to `[-360°, +360°]` for this prototype, and the cursor position
  is `railCenterZ - accumulatedDegrees / 360 × railHalfLength`, so positive/clockwise motion moves
  toward the throat; the signed readout uses the same fraction and `UnitTicks` quantity;
- for an integral rung the readout may show whole/fractional canonical ticks; for a sub-tick rung it
  explicitly labels the fractional presentation quantity. The cursor always remains observable, but
  no branch pretends existing world content was resampled at sub-tick resolution;
- it does **not** call `ITimelineController.PushTick`, persist state, or mutate the world document;
- changing focus, moving the outer ring, disabling the tunnel, or reloading the bundle resets the
  fine preview to zero.

This behavior is intentionally observable and reversible. The refinement session chooses one of
the three authoritative semantics after seeing it in the exported app; this prototype does not
silently choose on the user's behalf.

## 7. Boundaries and existing sources of truth

- Track identity/order/rung: `ILayerTrackRegistry` and `LayerTrackDescriptor.TimeDomain.Rung`.
- Canonical units and labels: `TimelineModel`, `TimelineTimeFormatter`, and existing unit conversion
  tables.
- Shared tick mutation: `ITimelineController` through the existing scrub-preview/scrub-commit path.
- Track content: existing filmstrip/graph/generic presenter sources. No placeholder production
  textures or smoke-only registrations.
- Active layer state remains owned by the existing timeline controller.

Pure presentation modules own only cylinder layout, carousel focus, ring-angle mapping, hit-region
dispatch, and the provisional fine-preview state. Godot nodes render those decisions and forward
real input; they do not own duplicate domain time.

## 8. Tests and runtime verification

Implementation follows RED → GREEN → REFACTOR.

Godot-free tests must cover:

- `±360°`, fractional, multi-revolution, and clamped outer time mapping;
- positive/negative midpoint rounding symmetry and rounding-before-clamping;
- focused-window ordering for `N = 0..6`, unique identities, initial focus, and cyclic movement;
- `30°` slot pitch, symmetric `15°` snap threshold, and positive/negative multi-slot wall drags;
- bottom-center focus selection;
- inner-ring rebind across several symbols returned by `TimelineModel.GetLadderRungs()`, including
  integral and sub-tick `UnitTicks`, plus null/unknown fallback and missing-track behavior;
- hit-region exclusivity among outer ring, inner ring, and wall;
- per-frame motion consumption, requested-tick echo, a release commit equal to the latest accumulated
  angle, one commit total, and cancellation; and
- reset of provisional fine preview on focus/base-time/disable transitions.

The exported-window gate must exercise real mouse paths for wall rotation and both rings, including
release over existing HUD panels. It must show that globe orbit did not move, capture the oblique
cylinder screenshot, capture the outer gesture's structured angle/tick evidence and the inner axial
cursor movement, record the tunnel ownership log plus the three identical globe-orbit pose samples,
reload the world bundle while enabled, and record the old-ALC collection line.

## 9. Explicitly deferred refinement

The following are not silently treated as complete by this prototype:

- authoritative semantics for the inner-ring adjustment;
- final visible-slot count, carousel wrapping policy, acceleration, and inertial animation;
- final materials, typography, filmstrip density, graph presenters, and camera polish;
- the separately identified stage-only reload, explicit `Unload`/`UnloadAll`, filmstrip graph-revision
  cache, layer-toggle, and very-large-ring-count defects unless this prototype directly touches the
  affected path.

The next refinement begins from runtime evidence and chooses one fine-time semantic; it does not
re-derive the two-ring or bottom-center-focus contract.
