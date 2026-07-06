# World-generation -> Cartography flow (design)

> **AUDIT (2026-07-06, code-verified):** SUPERSEDED — the `carto.*` node-provider leg was never built; cartography is consumed as parts (`GlobePlateSurfaces`). Authority: `world-generation-consolidation-refactor.md` + `planet-domain-station-map.md`. _(See the authority index in `vault/README.md`.)_


**Status:** PROPOSED (2026-06-20). Design for the real world-generation pipeline and its
cartographic output, assembled through the node-graph paradigm. Supersedes the toy
`geosphere.plate-seeds` placeholder. Extends `node-graph-paradigm.md` and
`service-tier-architecture.md`; honors `fantasim-ecosystem-parts-vs-assembly` and the
world<->cartography boundary.

---

## 1. North star (end-to-end)

A user authors and runs **one node graph** that generates a real planet and renders it:

```
seed-points -> voronoi -> plates.grow -> plates.boundaries -> plates.kinematics
                                                                      |
                                                          world.export (PlanetBody + vectors)
                                                                      |
                                              carto.project / carto.globe / carto.tile -> product
                                                                      |
                                                          truthstream.commit (provenance)
```

Mapped onto the three-layer model (`node-graph-paradigm.md` §3):

| Layer | This flow |
|---|---|
| Paradigm | Node graph (`App.NodeGraph`) -- the shared executor/recipe |
| Orchestration axes | World axis (in-process generation) + Cartography axis (in-process rendering parts) |
| UI seams | `App.Ui.Seam` (graph render), later `App.Cartography.Seam` (map/globe render) |

Generation and cartography are **two function-providers in the same graph**, not two engines.

## 2. Load-bearing constraints (do not violate)

1. **Parts vs assembly.** `fantasim-cartography` ships *parts* (projection, globe, tile,
   styling, export) -- callable builders/codecs/contracts. *Assembly* (ordering parts,
   creating truth, "producing a world", running a node graph) belongs to the authoring tool
   **fantasim-app-godot**. We never add an orchestrator/pipeline to cartography.
2. **World<->cartography boundary (read-only).** World owns planet truth and *emits* exports
   (`PlanetBodyModel`, vector geometry, raster frames, manifests). Cartography *imports* them
   through a read-only bridge and turns them into maps/globes/tiles. Cartography never writes
   back into world truth.
3. **Node-graph transport = handle tokens.** Rich CLR object graphs (tessellation, plate
   model, boundaries, kinematics, export records) thread between nodes as **run-scoped handle
   tokens**, not serialized JSON. Only params and the final sink serialize. (Rationale: the
   `ITruthEventDraft[]` interface array cannot round-trip through System.Text.Json; see the
   node-graph review.)
4. **ref-projects is read-only.** Algorithms and contracts are *lifted* (copied into the
   active repos as new source), never edited in place.

## 3. Architecture: providers in one graph

| Provider | Tier | Repo | Claims | Backed by |
|---|---|---|---|---|
| `WorldFunctionProvider` | T3 | fantasim-app-godot | `geosphere.*`, `world.*` | fantasim-world operators (pkg refs) |
| `CartographyFunctionProvider` | T3 | fantasim-app-godot | `carto.*` | fantasim-cartography parts (pkg refs) + bridge glue |

Both adapt their domain to `INodeFunctionProvider` and run their work through a handle-token
context. The `GraphExecutor` stays domain-agnostic.

## 4. Data model

### 4a. Generation types (fantasim-world, on unify-*)

Adopt unify geometry types at the operator boundary (retire the hand-rolled `SpherePoint`,
whose own doc-comment flags it as provisional):

- seeds: `IReadOnlyList<SphericalPoint>` (unify-geometry)
- tessellation: unify-cell `SphericalVoronoiTessellation` (cells, adjacency, boundary polys)
- `PlateModel`: cell->plate assignment, plates (id, class, member cells, seed cell)
- `PlateBoundary`: plate-pair, ordered path (`SphericalPoint[]`), classification
- `EulerPole`: axis (`UnitVector3`) + angular velocity; `PlateKinematics`: per-plate pole,
  per-boundary relative velocity + divergent/convergent/transform

