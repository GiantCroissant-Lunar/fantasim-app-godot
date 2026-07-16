# Plan: formed relief on slab tops (directive 3b — "the flat plate is not what we want")

**Source:** `vault/specs/2026-07-16-layer-first-presentation-directives.md` §3 + round-2
refinement 3b (user verdict 2026-07-16: slab tops render smooth; mountains/trenches/volcanoes
must read as FORMED by convection/boundary processes; Keeter-style bulk at crust scale;
biomes out of scope).

**The marriage, not new machinery.** All four ingredients exist and drive the World view;
none is applied to the exploded/mantle-layer slab presentation:
1. Signed broad relief — `CellElevations` (canonical dry-crust design, 2026-07-13).
2. Boundary-conditioned detail — `TectonicDetailSampler`
   (`project/contracts/App.World.Rendering/Globe/TectonicDetailSampler.cs`): ridged belts on
   convergent boundaries, quiet interiors, per-feature-kind amplitude/frequency — this IS
   "formed by mantle convection" made visible, and it is already a pure position→context
   sampler.
3. Banded hypsometric ramp + tone split (planet-look north-star,
   `vault/specs/2026-07-05-planet-look-north-star.md` — silhouette budget stays ≤0.5%R for
   PLANET views; slab views have their OWN declared scale, see next point).
4. Crust-scale thickness — `RadialSectionProfile` (D3, ratio-locked crust:mantle proportion).

**Design decisions (locked for this slice):**
- Slab TOP surfaces in the mantle-layer/exploded composition sample the same elevation truth
  + detail sampler + ramp as the World view, with displacement scaled by the slab view's OWN
  declared exaggeration profile (S1/S2 discipline: declared parameter + on-screen profile
  label; do NOT reuse the world-view lens blindly — slabs are already radially exaggerated
  via RadialSectionProfile, so the relief scale must compose with it, ratio-locked).
- Slab WALLS get lighting so thickness reads (the M-B open item "wall lighting") — lit
  material with the section profile's strata tint, not flat black.
- **Terrain-diffusion adoption (hub deposit `fantasim-hub/vault/research/
  2026-07-16-terrain-diffusion-evaluation.md` §3):** (a) detail is CONDITIONED ON COARSE
  CAUSAL CONTEXT — the sampler's inputs stay the per-cell feature bundle (boundary kind,
  weight, elevation), never position-only noise; document the conditioning schema at the
  sampler call site. (b) every sampled product is a PURE FUNCTION of
  (cell/position, tick, seed, declared params) — deterministic identity, no query-order or
  camera dependence. Assert both in tests.
- Eye gate (lead + user) judges against BOTH references: Sketchfab (thick separable slabs,
  boundary legibility) and Keeter (relief bulk at scale). Agent does NOT self-certify look.

**Code anchors:** the mantle-interior/exploded composition —
`project/plugins/App.Presentation/MantleInteriorViewComposer.cs`,
`PlanetPresentationBinder.MantleViews.cs` (post-x-ray-retirement state @e4a217e),
`PlateSurfaceMeshFactory.cs` / `TectonicDetailSampler` wiring in the World path (copy the
pattern, don't fork the sampler), `RadialSectionProfile` consumers.

**TDD order:**
1. Failing test: slab-top mesh generation invokes the detail sampler with the cell's feature
   context (assert sampler inputs, Godot-free).
2. Failing test: identical (cell, tick, seed, params) → bit-identical slab-top heights;
   different boundary kinds → distinct relief character (ridged vs quiet), asserting the
   conditioning schema.
3. Failing test: slab relief scale composes with RadialSectionProfile under the declared
   ratio (pin the ratio; no double-scaling — the slab x4 double-scale lesson).
4. Implement slab-top displacement + ramp coloring; then wall lighting/material.
5. Full suite green.

**Out of scope:** LOD/tessellation changes (separate slice); tunnel work; plate-motion
rebinding (3c — next slice); biomes; any engine/truth change; project.godot.

**Acceptance (agent):** suite green; sampler-conditioning + determinism + ratio tests in
place. **Acceptance (lead + user eye):** windowed — exploded/mantle view shows slab tops with
visible belts/mountains at convergent boundaries, quiet interiors, banded ramp; walls lit;
S2 label names the slab profile; screenshots against both references.

**Agent constraints:** assigned worktree only; NO commits/pushes; no export/bundle/install
tasks; no vault/ edits; fantasim-cartography is READ-ONLY reference — if a builder change is
genuinely required, STOP that sub-task and record the exact needed change in
AGENT-SUMMARY.md; absolute paths for shell ops.
