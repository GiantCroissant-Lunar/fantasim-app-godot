# Asymmetric cockpit tunnel — empirical slice design

**Status:** APPROVED FOR EMPIRICAL IMPLEMENTATION — the user directed “implement and find out”
after approving the spatial/data direction. The exported eye gate may disprove visual tunings, but
it may not relax the lifecycle, time-authority, data-provenance, or evidence contracts below.  
**Date:** 2026-07-12  
**Extends:** `2026-07-11-tunnel-timeline-design.md`  
**Supersedes for this slice:** the bottom-center focus, centered/full-globe framing, flat preview
quads, gesture-relative outer-ring phase, and HUD ownership in
`2026-07-12-rotating-tunnel-two-ring-prototype-design.md`.

## 1. Session-scale goal and named gate

Deliver the first trustworthy asymmetric cockpit-tunnel slice:

- one left-center focused track inside one visible cylinder;
- the one real current planet projected into the right third, large enough that vertical cropping is
  acceptable, with its center on the same `CurrentPlaneZ` as current track content;
- real filmstrip samples rendered as small shaded 3D spheres, not ECS worlds;
- exactly two camera-relative 3D dial controls with stationary readouts;
- deterministic outer phase from canonical time and presentation-only inner/fine inspection;
- fail-closed HUD, F9, timeline/world/stage reload behavior; and
- durable evidence from a fresh exported process.

The goal closes only through the **asymmetric-cockpit-tunnel exported gate**. Green builds or unit
tests are necessary but not substitutes for this gate. Evidence is committed under:

`vault/specs/evidence/2026-07-12-asymmetric-cockpit-tunnel-gate/`

## 2. Locked product decisions

1. There is one authoritative `ITimelineController.Tick`, a `long` clamped to `[0, MaxTick]`.
2. The outer dial remains canonical: one clockwise revolution is `+1 kb`; one counter-clockwise
   revolution is `-1 kb`.
3. The inner dial remains presentation-only. It never calls `ITimelineController.PushTick`, persists
   a layer offset, or mutates world truth.
4. There are exactly two physical dial controls. Current-plane cues, labels, tethers, and depth
   shading are not interactive rings and must not resemble a third dial.
5. The one real composed planet remains on the cylinder axis. The camera creates the asymmetric
   composition; the planet is not moved into a separate dashboard stage.
6. A small sphere is a 3D projection of an existing layer filmstrip frame at a requested tick. It is
   not a new world, ECS instance, branch, simulation, or second `PlanetPresentationDocument` binding.
7. Only real preview products become spheres. Existing procedural placeholder maps remain honest
   unavailable/labeled content in the tunnel.
8. Track identity, order, activity, rung, cadence, content kind, and archive state come from the real
   `ILayerTrackRegistry`. No demo metadata or fabricated production tracks are allowed.
9. World or stage loss resets to safe 2D mode. The slice does not silently re-enable the tunnel after
   those bundles reload. A timeline-only reload preserves a still-effective tunnel.
10. Visual tuning is empirical. Authority, provenance, cancellation, and gate requirements are not.

## 3. Spatial contract

### 3.1 Coordinate system and initial tuning

Tunnel-local `Z` is time depth. The mouth is toward positive `Z`; future time recedes toward negative
`Z`. The first implementation starts from:

| Quantity | Initial value |
|---|---:|
| `TunnelRadius` | `5.0` |
| `MouthZ` | `0.0` |
| `CurrentPlaneZ` | `-5.0` |
| `ThroatZ` | `-20.0` |
| `TimelineDepth` | `CurrentPlaneZ - ThroatZ = 15.0` |
| focused carousel angle | `180°` (left wall) |
| vertical field of view | `60°` |
| radial camera clearance | `0.5` |
| planet camera clearance | `0.25` beyond visual radius + near clip |
| camera position seed | `(-1.8, 0.6, -0.8)` |
| camera target seed | `(-1.8, 0.0, -7.0)` |

The camera seed looks nearly parallel to the cylinder axis from an off-axis interior point. The
on-axis planet therefore projects right instead of being re-aimed to screen center.

These inequalities are hard invariants:

```text
MouthZ >= cameraZ > CurrentPlaneZ > ThroatZ
length(cameraXY) < TunnelRadius - radialClearance
distance(camera, planetCenter) > planetVisualRadius + nearClip + planetClearance
```

`radialClearance` is `0.5`; `planetClearance` is `0.25`. A negative camera `Z` alone is not proof
that the camera is inside or outside the planet.

