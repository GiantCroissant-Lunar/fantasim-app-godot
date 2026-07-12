# 2026-07-12 tunnel timeline — review + hardening pass

**Status:** code landed and unit-gated; **live windowed gate OWED** (see below).
**Scope:** bugs + perf + hardening over the shipped tunnel timeline (rotating two-ring prototype +
interior-view amendment + asymmetric cockpit). No product/UX behavior changes.

## How this pass was scoped

A three-agent review read the tunnel across three dimensions — binder lifecycle/corridors,
input/rings/gestures, and the Godot-free seam policies + their tests. The seam review ran the full
tunnel unit suite (165/165 at the time). Findings were triaged into: correctness/safety bugs,
cheap hardening, performance, input-model enhancements (design decisions), and docs/hygiene. The
user selected the **bugs + perf + hardening** batch; enhancements were deferred as design work.

## Fixes landed (each TDD where unit-testable)

### Correctness / safety
1. **Fine-request scheduler latest-wins hole** (`TunnelFineRequestScheduler.Offer`) — re-offering the
   currently-active-but-cancelled key was a no-op, so scrubbing the fine ring back onto an in-flight
   bucket left the superseded pending request to start and the desired preview never rendered until
   another gesture. Now the re-offer re-queues the key as pending. Pinned by
   `Reoffering_the_cancelled_active_key_requeues_it_so_latest_wins_holds`.
2. **Loss-sequence ALC-pin risk** (`TunnelLossSequence.Run`) — HUD prep ran before teardown with no
   `try/finally`; a throw in the HUD callback skipped the entire mount/relay/frame teardown and would
   pin the outgoing world ALC. Teardown is now fenced in `finally` (failure still rethrown). Pinned by
   `HudPreparationThrow_StillPerformsGeometryTeardown_ThenRethrows`.
3. **Relay swallowed exceptions + gesture leak** (`TunnelInputRelay._Input`) — a bare
   `catch { handled = false; }` hid fail-loud seam guards and let a half-owned gesture fall through to
   a concurrent globe drag. Now surfaces via a new severable `OnError` delegate (wired to the binder
   logger), cancels the gesture, and fails closed by consuming the faulted event.
4. **Wall carousel angle about the wrong center** (`TunnelPresentationBinder.Input`) — the wall drag
   measured its angle on the off-axis instrument dial plane but rotated about the tunnel axis, so a
   move near the dial singularity could snap several 30° steps at once. Wall gestures now measure the
   pointer angle about the **tunnel axis** (`TunnelRayHitMapper.TryIntersectMouthPlane` on an
   axis-perpendicular plane); rings keep the instrument-plane angle.
5. **Planet-click starts a carousel drag** — under the interior camera nearly every ray also hits the
   wall, so clicking the opaque planet silently began a wall gesture. New Godot-free
   `TunnelRayHitMapper.TryIntersectSphere` rejects a wall pick the planet occludes (6 unit tests).
6. **Nested left-press fall-through** — a second press without an intervening release now is consumed
   instead of falling through to a concurrent globe drag.

### Hardening (match the seam's fail-loud discipline)
- Non-finite `accumulatedDegrees` now throws in `TunnelFinePreviewMapper.Map` and
  `TunnelCorridorLayout.SnapFocus` (were silently propagating NaN / casting to a garbage long),
  matching `TunnelScrubMapper.MapOuterAngleToTick`. Pinned by new theory tests.
- `TunnelFineRequestScheduler` main-thread-confinement contract written into its doc comment.

### Performance
- Per-motion structured logs (outer/inner/wall) demoted from Information to Debug behind
  `IsEnabled(LogLevel.Debug)` guards — they emitted hundreds of boxed records/sec during a drag; the
  wall log's per-motion `SnapFocus` recompute now sits behind the same guard. Press/commit/release
  boundary logs stay at Information as the gate's evidence trail.
- `UpdateCorridorActivityStyles` caches an active-flag bitmask and early-outs before rewriting any
  corridor material when activity is unchanged (the common case every drag frame). Invalidated
  wherever `_corridorNodes` is rebuilt.
- `UpdateRingLabels` made idempotent (`EnsureReadoutLabel`) — it reallocated three `Label3D` nodes
  per call; now creates once and updates text in place.

## Unit verification

Full suite green after the pass (`task test`): App.World 569, App.World.Composition 107,
App.Timeline 318, App.Presentation 173, App.Camera 30, App.Render 46, App.NodeGraph 60, plus the
smaller projects — **0 failures**. The tunnel + coalescer focused set went 165 → 172 → (with the new
ray/sphere and hardening tests) higher, all passing.

## OWED — live windowed gate

The seam changes (`App.Timeline.Seam`, and the `App.Presentation` binder) are resident/collectible
but the seam is **resident**, so the live gate needs one **fresh full export**, not a bundle
hot-reload. On that build, re-run the tunnel acceptance: enable via `timeline.tunnel_view` / F9,
screenshot the oblique interior, exercise real-mouse wall / outer / inner gestures (confirming the
wall carousel no longer over-snaps and clicking the planet does nothing), and drive a live enabled
world reload to confirm `Hot-reload: old ALC collected for bundle world` with no pin. Record the
result here.

## Deferred (design decisions, not landed)

- Moving press acceptance out of `_Input` (would risk the gated globe-preemption the tunnel relies on).
- Input-model enhancements: scroll-wheel / keyboard dial control, hover feedback before press, and
  saturating the inner-ring accumulator (currently winds invisible debt past ±360°).
