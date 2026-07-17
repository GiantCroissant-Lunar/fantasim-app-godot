# Crust-volume generation: one geological state, two projections

**Status:** SUPERSEDED AS IMPLEMENTATION AUTHORITY on 2026-07-17 after the user rejected
the delivered Slice A/B representation. Preserve this document as design history only. Its
replacement is `2026-07-17-spherical-plate-material-volume-design.md`, which must pass its
written-spec review before a new implementation plan begins.

### Supersession findings

The delivered Slice A/B work disproved that this document was specific enough to prevent another
radial-shell substitution:

- `CrustVolumeState` was populated with per-cell outer elevation, radial thickness, and feature
  arrays. Its density query described one radial column; it did not define non-radial plate
  material or permit separate overriding and down-going plate intersections along one direction.
- The assembled path displaced spherical plate caps from those elevation arrays. It therefore
  remained a heightfield presentation rather than the visible envelope of already-deformed solid
  plate volumes.
- The exploded path continued to extrude those caps radially. The production code explicitly
  deferred continuous buried underlap, so separating the plates could not reveal plate-on-plate or
  plate-under-plate anatomy that did not exist in the state.
- Passing builds, deterministic digests, and existing tests established engineering consistency
  of the scaffold. They did not establish the user's visual or geological acceptance gate.

The user additionally clarified that color and literal crust-to-core units are not acceptance
concerns. The binding assembled target is now
`vault/reference/2026-07-17-user-reference-closed-contact-assembled-planet.jpg`: ordinary plate
contacts form a closed outer envelope; coherent large tectonic forms and readable fine relief must
both live on the crust geometry. The assembled view occludes buried crust, while the whole-globe
radial exploded view must reveal the same stored plate volumes and their actual under/over
relationships.

**Binding product authority:**

- `vault/specs/2026-07-16-assembled-world-northstar.md`
- `vault/reference/2026-07-17-user-discussion-dual-crust-representation.txt`
- `vault/reference/2026-07-17-user-discussion-architecture-substitution-history.txt`
- `vault/reference/2026-07-17-user-reference-assembled-final.png`
- `vault/reference/usgs-vigil-plate-boundaries-cross-section.gif`
- `vault/reference/sketchfab-exploded-tectonic-plates.jpeg`

This design resolves one ambiguity in the north-star without weakening it:
the normal assembled planet shows the complete plate assembly, its joints, exposed thickness,
and the surface consequences of plate interaction, but it does **not** expose buried subducting
crust through the overriding plate. Exploded and cutaway presentations reveal that buried
under/over anatomy. The hidden volume is nevertheless causal in both views.

## 1. Problem statement

The current planet has two independently authored stories:

1. scalar topography adds trench, arc, ridge, and fault shapes to a radial surface; and
2. the slab renderer extrudes plate caps and appends a narrow subduction tongue.

Those stories can be tuned until a screenshot contains a trench or a tongue, but neither proves
that plate A is a continuous crustal volume bending under plate B and causing the visible trench,
mountain belt, or volcanic arc. The duplication is present in the type system as well:
`PlateBoundaryKind` is mirrored by `SlabJointKind`, convergent polarity is mirrored by
`SlabJointPolarity`, and a `PlateBoundaryArc` is regrouped into a `SlabJointClassification`.
`BoundaryProfileParameters` and `SlabJointMechanicsProfile` then shape the same interaction by
separate rules.

The radial extrusion/tongue renderer is therefore an **interim scaffold**, not the product
architecture. Its successful tests characterize that scaffold; they do not establish the
north-star.

## 2. Decision

Generate one deterministic, plate-owned crust-volume state per materialized tick, and derive two
projections from it:

```text
engine topology + plate kinematics + crust state + boundary interaction parameters
                                  |
                                  v
                         CrustVolumeState
                     (compact field definition)
                         /             \
                        /               \
        outer-envelope sampling        plate-volume extraction
                  |                            |
                  v                            v
       assembled adaptive surface     exploded/cutaway solid parts
```

The two projections are not two planet generators:

- **Assembled projection:** samples the outer visible envelope of the plate-owned volumes through
  the existing adaptive globe surface path. It shows mountains, trench hinges, volcanic arcs,
  ridges, faults, plate joints, and exposed thickness. Occluded underlap remains hidden.
