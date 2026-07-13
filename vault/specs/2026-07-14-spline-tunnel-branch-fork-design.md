# Spline tunnel, fork-in-place branches, and causal ribs — design

**Status:** approved in conversation 2026-07-14 (user: "fork in place" + tunnel-shooter/spline
framing; verbs confirmed audit > compare > switch; slice order and stationary-first camera
delegated to lead recommendation). Slice 1 is buildable now; slices 2-3 are directional and get
their own concept-lock before implementation.

**Scope:** how the tunnel timeline represents the `branch` axis of `TruthStreamIdentity`
(variant, branch, L, domain, model) and the mantle→plate→crust causal relation, and the first
implementation slice (bent spline bore).

**Related authority:**
- `vault/specs/2026-07-11-tunnel-timeline-design.md` (Concept A tunnel; no branch treatment)
- `vault/specs/2026-07-13-canonical-world-history-and-dry-crust-design.md` §5.2 (branch
  ancestry), §6 (two authoring directions), Phase B/D roadmap
- `vault/specs/2026-07-07-layer-presentation-input-parity-canonical-units-directives.md`

## 1. Decision

The tunnel becomes a **spline-bore graph** rather than a straight cylinder, and branches are
**fork-in-place junctions** on that graph. Causality is a separate, angular relation rendered as
**ribs between corridors**, not tunnel topology.

User-confirmed verbs, in priority order: **audit** (lineage truth at the fork: parent cursor,
source digest, parser/params) is the daily verb; **compare** is occasional and transient (a
summoned ghost at a seam, never a permanently forked view); **switch** is plumbing (steer into a
throat).

### 1.1 Honesty constraints (binding)

1. **Flying is scrubbing.** Camera/bore arc-length maps deterministically to canonical tick.
   No momentum, no physics, no position that is not a tick. If "where am I" and "what tick is
   it" can ever disagree, the design is broken.
2. **Fork placement is truth, not level design.** A junction sits at exactly the
   `world.branch-created.v1` cursor's tick. The shared prefix renders as ONE bore — parent
   events are composed, never duplicated. Sibling suffixes render as capped **throat stubs**
   (a few rings past the seam) until entered.
3. **Curvature must not eat legibility.** Parallel-transport frames (no twist/roll
   accumulation), a curvature cap so rung rings still read as rings, deterministic curvature
   (seeded, never wall-clock/random at runtime), and the instrument layer (two-ring control,
   readouts, headers) stays rig-anchored exactly as shipped.

### 1.2 Causal ribs (Phase D tie-in, separate slice)

Corridors order around the ring by causal lineage (mantle · plates · crust adjacent). Short
directional ribs run between corridor wedges: **solid = causal forcing** (plates→crust
transport/boundaries), **dashed-reverse = reconstruction** — today's mantle corridor honestly
renders plates→mantle dashed because `MantleHistoryAdapter` reconstructs convection FROM plate
boundaries. When the mantle-first producer lands (canonical spec Phase C), the stored lineage
flips the rib to solid mantle→plates with no presentation redesign. Rib direction must derive
from stored lineage/event cursors (Phase D), never from hardcoded labels.

## 2. Slice map

- **Slice 1 — bent bore (THIS SLICE, buildable now):** replace the straight cylinder with a
  gently curved spline bore. Stationary occupant camera, zero branch dependency, de-risks all
  frame/legibility math. Also solves the 2 kb ring-crowding problem by line-of-sight
  self-occlusion instead of an artificial falloff curve.
- **Slice 2 — engine branch-created (fantasim-world):** minimal §5.2 — `world.branch-created.v1`
  record + parent-cursor validation + parent-prefix composition; upgrade the different-source
  import conflict ("new branch required") into actually creating a branch. Own concept-lock and
  parity/oracle treatment before implementation.
- **Slice 3 — junctions:** fork seams + throat stubs at real branch ticks, steer-to-switch
  (camera swings into the chosen throat, branch rematerializes through its composed history),
  audit panel at the seam, transient compare ghost. Requires slice 2. No demo/fake junctions
  ever ship (house no-smoke rule).
