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
orbit what they draw.
