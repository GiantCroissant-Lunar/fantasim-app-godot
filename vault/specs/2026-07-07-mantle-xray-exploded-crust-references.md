# Design references: mantle x-ray view + exploded crust (concept seed)

**Status: REFERENCE-LOCK (2026-07-07). User-supplied visual targets for the next render arc(s);
not yet a dispatched plan. Companion to `vault/plans/2026-07-07-gplates-truth-playback-and-viewport-systems.md`.**

## The three visual references (user-supplied)

1. **GPlates surface motion** — Cao et al. 2024, 1.8 Ga reconstruction video. Target for
   *surface* motion character: many crisp varied landmasses, stately rates, smooth interpolation.
   (Data: Zenodo 13340841. Already the anchor of the P-packet arc.)
2. **Mantle interior** — CitcomS-style assimilated-flow imagery (Cao et al. 2021 line): ghosted
   surface with coastline/boundary wireframe; interior isosurfaces of thermal anomaly — blue cold
   slabs sinking under trenches, yellow/red plumes rising between. Plates→mantle causality.
3. **Exploded crust** — Sketchfab "Exploded view of tectonic plates" (linajakaite;
   sketchfab.com/3d-models/exploded-view-of-tectonic-plates-e9eeeeab3ba844aabf6b9f88b7ea8bc3;
   full animation strike-dip.com/tectonic-plates/): each plate a discrete curved slab with real
   THICKNESS and side walls, exploded radially like a puzzle. Target for the *crust* as pieces.

## Asset mapping (what already exists toward each)

| Need | Existing asset |
|---|---|
| Volumetric mantle anomaly field at any (tick, position) | engine `PlateHistoryForcingSource` (0.1.9) — deterministic, conditioned on plate boundary history |
| Cut-open render path | `render.cutaway` live since W3a (cut-face brightness = known look item) |
| Plate boundary arcs + coastline contours for the wireframe ghost | presentation document `BoundaryArcs`/`BoundarySections` + fraction-contour coastline |
| Per-plate crust pieces | Cartography.Globe per-plate caps (watertight arc, 2026-06-22) |
| Slab thickness per cell (continental root vs thin ocean floor) | `CellCrustThickness` already sampled per tick in the presentation document |
| Orbit camera to inspect it | App.Camera + phantom_camera composed (open defect: lazy PhantomCameraHost lookup) |
| Forcing hooks for mantle→plate feedback | `AsthenosphereProfile` already carries ConvectionVector, BasalTractionMagnitude, ViscosityTier, MantlePlumeIndicator |

## Staged shape (for the eventual plan)

- **M-A: x-ray mantle view.** Sample the conditioned field on a spherical-shell grid per tick →
  isosurface extraction (± anomaly thresholds; render seam, compute-shader friendly) → blue/warm
  interior meshes; ghost the crust shell (~20% opacity) with boundary wireframe. Composes with
  cutaway rather than replacing it.
- **M-B: exploded/solid crust.** Extrude each plate cap into a closed solid: top = existing
  relief surface, bottom = radial offset by `CellCrustThickness`, side walls along boundary arcs.
  Exploded mode = per-plate radial translation about plate centroid (slider/command-driven).
  Thickness is data-true: continental keels visibly thicker than ocean floor.
- **M-C: traction feedback (separate, gated).** Integrate `BasalTractionMagnitude` +
  `ConvectionVector` over plate area → modulate plate Euler poles. Mantle-driven plate motion,
  and a physical damping path for the too-hot rates the 1 Gy sweep exposed. Changes truth
  authorship → own slice, motion gates re-run, doubt-driven review before it stands.

Ordering note: the two open windowed defects (inert per-tick light path in export; camera host
lookup) precede any new render arc — M-A/M-B are pointless if the app can't smoothly seek or
orbit what they draw. (CLOSED 2026-07-07: P8 landed; light-path report was a gate artifact.)

## Volumetric field method — v2, METHOD-LOCKED 2026-07-07 (user-approved discussion)

**Diagnosis of v1:** `PlateHistoryForcingSource` normalizes every query onto the unit sphere —
the anomaly is RADIALLY CONSTANT (it is a basal-forcing layer, correct for M-C traction), and
its sources are per-segment point kernels. Any isosurface of it is a sphere-ish shell; lateral
structure is blobs. "I only see a sphere" is the expected output of v1, not a bug in the
sampler. The fix is a true volumetric `T'(direction, radius, tick)`.