- **Flight mode (deferred):** camera rides the playhead on the same spline substrate. Pure
  navigation-feel risk with no new information content; held until the bent bore passes the
  user's eye.
- **Causal ribs (independent):** can land any time after slice 1; direction wiring becomes
  honest automatically via Phase C/D.

## 3. Slice 1 concrete design — bent bore

### 3.1 New pure module

`project/plugins/App.Presentation/Tunnel/TunnelBoreSpline.cs` — Godot-free, UnifyMaths types
(`Vector3D`, `Quaternion`), same style as `TunnelCameraFraming`/`TunnelCorridorLayout`.

Contract:

- `TunnelBoreSpline.Create(long seed, double straightRadius, double curvatureCapRadPerUnit, double maxDepth)`.
- `Evaluate(double signedDepth)` → `(Vector3D Position, Quaternion Frame)` where `signedDepth`
  is measured from the current-tick plane in the existing tunnel depth units.
- **Near-field is exactly straight:** for `|signedDepth| <= straightRadius` the result is
  identical to the current straight bore (position on the axis, identity frame). All shipped
  interaction (ring scrub, wall carousel picking, planet occlusion, camera framing) operates
  inside this window, so `TunnelRayHitMapper`, `TunnelPointerAngleSourcePolicy`,
  `TunnelPlanetOcclusionPolicy`, `TunnelCameraFraming` and the input relays are untouched.
- Beyond the straight window, curvature ramps in C1-continuously and is capped. Curvature at a
  given absolute tick is a smoothed deterministic function of `(seed, rung(tick))` so the bend
  at a tick is stable across scrubs and sessions; scrubbing makes the bore appear to flow
  through the bend without any camera motion.
- Frames use parallel transport along the curve (no roll accumulation); arc-length is
  monotonic in depth.

### 3.2 Binder mapping

- `TunnelPresentationBinder.Rings.cs`: rung-ring planes placed at `Evaluate(depth)` position
  with the transported frame instead of straight-Z offsets.
- `TunnelPresentationBinder.Corridors.cs`: corridor wall quads / filmstrip frames follow the
  same frames (per-segment placement; corridors bend with the bore for free).
- The seed is the stable hash of the active branch's stream-identity branch axis (today:
  `main`), so future branches naturally get distinct but deterministic bends.
- Interactive depth window asserted ⊆ straight window by test, so no input mapper changes.

### 3.3 What does NOT change in slice 1

Camera (stationary occupant), two-ring control, readouts/headers and their safe-bounds
contract, planet zoom/occlusion, scrub semantics and the fine-preview scheduler, all input
paths, the 2D face, bundle topology. No new bundle, no `collectible-bundles.json` change.

## 4. Acceptance gates (slice 1)

1. Pure-module tests (App.Presentation.Tests): near-field exact straightness; determinism
   (same seed → identical frames across instances); curvature cap honored at sampled depths;
   parallel-transport twist bound (accumulated roll < tolerance over full span); C1 continuity
   at the straight/curved boundary; monotonic arc-length; interactive-window ⊆ straight-window
   contract test against the shipped constants.
2. Existing App.Presentation.Tests + App.Timeline.Tests suites stay green (framing, input,
   occlusion, corridor policies untouched).
3. Windowed gate (lead): fresh export or hot-reload per changed tiers; tunnel enabled via F9 /
   `timeline.tunnel_view`; far bore visibly curves while near-field rings/instruments are
   unchanged; scrub flows the bend correctly; screenshot evidence; `old ALC collected` for any
   reloaded bundle. **User eye-judgment owns the curvature feel** (cap/straight-radius are
   hot-tunable constants for the sitting).

## 5. Deferred decisions (deliberate)

- Curvature semantics beyond aesthetics (e.g., bend keyed to a world metric) — YAGNI until the
  eye pass.
- Junction/seam visual language, audit-panel layout, compare-ghost form — slice 3 concept work.
- Flight-mode navigation feel — after slice 1 eye-judgment.
- Whether branch stubs preview sibling wall content or render as neutral throats — slice 3.
