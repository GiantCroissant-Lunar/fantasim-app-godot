# Slice-3 subduction tongue — windowed gate (2026-07-17 ~10:57)

The renderer-as-reference milestone: after five image-generation attempts failed to compose
the overlap relationship (registry doc, track closed), the app itself now renders it from
real plate data. zai glm-5.2 dispatch (watertight tongue extrusion), lead integration.

## Identity
- main @ a23fca1 (merge a49eee2 of feat/slice3-subduction-tongue: 2b5a671 params, 492a364
  TDD scaffold, 88fa596 tongue geometry; then binder wiring a23fca1).
- App.World suite 757/757 (743 + 14 tongue proofs incl. watertight-by-edge-counting,
  no-interpenetration, exploded bit-identical translation).
- Export rebuilt; relaunch PID 54055 lsof-verified; ingress 4s; 0 fatal/unhandled.
- Boot log: `World slab joints shaped: joints=24, convergent=3, tongues chained.`

## What the shots show
- `50-slice3-assembled-closeup.png` — assembled: tongues dive into the trench (mostly hidden
  by design; tip sliver visible at the joint).
- `51/52` — exploded 0.22: joints as open channels, tongue tabs along boundaries like zipper
  teeth, slab walls lit.
- `53-slice3-maxexplode-low.png` — exploded 0.35 low angle: tongue teeth in shadow along the
  open joint; the atmosphere rim visible THROUGH the channel (the joints are true open space
  between thick pieces, not surface cracks).

## Honest deltas
1. The unambiguous "A's surface continuing UNDER B's lip with light between" money shot is
   still not captured — 3 convergent joints among 24, no camera targeting; blind orbit hunts
   are wasteful. NEXT TOOL: `camera.frame_joint {plateA,plateB}` ingress command.
2. Tongue reach 0.05R / drop 0.06R are first-guess eye-tune values.
3. Surface albedo still soft (1-kb product) — unchanged from the eye-tune-1 queue.
The app is left open at max explode for the user's own orbiting.
