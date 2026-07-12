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

## Live windowed gate — deterministic half PASSED (fresh export)

Ran on a **fresh full desktop export** (`0.1.2`, resident common layer stripped/provisioned;
`task build:godot:desktop` → `task bundles` → `task bundle:install`) because the seam is resident.
App PID 88760, log `/private/tmp/fantasim-tunnel-hardening-gate/app.log`.

- **Enables + renders** — `timeline.tunnel_view {enabled:true}` → `effective:true`; the interior view
  renders correctly with the resident-seam changes baked in: planet large at center, both dial rings
  with handle markers, five corridors, outer readout `tick 60000000 | kb`, focused readout
  `Stagnant Lid | ka | 0 ticks — active at current time`, activity ledger `0 failed`
  (`interior-enabled.jpg`).
- **Live world reload while enabled — ALC collected, no pin** (`world-reload-segment.log`), the exact
  required sequence and zero tunnel exceptions in the segment:

  ```
  Bundle unloaded: world
  Bundle loaded: world from .../bundles/world.pck
  resource.reload_bundle: reloaded 'world'.
  Hot-reload: old ALC collected for bundle world
  ```

  This exercises the teardown path this pass changed (`TunnelLossSequence` try/finally, relay
  `OnError`/sever): **no ALC pin was introduced.**
- **Re-mounts + re-enables on the fresh binder** — post-reload `timeline.tunnel_view {enabled:true}`
  → `effective:true`; ledger shows `resource.reload_bundle.result ok` → `world.presentation.rebound
  rebind scheduled` → `timeline.tunnel_view result ok`, `0 failed` (`post-reload-reenabled.jpg`).
- **Disable restores the globe cleanly** — `camera.debug` after disable:
  `activePcamPath = …/PCam_globe_default`, `draggingNow = False`, `dragMotionsApplied = 0`,
  `orbitDistance = 4.0` (the baseline) — no leaked drag state from the input changes.

### Still owed — real-mouse sitting (needs the user's hands)

The app is left running (PID 88760) with the tunnel enabled. The one part a command run cannot cover
is OS-mouse gesture verification: (1) a **wall** drag should snap the carousel one step per ~30° and
**not** over-snap near the dial center (the axis-angle fix); (2) clicking the **planet** should do
nothing (the occlusion fix); (3) an **inner-ring** drag should move the fine cursor/readout with the
authoritative tick unchanged. Confirm those three and this gate is fully closed.

## Deferred (design decisions, not landed)

- Moving press acceptance out of `_Input` (would risk the gated globe-preemption the tunnel relies on).
- Input-model enhancements: scroll-wheel / keyboard dial control, hover feedback before press, and
  saturating the inner-ring accumulator (currently winds invisible debt past ±360°).
