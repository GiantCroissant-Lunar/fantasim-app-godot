# Render surface & motion — closing the mesh gap (design)

> **Status:** DRAFT 2026-06-21 (design discussion). Extends and corrects
> [rendering-and-lod.md](rendering-and-lod.md). Written after the globe rendered as a **shattered
> shell** (cracks, gaps, the inner sphere showing through) instead of a coherent planet.
> Grounded in today's implementation: [`GlobeView.cs`](../../project/plugins/App.World.Seam/GlobeView.cs),
> [`CellElevationModel.cs`](../../project/plugins/App.World/Cells/CellElevationModel.cs),
> [`GlobeReconstructor.cs`](../../project/plugins/App.World/Globe/GlobeReconstructor.cs).
>
> **Update 2026-06-21 (architecture locked + foundation built).** Per the user's call, **projection +
> the globe surface are owned by `fantasim-cartography`, not reinvented in the app** (parts vs assembly).
> Built this session: the `Cartography.Globe` part — `GlobeSurfaceBuilder.Build(vertices, triangles,
> heights, radius)` → a **watertight** `GlobeSurface` (shared-vertex `Positions`, pass-through
> `Triangles`, `SmoothNormals` per vertex + `FlatNormals` per face), plus `GatherVertexHeights`
> (per-cell → per-vertex). Godot-free, 31 new tests green. Also renamed `App.World.Projection` →
> `App.World.FieldView` (it is a UI read-model, not a map projection). **This supersedes §2's "built in
> the seam":** the surface geometry is now a cartography part; the T4 seam only *assembles* it into a
> Godot `ArrayMesh`, then shades (blocky `FlatNormals` / smooth `SmoothNormals`) and moves it.

---

## 0. Why this doc exists — the gap

The prior render design (`rendering-and-lod.md`) said *"displace each vertex radially outward by its
elevation."* That sentence is correct in spirit but **under-specified**, and the implementation filled
the silence with the wrong choice. Three things were never pinned down:

1. **Watertightness.** The doc never said the globe must be **one coherent surface whose neighbouring
   cells share their vertices.** The code built **each cell as its own loose triangle** — 3 vertices per
   cell, unshared — and pushed each whole triangle out by that one cell's elevation
   ([`GlobeView.BuildMesh`](../../project/plugins/App.World.Seam/GlobeView.cs)). Wherever two neighbours
   have different elevations, their edges no longer meet — **the surface cracks.** At plate seams (big
   elevation jumps) the cracks become the gaping tears you see; the inner mantle shell shows through.
   This happens **even at tick 0**, purely from how the mesh is built. It is not a geology problem.

