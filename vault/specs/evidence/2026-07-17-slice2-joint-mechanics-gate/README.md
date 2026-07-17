# Slice-2 joint mechanics — windowed gate (2026-07-17 ~08:09)

Convergent/divergent/transform mechanics in the World slab assembly, dual-dispatched
(zai glm-5.2 geometry + ollama-cloud glm-5.2 classifier), lead-unified and binder-wired.

## Identity
- main @ f9065e2 (b80848f classifier → 8ce17d2+33e883b mechanics → c6db63a seam unification
  → f9065e2 binder wiring). Full workspace suite green (18 assemblies; App.World 743/743).
- Export rebuilt 08:05:54; relaunch PID 27541, lsof-verified, ingress 7s, 0 fatal/unhandled.

## Lifecycle evidence (boot log, committed tick 100M)
`World slab joints shaped: joints=24, convergent=3.` followed by
`World slab assembly mounted: plates=10, jointGap=0.006R.`

## Visual evidence
- `30-slice2-world-joints.png` — whole globe, joints as seams (distance 6).
- `32-hunt-y270-p-30.png` — sunlit face: joints read as 3D grooves with lit slab-edge walls.
- `33-convergent-closeup.png` (+ OS-level screenshot in session transcript, frontmost PID
  verified) — the convergent signature at distance 1.9: one slab edge as a LIT RAISED WALL
  (overriding margin), opposite side dropping into the dark groove beneath it (subducting dip);
  trench-like shadow wedge at the dive line.

## Honest deltas (the user-eye fine-tune queue)
1. The underride reads as raised-wall-over-dark-dip; an unambiguous "subducting shelf sliding
   beneath an overhang" money-shot needs the declared knobs moved (dip 0.06R, raise 0.012R,
   band 0.12 rad, divergent x2.5, clearance 0.004R) — that tuning belongs to the user's eye.
2. Camera cannot target a named joint; hunting was manual orbits. Follow-up: a
   `camera.frame_joint {plateA,plateB}` ingress command for gates.
3. Slab tops still carry the 1-kb scrub-preview blur at close range (albedo, not geometry).
4. Boundary staircase zigzag unchanged (cell-resolution joints; smoothing is its own slice).
