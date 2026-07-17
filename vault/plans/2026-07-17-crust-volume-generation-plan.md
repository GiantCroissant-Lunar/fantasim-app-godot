# Plan: migrate planet generation to canonical plate-owned crust volumes

**Status:** ACTIVE — design approved by the user on 2026-07-17.

**Design:** `vault/specs/2026-07-17-crust-volume-generation-design.md`

**Product authority:** `vault/specs/2026-07-16-assembled-world-northstar.md`

**User verification override:** do **not** add new tests, test files, snapshots, or test-only
harnesses during this plan. The repository already has nearly 2,000 tests while the visible
product remains far behind the target. Each task instead closes with compilation, a production
dependency/type audit, existing focused checks only when they are useful, and exported-window
paired visual evidence at the integration gates. Existing tests may be updated or deleted only
when a production contract migration makes them stop compiling; do not expand their behavioral
scope.

**Detected implementation stack:** Godot 4.7 (`Godot.NET.Sdk/4.7.0`), .NET 8, C# latest,
FantaSim/Unify geometry and geosphere project references, and a `build/build.config.json`-driven
Godot export. Outer verification uses `dotnet unify-build`, not a hand-authored build pipeline.

## 1. Non-negotiable migration rules

1. `CrustVolumeState` is the only pre-approved new domain type.
2. Evolve existing owners before adding a contract. Small disposable render structs may not carry
   geological meaning.
3. A task that replaces an authority updates all production callers and deletes the old authority
   in the same commit.
4. `BoundarySectionDocument` remains output-only UI data.
5. The assembled and exploded/cutaway paths must display/log the same state digest.
6. The old radial extrusion, slab-joint deformation, and appended tongue may be used only to capture
   the initial baseline. No fallback to them is allowed after the canonical renderer mounts.
7. No new terrain polish until the continuous subduction volume passes the paired visual gate.

## 2. Session-sized delivery slices

The overall planet is a project arc, not one session goal. Work proceeds through falsifiable slices.
The current implementation slice is **Slice A**. Later slices do not begin until the preceding
visual or structural gate is deposited.

### Slice A — canonical boundary authority and volume-state skeleton

**Goal:** one real mobile-plate presentation document contains one deterministic
`CrustVolumeState` built from materialized topology/crust data, and no production renderer depends
on `SlabJointKind`, `SlabJointPolarity`, `SlabJointClassification`, or `SlabJointClassifier`.

**Gate:** the app compiles/exports; a CodeGraph + repository dependency audit finds zero production
references to the deleted slab mirror types; the exported app logs one stable volume-state digest
for a known tick. This slice does not claim visual success.

### Slice B — assembled outer envelope

**Goal:** the normal World view samples the state outer envelope through the existing adaptive
surface path. Tectonic skeleton dominates bounded plate-anchored detail.

**Gate:** exported gray-geometry capture shows plate joints plus legible trench/mountain/ridge/fault
surface consequences without exposing buried crust; the capture and log identify the state digest.

### Slice C — continuous convergent volume and paired view

**Goal:** a down-going plate is one continuous volume bending beneath the overriding plate, and that
same deformation creates the assembled trench/wedge/arc.

**Gate:** same-tick paired assembled and cutaway/exploded captures carry the same state digest;
assembled hides the buried slab; cutaway/exploded reveals continuous underlap; no appended strip is
present. The user's eye decides pass/fail.

### Slice D — remaining interactions and regimes

**Goal:** collision, divergent, transform, stagnant-lid, and magma-ocean follow the approved grammar.

**Gate:** paired captures for each interaction plus one exported capture per regime. Collision has
no fake tongue; divergent shows actual separation/young crust; transform shows shear without an
invented chasm; stagnant-lid is one thick shell.

### Slice E — chunked adaptive extraction and time/topology

**Goal:** cutaway/exploded extraction is bounded, asynchronous, local, deterministic, and follows
birth/split/merge topology through time.

**Gate:** logged budgets show no global runtime `N³` allocation; only visible/intersecting chunks
extract; A → B → A reproduces A's state/mesh identities; before/after topology captures are
deposited.

## 3. Task sequence

### Task 0: establish the baseline and name the scaffold

**Inspect first with CodeGraph:**

- `project/contracts/App.World.Rendering/Globe/SlabJointClassifier.cs`
- `project/contracts/App.World.Rendering/Globe/WorldSlabAssemblyComposer.cs`
- `project/contracts/App.World.Rendering/Globe/PlateSolidBuilder.cs`
- `project/plugins/App.Presentation/PlanetPresentationBinder.WorldSlabAssembly.cs`
- `project/plugins/App.Presentation/PlanetPresentationBinder.CutawayExploded.cs`