### 4b. World export contracts (fantasim-world) -- adapt from ref, reconcile version skew

Correction (verified 2026-06-20): the export contracts **exist in ref** and are curated-ported
into the active world, NOT authored from scratch. Two ref contract projects:

- `World.Export` -> `GiantCroissant.FantaSim.World.Export.Contracts` (the exact package
  cartography references): `CanonicalScale`, `WorldScalePresets`, `CosmologyManifestReference`
  (units/scale/cosmology).
- `World.Observation` -> `FantaSim.World.Observation`: the rich world-product surface that maps
  ~1:1 onto our slice-1 output -- `WorldSnapshot` (`WorldManifest` + `WorldElement[]` +
  `WorldBoundary[]` + `WorldJunction[]` + `PlateMotion[]` + `ProvenanceEntry[]`), `PlanetBody`,
  and boundary classification enums (`WorldBoundaryMotionSense`{Divergent,Convergent,Transform,
  ObliqueDivergent,ObliqueConvergent}, `WorldBoundaryNormalMotion`, `WorldBoundaryShearMotion`).
  `PlateMotion` already carries `EulerPole` / `RotationRate` / `AngularVelocity` / `SurfaceVelocity`.

**Version skew to reconcile (this is "adjust for the current code").** Cartography pins
`World.Export.Contracts` **0.1.0** and uses `PlanetBodyModel` + `PlanetBodyShapeKind` (enum); the
current ref renamed/moved that to `World.Observation.PlanetBody` with `int ShapeKind`. Otherwise
field-identical:

| Cartography expects (0.1.0) | Current ref |
|---|---|
| `World.Export.Contracts.PlanetBodyModel` | `World.Observation.PlanetBody` |
| `ShapeKind : PlanetBodyShapeKind` (enum) | `ShapeKind : int` |
| PlanetBodyId, PlanetName, SemiMajor/Minor, AuthalicRadius (meters) | identical |

Our generation output maps onto these directly: Voronoi cells + plate assignment -> `WorldElement`
(`PlateId`, center, vertices, neighbors); plate boundaries -> `WorldBoundary` (+ motion enums);
Euler-pole kinematics -> `PlateMotion`; planet -> `PlanetBody`; all wrapped in one `WorldSnapshot`
with provenance. We do not invent export records -- we fill ref's.

### 4c. Cartography import shapes (fantasim-cartography) -- already exist, generic enough

- `IPlanetBodyResolver.TryResolve(id, out PlanetBodyModel)`
- `IVectorGeometryProvider.GetGeometriesAt(tick) -> ImportedVectorGeometry(Provenance, Tick,
  Kind, IReadOnlyList<CartographicPosition>)`
- `ImportedVectorGeometryKind = {Point, LineString, Polygon}` -- **generic primitives, no
  domain enum**. Plate boundaries -> `LineString`; plate regions -> `Polygon`. No cartography
  enum change required.
- `CartographicPosition(LatitudeDegrees, LongitudeDegrees, HeightMeters, Crs)` -- so the world
  export converts unit-sphere `SphericalPoint` -> lat/lon degrees at the export boundary.

## 5. Operators and functions (full pipeline)

