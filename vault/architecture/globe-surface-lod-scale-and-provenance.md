# Globe Surface LOD - Scale & Provenance

> **Status:** Locked 2026-07-04 (adaptive subdivision review slice).
> Companion to `rendering-and-lod.md` and `vault/plans/2026-07-03-dry-crust-adaptive-subdivision.md`.
> Records the architecture decisions that govern render-side globe subdivision: where truth lives,
> what scale metadata must distinguish, and how child identity stays canonical.

---

## 1. Truth stays on the UnifyCell geodesic grid

Simulation truth lives on `UnifyCell.GeodesicSphereTessellation` and the plate cells derived from it.
That grid is the authoritative spatial reference: cell ownership, elevations, fields, and plate ids
are all expressed against it. Nothing in this slice moves truth off that grid.

Render LOD / adaptive subdivision is a **derived presentation product**, not truth-stream state. It
must never be persisted as simulation state, never fed back into the crust/field sim, and never
mutate cell ids or plate ownership. The `AdaptiveGlobeSurface` produced by
`Cartography.Globe.Core.AdaptiveGlobeSurfaceBuilder` is a render-facing projection of the same
tessellation+heightfield truth that `GlobeSurfaceBuilder` already consumes; it adds vertices and
triangles for shading density, not new cells.

This is the same "store little, derive much" rule `rendering-and-lod.md` already states for fields ->
relief. Subdivision extends it to the mesh topology layer: the envelope is truth, the sub-faces are
renderer detail.

## 2. Hierarchy must come from Unify, not invented ad hoc

When a later slice needs a real cell hierarchy (recursive subdivision, chunked LOD, parent/child
refinement), use `UnifyCell.HierarchicalCellId` or an equivalent Unify-owned hierarchy contract. Do
**not** invent `ParentCellId` / `RenderLevel` / `LocalChildIndex` from scratch on the cartography or
app side. Two reasons:

1. The geodesic truth grid already has a canonical hierarchy (frequency doubling). Re-implementing it
   in a render layer would duplicate it, drift from it, and create a second source of truth for
   "which cell is inside which".
2. A render-invented hierarchy would be tempted to key children by emission order, which is not
   stable across refinement regimes. Canonical identity must not depend on the order a builder
   happens to emit triangles.

For this slice, `AdaptiveGlobeSurface.VertexProvenance` deliberately carries only **indices into the
original input vertex list**, not a cell hierarchy. An `Original` record points at one base vertex; a
`Midpoint` record points at the two base endpoints of the split edge. That is enough for a consumer
to reattach base-parallel attributes (colours, uv, etc.) to appended vertices without inventing a
hierarchy, and it stays out of the cell-identity business entirely.

## 3. Canonical child identity, not emission-order identity

When recursive subdivision lands, stable child identity must be **canonical** - a function of the
parent cell and the child's position within it (e.g. the edge-split or the four-way frequency-doubled
subcell), never a function of which triangle the builder emitted first. Emission order depends on
iteration order, plate grouping, and split decisions, all of which can change between regimes; a
child id that depends on it would silently rename the same piece of planet across ticks or views.

The current depth-1 builder already respects this principle for midpoints: the `EdgeKey` is the
sorted endpoint pair, so a midpoint on edge (a,b) is the same vertex regardless of which incident
triangle asked for it first. A future recursive scheme must extend the same idea to sub-cells.

## 4. Scale metadata: four kinds, never conflated

The renderer applies a chain of displacements and scales. They are not interchangeable, and a
correct LOD system must keep them labelled separately:

- **Physical metres.** The simulation's elevation field is in metres (continents +500 m, oceans
  -500 m, peaks ~10,000 m). This is the truth-stream unit.
- **Unit-sphere displacement.** `GlobeSurfaceBuilder.Build` displaces each unit-sphere vertex by
  `height` added to `radius`, so the height is still in the same units as `radius` (unit-sphere
  radii). The `exaggeration` factor in `GlobePlateSurfaces.BuildSurfaces` maps metres -> unit-sphere
  displacement (about 0.00012 for the world view).
- **Post-lens displacement.** The non-linear height profile
  (`sign(m) * |m|^heightExponent * exaggeration`) is applied after the unit-sphere conversion. It
  changes the relief ratio without changing the metres or the exaggeration. It is a render-only
  lens, not truth.
- **Screen / render scale.** The Godot camera + mesh transform maps the unit-sphere displaced
  positions to pixels. This is where view-dependent LOD thresholds (screen-space error, pixel size)
  would enter; the current slice does not use them.