- **Exploded/cutaway projection:** extracts the same plate-owned volumes, preserving plate identity
  and overlap. It reveals the continuous down-going slab, overriding wedge, underside, and side
  walls.

Both projections carry the same `CrustVolumeState` identity/digest. A renderer may discard and
rebuild its mesh; it may not reinterpret the geology.

## 3. Why this extraction architecture

### 3.1 Rejected: whole-planet uniform marching cubes

The existing deterministic `MarchingCubes` implementation is valuable as a comparison extractor,
but a global Cartesian `N³` field is the wrong production architecture. It spends resolution in
empty space and plate interiors, blurs thin plate interfaces at affordable resolutions, and makes
overlapping plate ownership awkward. It also conflicts with the binding chunked/tiled/adaptive
performance clause.

### 3.2 Deferred: full adaptive dual contouring

Dual contouring is attractive for sharp edges and adaptive cells, but a first implementation would
also require an octree, QEF solving, crack-free transitions, stable normals, and LOD stitching.
That is too much new machinery before the field semantics themselves are visually proven.

### 3.3 Selected: plate-owned implicit fields with projection-specific sampling

Each plate owns a compact implicit thickness/occupancy field in plate-material coordinates.
Boundary-local deformation modifies that field continuously. The assembled renderer asks for the
outer envelope on the spherical adaptive mesh; the cutaway/exploded renderer samples only
plate/chunk regions that are visible or intersected.

The existing `MarchingCubes` implementation is reused for the first bounded extractor comparison.
It must not be copied or renamed into another implementation. If the comparison shows that sharp
interfaces cannot meet the visual gate, the extractor behind the same field/mesh contract can be
replaced by an adaptive surface-net or dual-contouring implementation without creating a second
geological model.

## 4. Geological interaction grammar

All amplitudes and distances are declared parameters in consistent normalized-radius/angular
units. Plate-anchored noise is evaluated only after the tectonic skeleton and remains bounded so
it cannot relocate, invert, or erase a tectonic form.

### 4.1 Convergent subduction

- The down-going plate volume bends continuously at the boundary hinge and extends beneath the
  overriding plate. It is not an appended ribbon.
- The trench is the outer-envelope consequence on the down-going side of the hinge.
- The overriding crust thickens into an accretionary/orogenic wedge.
- A volcanic arc is set back onto the overriding plate and is driven by the same subduction
  interaction/activity state.
- The assembled view hides the occluded slab. Exploded/cutaway views reveal it without changing
  the state or recomputing a different path.

### 4.2 Continental collision

Neither plate receives a fake subduction tongue. Both plate-owned volumes shorten/thicken and
their outer envelope produces a broad mountain belt. Any polarity field is explicitly absent.

### 4.3 Divergent boundary

The two existing volumes taper and part at the axis. New thin crust occupies the spreading centre
and thickens with age. The outer envelope shows ridge shoulders and an axial rift; exploded view
shows the actual separation and young material.

### 4.4 Transform boundary

The interaction offsets/shears the plate-local field along the joint, producing a narrow fault
trace and bounded scarp. It does not invent a large gap or mountain belt without corresponding
crust state.

### 4.5 Regimes

- **magma-ocean:** no solid crust volume; retain the molten presentation family.
- **stagnant-lid:** one unbroken thick shell volume, including side/underside only where cut.
- **mobile-plate:** multiple plate-owned volumes and the complete interaction grammar above.

## 5. Canonical ownership ledger

This ledger is a construction constraint, not cleanup deferred until later. A migration step is
incomplete while both the old and new authority remain reachable from the production/default path.

