# Spherical plate-material volumes — replacement Slice A/B design

**Status:** USER APPROVED, including written-spec review, on 2026-07-17. This document is the
implementation-planning authority for Replacement A0/B0.

This specification replaces the implementation authority of
`2026-07-17-crust-volume-generation-design.md`. That earlier document remains as design history
because its implementation demonstrated an ambiguity this specification must close.

## 1. Binding authority

- `vault/specs/2026-07-16-assembled-world-northstar.md`
- `vault/reference/2026-07-17-user-reference-closed-contact-assembled-planet.jpg`
- `vault/reference/2026-07-17-user-reference-assembled-final.png`
- `vault/reference/2026-07-16-user-reference-thick-crust-planet.png`
- `vault/reference/sketchfab-exploded-tectonic-plates.jpeg`
- `vault/reference/usgs-vigil-plate-boundaries-cross-section.gif`
- `vault/reference/2026-07-17-user-discussion-dual-crust-representation.txt`
- `vault/reference/2026-07-17-user-discussion-architecture-substitution-history.txt`

The user's eye judges the exported assembled and exploded views against these references. Passing
builds, tests, digests, labels, or logs cannot substitute for the visual gate.

## 2. User decisions

1. The normal assembled world has **completely closed contacts**. Ordinary plate boundaries are
   visible through geometric creases, steps, ridges, trenches, folds, and faults, not open seams.
2. The assembled view hides buried subducting crust. The hidden crust remains causal.
3. Every plate is one continuous thick crust body based on the planet's spherical shell.
4. Large tectonic forms deform that solid body. Fine detail modifies its outer surface afterward.
5. Broad tectonic forms and a readable band of finer geometry coexist at globe distance. Zoom may
   add still finer geometry through adaptive detail.
6. The primary exploded interaction separates all complete plates radially. It reveals plate
   edges, undersides, and stored under/over geometry without regenerating geology.
7. Cells and chunks are invisible simulation and extraction partitions. They are not geological
   pieces and never explode independently.
8. Color is not an acceptance concern.
9. Crust thickness and deformation use a declared visual scale independent of the core's scale.
   Literal crust-to-core physical units are not an acceptance concern.

This refines north-star clauses 1 and 7: "visibly made of parts" now means that closed plate
contacts remain legible through their geometric response. It does not authorize empty gaps between
ordinary contacts in the assembled view.

## 3. Established and disproven conclusions

### 3.1 Established

- The planet remains a globe. Its core, ordinary crust, plate reference domains, and assembled
  silhouette remain spherical or approximately spherical.
- Local tectonic deformation may move plate material outward, inward, or tangentially.
- A subducting part remains attached to its plate while bending beneath another curved plate.
- The assembled and exploded views must consume the same generated plate-volume state.
- Performance partitioning follows the geology; it does not define the geology.

### 3.2 Disproven by the delivered Slice A/B work

- **Elevation plus radial thickness is not a plate volume.** The delivered `CrustVolumeState`
  stored per-cell outer elevation, thickness, and feature arrays. Its density query described one
  centre-pointing radial column.
- **Displaced spherical caps are not an assembled projection of a folded volume.** The assembled
  renderer read those elevation arrays and displaced cap vertices.
- **Radially extruded caps cannot reveal missing underlap.** The exploded renderer separated
  cookie-cutter solids while continuous buried crust was explicitly absent from production state.
- **A stable digest does not prove correct semantics.** A deterministic radial-shell substitute
  remains the wrong representation.
- **More color, lighting, or literal unit tuning cannot repair the representation.**

These are negative results. An implementation that repeats any of them fails even if it reuses the
approved type names.

## 4. Selected architecture

### 4.1 Name

The selected model is:

> **Spherical plate-material volumes with non-radial boundary deformation**

"Spherical" describes the planet-scale foundation and ordinary plate shape. "Non-radial" means
tectonic deformation is not restricted to moving the top and bottom of centre-pointing columns.

### 4.2 Material domain

Each plate owns one continuous material domain. A material point has:

```text
plate-surface coordinate + through-thickness coordinate
```

The undeformed mapping places that point in a thick curved region of the spherical shell. A
tick-specific deformation maps the same material point into 3D world space:

```text
plate material point
    -> spherical-shell placement
    -> large tectonic deformation
    -> outer-surface detail, when the point lies on the visible face
    -> 3D world position
```

Large deformation is allowed to change radial position, tangent position, orientation, and local
thickness. Fine detail cannot change plate ownership, invert a boundary, detach material, or
create an underlap.