| # | function-id | in -> out | does | source |
|---|---|---|---|---|
| 1 | `geosphere.seed-points` | params -> `seeds` | icosphere points (unify-cell `GeodesicSphereTessellation`) | unify-cell |
| 2 | `geosphere.voronoi` | `seeds` -> `tessellation` | spherical Voronoi (mesh kernel) | unify-cell |
| 3 | `geosphere.plates.grow` | `tessellation` -> `plate_model` | flood-fill region growth over adjacency | lift ref `ProceduralPlateRegionBuilder` |
| 4 | `geosphere.plates.boundaries` | `tessellation`,`plate_model` -> `plate_boundaries` | boundary paths between differing plates | lift ref `BoundaryPathBuilder` |
| 5 | `geosphere.plates.kinematics` | model,boundaries -> `plate_kinematics` | Euler pole per plate, velocity, boundary class | lift ref `EulerPoleReconstructionSolver` |
| 6 | `world.export.snapshot` | model,boundaries,kinematics -> `world_snapshot` | build a `WorldSnapshot` (PlanetBody + WorldElement[] + WorldBoundary[] + PlateMotion[] + provenance) | map onto ref `World.Observation` |
| 7 | `geosphere.plates.draft` | model,boundaries,kinematics -> `drafts` | encode to `ITruthEventDraft[]` | extend codec |
| 8 | `world.truthstream.commit` | `drafts` -> `head` | append to hash-chained store | exists |
| 9 | `carto.project` | `world_export` -> `map_product` | project vectors via `ICartographicProjection` | cartography parts |
| 10 | `carto.globe` | `world_export` -> `globe_product` | build globe mesh + layer bindings | cartography parts |

Slice 1 ships 1-8 + at least one of 9/10 (the first rendered product). `carto.tile`/export are
later. The placeholder `geosphere.plate-seeds` is **deleted**.

## 6. The seams

1. **World -> Export** (`world.export.planet` operator, in fantasim-world): turns the in-process
   plate model into export records; converts geometry to lat/lon meters. World owns this.
2. **Export -> Cartography bridge glue** (in fantasim-app-godot, the assembly): app implements
   cartography's `IPlanetBodyResolver` / `IVectorGeometryProvider` over the `world_export`
   handle. This is the sanctioned "cartography import glue" -- it lives in the assembly tool,
   never in cartography, and is not a second source of truth.
3. **Node-graph transport**: a `NodeGraphGenContext`/handle-token table threads the rich
   objects; `RunContext.BeforeRun` opens the run table + snapshots source params (cache
   invalidation), `AfterRun` drops it + raises `GenerationChanged`.

## 7. "Restore cartography (adjust if necessary)" -- concrete

- **Restore** = curated-port ref's `World.Export` + `World.Observation` into the active world and
  pack; pack `fantasim-cartography` + its unify deps to the local feed on macOS; consume its parts
  via package refs in fantasim-app-godot. Blocked on the unify libs + the world export packages
  being in the feed.
- **Adjust** = reconcile the `PlanetBodyModel` (carto 0.1.0) vs `PlanetBody` (current
  `World.Observation`, `int ShapeKind`) skew (§4b). Two options -- see §11. The import enum is
  generic (LineString/Polygon), so no enum change there. The app-side bridge glue maps
  `WorldSnapshot` -> cartography's `IPlanetBodyResolver` / `IVectorGeometryProvider`
  (`WorldBoundary.SampleCoordinates` Vector3 -> lat/lon `CartographicPosition` LineStrings).

## 8. Build phasing

| Phase | Repo(s) | Delivers | Unblocks |
|---|---|---|---|
| 0 Foundation | plate-projects, feed | unify-{maths,geometry,cell,topology} build+pack to feed on macOS; **kernel spike** (osx-arm64 spherical kernel; fallback = managed Delaunay) | everything |
| 1 Real generation | fantasim-world | operators 1-5 + 7-8 on unify-cell; truth-stream; pack | the node-graph leg |
| 2 World export seam | fantasim-world | curated-port ref `World.Export` + `World.Observation`; reconcile the PlanetBody skew (§11); operator 6 builds `WorldSnapshot`; pack | cartography restore |
| 3 Cartography restore | feed, fantasim-cartography | pack cartography (+deps) to feed; minimal adjust | the render leg |
| 4 Assembly + render | fantasim-app-godot | `WorldFunctionProvider`, `CartographyFunctionProvider` + bridge glue, end-to-end recipe, RunContext hooks, compose, first rendered product (9 or 10) | the north star |