**Actions:**

1. Record the current production call paths:
   `BuildSlabTopCaps → PlateSolidBuilder.Build → SlabJointClassifier.Classify →
   ShapeSlabJoints → ShapeSubductionTongues`.
2. Capture one exported assembled screenshot and one exploded/cutaway screenshot at the same
   mobile-plate tick.
3. Deposit them under
   `vault/specs/evidence/2026-07-17-crust-volume-baseline/` with the exact command/tick/seed.
4. Add a short progress-log entry below establishing that the narrow tongue/radial extrusion is the
   scaffold being replaced, not an acceptable fallback.

**Verify:** screenshot files render; the exported app remains open and its ALC collection/reload log
is clean before changing bundle code.

**Commit:** `docs(evidence): capture crust-volume scaffold baseline`

### Task 1: make the existing boundary segment canonical

**Modify:**

- `project/contracts/App.World/PlateBoundaryArc.cs`
- the current arc construction in `project/plugins/App.World/Globe/GlobeReconstructor.cs`
- polarity calculation in `project/plugins/App.World/Topography/ConvergentPolarity.cs`
- boundary consumers in:
  - `project/plugins/App.World/Crust/WorldCrustMaterializer.cs`
  - `project/plugins/App.World/Topography/CellBoundaryField.cs`
  - `project/plugins/App.World.Composition/Mantle/MantleHistoryAdapter.cs`
  - `project/plugins/App.Presentation/PlateBoundaryFocusRenderer.cs`

**Delete after updating every production caller:**

- `SlabJointKind`
- `SlabJointPolarity`
- `SlabJointClassification`
- `SlabJointClassifier`
- their file `project/contracts/App.World.Rendering/Globe/SlabJointClassifier.cs` when empty

**Actions:**

1. Add convergent polarity/collision and only the kinematic values actually available from the
   engine to `PlateBoundaryArc`; validate that a subducting id belongs to the segment pair.
2. Preserve individual ordered boundary segments. Do not regroup all arcs for one pair and choose a
   priority kind.
3. Change `WorldSlabAssemblyComposer` and both presentation binder paths to accept canonical arcs
   directly as a temporary compiler bridge to Task 4; do not create an adapter record.
4. Update existing affected tests only enough to compile against the canonical record, without
   adding cases.
5. Delete all mirror types/classifier code in this task.

**Verify:**

- compile the affected contract/plugin/host projects in a tight inner loop;
- CodeGraph callers for every deleted symbol return none in production;
- `rg` finds no production declaration/reference to `SlabJointKind`, `SlabJointPolarity`,
  `SlabJointClassification`, or `SlabJointClassifier`;
- `BoundarySectionDocument` appears only downstream of boundary mechanics.

**Commit:** `refactor(world): make boundary arcs canonical`

### Task 2: collapse the interaction parameter authorities

**Modify:**

- `project/plugins/App.World/Topography/BoundaryProfileParameters.cs`
- `project/plugins/App.World/Topography/BoundaryProfileShape.cs`
- `project/plugins/App.World/Topography/CellBoundaryField.cs`
- `project/contracts/App.World.Rendering/Globe/SlabJointMechanicsProfile.cs`
- `project/contracts/App.World.Rendering/Globe/SlabTopReliefProfile.cs`
- `project/contracts/App.World.Rendering/Globe/WorldSurfacePresentationProfile.cs`
- `project/plugins/App.Presentation/PlanetPresentationBinder.WorldSlabAssembly.cs`
- `project/plugins/App.Presentation/PlanetPresentationBinder.CutawayExploded.cs`

**Actions:**

1. Move the valid declared interaction controls—hinge width, dip/depth, overriding wedge,
   arc setback, collision thickening, rift width, and transform shear band—into the existing
   boundary profile owner with explicit units.
2. Extend `CellBoundaryField` only where the volume query needs a stable tangent/cross-boundary/
   along-boundary frame.
3. Change `BoundaryProfileShape` from an independent additive terrain authority into the
   outer-envelope evaluator used by the future volume state.
4. Remove slab-specific parameter values and default-path selection as soon as callers use the
   canonical profile. Do not keep properties that merely forward to the old profile.
5. Keep `BoundarySectionDocument` generation downstream as a projection.

**Verify:** compile; one production declaration owns every interaction parameter; repository search
finds no second hinge/dip/trench/arc amplitude used by the default renderer.