### 4.3 Required volume semantics

`CrustVolumeState` remains the sole geological volume authority for one generated identity. Its
implementation must support these semantics, regardless of final method names:

- map a plate material coordinate and depth fraction to a 3D world point;
- determine whether a world point lies inside a specific plate volume;
- trace a direction or ray and return every ordered plate-volume interval it intersects;
- select the outermost visible interval for the assembled envelope;
- preserve plate identity for every interval and extracted mesh region;
- expose conservative plate and chunk influence bounds;
- carry a deterministic identity/digest independent of view mode.

The minimum non-radial proof is a convergent trace with separate ordered intervals:

```text
outside
  -> overriding plate interval
  -> space/interior interval
  -> attached down-going plate interval
  -> planet interior
```

The down-going interval must connect continuously through the trench hinge to the surface portion
of the same plate. A single outer-radius/inner-radius pair for the direction cannot satisfy this
contract.

The current `OuterRadiusMetresAtCell`, `InnerRadiusMetresAtCell`, and
`SignedDensityAtCellRadius` semantics may survive only as derived diagnostics or migration
projections. They cannot remain a construction path, canonical query, or input to either default
renderer.

### 4.4 Compact state, disposable samples

The canonical state stores or references compact topology, plate frames, boundary interactions,
material inputs, deformation parameters, and identity. It does not persist:

- a whole-planet dense voxel grid;
- extracted meshes;
- Godot resources;
- view-specific transforms;
- chunk-local samples or normals.

Those products are disposable caches derived from the state.

## 5. Geological deformation grammar

The grammar consumes the existing canonical plate topology, ordered boundary arcs, polarity,
kinematics, crust state, and activity. Parameters use declared normalized planet-radius, angular,
and visual-scale units rather than pretending that crust and core share a literal display scale.

### 5.1 Subduction

- The down-going plate bends continuously at the boundary hinge and travels inward and
  tangentially beneath the overriding plate.
- The descending volume is not an appended tongue, shelf, or ribbon.
- The trench is the outer-surface consequence on the down-going side of the hinge.
- The overriding plate compresses and thickens into an accretionary/orogenic wedge.
- A volcanic foundation and cone chain may rise on the overriding plate at a declared setback.
- The assembled envelope hides the buried interval. Exploding the same plates reveals it.

### 5.2 Continental collision

- Neither plate receives a fake subduction tongue.
- Both continuous volumes shorten, fold, and thicken.
- Their outer faces form a broad mountain belt with roots in the thickened crust.

### 5.3 Divergence

- The two existing bodies taper and separate at the axis.
- New thin crust occupies the spreading centre and thickens with age.
- The outer surface forms ridge shoulders and an axial rift.

### 5.4 Transform

- Plate material shears tangentially along the interaction.
- The surface forms a narrow fault system, offsets, and bounded scarps.
- The interaction does not create an arbitrary open gap or broad mountain belt.

### 5.5 Regimes

- **Magma ocean:** no solid plate volume; retain the molten presentation family.
- **Stagnant lid:** one continuous thick spherical-shell volume with born-rough outer detail.
- **Mobile plate:** multiple continuous spherical plate-material volumes with the full
  interaction grammar.

## 6. Multi-scale crust detail

Detail is one dependent layer of the volume definition, not an independent planet generator.

1. **Broad tectonic form:** mountain systems, trench basins, spreading ridges, collision belts,
   and whole-slab bending. This stage deforms the solid volume.
2. **Medium formed detail:** individual peaks, volcano cones and chains, secondary trench
   structure, ridge segmentation, and fault systems. This stage modifies the outer face and must
   remain readable at globe distance.
3. **Fine material detail:** fractures, small ridges, roughness, and age/formation-conditioned
   texture. Adaptive extraction reveals more of this band as the camera approaches.

All detail is deterministic in plate-material coordinates, so it moves with the plate and remains
continuous across cell and chunk boundaries. Interior roughness is lower than tectonic belts but
is never forced to a smooth eggshell.

Separate declared controls govern broad-form height and width, mountain/trench sharpness, volcano
size, medium-detail amplitude, and close-range fine-detail amplitude. Color does not participate
in the geometry gate.

## 7. Two projections of the same state

### 7.1 Assembled

The assembled renderer evaluates the generated plate volumes and displays the outermost visible
surface:

- ordinary contacts form one closed outer envelope;
- boundaries read through their physical deformation;
- buried plate material is depth-occluded;
- no visible cell grid, chunk grid, artificial seam, or renderer-authored boundary strip exists;
- large and medium/fine relief remains visible in neutral gray geometry.

This projection is not permitted to reconstruct the world from independent elevation arrays.

### 7.2 Exploded

The exploded renderer extracts each complete plate with its outer face, underside, side boundary,
thickened roots, and attached bent underlap. It then applies one rigid radial translation to each
whole plate.

The explosion transform:

- does not change the state digest;
- does not bend or extend a plate;
- does not recompute boundary mechanics;
- does not move individual chunks independently;
- reveals geometry that was already present and occluded while assembled.

Returning the factor to zero restores the same assembled relationships.

The interior/core remains spherical and may use a separate visual scale. It is context for the
crust, not the source of crust thickness.

## 8. Cells, chunks, extraction, and LOD

### 8.1 Semantic hierarchy

```text
plate = one geological body
chunk = invisible extraction, culling, and residency partition
cell = invisible simulation sample or control point
```

A plate may use many chunks, and a chunk may sample many cells. Neither subdivision changes plate
identity or creates visible edges.

### 8.2 Extraction rules

- Chunks are plate-owned cache products with deterministic shared boundary samples.
- Adjacent chunks stitch without cracks at equal or different LOD.
- Broad deformation is present at every LOD.
- Resolution concentrates at interaction zones, high curvature, silhouettes, cut faces, and
  camera-near detail.
- Quiet interiors remain coarse.
- Hidden or stale chunks may be discarded and rebuilt.
- No production path allocates one uniform whole-planet Cartesian `N^3` field.

The existing marching-cubes implementation may be reused for bounded extractor comparisons. It is
not the geological model. If it cannot preserve the required form, the extractor may change behind
the same volume-state and mesh boundary without creating a second world representation.

## 9. Ownership and duplicate-type guard

| Owner | Disposition |
|---|---|
| `CrustVolumeState` | **Replace internals and evolve in place.** It becomes the spherical material-volume authority; do not create `CrustVolumeState2` or a peer authority. |
| `WorldCrustMaterialization` | **Evolve.** It remains the only app-side construction path from materialized engine state. |
| `PlateBoundaryArc` | **Reuse and evolve.** It remains the canonical ordered interaction carrier. |
| `CellBoundaryField` / `CellBoundarySample` | **Reuse and evolve.** They provide boundary-local coordinates and sampling, not a second mechanic. |
| `BoundaryProfileParameters` / `BoundaryProfileShape` | **Reuse and evolve.** They become the single interaction/deformation parameter grammar. |
| `CellElevations`, `CellFeatures`, `CellCrustThickness` | **Demote.** They may be inputs, diagnostics, or projections; they cannot remain parallel geometry authority. |
| `GlobePlateSurfaces` / adaptive surface machinery | **Reuse and evolve.** They sample the outer envelope; they do not reconstruct a radial planet independently. |
| `PlateSolid` / `PlateSolidBuilder` | **Evolve behind the existing responsibility.** Remove radial extrusion semantics rather than adding a competing solid builder. |
| `MarchingCubes` | **Reuse as an extractor comparison only.** Never copy its algorithm into a new geological authority. |
| `SlabJointKind`, `SlabJointPolarity`, `SlabJointClassification`, slab-specific mechanics authorities | **Delete after canonical data is consumed directly.** |
| `BoundarySectionDocument` | **Projection only.** It cannot be read back to reconstruct mechanics. |

Before adding any domain type, implementation work must record its semantic responsibility, the
CodeGraph search for existing owners, and why none can evolve safely. Small mesh/cache value types
may not carry geological authority.

## 10. State, time, and data flow

```text
engine topology + plate frames + crust state + canonical boundary arcs
                                |
                                v
              spherical plate-material CrustVolumeState
                    /                              \
                   v                                v
          assembled outer envelope        per-plate solid extraction
                                                    |
                                                    v
                                            radial explode transform
```

State identity includes the world seed, graph revision, tick, topology/resolution inputs,
algorithm/schema version, and parameter digest. Changing view mode does not change identity.

Forward or backward scrubbing to A -> B -> A reproduces the same state identity and derived
geometry for A. Plate birth, split, and merge change material domains through the engine topology;
presentation code may not simulate those events independently.

## 11. Failure behavior

- Invalid boundary ownership or polarity is a construction error, not a guessed interaction.
- Unsupported interaction data produces a neutral continuous plate volume plus a diagnostic. It
  never falls back to a tongue or radial joint scaffold.