### 3.2 Current plane and canonical depth

`CurrentPlaneZ` names geometry centers:

- the bound current planet center is `(0, 0, CurrentPlaneZ)` in tunnel-local space;
- a filmstrip sphere whose requested tick equals the controller tick has its center at
  `CurrentPlaneZ`; and
- future filmstrip sphere centers use:

```text
fraction = (requestedTick - currentTick) / kb.UnitTicks
z = CurrentPlaneZ - fraction * TimelineDepth
```

Only `fraction` in `[0, 1]` is visible. The first slice displays current-to-future samples only; past
samples that would approach the camera are not mounted. Near `MaxTick`, the unused far segment stays
empty rather than stretching a partial tick range to the throat.

A sparse set of non-interactive chevrons and a faint volumetric slice mark `CurrentPlaneZ` on visible
corridors. There is no annular mesh, hit region, or dial marker for this cue.

### 3.3 Widescreen projection contract

Projection tests cover both `16:9` and `16:10`. At each aspect:

- planet center normalized `X` is within `[0.62, 0.82]`;
- planet center normalized `Y` is within `[0.35, 0.65]`;
- projected planet height is within `[0.80, 1.20]` of viewport height;
- at least one planet vertical bound is within `5%` of, or beyond, a viewport edge;
- both the focused corridor's current anchor and the instrument center are within `X=[0.12, 0.35]`,
  `Y=[0.35, 0.65]`;
- the left focus and planet do not overlap; and
- mouth, wall, and at least two separated axial depth cues remain visible around the planet.

The exact camera seed may change during the eye pass only if all projection and clearance contracts
still pass. A framing test that merely permits `cameraZ > MouthZ` is invalid.

## 4. Snapshot-sphere data contract

### 4.1 Bounded first-slice population

The carousel retains five visible track slots. Every visible real filmstrip track gets exactly four
planned samples from `TimelineFilmstrip.PlanSlots`, for a maximum of twenty corridor spheres. Adaptive
density is deferred until this bounded presentation is trustworthy.

Each sample uses a Godot `SphereMesh` with full equirectangular UVs. The texture is not cropped into a
disc or applied as a flat billboard. Every sphere owns a distinct material instance so updating one
sphere cannot change another; immutable cached `ImageTexture` resources may be shared.

### 4.2 Source policy

The tunnel sphere policy accepts these existing real `LayerFilmstripPreviewMap.SourceKind` values:

- `crust-low-res`;
- `plate-low-res`; and
- `mantle-shell-low-res`.

`pre-crust`, `magma-ocean`, `stagnant-lid`, `atmosphere-placeholder`, `layer-placeholder`, and unknown
source kinds do not become spheres. Their corridor stays visible with the real track identity and an
explicit `preview unavailable` state. This does not change the existing 2D timeline behavior; the 2D
texture sink may continue rendering its current maps.

The preview controller delivers texture plus frame metadata to sinks. The 2D sink consumes the
texture. The tunnel sphere sink uses `SourceKind` to choose real sphere versus unavailable sector.
Provider failure, cancellation, or null output leaves the unavailable state; it never substitutes a
procedural sphere.

### 4.3 Texture identity and provenance

The current cache can alias different crust renders that share a governing snapshot. This slice fixes
the actual texture identity. It includes:

```text
SphereId
LayerId
RequestedTick
SnapshotTick
ViewRung
Width
Height
GraphRevision
```

`RequestedTick` identifies the frame that was rendered. `SnapshotTick` records the governing stored
state. They are not interchangeable. The request-to-texture fast-path key also includes
`RequestedTick` and `GraphRevision`.

The tunnel reads `GraphRevision` through the cheap T1
`IService.GetGenerationProductsAsync().GraphRevision` call immediately before planning requests. It
does not call `GetPlanetPresentationAsync` per sphere. A completion from an older graph revision or
mount generation is discarded.

## 5. Focus instrument and time semantics

### 5.1 Exactly two camera-relative dials

The focus instrument is camera-local 3D geometry anchored near left-center and tethered visually to
the focused wall corridor. It has three sibling roots:

1. outer rotating mesh/marker root;
2. inner rotating mesh/marker root; and
3. non-rotating readout/lens root.

Labels never become children of rotating roots. Hit testing intersects the camera-local instrument
plane and the same radii used by the visible meshes.

Outer visible phase is derived from the target canonical tick, never from a stale gesture angle:

