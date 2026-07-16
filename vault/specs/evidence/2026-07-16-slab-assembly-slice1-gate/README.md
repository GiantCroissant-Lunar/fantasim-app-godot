# Slab-assembly World view — slice 1 windowed gate (2026-07-16 ~23:21)

First slice of the assembled-world north-star (vault/specs/2026-07-16-assembled-world-northstar.md):
the DEFAULT World view now mounts the per-plate solid slab assembly with a declared joint gap,
watertight sphere demoted to a fallback flag.

## Identity
- Code: main @ d9613b9 (3 commits: 537baf4 declared params, e9fef6c shared slab emission
  extraction, d9613b9 default World mounts assembly). Full suite 1,807 passed / 0 failed.
- Export rebuilt 23:19:06 (task build → build:godot:desktop → bundles → bundle:install).
- Relaunch: PID 77273, lsof-verified exe, ingress 4s, 0 fatal/unhandled.

## Lifecycle evidence
- Boot (magma-ocean, tick 0): NO mount line — correct, no crust snapshot pre-onset.
- After scrubCommit seek to tick 100M (mobile-plate):
  `World slab assembly mounted: plates=10, jointGap=0.006R.`

## Visual evidence
- `20-slab-world-default.png` (in-app capture) + OS-level screenshot in session transcript
  (23:21, frontmost PID 77273 verified): the default World globe renders with visible dark
  joint seams between 10 slabs. No layer selected, no exploded factor — this IS the world now.

## Honest deltas (fine-tune queue, user's eye owns all of these)
1. Joints read as thin cracks at 0.006R from distance 6 — declared parameter, tune by eye.
2. Slab tops still blurry-smooth tan: 1-kb scrub-preview albedo dominates + timid relief
   amplitudes (BirthRoughnessProfile 60–200 m, SlabTopRelief interior 0.15×) — amplitude
   re-tune is the next fine-tune conversation, now against a slab path with NO 0.5%R clamp.
3. Silhouette still circular at this distance (amplitudes, not clamp — slab path is uncapped).
4. No joint mechanics yet (underride/trench/mountain piling) — slice 2 per the north-star.
5. Boundary staircase (cell-resolution zigzag) unchanged.
6. Non-uniform joint width (centroid-translation geometry) — future uniform-retraction slice.
