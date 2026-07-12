# Agent summary

## Rotating tunnel two-ring prototype — 2026-07-12

The approved prototype is implemented and runtime-gated.

Durable decisions:

- The tunnel is a real hollow cylinder along local Z, with the mouth at Z=0 and throat at
  Z=-14. Curved corridors carry real filmstrip content toward the existing globe at the throat.
- Exactly two physical controls exist. The outer ring owns canonical coarse time; one clockwise
  revolution is exactly `+1 kb`. The single inner ring always belongs to the bottom-center focused
  track and resolves that real descriptor's rung.
- The visible window is five unique tracks. Wall rotation is cyclic, clockwise-positive, and snaps
  in 30° steps; only the bottom-center track owns the inner ring.
- The inner ring is deliberately view-only in this prototype. It moves a magnified axial fine
  cursor and signed readout but never calls `ITimelineController.PushTick` or persists an offset.
- The dedicated tunnel camera uses FOV 60°, local position `(12,8,32)`, and local target `(0,-9,-8)`.
  This leaves the real Timeline HUD visible while making mouth, wall, throat, and globe
  eye-judgeable.
- Tunnel input owns accepted press/motion/release through HUD crossings. Runtime camera snapshots
  prove no globe-orbit motion or stale drag for wall, outer, or inner gestures.
- World-bundle teardown severs input/controller/registry/filmstrip references before detaching the
  mount. A live enabled reload produced `Hot-reload: old ALC collected for bundle world` and the
  next binder remounted successfully.
- Current production descriptors are all rung `ka`; generic rung rebinding is tested without
  inventing production data.

Evidence is in
[`vault/specs/evidence/2026-07-12-rotating-tunnel-two-ring-prototype/`](vault/specs/evidence/2026-07-12-rotating-tunnel-two-ring-prototype/README.md).

Open product question for the next visual refinement: after judging the running prototype, should
the fine ring (1) mutate shared world time, (2) create a layer-local time offset, or (3) remain a
view-only inspection? Do not infer an answer from the prototype's temporary view-only behavior.