A future LOD system must carry these labels through the pipeline. `AdaptiveSubdivisionOptions` today
only carries `EdgeHeightDeltaThreshold` in unit-sphere displacement units and `Radius`; when screen-
space thresholds arrive, they belong in a separate, view-dependent options struct, not folded into
the truth-side threshold.

## 5. Boundary-aware refinement signals

The current refinement trigger is a single scalar: `|height[a] - height[b]| >= threshold` on the
post-exaggeration, post-lens per-vertex height. That is enough for the dry-crust visual slice, but it
is not the right long-term predicate. A boundary-aware refinement system should be driven by:

- **Boundary kind** - plate interior, plate boundary, coast, ridge, trench. Different features want
  different refinement.
- **Signed boundary distance / polarity** - how far inside / outside the feature the edge is, so the
  refinement can fade smoothly instead of cutting a hard line.
- **Source cell / plate ids** - so a sub-face always knows which truth cell it refines, even after
  multiple levels (this is what `SourceTriangleIds` + `VertexProvenance` preserve today for depth-1).
- **Feature importance** - orogenic pressure, crust age, volcanic activity. These are the sim fields
  that say "rough here", and they are the natural drivers of relief-driven refinement.

Post-exaggeration height delta is a fine **proxy** for feature importance on a dry crust with no
hydrosphere, but it conflates the lens with the signal. When the hydrosphere returns, a coastline
should refine because it is a coastline (boundary kind + signed distance), not because the height
delta happens to cross a threshold after the lens compresses basins.

## 6. S2 as an index, not as the truth grid

S2 may be useful as a **spatial index / bridge** for chunked LOD: it gives a stable cell-id scheme
over the sphere that is independent of the geodesic tessellation frequency, which helps with
neighbour queries and chunk paging. The workspace already has an S2 wrapper under
`UnifyTopology.Sphere.S2`; if render LOD needs S2, route it through that Unify-owned surface.

S2 should **not replace** the current `UnifyCell.GeodesicSphereTessellation` truth grid in this
slice. Promoting S2 to truth would move the sim's spatial reference away from the plate/cell grid
that the existing crust, boundary, and field products already use. The render layer may treat S2 as
an auxiliary index; the simulation still owns truth on the geodesic grid.

H3 is **absent** from the current stack unless proven otherwise. Do not assume it is available; if a
future slice wants hex hierarchical refinement, verify the dependency exists in the workspace first
and route it through Unify the same way.

## 7. Chunked LOD is a later slice

Full chunked LOD - explicit geodesic chunks/fragments with their own `MeshInstance3D`, visibility
policy, and camera-distance refinement - is **not in this slice**. The current adaptive subdivision
refines a single per-plate cap surface; it does not partition the globe into independently streaming
chunks.

When chunked LOD arrives, it should be:

- **Explicit geodesic chunks**, not Godot `ArrayMesh` built-in LOD. Built-in LOD is per-surface and
  distance-driven; it cannot refine one region while keeping another coarse, which is the whole
  point of chunked LOD. (See `rendering-and-lod.md` and the dry-crust plan's Task 5 notes.)
- **Scale- and provenance-tagged.** Each chunk carries its scale metadata (section 4) and the
  provenance of its vertices back to the truth cells (section 2), so the render seam can reattach
  attributes and the sim can never mistake a chunk for a cell.
- **A separate plan.** Chunked LOD changes scene structure, visibility policy, and camera-distance
  behaviour; it belongs in its own `vault/plans/` document, not folded into the adaptive subdivision
  plan.

## 8. What this slice actually changed

- `AdaptiveGlobeSurface` now carries `VertexProvenance[]` parallel to `Surface.Positions`, so a
  consumer can reattach base-parallel attributes (colours today, uv/tangents later) to appended
  midpoint vertices without a fallback. `PlateCapMeshBuilder.BuildTerrain` uses it to interpolate
  midpoint terrain colours from the two endpoint base colours, fixing the silent
  `MissingTerrainColor` fallback on refined vertices.
- `AdaptiveSubdivisionOptions.MaxDepth` is now honest: values > 1 throw
  `ArgumentOutOfRangeException` instead of silently running depth-1. Recursive subdivision is
  deferred to the chunked-LOD slice.
- No truth-stream state changed. No new cell hierarchy was invented. No S2/H3 dependency was added.