**Commit:** `refactor(world): unify boundary interaction parameters`

### Task 3: add the sole new domain authority

**Add:**

- `project/contracts/App.World/CrustVolumeState.cs`

**Modify:**

- `project/plugins/App.World/Crust/WorldCrustMaterializer.cs`
- `project/plugins/App.World/Services/Service.cs`
- `project/contracts/App.World/PresentationLayers.cs`
- crust cache contract/codec files under `project/contracts/App.World/Persistence/`
- crust cache construction/restore paths in `project/plugins/App.World/Services/Service.cs`

**Actions:**

1. Define immutable `CrustVolumeState` identity, digest, canonical arcs, plate volume definitions,
   conservative bounds, outer-envelope query, per-plate occupancy/thickness query, and state
   validity checks.
2. Keep its representation compact. Reference existing immutable topology/materialization arrays
   where possible; do not allocate a dense voxel grid.
3. Add `WorldCrustMaterialization.BuildVolumeState(tick, canonicalProfile)` as the sole construction
   seam.
4. Carry the state through the existing `PlanetPresentationDocument`.
5. During this task, make legacy `CellElevations`, `CellFeatures`, and `CellCrustThickness` derived
   compatibility projections from the state where still required. Mark their removal point in
   Tasks 4 and 5; do not let producers write both independently.
6. Extend the existing cache schema/codec only with compact deterministic input/state identity.
   Meshes and voxel samples remain disposable.
7. Log the digest at document construction and view binding.

**Verify:**

- compile twice from a clean unchanged source state;
- fetch the same tick twice and confirm identical logged digest;
- inspect allocations/state shape to confirm no `nx*ny*nz` or equivalent global array;
- CodeGraph shows `WorldCrustMaterialization` as the only constructor path.

**Commit:** `feat(world): materialize canonical crust volume state`

### Task 4: drive assembled terrain from the volume outer envelope

**Modify:**

- current assembled surface construction in `project/plugins/App.Presentation/PlanetPresentationBinder.cs`
- slab/default assembly path in
  `project/plugins/App.Presentation/PlanetPresentationBinder.WorldSlabAssembly.cs`
- `project/plugins/App.Presentation/PlanetPresentationBinder.CutawayExploded.cs`
- existing cartography integration around `GlobePlateSurfaces` /
  `AdaptiveGlobeSurfaceBuilder`
- `project/plugins/App.World/Topography/BoundaryProfileShape.cs`
- established detail samplers (`PlateFrameSampler`, `BirthRoughnessProfile`,
  `TectonicDetailSampler`) at their existing paths

**Actions:**

1. Feed adaptive surface height finalization from `CrustVolumeState` outer-envelope queries.
2. Resolve occlusion by selecting the outermost visible plate at each assembled sample.
3. Apply bounded plate-material detail after the tectonic envelope; cap it relative to the local
   tectonic form so it cannot fill a trench or move a ridge.
4. Preserve discrete plate ownership/joints and visible thickness without exposing buried underlap.
5. Stop reading independently authored `CellElevations`/`CellFeatures` in the assembled path.
6. Remove the radial slab assembly as the default World owner. Do not delete its extractor yet;
   Task 5 evolves that file for cutaway/exploded.

**Verify:** outer UnifyBuild compile; export/reload the live app; capture the Slice B gray assembled
gate with digest/tick/seed and deposit conclusions. If the tectonic forms are not legible, record
failure and diagnose the field/profile before adding detail.

**Commit:** `feat(presentation): render crust volume outer envelope`

### Task 5: evolve the existing solid path into continuous volume extraction

**Rename/evolve, without parallel mesh contracts:**

- `project/contracts/App.World.Rendering/Globe/PlateSolidBuilder.cs`
- `project/contracts/App.World.Rendering/Globe/WorldSlabAssemblyComposer.cs`
- `project/plugins/App.World.Composition/Mantle/MarchingCubes.cs`
- `project/plugins/App.Presentation/PlanetPresentationBinder.CutawayExploded.cs`
- `project/plugins/App.Presentation/PlanetPresentationBinder.WorldSlabAssembly.cs`

**Actions:**

1. Evolve `PlateSolid`/`PlateSolidBuilder` into the canonical plate-volume mesh/extractor boundary.
   Keep one mesh DTO; do not add `CrustIsosurfaceMesh`.
2. Extract a bounded interaction region for a known convergent segment using the existing
   `MarchingCubes.Extract` implementation as the first comparison.
3. The density query must produce one continuous down-going plate volume, overriding wedge
   thickening, and a stable hinge. Remove `ShapeSlabJoints` and `ShapeSubductionTongues`.