2. **Cell → vertex.** Fields live on **cells** (continental-fraction, orogenic-pressure, …); a displaced
   mesh moves **vertices**. The step that turns per-*cell* values into per-*vertex* heights was never
   described, so it was skipped (each cell just used its own value on its own 3 corners → see #1).

3. **Motion.** The doc never said what the surface *does* when plates move. Rotating each plate rigidly
   (which the shader does, [`GlobeView` vertex shader](../../project/plugins/App.World.Seam/GlobeView.cs))
   pulls neighbouring plates' cells apart → more tearing as time advances.

**This doc fills all three.** The motion + geology design from session `8369eaf1` is unchanged and
correct; this is purely the render-mesh layer it never reached.

---

## 1. The one reframe: surface vs motion

Two concerns got tangled. Separating them is what makes this tractable:

- **The surface** — turn the fields *at one tick* into a coherent, watertight, relief 3D planet.
  **This is the shattered part, and fixing it is the entire gap between the current image and the
  reference.** It is motion-agnostic — it is correct at any single tick.
- **The motion** — how that surface changes from tick to tick *without tearing.* A separate problem,
  built on top of a correct surface.

**Build the surface first.** It un-shatters the globe and delivers the reference look on its own.

---

## 2. Part 1 — The surface (watertight relief)

### 2.1 Watertight is a hard rule (not a preference)

The globe is **one mesh.** Adjacent cells **share** the vertices on their common edge — there is exactly
**one** vertex at each geodesic corner, used by all the cells (≈5–6 triangles) that touch it. A corner
has **one** height, so the surface cannot crack. This is the non-negotiable fix for the shatter.

> Plain version: today the globe is a pile of loose tiles, each floating at its own height, so gaps open
> between them. We replace it with a single skin stretched over shared corner-pegs — pull a peg out and
> the whole skin around it rises smoothly, no gaps.

Concretely, the geodesic tessellation (frequency 3 → 1280 triangular cells, ~642 shared corners) is built
**once** as a shared-vertex `ArrayMesh`: one vertex per geodesic corner (indexed), triangles reference
those shared indices. Displacement moves the **shared** corners, never per-cell copies.

### 2.2 Cell → vertex heights (the missing step)

Fields are per-cell; the shared corners need one height each. The rule:

> **A corner's height = the area-weighted average of the elevations of the cells that touch it.**

Each cell's elevation is still derived exactly as today
([`CellElevationSystem.Derive`](../../project/plugins/App.Ecs/Systems/CellElevationSystem.cs): continental
base + orogenic uplift + volcanic + age-deepening). The new step is only the **gather** from the ~6
surrounding cells to the shared corner. This is the low-frequency **envelope** (§2.4).

### 2.3 Crisp per-cell colour despite shared vertices

Sharing vertices must **not** blur the per-cell plate/feature colours (the tectonic view needs sharp cell
boundaries). So **colour per face, not per vertex**: each triangle samples its own cell's
plate-id / feature-kind (the existing `u_cell_types` lookup already keys by cell). Geometry is shared and
smooth-connected; colour stays per-cell and crisp. Height is shared (no cracks); colour is discrete (sharp
cells). These are independent and we want both.

### 2.4 Relief = envelope + peaks — and this is CORE, not "later"

The reference reads as a planet because its **geometry** is rugged everywhere. That requires two layers,
and the prior doc wrongly filed the second under "later":

- **Envelope (low frequency, from the fields).** The §2.2 corner heights. This is "where, how high, how
  rough" — continents up, oceans down, mountains at convergent belts. By itself it is smooth swells +
  ridge-lines along seams — *not enough* to look like the reference.
- **Peaks (high frequency, seeded).** A **seeded ridged/fractal noise** added to each corner's height,
  with **amplitude and roughness keyed by the cell's `orogenic-pressure` and `crust-age`** (tall, young,
  rough belts get jagged peaks; old cratons stay smooth; abyssal plains stay flat). Deterministic from a
  seed + position — never stored, revealed by zoom. **This is what turns swells into terrain.** It is
  not optional polish; it is half of "looks like a planet."

> Simulation = the envelope (where/how-high/how-rough). Renderer = the peaks. Both are required for the
> look; the envelope alone is the flat-plateau globe we have now.

**Exaggeration is by design.** The crust is the *focused layer*, so relief is rendered far larger than the
real crust-to-planet ratio (per [crust-geology.md](../../../fantasim-world/vault/architecture/crust-geology.md):
"features shown with scale exaggerated"). Tune for drama, not realism of scale.

### 2.5 Aesthetic: blocky, via flat normals

The reference's chunky, cliff-faced look comes from **flat (per-face) normals** on a **strongly displaced**
mesh: steep faces where neighbouring corner-heights jump read as cliffs; flat normals give the faceted,
stepped quality. We get the blocky look from **watertight mesh + flat normals + strong displacement +
ridged noise** — *no loose tiles, no walls needed.* (True vertical walls / per-cell extrusion — exact
Minecraft cliffs — is a later aesthetic option in §4, not required for the reference read.)

---

## 3. Part 2 — The motion (no tearing) — THE ONE OPEN FORK

A correct surface is watertight at any single tick. The question is what happens **between** ticks as
plates move. Two correct models; this is the decision to make.

### Option A — Lagrangian plate-caps (recommended for this project)

Plates move as rigid caps (the reconstruction already wired —
[`CellReconstructor`](../../../fantasim-world/project/plugins/Geosphere.Plate.Topology/CellReconstructor.cs)).
The surface is watertight **within** each plate. **Boundaries are rendered as geology, not tears:**

- **Divergent** → the gap that opens between separating plates is **fresh young crust upwelling** (a
  ridge). Render it by revealing a thin "young-crust" shell sitting just under the plates (raise/colour
  the existing mantle shell to sea-level basalt) — the gap *is* the mid-ocean ridge, which is correct.
- **Convergent** → plates overlap; the **overriding plate draws on top**, the other dips under (trench /
  subduction). The existing per-plate shell offset already gives a crude z-order to build on.

**Why recommended:** it matches the project's whole reason for being — *plates as objects that visibly
collide* — and turns the boundary "tearing" into the actual tectonic features (ridge / trench) instead of
a bug. The blocky reference look fits naturally.
**Cost:** boundary handling (young-crust shell + overlap z-order) is real work; large drifts need the
boundary cells re-stitched eventually.

