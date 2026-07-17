# Reference-match arc — windowed gate (2026-07-17 ~11:51)

User's final directive: match `vault/reference/2026-07-17-user-reference-assembled-final.png`
from the app; stop deriving from sim numbers. Three commits of eye-numbers:

- @dbd984d displayed-thickness floor 0.12R (chunks regardless of engine metres)
- @14d10c5 relief amplitude 1400m, joint gap 0.035R, emissive molten interior under both slab
  families (x2 scale bug found and fixed — the slice-4 core had been invisible)
- @89062e8 THE unlock: TectonicDetailSampler residual cap 250->1500m, interiors 0.28->0.65 —
  the calm-interiors 1/3-law retired in both doctrine tests (boundary legibility now = joint
  geometry). The sampler SUPERSEDES base noise at vertices; every prior amplitude change was
  a no-op against the 250m cap. Suites 757/757 + 253/253.

## Verification chain (per the don't-lie standard)
Two silent edit failures were caught by verifying renders against claims: the interior-
multiplier replace that matched zero sites, and the sampler-supersedes discovery. PID 24114
lsof-verified; in-app capture `72-refmatch3-assembled.png` + OS-level screenshot at claim time.

## Self-verdict vs the acceptance image
FIRST render in the reference's family: chunky faceted rock across the WHOLE surface, knobbly
silhouette (limb visibly jagged), molten glow in the widest seam. `70` (before) vs `72`
(after) shows the sampler unlock doing what four days of clamped iterations could not.
REMAINING (eye-tune queue): boulder CLUSTERING (larger-scale masses vs uniform bumpiness),
glow coverage in narrower seams (raise interior sphere 0.86->0.9R), blue atmosphere-shell
slivers clipping at the limb, banded hypsometric colors, per-plate hue variation.