Phase 0 is gating. Phases 1 and 2 are both in fantasim-world and can interleave. Phase 4 is the
assembly that ties it together.

## 9. Testing (TDD, invariant-based)

- Per-operator property tests: seeds unit-length + deterministic; Voronoi cells cover the sphere
  (areas ~= 4*pi), adjacency symmetric, boundaries closed; every cell in exactly one contiguous
  plate; each boundary separates two distinct plates; **velocity zero at the Euler pole**;
  relative-velocity sign -> correct classification.
- Export: round-trip a plate model -> `world_export` -> `ImportedVectorGeometry`, assert lat/lon
  in range and feature counts match plate/boundary counts.
- Integration: full recipe through `GraphExecutor` + both providers + fake truth store -> a
  `StreamHead` with expected event count; assert handle-token threading (no STJ of rich types).
- Cartographic golden: a small fixed plate set -> equirectangular projection -> expected pixel
  extents (deterministic).
- Determinism: same seed -> identical commit hash chain.

## 10. Risks

1. **Native spherical kernel on macOS** -- spike first; fallback to managed Delaunay
   (stereographic + NTS). Gates Phase 0.
2. **World export contract skew** -- ref's `World.Export` + `World.Observation` exist but
   cartography pins an older `PlanetBodyModel` shape; reconcile per §11 during the curated port.
   Not greenfield. (Earlier draft wrongly called this greenfield -- corrected 2026-06-20.)
3. **Determinism through the native kernel** -- unify-cell sorts neighbors; add canonical
   ordering at draft/export encode.
4. **Cross-platform build configs** -- unify build.config.json files carry Windows paths;
   unify-topology has feed-sync disabled.
5. **Scope** -- defer crust/geology, elevation/terrain, tiles/export packages, styling depth,
   and editable graph authoring UI.

## 11. Open decisions (to confirm)

- **Build order**: Phase 1 (generation) before Phase 2/3 (export+cartography), or pull the
  cartography seam earlier? (User signaled cartography is essential; default keeps generation
  first since export has nothing real to emit until plates exist.)
- **First rendered product** (Phase 4): equirectangular plate-boundary map (`carto.project`) vs
  3D globe (`carto.globe`). Default: the 2D map -- simpler golden test.
- **Doc home**: this file lives in `vault/architecture/`; confirm or move to
  `docs/superpowers/specs/`.
- **PlanetBody reconciliation** (§4b skew): **(a)** restore cartography into the editable
  workspace (`yokan-projects/fantasim-cartography`, curated port) and adjust its `WorldBridge` to
  consume the current `World.Observation.PlanetBody` (`int ShapeKind`) -- one planet-body type,
  cartography conforms to current world truth; or **(b)** keep cartography's 0.1.0
  `PlanetBodyModel`/`PlanetBodyShapeKind` surface unchanged and have the world emit a matching
  compat `PlanetBodyModel` in `World.Export.Contracts`. Default **(a)** -- matches "restore (but
  adjust if necessary)" and the curated-restoration discipline; pick (b) only to consume cartography
  as the unmodified 0.1.0 package.

## 12. References

- `vault/architecture/node-graph-paradigm.md` -- the paradigm + function-provider pattern
- `vault/architecture/service-tier-architecture.md` -- tier model
- `fantasim-world/vault/architecture/fantasim-ecosystem-parts-vs-assembly.md` -- the authority rule
- `ref-projects/fantasim-cartography/docs/world-cartography-boundary.md` -- the read-only boundary
- `ref-projects/fantasim-cartography/AGENTS.md` -- parts-vs-assembly + plate-projects reuse
- `ref-projects/fantasim-world/project/plugins/Geosphere.Discrete.Topology/` -- ref algorithms to lift
- `plate-projects/unify-cell/dotnet/src/UnifyCell.Core/SphericalVoronoiTessellation.cs` -- Voronoi core