4. Stitch the bounded extraction to coarse plate-interior top/bottom/wall surfaces without cracks.
5. If marching cubes fails the gray-geometry gate because the affordable field is too rounded,
   record the evidence and replace only the extractor behind this same contract with an adaptive
   surface-net/dual-contouring implementation. Do not retain both as production authorities.
6. Use one extracted plate mesh in assembled, exploded, and cutaway visibility transforms; exploded
   transforms may move a mesh but never reshape it.

**Verify:** exported paired Slice C capture, same digest, continuous underlap, buried slab occluded
assembled, no appended strip, gray material. Record user verdict in the progress log.

**Commit:** `feat(world): extract continuous interacting plate volumes`

### Task 6: remove scaffold residue and legacy presentation truth

**Delete or finish migrating:**

- `project/contracts/App.World.Rendering/Globe/WorldSlabAssemblyComposer.cs` if no canonical
  extraction responsibility remains
- `SlabJointMechanicsProfile`
- `SlabTopReliefProfile`
- radial/default scaffold switches in `WorldSurfacePresentationProfile`
- legacy presentation properties that independently carry `CellElevations`, `CellFeatures`, and
  `CellCrustThickness`
- dead scaffold binder fields/methods and obsolete tests that only compile against removed contracts

**Actions:**

1. Run CodeGraph callers before each deletion.
2. Route cut-face thickness/material bands through volume queries.
3. Keep only compatibility projections required by a non-planet consumer, and rename/document them
   explicitly as projections. No presentation producer may author them independently.
4. Remove all scaffold selection/fallback behavior from the default runtime.

**Verify:** production search contains no `ShapeSubductionTongues`, `ShapeSlabJoints`,
`SlabJointMechanicsProfile`, `SlabTopReliefProfile`, or radial extrusion default; compile/export;
the app still passes the same paired gate.

**Commit:** `refactor(world): retire radial slab scaffold`

### Task 7: complete interaction grammar

**Modify:**

- canonical boundary interaction/profile files from Task 2
- `CrustVolumeState` and its materializer
- canonical plate-volume extractor

**Actions in separate commits:**

1. continental collision: symmetric thickening, no polarity/tongue;
2. divergent: tapering old plates, parted volumes, young axial crust thickening with age;
3. transform: bounded lateral shear and fault scarp;
4. volcano ownership/activity: plate-owned construction, subduction context where applicable;
5. magma-ocean and stagnant-lid volume behavior.

**Verify after each:** outer compile and exported gray paired gate for that interaction. Deposit a
failure rather than tuning unrelated noise when the geometry does not read.

**Commits:**

- `feat(world): shape collision crust volumes`
- `feat(world): shape divergent crust volumes`
- `feat(world): shape transform crust volumes`
- `feat(world): derive volcanoes from crust interactions`
- `feat(world): complete crust volume regime coverage`

### Task 8: chunking, budgets, cancellation, and deterministic time

**Modify/add only rendering infrastructure, not another geology model:**

- canonical plate-volume extractor/cache files near the evolved `PlateSolidBuilder`
- presentation scheduling in the existing binder partials
- existing crust cache identity/records

**Actions:**

1. Partition by plate plus stable spherical interaction/cut chunk coordinates and LOD.
2. Expose conservative affected bounds from `CrustVolumeState`.
3. Extract off the main thread; publish immutable complete chunks on the Godot main thread.
4. Cancel/replace stale work on timeline changes and retain the last complete matching state until
   replacements arrive.
5. Measure the current exported baseline, then declare resident-chunk, publish-per-frame, and
   extraction-latency budgets in the design/progress log.
6. Prove no assembled global 3D extraction and no runtime global `N³` field.
7. Capture A → B → A identities and a birth/split/merge transition.

**Verify:** Slice E performance/time/topology gate plus a final `dotnet unify-build
BuildGodotDesktop` export and live ALC reload/collection proof.

**Commit:** `perf(world): chunk adaptive crust volume extraction`

## 4. Build and runtime verification commands

The exact build target is driven by `build/build.config.json`.

```bash
dotnet tool restore
dotnet unify-build BuildGodotDesktop --configuration Debug
```

Raw `dotnet build <project>.csproj` is permitted only as a tight compile-error diagnostic.
The outer acceptance build remains UnifyBuild. Windowed verification follows the repository's
`verify-windowed` and live bundle reload/ALC procedure; screenshots are OS-level captures from the
exported app, not editor or test renders.

## 5. Required progress log format

Append after every slice:

```text
YYYY-MM-DD / Slice:
ESTABLISHED:
- claim + evidence path/log/digest
DISPROVEN:
- attempted hypothesis + evidence (include negative results)
TYPE OWNERSHIP:
- new/renamed/deleted owners and zero-caller evidence
USER VERDICT:
- pending / exact verdict
NEXT SESSION GOAL:
- one falsifiable slice and its own gate
```

## 6. Progress log

### 2026-07-17 / architecture and planning

**ESTABLISHED:**

- CodeGraph found real semantic duplication:
  `PlateBoundaryKind`/`SlabJointKind`,
  convergent polarity/`SlabJointPolarity`, and
  `PlateBoundaryArc`/`SlabJointClassification`.
- The current exploded production path is explicitly:
  `BuildSlabTopCaps → PlateSolidBuilder.Build → SlabJointClassifier.Classify →
  ShapeSlabJoints → ShapeSubductionTongues`.
- `BoundaryProfileShape` separately adds surface-only trench/arc/ridge/fault contributions.
- No existing type owns a compact queryable plate-owned 3D crust state; one
  `CrustVolumeState` is justified.
- The user approved the single-state/two-projection design and instructed implementation to start
  without new tests.

**DISPROVEN:**

- More tuning of an appended tongue plus scalar surface profile cannot establish continuous plate
  underlap or shared causality. The representation itself is the failure.
- A global uniform marching-cubes grid is not compatible with the required adaptive/chunked runtime
  architecture, though the existing extractor remains useful for a bounded comparison.

**TYPE OWNERSHIP:**

- Design ledger locks one new domain owner and the required reuse/evolve/delete set.
- No implementation types have yet changed.

**USER VERDICT:** architecture and immediate implementation approved.

**NEXT SESSION GOAL:** Slice A — canonical boundary authority and volume-state skeleton; gate is
zero production dependencies on slab mirror types plus one stable state digest from the exported
app.

### 2026-07-17 / Slice A — canonical authority and volume-state skeleton

**ESTABLISHED:**

- `PlateBoundaryArc` now owns canonical boundary kind, convergence, polarity, overriding plate,
  underriding plate, and pressure mechanics used by production.
- `CrustVolumeState` is the single immutable materialized owner of the coupled tick, globe,
  canonical arcs, elevations, thickness, features, continental fractions, and deterministic
  digest.
- `WorldCrustMaterializer` is the sole external construction seam. Presentation compatibility
  accessors project from `PlanetPresentationDocument.CrustVolume` instead of storing parallel
  products.
- The exported app at HEAD `55aefc798b82206d285d3350e4b2fd5957708b65` passed an A → B → A
  timeline gate: tick 107,000,000 returned digest
  `67031039ada9c499b981994e731b1caa36d1eccaa82fa4cd81b09a99e6f95c7e` before and after
  tick 112,000,000 produced a different digest.
- PID 12461 remains open on `127.0.0.1:19317`; stderr and Godot error logs are empty.
- Full commands, hashes, log paths, ownership searches, and viewport are deposited in
  `vault/specs/evidence/2026-07-17-crust-volume-slice-a/`.

**DISPROVEN:**

- A successful compile did not prove the old appended subduction-tongue renderer was compatible
  with canonical per-segment arcs. The first exported run crashed because the scaffold inferred
  original top-vertex count from a mesh that earlier segments had already expanded.
- Keeping that scaffold as a temporary second geometry authority was not viable. Production calls
  were retired; assembled mode hides buried underlap, and a later slice will extract real cutaway
  geometry from `CrustVolumeState`.
- The exact-process viewport is alive but visually unacceptable: it is too close and dominated by
  existing exploded geometry. Slice A therefore makes no visual-success claim.

**TYPE OWNERSHIP:**

- Added exactly one domain owner: `CrustVolumeState`.
- Deleted four mirror owners: `SlabJointClassification`, `SlabJointClassifier`, `SlabJointKind`,
  and `SlabJointPolarity`.
- Source audit shows one external `CrustVolumeState.Create(...)` caller, no retired mirror-type
  references, no legacy per-product assignments, and no production caller of
  `ShapeSubductionTongues(...)`.

**USER VERDICT:** architecture approved; visual verdict for the generated planet remains pending
and is explicitly not satisfied by this slice.

**NEXT SESSION GOAL:** Slice B — generate the assembled outer envelope from `CrustVolumeState` so
one complete unobstructed globe shows legible large-scale relief while buried underlap remains
hidden; gate is an exported-app screenshot plus the same A → B → A identity proof.