- A failed or cancelled chunk extraction leaves the last complete compatible chunk visible until
  replacement succeeds.
- A state/mesh identity mismatch rejects the mesh.
- Missing disposable cache data is rebuilt from canonical state.
- View changes never regenerate geological mechanics.

## 12. Falsifiable acceptance gates

The user requested no large new test expansion for this arc. Verification therefore emphasizes a
small structural diagnostic, caller/authority audits, and exported-window evidence. Existing tests
may be run where useful, but build/test success is not product acceptance.

### 12.1 Spherical-volume gate

- Ordinary plate interiors have curved spherical-shell top and underside geometry.
- The assembled silhouette remains a complete globe.
- Exploded pieces remain curved shell regions rather than flat slabs or visible voxel chunks.

### 12.2 Underlap state gate

At a known convergent boundary:

- one trace returns distinct overriding and down-going plate intervals;
- the down-going interval is continuously attached through the hinge to the same plate's surface
  region;
- the result exists before either view renders it.

### 12.3 Same-state paired visual gate

Capture the same seed, tick, parameters, and `CrustVolumeState` identity in neutral gray:

1. assembled view; and
2. whole-globe radial exploded view.

The assembled capture must show:

- closed ordinary contacts;
- amplified trench, overriding mountain belt, and volcanic chain;
- readable broad and medium/fine relief;
- no visible buried slab, cell grid, chunk grid, or artificial seam.

The exploded capture must show:

- entire curved plates separated as intact bodies;
- side faces, undersides, thickened roots, and a continuous attached down-going volume;
- the overriding/down-going relationship;
- no appended strip, tongue, shelf, or renderer-authored overlap.

The user's comparison against the reference registry is the final gate.

### 12.4 Detail and LOD gate

- A full-globe capture retains broad forms and readable medium detail.
- A close capture of the same state adds fine geometry without relocating the broad forms.
- Chunk and LOD transitions remain invisible.
- Resolution is observably non-uniform and concentrated where needed.

### 12.5 Authority gate

Caller and repository audits show:

- no second plate-volume authority;
- no production/default dependency on radial extrusion or slab-joint mechanics;
- no renderer-side underlap generation;
- no global dense `N^3` runtime allocation.

### 12.6 Regime/time gate

Exported captures show magma ocean with no solid volume, stagnant lid with one born-rough thick
shell, and mobile plate with multiple interacting volumes. A -> B -> A reproduces A.

## 13. Replacement delivery sequence

> **Amended 2026-07-18:** after A0 passed structurally and B0 failed the visual gate, the user
> chose visual-fidelity slices V1→V3 as the continuation — see
> `2026-07-18-visual-fidelity-slices-decision.md`. The tessellation/shading portion of §8
> needed for a closed, cell-invisible skin moves ahead of full A/B visual acceptance; the rest
> of step 4 remains deferred.

The old Slice C is frozen. It must not proceed on top of the rejected radial Slice A/B semantics.

1. **Replacement A0 — state semantics.** Replace `CrustVolumeState` internals with the spherical
   material domain, non-radial convergent deformation, and ordered multi-interval trace. Pass the
   underlap state gate.
2. **Replacement B0 — paired visual proof.** Render one accepted convergent case in assembled and
   whole-globe exploded views from the same identity. Include broad tectonic form and readable
   surface detail. Pass the user's paired visual gate.
3. **Replacement A1/B1 — complete grammar.** Extend the same state and paired projections to
   collision, divergence, transform, live topology, and all solid-crust regimes.
4. **Only after A/B acceptance — adaptive production extraction.** Complete chunk residency,
   culling, LOD stitching, cancellation, and measured budgets behind the same authority.
5. **Authority retirement.** Remove the radial extrusion, slab-joint, independent elevation
   authority, and all production switches that can reach the substitute path.

The first implementation plan must stop at Replacement A0 and B0. That session-scale paired proof
is deliberately smaller than the whole project but exercises the hardest semantic requirement.

## 14. Non-goals

- biome and hydrology presentation;
- color matching;
- literal scientific crust-to-core display ratios;
- a global uniform voxel planet;
- a flat plate representation;
- a boundary-focused explode mode;
- implementation or test expansion inside this design session.

## 15. Completion statement for this design

This design is complete only when:

- the user has approved this written specification;
- that approval is recorded in the committed specification;
- the next step uses `writing-plans` to plan Replacement A0/B0 without adding a parallel domain
  authority.

No code implementation belongs to this design goal.