### Option B — Eulerian fixed sphere (simpler, smoother)

One **fixed** watertight sphere that never moves. Plate motion is expressed as the **fields advecting
across it**: for each fixed vertex, reconstruct which plate/cell sits there at tick T and sample that
cell's elevation/colour. Continents visibly drift because the *data* at each point changes.

**Why considered:** it **never tears** and needs **no boundary special-casing** — the simplest fully
correct surface-in-motion. **Cost:** plates become "data flowing over a ball" rather than rigid objects
that crash together; the collision is less visceral, and convergent overlap must be resolved by sampling
priority rather than shown as pile-up.

### Recommendation

- For the **reference look as fast as possible**: it doesn't matter — **Part 1 is motion-agnostic**, so
  build the watertight relief surface first and the globe stops shattering regardless.
- For the **motion model itself**: **Option A**, because watching plates collide and raise mountains /
  open ridges is the point of the simulation, and it makes boundaries meaningful instead of broken.
- Pragmatic path: ship Part 1 (Option-agnostic), then add **Option A** boundary geology incrementally
  (young-crust shell first — it alone removes the "mantle shows through divergent gaps" artifact).

**← This is the call I need from you.**

---

## 4. Part 3 — Zoom / LOD (carried forward, unchanged)

From session `8369eaf1`, still correct:

- Cells form a **tree**; zoom subdivides (unify-cell geodesic refinement) → more shared vertices.
- **Store a fraction, not an enum** (`continental-fraction`); subdivide by **distributing** so children
  area-average to the parent. A coarse "ocean" cell can reveal continental children on zoom.
- Fine pattern = **seeded procedural refinement** (+ optional authored overrides). **Resolution is not a
  truth-stream axis** — zoom detail is a reproducible *view*, never stored.
- The §2.4 peaks are the within-cell expression of this same idea (coarse truth + seeded detail).
- *Later aesthetic:* per-cell **extrusion / vertical walls** for an exact voxel-cliff style, if wanted.

---

## 5. Staging (build order)

1. **Surface watertight + envelope** — shared-vertex mesh, cell→corner heights, per-face colour, flat
   normals, exaggerated displacement. **Kills the shatter.** (Touches `GlobeView` mesh build + the
   compute/displace path; the field derivation is unchanged.)
2. **Peaks** — seeded ridged noise keyed by orogenic-pressure / age. **Delivers the reference look.**
3. **Motion** — the chosen Option (A recommended): young-crust shell for divergent gaps, overlap z-order
   for convergent.
4. **LOD + extrusion polish** — subdivision on zoom; optional vertical walls.

**Stages 1–2 alone get the planet looking like the reference.** Stage 3 makes it move correctly.

---

## 6. What changes in today's code (concrete)

- [`GlobeView.BuildMesh`](../../project/plugins/App.World.Seam/GlobeView.cs) — stop emitting 3 unshared
  verts per cell. Build a **shared-vertex** mesh: one vertex per geodesic corner (indexed triangles).
  Keep `uv.x = plateId`, `uv.y = cell-data U` **per face** for colour; height is **per shared vertex**.
- **Cell→corner gather** — new step (CPU or compute): corner height = avg of touching cells' elevations
  (the `CellElevationModel` output is unchanged; this consumes it).
- **Displacement** — displace the **shared corners** by `(envelope + peaks) × exaggeration`; recompute
  **flat per-face normals** from the displaced triangle (the compute path already emits face normals).
- **Mantle shell** — repurpose as the **young-crust under-shell** (Option A) at ≈ sea level, basalt
  colour, so divergent gaps read as ridge crust instead of a dark void.
- **Motion** — keep the per-plate Euler rotation, but apply it to the shared surface per Option A/B
  (decision pending). The reconstruction itself is already wired and is **not** what's broken.

---

## 7. Open decisions (for review)

1. **Motion model — Option A (Lagrangian caps + boundary geology) vs B (Eulerian fixed sphere).**
   Recommended: **A**. (§3)
2. **Blocky now vs true vertical walls later** — recommended: flat-normal blocky now (§2.5), walls
   deferred (§4).
3. Everything in Part 1 (watertight + cell→vertex + envelope/peaks + per-face colour) I'm treating as
   **settled** unless you push back — it's the direct fix for the shatter and matches your own design.
