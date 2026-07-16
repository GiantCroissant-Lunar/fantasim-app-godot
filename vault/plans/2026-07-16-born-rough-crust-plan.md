# Plan: born-rough crust — plate-anchored birth roughness (directive 3d, "not egg shell")

**Source:** `vault/specs/2026-07-16-layer-first-presentation-directives.md` round-3
refinement 3d. Crust must read rough from the FIRST tick it exists, in every crust-bearing
regime (stagnant-lid included); collision orogeny then grows ON TOP of that. Absolute relief
scale is LOCKED (round-3 decision) — birth roughness is small-but-present texture, not fake
mountains.

**The defect this also retires:** the WorldPeaks interior fabric is sphere-fixed — the noise
is sampled in a frame that does not rotate with plates, so terrain texture stays put while
plates drift under it (documented limitation, 2026-07-02 arc spec §5c-i). Crust texture must
be a property OF the crust.

**Design decisions (locked):**
1. **Plate-material sampling frame.** Birth roughness at a vertex = noise sampled at the
   vertex's PLATE-LOCAL coordinate: rotate the current unit position BACK by the owning
   plate's rotation-at-tick (the motion spine reconstructs per-tick rotations — that data is
   the frame). Texture then rides each plate by construction. Test: advance the tick, rotate
   a plate; the sampled roughness field, expressed in the plate frame, is BIT-IDENTICAL.
2. **Derived + deterministic (no truth change).** Pure function of (plate-material coord,
   crust age at tick, world seed, declared params). Uses the house primitive
   `UnifyMaths.Numerics` FbmNoise3/GradientNoise3 (shipped 2026-07-16 @ae6bb90, bit-exact
   port of NoiseRelief) — do NOT hand-roll another noise. No query-history dependence.
3. **Conditioning (terrain-diffusion adoption):** amplitude scales with crust AGE at the
   sampled tick (newly solidified crust: base solidification texture; older crust:
   accumulated battering — a declared monotone ramp with declared floor/ceiling), modulated
   by continental fraction where available. All parameters DECLARED on a profile record
   (mirror SlabTopReliefProfile's shape); no magic constants at call sites.
4. **Amplitude budgets:** in the WORLD view the birth roughness is the interior fabric and
   MUST live inside the north-star budget (interior <= 0.15x belts; silhouette <= 0.5%R —
   the existing clamps stay in charge). In the slab/exploded view it composes with
   SlabTopReliefProfile's declared scale. The 250 m residual cap
   (TectonicDetailSampler.MaxResidualAmplitudeMetres) is CURRENT INTENDED design for the
   detail path — birth roughness enters as part of the ELEVATION-side signal (like
   CellElevations), not by raising that cap.
5. **Exists in every crust-bearing regime.** Stagnant-lid crust (pre-plate) has no plate
   rotations — its material frame is the base sphere frame (identity), which is correct:
   nothing moves yet. The field must not vanish or discontinuously jump at the
   stagnant-lid -> mobile-plate transition (plates inherit the base-frame texture at birth;
   test the continuity at the onset tick).

**Where (discover exact seams, these are the anchors):** the elevation-side composition that
feeds both the World path (`PlanetPresentationBinder.PlateSurface.cs` elevations resolution)
and the slab path (`SlabTopReliefComposer.BuildCaps` elevationsByCell) — birth roughness is
an additive, deterministic per-vertex (or per-sample) elevation component derived from the
snapshot. Plate rotation-at-tick: discover what the presentation snapshot
(`WorldGlobeSnapshot`) exposes; if per-plate rotations are NOT reachable from the
presentation layer, STOP that sub-task and record in AGENT-SUMMARY.md exactly what contract
data is missing (do not smuggle engine access in).

**TDD order:**
1. Failing test: birth-roughness field is a pure function — identical (coord, age, seed,
   params) -> bit-identical values; different seeds decorrelate.
2. Failing test: PLATE-ANCHORING — sample a vertex on a plate at tick T and at tick T+dT
   (plate rotated); expressed in the plate-material frame the values are bit-identical.
3. Failing test: age conditioning — amplitude at age 0 equals the declared floor; grows
   monotonically to the declared ceiling.
4. Failing test: onset continuity — the field at the last stagnant-lid tick equals the field
   at the first mobile-plate tick for unmoved plates.
5. Implement; wire into World + slab elevation composition inside the declared budgets.
6. Full suite green.

**Out of scope:** truth/engine changes; raising the 250 m detail cap; auto-fit scaling (user
rejected); LOD/tessellation; tunnel; project.godot; vault edits.

**Acceptance (agent):** the four TDD invariants green + full suite; git status clean of
unintended files. **Acceptance (lead windowed + USER EYE, the final gate):** at an EARLY
crust tick (stagnant-lid, e.g. tick 50M) the planet reads as a rocky body — visible noise
texture, no eggshell — in both World and slab views; scrubbing across onset shows no texture
pop; at late ticks mountains ride ON TOP of the birth texture; texture visibly MOVES WITH a
drifting plate across a multi-Ma scrub (the sphere-fixed defect's disproof).

**Agent constraints:** assigned worktree only; NO commits/pushes; no export/bundle/install;
absolute paths; fantasim-cartography READ-ONLY (report needed builder changes instead);
UnifyMaths.Numerics is consumed as the house noise — if the package/reference is not already
available to the target projects, record what reference addition is needed rather than
vendoring a copy.