```text
phaseTurns = (tick mod kb.UnitTicks) / kb.UnitTicks
visualDegrees = -360 * phaseTurns
```

During an outer drag, `tick` above is the mapped/clamped preview target. After release, playback, or
external seek, it is `ITimelineController.Tick`. Seeking back to zero therefore restores zero phase.

The initial focused track is the first active non-archived registry track, falling back to the first
non-archived track when none is active. Later focus and time changes do not auto-jump. If the focused
track is inactive, the inner ring remains bound but visibly locked, does not own a gesture, and says
`inactive at current time`.

### 5.2 Presentation-only fine lens

The inner ring retains the existing raw `double` fine quantity and signed readout. It derives a render
sample without claiming fractional authority:

```text
sampleDelta = truncateTowardZero(rawTickQuantity)
sampleTick = clamp(baseTick + sampleDelta, 0, MaxTick)
```

Truncation selects the last fully crossed canonical tick in either direction. A sub-tick movement
holds the current texture while its fractional readout and cursor remain visible.

The focused lens sphere is a bounded, enlarged presentation copy under the non-rotating instrument
root. It has its own material instance, shares only immutable cached textures, and is labeled/tethered
as `inspection`. It does not replace or move any canonical corridor sphere. The full current planet
continues to show the authoritative outer tick.

Fine requests use a dedicated latest-wins lane:

- compute a bucket from the focused descriptor's positive `Content.CadenceTicks`; use the sampled
  tick itself only when cadence is absent or non-positive;
- request only when the bucket changes;
- limit starts to ten per second;
- allow at most one active provider call;
- cancel the active call when a newer bucket wins; and
- start only the newest pending bucket after cancellation unwinds.

Focus change, base-time change, disable, controller loss, world/stage change, and disposal cancel the
lane and free the lens node. Every completion checks cancellation epoch, mount generation, graph
revision, and focused track identity before applying.

While fine inspection is non-zero, non-focused sphere materials keep their exact base-time textures
and desaturate. They are not re-sampled behind the user's back. Fine reset restores their normal
materials; a canonical base-time change resets fine inspection before any ordinary corridor rebuild.

The current production registry assigns `ka` to every descriptor. The gate may prove the real global
`kb` versus focused `ka` inspection, but it must not claim heterogeneous track-to-track rung behavior.

## 6. Gesture contract

The tunnel owns at most one gesture:

| Region | Gesture | Effect |
|---|---|---|
| outer ring | coarse dial | canonical scrub preview/commit |
| inner ring | fine dial | presentation sample/readout only |
| cylinder wall | carousel | focused registry track only |
| elsewhere | none | normal application input remains available |

Accepted presses are handled. Motion and release remain owned through the strong input path when the
pointer crosses a HUD control. Disable, focus loss, controller loss, bundle change, and disposal cancel
without a stale commit. No accepted tunnel gesture changes globe orbit/camera controls. Key-repeat
events do not repeat F9.

## 7. Lifecycle and HUD ownership

### 7.1 Effective activation

`ITunnelPresentation` exposes a synchronous activation attempt and effective state. Enabling succeeds
only when mount, controller, camera, stage, and world dependencies are ready. A failed enable leaves
the tunnel effectively disabled and the 2D HUD visible. Public state never reports a requested-but-
invisible tunnel as enabled.

The timeline command returns both requested and effective outcome plus a failure reason. Disabling is
idempotent and always restores the previous camera before reporting effective false.

### 7.2 One command path and ordered transitions

`timeline.tunnel_view` is the sole mode owner. Its handler serializes transitions under the timeline
lifecycle gate and assigns a monotonically increasing mode epoch. F9 invokes this command only. If the
command service is absent, the command throws, the result fails, or the epoch becomes stale, F9 logs an
inert failure and never calls `SetEnabled` directly.

World or stage `RuntimeChanging` performs this order:

1. increment mode and bundle generation epochs;
2. set desired HUD visibility to true and apply it to the bound face;
3. cancel gestures and preview work;
4. disable effective tunnel state and restore the previous camera; then
5. sever bundle references and allow teardown.

Late command/deferred/render completions compare their captured epochs before changing state.

### 7.3 Face bind and reload rules

The current timeline generation stores `DesiredHudVisible` in its `ITimelineFaceContext`. After the
new `TimelineFace` binds the cross target, it applies that value directly. A hide/show request issued
before binding is therefore replayed instead of discarded.