| Disposition | Existing/new owner | Required action |
|---|---|---|
| **REUSE + EVOLVE** | `PlateBoundaryArc` in `contracts/App.World` | Make the existing boundary segment the canonical carrier of plate pair, kind, ordered path, convergent polarity/collision, and required kinematics. Rename only if the final name materially improves meaning; do not wrap it in a parallel interaction record. |
| **DELETE** | `SlabJointKind` | Use `PlateBoundaryKind` directly. |
| **DELETE** | `SlabJointPolarity` | Use the canonical arc polarity. `ConvergentPolarity` remains an internal calculator, not a second stored contract. |
| **DELETE** | `SlabJointClassification` and `SlabJointClassifier` | Consumers use canonical ordered boundary segments. Do not collapse all segments for a plate pair into one priority-classified joint. |
| **PROJECTION ONLY** | `BoundarySectionDocument` | Continue as a UI/read projection. It must never be read back to reconstruct mechanics. |
| **REUSE + EVOLVE** | `CellBoundaryField` / `CellBoundarySample` | Extend the established boundary-local frame/sampling responsibility as needed for signed cross-boundary distance, tangent, side, and along-boundary coordinate. |
| **REUSE + EVOLVE** | `BoundaryProfileParameters` / `BoundaryProfileShape` | Become the single boundary interaction parameter grammar and outer-envelope evaluator. Absorb valid controls from slab-specific profiles. |
| **DELETE AFTER ABSORPTION** | `SlabJointMechanicsProfile`, `SlabTopReliefProfile`, and slab-specific world profile switches | Remove independent mechanics/topography authorities and the scaffold selection path once canonical rendering is mounted. |
| **NEW — ONE JUSTIFIED DOMAIN TYPE** | `CrustVolumeState` | No current type owns a compact, queryable, plate-owned 3D crust field for one tick. This is the sole new domain authority. |
| **REUSE + EVOLVE** | `WorldCrustMaterialization` | Add `BuildVolumeState(tick)` (or equivalent) as the only app-side construction route from materialized engine state. |
| **REUSE + MIGRATE** | `PlanetPresentationDocument` | Carry/reference the volume state identity and data needed by both projections. Migrate away from independently authoritative `CellElevations`, `CellFeatures`, and `CellCrustThickness`; do not leave permanent parallel truth. |
| **REUSE** | `GlobePlateSurfaces`, `AdaptiveGlobeSurfaceBuilder`, `PlateCap`, `PlateFrameSampler`, `BirthRoughnessProfile`, `TectonicDetailSampler` | Preserve current topology, adaptive surface, plate-material coordinates, and bounded-detail work. Feed them from the canonical volume state. |
| **RENAME + EVOLVE** | `PlateSolid` / `PlateSolidBuilder` | Become the canonical plate-volume mesh/extractor boundary. Remove radial extrusion and appended-tongue semantics rather than adding `CrustIsosurfaceMesh` beside it. |
| **REUSE FOR COMPARISON** | `MarchingCubes` | Use the existing implementation unchanged or minimally generalized behind the canonical extractor boundary. Never copy its tables or algorithm into a new class. |
| **REUSE** | `CrustProductCacheRecord`, schema, codec, and document store | Persist compact state/parameter identity and inputs where appropriate. Never persist global voxel grids or render meshes as canonical truth. |

### 5.1 Duplicate-type guard

Before adding any type, the implementing change must record:

1. the semantic responsibility;
2. the CodeGraph search/exploration used to locate current owners;
3. why none of those owners can be evolved safely.

`CrustVolumeState` has passed that check. No other new domain type is pre-approved by this design.
Small rendering value types are allowed only when they describe disposable mesh/chunk output and
cannot carry geological authority.

Compiler errors, CodeGraph caller audits, and repository searches after each migration step must
show that deleted mirrors are no longer production dependencies. Temporary adapters may exist
inside one uncommitted edit to keep the compiler useful; they may not be committed or remain on the
default path.

## 6. `CrustVolumeState` responsibilities

The state is immutable for one identity:

```text
seed + graph revision + tick + schema/algorithm version + parameter digest
plate volume definitions[]
canonical boundary segments[]
plate-local material/detail inputs
deterministic digest
```

It exposes queries rather than a dense voxel array:

- occupancy/signed density for a specific plate at a world or plate-local point;
- outer-envelope radius/elevation and winning visible plate at a unit direction;
- local thickness/material band for cut faces;
- conservative plate/chunk bounds and boundary influence bounds;
- deterministic invalidation keys.

The state may reference immutable engine/materialization data already held by
`WorldCrustMaterialization`; it must not clone large cell arrays merely to rename them.

## 7. Persistence, caching, and time

The authoritative compact definition is a deterministic derived product of the tick's materialized
world state. Dense samples, extracted mesh chunks, normals, and Godot resources are disposable
caches.

Cache identity includes at least seed, frequency/resolution, spin, graph revision, tick, state
schema/algorithm version, and parameter digest. Boundary/plate chunks include plate id and chunk
coordinate/LOD. Invalidation is local where the state change is local. Forward and backward
scrubbing to A → B → A must reproduce the same state digest and mesh buffers for A.

