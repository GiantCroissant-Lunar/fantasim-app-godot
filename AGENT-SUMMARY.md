# Agent summary

## Tunnel timeline — 2026-07-12

Implemented and runtime-gated across three arcs: the rotating two-ring prototype, an interior-view
+ HUD-ownership amendment, and the asymmetric cockpit refinement. The current geometry/framing
constants below supersede the earlier prototype's oblique-spectator values.

Durable decisions:

- The tunnel is a real hollow cylinder along local Z (`TunnelCameraFraming`): mouth at Z=0, the
  current-time plane at Z=−5 where the real globe is re-anchored (large at frame center), throat at
  Z=−20, `TunnelRadius` 5. Curved corridors carry real filmstrip content from the current plane
  toward the throat; `TunnelCorridorDepthPolicy` keeps time-bearing walls on `[currentPlane, throat]`
  so none crosses in front of the current plane into the near field.
- Exactly two physical controls. The outer ring owns canonical coarse time; one clockwise revolution
  is exactly `+1 kb`. The single inner ring belongs to the focused track and resolves that real
  descriptor's rung. Both are dials on a small camera-relative **instrument plane** (anchor
  `(-2.2, 0, -4)`, radii 0.38–0.82), not annuli on the physical mouth.
- The visible window is five unique tracks; the focus slot sits **left** of center
  (`LeftFocusAngleDegrees = 180`). Wall rotation is cyclic, clockwise-positive, snapping in 30° steps.
- The inner ring is view-only: it moves a magnified axial fine cursor and signed readout but never
  calls `ITimelineController.PushTick` or persists an offset.
- The interior camera sits near-axial inside the mouth (`LocalPosition (-2.0, 0.6, -0.8)`,
  `LocalTarget (-2.0, 0, -7)`, FOV 60°). Enabling the tunnel **hides the 2D HUD**; F9 routes through
  the `timeline.tunnel_view` command so `TimelinePlugin` owns that HUD visibility, and compose
  re-applies it on every world rebind.
- Input is owned in `_Input` and consumed via `SetInputAsHandled()` to preempt globe orbit. Wall
  gesture angle is measured about the **tunnel axis** (via `TunnelRayHitMapper`), while the two ring
  dials are measured on the instrument plane. A wall pick the opaque planet occludes is rejected.
- World/stage teardown severs input/controller/registry/filmstrip references before detaching the
  mount; `TunnelLossSequence` fences HUD-prep-before-geometry teardown in a `finally` so a HUD-prep
  throw can never skip teardown and pin the ALC. A live enabled reload emits
  `Hot-reload: old ALC collected for bundle world` and the next binder remounts.
- Current production descriptors are all rung `ka`; generic rung rebinding is tested without
  inventing production data.

Evidence:
[`.../2026-07-12-rotating-tunnel-two-ring-prototype/`](vault/specs/evidence/2026-07-12-rotating-tunnel-two-ring-prototype/README.md),
[`.../2026-07-12-tunnel-interior-view-gate/`](vault/specs/evidence/2026-07-12-tunnel-interior-view-gate/README.md),
[`.../2026-07-12-tunnel-review-hardening/`](vault/specs/evidence/2026-07-12-tunnel-review-hardening/README.md).

### Review + hardening pass — 2026-07-12 (late)

A three-agent review of the shipped tunnel drove a bugs + perf + hardening pass (all unit-gated;
full suite green). Fixes: `TunnelFineRequestScheduler` latest-wins hole on re-offering the
cancelled-active key; non-finite guards in `TunnelFinePreviewMapper.Map` and
`TunnelCorridorLayout.SnapFocus` (matching `TunnelScrubMapper`); `TunnelLossSequence` try/finally;
`TunnelInputRelay` now surfaces handler exceptions (new `OnError`) and fails closed instead of
leaking a gesture into globe orbit; wall carousel angle measured about the tunnel axis; planet
occlusion in hit-resolution (`TunnelRayHitMapper.TryIntersectSphere`); nested-press consumed;
per-motion logging demoted to Debug; per-tick corridor activity-style rewrite cached behind an
active-flag mask; `UpdateRingLabels` made idempotent. **Owed:** the live windowed gate (real mouse,
screenshot, ALC-collection) on a fresh export, since the seam changes are resident.

Deferred (design decisions, not landed): moving press acceptance out of `_Input` (would risk the
gated globe-preemption); scroll-wheel/keyboard dial control, hover feedback, and inner-ring
accumulator saturation (input-model enhancements).

Open product question for the next visual refinement: after judging the running view, should the
fine ring (1) mutate shared world time, (2) create a layer-local time offset, or (3) remain a
view-only inspection? Do not infer an answer from the current view-only behavior.