**Decision: field-first** (not geometry-first meshes): one coherent volumetric field means
cutaway slices work for free, thresholds are tunable, and the SAME field evaluated at
asthenosphere depth is what M-C traction later consumes. Geometry-first is the fallback only
if isosurfaced plumes prove too blobby.

**The six ingredients (each maps to a reference-image property):**
1. **Slab ribbons, polyline-sampled.** Convergent boundary arcs sampled as polylines (never
   midpoints); each sample sweeps a curve downward — lateral displacement grows with depth
   (dip), depth extent = subduction age × sink rate, capped at the CMB (~0.55R). Anomaly =
   Gaussian sheet around the swept ribbon, amplitude decaying with age. Scrubbing time shows
   slabs GROWING — history made visible.
2. **Plume tubes rooted in a basal blanket.** Broad warm blanket near the CMB in regions far
   from downwelling (inverse-distance-to-slab — the LLSVP analog per Cao 2021: slabs sculpt
   basal structures); plumes = vertical tubes rising from blanket maxima to ~0.9R with
   widened mushroom heads.
3. **Domain-warped fBm modulation** of ribbon/tube surfaces (multi-scale, deterministic,
   SplitMix64-seeded) — the organic lumpy silhouette; analytic primitives alone read as
   clip-art.
4. **Two thresholds per polarity:** translucent outer + opaque inner isosurface (blue/blue,
   orange/red) — layered translucency is what reads as volumetric. Godot: opaque core sphere
   + inner surfaces first, transparent outers with explicit render priority.
5. **Normals from the field gradient**, not mesh triangles — smooth at modest grid cost.
6. **Stage dressing:** dark core sphere ~0.55R, crust ghosted 10–15% with white coastline +
   green boundary wireframe, soft rim light. Composes with M-B: the exploded crust parts to
   reveal this interior.

**Honesty upgrade bundled in:** polyline sources allow computing REAL relative velocity at
each boundary point from the two plates' Euler poles → physically-derived dip directions and
convergence rates, replacing P2's documented geometric-convention placeholder.

**Division of labor:** engine (fantasim-world, `Geosphere.Asthenosphere.Convection` plugin)
owns the volumetric `MantleAnomalyField` + gradient + tests; the app reuses M-A's shell-grid
sampling / marching-cubes / `render.mantle` plumbing, swapped onto the new field at
integration. Existing `PlateHistoryForcingSource` (basal layer) stays intact for M-C.

## NORTH-STAR EYE GATE (user directive 2026-07-07: closeness to reference 2 is FIRST PRIORITY)

The acceptance gate for the mantle view is the USER'S EYE against the reference image. Tests
green ≠ done. After integration, the lead runs a LOOK-ITERATION LOOP — screenshot →
compare → tune → re-render — against this checklist; do not commit look changes blind:

1. **Slabs read as descending CURTAINS/sheets** hanging under the trench arcs — never blobs,
   beads, or radial shells. Silhouettes lumpy/organic (noise working), edges torn not smooth.
2. **Plumes read as rising columns with mushroom heads**, rooted in visible warm basal
   structure, standing in the gaps between slab graveyards.
3. **Layered translucency is visible**: a translucent outer envelope around an opaque core on
   BOTH polarities (blue-over-blue, orange-over-red). If it reads as one flat surface per
   polarity, the thresholds/alpha need work.
4. **The ghosted surface reads as a reference frame**: coastline/boundary wireframe legible
   over the interior at ~10-15% shell opacity; the viewer can tell where a slab is relative
   to the surface plates.
5. **Depth composition**: dark core sphere anchors the center; interior structures fill the
   volume between core and shell; rim/soft lighting gives the isosurfaces 3D form (not flat
   cutouts).
6. **Time tells the story**: scrubbing shows slabs deepening with subduction age and the
   basal blanket/plumes re-arranging — motion consistent with the surface plates.

Tuning knobs live APP-SIDE (the app constructs `MantleFieldConfig` + `MantleViewConfig`):
field params (ribbon width, noise amplitude/warp, blanket/plume strengths), thresholds,
colors/alpha, grid resolution — so the iteration loop is a bundle rebuild (~1 min), NOT an
engine repack. Iterate in the windowed app via `render.mantle` + `render.screenshot`.