Mesh extraction runs asynchronously and publishes a completed immutable chunk on the main thread.
There is no synchronous global `N³` extraction during a timeline tick.

## 8. Rendering and occlusion rules

Assembled and exploded/cutaway renderers consume the same state but have different visibility:

- assembled: depth/ownership selects the outermost visible plate surface; buried overlap is
  occluded; joints remain legible; visible side/underside geometry is allowed at silhouettes,
  gaps, and cut planes;
- exploded: plate transforms separate already-extracted plate-owned meshes; no mechanics are
  recomputed from the exploded transform;
- cutaway: only chunks intersecting the wedge/section need interior faces; their strata/thickness
  are queried from the same plate volume.

Changing view mode must not change the state digest.

## 9. Failure behavior

- Invalid boundary polarity (subducting plate not in the segment pair) is a construction error,
  not a best-effort guess.
- Missing/corrupt disposable cache data is logged and rebuilt from materialization.
- Unsupported interaction data renders a neutral continuous plate volume and emits a diagnostic;
  it must not fall back to the appended-tongue scaffold.
- Extraction failure keeps the last complete chunk/state visible until a replacement succeeds.
- A state/mesh digest mismatch rejects the mesh.

## 10. Falsifiable acceptance gates

Build success is necessary but never sufficient. The user explicitly requested no new tests for
this arc, so verification uses compiler/build gates, existing focused checks only when useful,
structural dependency audits, and exported-window evidence.

### 10.1 Same-state paired visual gate — north-star clauses 1, 2, 3, 7

Capture the same tick/seed/parameters in:

1. assembled view; and
2. exploded or cutaway view.

Both captures must display/log the same `CrustVolumeState` digest. In gray geometry:

- assembled reads as one complete chunky planet made of plate parts;
- convergent boundaries visibly produce a trench at the hinge, overriding mountain belt, and
  set-back volcanic arc where activity warrants;
- buried slab is not visible through the assembled surface;
- exploded/cutaway reveals a continuous down-going plate volume beneath the overriding volume;
- there is no narrow appended strip/tongue detached from the down-going plate.

The user's eye is the final gate.

### 10.2 Geometry gate — north-star clauses 2, 3, 5

- extracted plate parts have consistently oriented outer/side/underside surfaces;
- shared chunk boundaries do not crack;
- the same state and extraction identity produce identical buffers;
- flat gray material preserves the required forms;
- production/default call paths contain no `ShapeSlabJoints`, `ShapeSubductionTongues`,
  radial-extrusion `PlateSolidBuilder`, or slab-classifier authority.

### 10.3 Performance gate — north-star clause 6

- assembled rendering performs no whole-planet 3D extraction;
- cutaway/exploded extracts only visible/intersecting plate chunks;
- no global `N³` allocation exists in the runtime path;
- resolution concentrates at boundary interaction zones and cut surfaces;
- declared per-frame publish budget, resident chunk budget, and extraction latency are logged;
- timeline scrubbing remains responsive while incomplete work is cancellable/replaced.

Numerical budgets are set from the existing exported-app baseline during the first implementation
slice; inventing an unmeasured number here would create a fake gate.

### 10.4 Topology/time gate — north-star clause 4

At a known birth/split transition and any available merge transition, the before/after paired
captures show the plate-owned volumes changing with topology. A → B → A returns to the identical A
state digest and geometry identity.

### 10.5 Regime gate

One exported-window capture each shows magma-ocean with no solid volume, stagnant-lid with one
thick shell, and mobile-plate with multiple interacting volumes. A smooth sphere substituted for
the stagnant shell fails.

## 11. Delivery sequence

1. Collapse duplicate boundary semantics into the existing canonical boundary segment.
2. Introduce the one justified `CrustVolumeState` and build it from `WorldCrustMaterialization`.
3. Make the existing adaptive assembled surface sample its outer envelope.
4. Implement continuous convergent volume deformation first and prove the paired view.
5. Add collision, divergent, and transform volume grammar.
6. Evolve the existing plate-solid path into chunked volume extraction, comparing the existing
   marching cubes extractor before selecting any more complex extractor.
7. Mount exploded/cutaway from the same state, then delete scaffold profiles/classifiers/default
   switches.
8. Add local chunk invalidation/budgets and prove time/regime gates.

No fine terrain-detail expansion occurs before the continuous convergent volume passes the paired
visual gate. That ordering prevents another polished surface from substituting for the missing
mechanics.