The context is not retained across timeline generations. On timeline plugin compose, a new context
re-derives `DesiredHudVisible` from the still-live tunnel's effective state. This preserves an enabled
tunnel across timeline-only reload without pinning the old timeline ALC.

World or stage unload resets to safe 2D. After those bundles reload, the user must explicitly enable
the tunnel again. Mount/controller/camera failure also reports effective false and keeps the HUD shown.

## 8. Component boundaries

The implementation separates policy from Godot rendering:

- **Cockpit framing/layout policy:** coordinate inequalities, tick-to-Z mapping, focus angle, and
  16:9/16:10 projection assertions. Godot-free.
- **Snapshot sphere policy:** accepted source kinds, frame population, cache identity, and unavailable
  behavior. Godot-free.
- **Dial phase/fine sample policy:** canonical outer phase, truncation, cadence buckets, and reset
  decisions. Godot-free.
- **Mode/lifecycle policy:** effective enable outcome, epoch ordering, HUD desired state, and reload
  transitions. Godot-free.
- **Preview controller:** bounded async work, cache, metadata delivery, fine latest-wins lane, and
  cancellation. It owns no timeline authority.
- **Tunnel Godot binder:** creates sphere/ring/wall nodes, applies policy outputs, forwards input, and
  maintains camera/mount ownership. It performs no domain time or layer-source math.
- **Timeline plugin/face:** owns the command, face-context replay, and HUD visibility. It does not own
  tunnel geometry.

All cross-bundle services are resolved lazily through T1 contracts. No resident-reachable callback,
task, material sink, or face context may retain a collectible generation after sever.

## 9. Error handling and degraded states

- Missing real preview: labeled unavailable sector, no sphere.
- Cancelled/stale preview: no apply and no warning spam.
- Provider exception: one structured warning containing sphere, layer, requested tick, and requested
  rung; the sector remains unavailable.
- Empty registry: cylinder remains visible, readout says `No track`, inner ring is locked, outer ring
  remains usable.
- Focused inactive track: identity remains visible, inner ring is locked.
- Missing mount/controller/camera/stage/world: activation fails closed and HUD stays visible.
- Timeline-only reload: tunnel remains effective if its world/stage generation is still healthy.
- World/stage failure or unload: safe 2D reset; no automatic tunnel resurrection.

## 10. TDD and verification contract

Implementation is RED → GREEN → REFACTOR. Required Godot-free tests include:

1. `CurrentPlaneZ` alignment and future-only tick-to-Z mapping, including `MaxTick` clipping.
2. Camera axial/radial/planet-clearance and normalized projection at `16:9` and `16:10`.
3. Left focus ordering, first-active initial focus, fallback, and inactive lock behavior.
4. Source-kind acceptance: crust/plate/mantle spheres; procedural/unknown unavailable sectors.
5. Cache inequality for requested tick, snapshot tick, rung, dimensions, and graph revision.
6. Fast-path invalidation when graph revision changes.
7. Outer phase at zero, arbitrary ticks, boundaries, playback, and external seeks.
8. Existing outer `±360° = ±1 kb` mapping and one-commit gesture guarantees.
9. Fine truncation toward zero, clamping, sub-tick hold, cadence buckets, and reset behavior.
10. Latest-wins scheduling: one active, bounded start rate, active cancellation, stale completion drop.
11. Inner ring never invokes `PushTick`.
12. Mode sequences for enable success/failure, concurrent/stale command epochs, disable, controller
    loss, timeline reload, world reload, stage reload, and disposal.
13. HUD desired-state replay after face bind.
14. F9 success through the command and inert behavior for missing/failed/stale command paths.
15. Cancellation/sever tests proving no preview or face callback retains the outgoing generation.

Godot seam tests and exported evidence must verify what pure tests cannot: node parenting, unique
per-sphere material ownership with shared immutable textures, hit-plane alignment, actual projection,
camera ownership, real mouse capture, and ALC collection.

## 11. Asymmetric-cockpit-tunnel exported gate

The committed evidence directory contains:

- `README.md` with exact commands, UTC/local timestamps, outcome, and negative results;
- `pid.txt` and process start time;
- executable and installed bundle SHA-256 files;
- complete build/export stdout;
- complete runtime stdout covering the gate window;
- raw request/response JSON for every remote command;
- raw structured gesture records;
- old-ALC collection excerpts for every reloaded collectible bundle; and
- post-action PNG screenshots with SHA-256 values.

Run from one fresh exported process:

1. Record health, PID/start time, executable hash, and installed bundle hashes.
2. Enable through `timeline.tunnel_view`.
3. Capture `16:9` and `16:10` frames proving left focus, right-third large/cropped planet, one
   `CurrentPlaneZ`, visible axial depth, snapshot spheres, exactly two rings, and stationary labels.
4. Perform a real-mouse outer gesture; record angle, requested/committed tick, and post-release phase.
5. Seek externally to zero and a non-zero tick; capture deterministic outer phase after each seek.
6. Perform a real-mouse inner gesture on an active track; capture fine readout/lens change and prove
   authoritative tick unchanged. Focus an inactive track and prove the ring is visibly locked.
7. Perform a real-mouse wall gesture; prove focus changes, labels stay stationary, and globe orbit pose
   remains byte-identical before/after/no-button-motion.
8. Toggle with F9; prove the log records the command path and HUD matches effective state.
9. While enabled, reload `timeline`, wait at least two seconds after the new face binds, and prove the
   2D HUD remains hidden while the tunnel remains visible.
10. Unload/reload `world`; prove HUD appears before teardown, tunnel stays safely disabled afterward,
    and the old world ALC collects.
11. Re-enable, unload/reload `stage`; prove the same safe-2D reset and old stage ALC collection.
12. Re-enable and repeat outer, inner, and wall mouse gestures after all hit-plane/framing changes.

The gate fails if provenance is missing, a screenshot precedes the action it claims to show, an
ingress command substitutes for a real-mouse claim, a reload lacks the exact old-ALC-collected line,
or heterogeneous track rungs are claimed from today's all-`ka` production input.

## 12. Explicitly deferred

- authoritative layer-local time or inner-ring world mutation;
- heterogeneous per-track rung/cadence semantics beyond the real metadata currently present;
- more than four sphere samples per visible track or adaptive density;
- 3D graph/generic presenters;
- real atmosphere/magma-ocean/stagnant-lid preview providers;
- branch/edit/simulate behavior for snapshot spheres;
- ECS worlds per frame;
- carousel inertia and final material/typography polish; and
- automatic tunnel restoration after world or stage reload.

## 13. Negative conclusions to preserve

1. Do not move the real planet off the cylinder axis to manufacture widescreen composition.
2. Do not use `WorldGlobeSnapshot` alone for per-layer spheres; it loses the selected layer's existing
   filmstrip semantics.
3. Do not bind another full `PlanetPresentationDocument` for sample spheres.
4. Do not omit `RequestedTick` from texture identity; governing snapshot tick is only provenance.
5. Do not turn procedural placeholder maps into world-looking spheres.
6. Do not request fine textures on every mouse motion without active cancellation/backpressure.
7. Do not quantize fractional fine motion and present it as authoritative time.
8. Do not add a third ring/current-time dial/per-track dial.
9. Do not let F9 bypass the command, even as a fallback.
10. Do not derive HUD state from requested-but-invisible tunnel state.
11. Do not retain a timeline face context across ALC generations to preserve HUD state.
12. Do not claim completion from tests/builds without the exported evidence manifest.

## 14. Grounding sources

- `vault/specs/2026-07-11-tunnel-timeline-design.md`
- `vault/specs/2026-07-12-rotating-tunnel-two-ring-prototype-design.md`
- `vault/plans/2026-07-12-rotating-tunnel-two-ring-prototype-plan.md`
- `vault/architecture/planet-domain-station-map.md`
- `vault/architecture/cross-alc-rules.md`
- `project/contracts/App.World/LayerFilmstripPreview.cs`
- `project/plugins/App.World/Services/Service.cs`
- `project/plugins/App.Timeline.Seam/FilmstripPreviewController.cs`
- `project/plugins/App.Timeline.Seam/FilmstripTextureCacheKey.cs`
- `project/plugins/App.Timeline.Seam/TunnelCorridorLayout.cs`
- `project/plugins/App.Timeline.Seam/TunnelFinePreviewMapper.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Corridors.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Rings.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Input.cs`
- `project/plugins/App.Timeline/TimelinePlugin.cs`
- `project/plugins/App.Timeline.Seam/TimelineFace.cs`

The design received a read-only Z.AI GLM-5.2 adversarial review followed by multiple fresh-context
doubt passes. Their actionable findings are incorporated above: requested-tick cache identity,
real-source classification, fine-sample quantization/backpressure, radial/planet clearance, and
lifecycle epochs.
