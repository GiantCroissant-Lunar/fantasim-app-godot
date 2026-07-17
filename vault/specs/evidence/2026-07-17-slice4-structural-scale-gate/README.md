# Slice-4 structural scale — windowed gate (2026-07-17 ~11:25)

The verified diagnosis behind every rejected render (user challenge: "you should verify it
and see the issue immediately" — upheld): (1) crust at near-realistic 8x exaggeration =
0.038R TILES against the user's explicit non-realistic-scale directive; (2) a planet-size
sphere stayed visible beneath the exploded slabs (recorded in the slice-1 report as a
"pre-existing latent issue" and wrongly left standing) — every render read as a cracked
ball wearing a skin, never as pieces forming a planet.

## Changes (@16265dc)
- CrustThicknessExaggeration 8 -> 36 (0.17R chunks; ratio-locked slab relief scales with it;
  ratio test pin moved with the eye decision, documented in-test).
- _plateSurfaceRoot hidden while the exploded slab family is active (ball-under-skin fix).
- Exploded interior = 0.55R core sphere (structure; material eye-tuned later).

## Identity
main @16265dc; App.World 757/757; export rebuilt; PID 76235 lsof-verified; ingress 4s.

## Self-verdict vs the reference registry (lead's own comparison, before the user's eye)
NOW MATCHES structurally: pieces read as thick chunks (walls visible at every joint and at
the limb); exploded state opens onto true interior space, not a ball; the planet reads as
assembled from pieces in both states.
STILL SHORT: surface albedo remains 1-kb soup (no material identity, no banding); joint
edges are cell-staircase zigzags, not formed edges; slab-top relief still shallow relative
to the references' mountains. These are the next eye-tune targets — none is structural.
