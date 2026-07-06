# Rendering & Level-of-Detail (app side)

> **AUDIT (2026-07-06, code-verified):** SUPERSEDED — historical. The render path described here (GlobeView/App.World.Seam) is dead code and StubWorldRuntime retirement never happened. Authority: `globe-surface-lod-scale-and-provenance.md` + `planet-domain-station-map.md`; the store-little-derive-much doctrine survives there. _(See the authority index in `vault/README.md`.)_


> **Status:** Locked 2026-06-21 (design discussion).
> Consumes the engine design in `fantasim-world/vault/architecture/world-gen-design-direction.md`
> and `geology-model.md`.

How the Godot app turns the engine's **fields** into a **time-evolving, zoomable rendered globe**.
The golden rule from the engine carries over: **store little, derive much.** The simulation produces
coarse field-truth over time; the renderer derives geometry and spatial detail.

---

## 1. Fields → relief (a mountain is not a triangle)

The simulation produces **fields**, not geometry. Rendering turns fields into a mesh in two steps.

**Step 1 — fields → elevation (a derived field).** The crust fields aren't elevation yet; derive an
elevation from them:

- `continental-fraction` → base level (land high, ocean low)
- `orogenic-pressure` → uplift (mountains)
- `crust-age` → ocean depth (older seafloor sits deeper)
- `volcanic-activity` → volcanic cones

This single derivation is where "how high / how peaky" is decided.

**Step 2 — elevation → displaced mesh.** The globe is a mesh whose vertices come from the cells.
**Displace each vertex radially outward by its elevation**, exaggerated for visibility. A mountain
range is then *a region of the sphere mesh pushed out and roughened* — not a triangle. A trench is
the same, displaced inward.

**Detail on zoom = procedural.** The per-cell elevation is the low-frequency **envelope**; the
individual ridgelines and peaks are **higher-frequency procedural displacement** (ridged/fractal
noise) whose amplitude and roughness are keyed by `orogenic-pressure` and `crust-age`. The sim says
"tall, young, rough belt here"; the renderer generates the peaks from a **seeded** function —
consistent every time, revealed by zoom, never stored.

> **Simulation = the envelope (where, how high, how rough). Renderer = the peaks.**

---

## 2. Level-of-detail (zoom) and cells changing "kind"

Cells form a **tree**: a coarse cell subdivides into finer children (unify-cell geodesic
refinement). Zoom in → more, smaller cells.

The trick that makes a coarse "ocean" cell reveal continental sub-cells on zoom: **store a fraction,
not an enum.** A coarse cell isn't "ocean" — it's `continental-fraction = 0.1`. "Ocean" is just what
`0.1` looks like below the threshold at that zoom. Subdivide and **distribute**: give ~10% of the
children high `cf` (land — an island/microcontinent) and the rest ~0, so the area-average still
equals the parent's `0.1`. Zoom back out and the children average to the parent — consistent both
ways.

Where the fine pattern comes from:

- **Procedural refinement** — a child's `cf` = parent + a *seeded* noise function of position
  (deterministic, infinite zoom, nothing to store).
- **Authored overrides** — OpenStreetMap-style manual edits pin specific fine cells.

**Categorical kind ("ocean"/"continental"/"mountain") is always a thresholded view of a continuous
field at the current resolution.** This is why **resolution is not a truth-stream axis** — zoom
detail is a reproducible *view* computed by the app/cartography, never stored as truth.

---

## 3. Time-scrubbing — the heartbeat on screen

A time-scrubber drives a **canonical tick**. Per tick:

1. reconstruct cell positions at that tick (engine: `world-stage Motion.ReconstructPoint` /
   `Geosphere.Plate.Reconstruction`),
2. (re)build or update the globe mesh from the reconstructed cells + derived elevation,
3. recolor / re-displace.

Dragging the scrubber should **visibly move the plates** and show mountains growing as they ride
their plates. This is the milestone the whole direction is organized around.

---

## 4. What needs building (app side)

Current state (2026-06-20): there is **no render path**. `WorldFunctionProvider` routes a single
`crust.generate` that returns *counts*, not geometry; the App.World service runtime is a
`StubWorldRuntime` (real runtime compiled out under `UseProjectReferences=false`); there is no globe
scene, mesh, timeline, or `App.World.Seam`.

To reach the heartbeat:

- **`App.World.Seam`** (T4, Godot) — build an `ArrayMesh` for the geodesic globe; vertices displaced
  by derived elevation (exaggerated relief); cells colored by field/feature.
- **Wire reconstruction** — per-tick cell positions from the engine reconstruction (not the
  `CrustVizEmitter` demo clock), driven by a canonical tick.
- **Activate the real runtime** — replace/retire the `StubWorldRuntime` path so generation + fields
  actually flow (resolve the `UseProjectReferences` gating so the shipped app runs real world code).
- **Time-scrubber UI** — a control that sets the canonical tick and re-renders.

Procedural peak detail, LOD subdivision, cartography projection, and authored edits are **later** —
the first milestone is a coarse displaced globe that **moves** when you scrub time.
