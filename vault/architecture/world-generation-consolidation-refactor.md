# World generation consolidation and refactor plan

> **2026-07-13 authority update:** canonical history/checkpoint/cache ownership, app `.rot` truth
> adoption, the `WorldRuntime` rename, and the signed dry-crust relief gate are now specified by
> `vault/specs/2026-07-13-canonical-world-history-and-dry-crust-design.md`. In particular, the
> existing persisted crust product is a disposable cache and must not be promoted to canonical
> checkpoint truth.

> **AUDIT (2026-07-06, code-verified):** CURRENT with drift — the parameterless `GetPlanetPresentationAsync()` this doc lists in the active path is now BANNED (station contract 3 / C3 conformance gate; `Rebind()` is tick-addressed); `Continents` mode postdates this doc; Phase 5 (Seam fate) remains open. _(See the authority index in `vault/README.md`.)_


**Status:** DRAFT (2026-07-03). Cross-repo architecture note for reducing duplicated
world-generation concepts across `fantasim-app-godot`, `fantasim-world`, and
`fantasim-cartography`.

Extends:

- [`world-generation-cartography-flow.md`](world-generation-cartography-flow.md)
- [`render-surface-and-motion.md`](render-surface-and-motion.md)
- [`node-graph-paradigm.md`](node-graph-paradigm.md)

## 1. Why this doc exists

The current app already covers a large amount of planet generation: plate onset,
plate topology, crust accumulation, boundary profiles, watertight plate surfaces,
terrain color, cutaway strata, generation-graph products, and timeline-triggered
crust runs. The problem is not absence of systems. The problem is that some of
those systems duplicate the same concept with different defaults and different
authority.

The most important mismatch:

- The world-generation graph can run a crust product through
  `WorldFunctionProvider.GenerateCrustAsync`.
- The visible planet surface is rebuilt independently by `Service.BuildPlanetPresentationRuntime`
  through `OnsetRoster` and `GlobeReconstructor`.
- These paths can use different plate rosters, defaults, and materialized data.

That means a product can be marked generated while the rendered planet is showing
a separately computed crust. This doc pins ownership and provides a refactor path
before more crust, slice, and hydrosphere work is added.

## 2. Current active path

The active exported app path is:

1. `Host.LoadWorldBundleAndMountPlanetAsync`
2. `PresentationComposition.CreatePlanetPresentation`
3. `PlanetPresentationBinder.Rebind`
4. `WorldService.GetPlanetPresentationAsync`
5. `Service.BuildPlanetPresentationRuntime`
6. `GlobeReconstructor.FromOnsetRoster`
7. `GlobePlateSurfaces.BuildSurfaces`
8. `PlanetPresentationBinder.BuildPlateMesh`

The older `App.World.Seam` path (`WorldViewComposition`, `GlobeView`,
`TimelineController`) is currently not mounted by the host. It is useful as a
reference implementation, especially because it already uses cartography
`FlatNormals` for a faceted crust look, but it should not keep accumulating new
behavior unless it is explicitly revived.

## 3. Ownership decisions

### 3.1 `fantasim-world`

Owns durable world-generation truth:

- Plate topology contracts and materialization.
- Boundary classification: convergent, divergent, transform, inactive.
- Crust fields currently emitted by `Geosphere.Crust`:
  - `geosphere.crust.continental-fraction`
  - `geosphere.crust.orogenic-pressure`
  - `geosphere.crust.volcanic-activity`
  - `geosphere.crust.crust-age`
- Derived crust feature kinds: mountain, volcanic arc, trench, ridge, fault.

It does **not** currently own `elevation-m` or `crust-thickness-m`.

### 3.2 `fantasim-cartography`

Owns rendering parts, not world assembly:

- `GlobeSurfaceBuilder`
- shared-vertex watertight globe surface construction
- `SmoothNormals` and `FlatNormals`
- deterministic noise relief
- map/globe projection primitives

It should not gain a world-generation pipeline. The app assembles cartography
parts around world products.

### 3.3 `fantasim-app-godot`

Owns assembly and presentation:

- Node-graph execution and product metadata.
- Runtime world service DTOs for the host.
- Presentation materialization until equivalent engine-side fields exist.
- Godot binding, meshes, materials, timeline, layer selection, and cutaway UI.

App-local synthetic fields are allowed while the engine lacks equivalent fields,
but they must be named and documented as app-derived presentation data, not engine
truth.

## 4. Duplicate concepts to consolidate

| Concept | Current duplicate paths | Classification | Action |
|---|---|---|---|
| Plate roster / motion | `WorldFunctionProvider.DefaultThreePlates`; `GlobeReconstructor.DefaultPlates`; `OnsetRoster.Build`; `GlobeReconstructor.FromOnsetRoster` | Bug risk | Introduce one app-side `WorldCrustRunSpec` built from generation graph options and onset roster. Use it for both graph crust generation and presentation materialization. |
| Crust pipeline execution | `WorldFunctionProvider.GenerateCrustAsync`; `GlobeReconstructor.RunCrustFeatures`; `RunCrustEvolution`; `RunCrustSnapshot` | Bug risk | Keep one materializer method that calls `CrustPipeline.RunAsync` once for a requested snapshot set and exposes all needed projections. |
| Elevation | `CellElevationSystem.Derive` plus boundary profiles; `SyntheticCrustLayer` `elevation-m`; old `CellElevationModel` ECS path | Mixed | Treat `CellElevationSystem + BoundaryProfileContribution` as the active presentation elevation. Do not advertise `SyntheticCrustLayer.elevation-m` as displayed terrain. |
| Crust thickness | `SyntheticCrustLayer.crust-thickness-m`; `CutawayStratumProfile.DefaultCrustThicknessMetres`; `PlanetPresentationDocument.CellCrustThickness` | Intentional scaffold, misleading naming | Rename docs/comments to app-derived thickness until `fantasim-world` owns a real thickness field. |
| Planet renderer | Active `PlanetPresentationBinder`; inactive `GlobeView` seam | Legacy debt | Make `PlanetPresentationBinder` the only active target. Mine `GlobeView` for flat-normal mesh behavior, then freeze or remove seam path. |
| Boundary rendering | `PlateBoundaryFocusRenderer` polylines; `BoundaryProfileShape` scalar topography; generic cutaway wedge | Missing product | Add boundary-normal section views for convergent/divergent/transform. Do not overload the radial cutaway wedge. |
| Render constants | `WorldGenerationRenderOptions`; `PlanetPresentationBinder` height constants; `GlobePlateSurfaces.DefaultPeaks`; `CutawayStratumProfile` | Manageable debt | Classify as world-authored parameters vs view-only look-dev parameters. Move world-authored ones behind graph options. |

## 5. Target shape

Add a single materialization layer in `App.World`:

```text
WorldGenerationGraph options
        |
        v
WorldCrustRunSpec
  seed, frequency, onset tick, schedule, plates, recipe, rates,
  boundary profile params, snapshot ticks, rotation reference tick
        |
        v
WorldCrustMaterializer
  one CrustPipeline.RunAsync call
        |
        +-- Globe snapshot at tick
        +-- Boundary arcs at tick
        +-- Crust state by tick
        +-- Crust features by tick
        +-- Presentation elevation by cell
        +-- App-derived crust thickness by cell
        +-- Product summary payload
```

The generation graph and the presentation document both consume this same
materialized result. The graph can still return JSON summaries and product
addresses, but those summaries must describe the same crust the presentation
uses.

### 5.1 Proposed types

Add a small run-spec type and a materializer. The exact folder can be finalized
during implementation; suggested home is a new `project/plugins/App.World/Crust/`
folder so these types do not look like graph-only code.

```csharp
public sealed record WorldCrustRunSpec(
    int Seed,
    int TessellationFrequency,
    long OnsetTick,
    long ReferenceTick,
    long RotationReferenceTick,
    IReadOnlyList<long> SnapshotTicks,
    IReadOnlyList<Plate> Plates,
    CrustInitRecipe Recipe,
    CrustEvolutionRates Rates,
    SphereRegimeSchedule GeosphereSchedule,
    SphereRegimeSchedule AtmosphereSchedule,
    BoundaryProfileParameters BoundaryProfiles,
    double VerticalExaggeration);

public sealed record WorldCrustMaterialization(
    WorldCrustRunSpec Spec,
    GlobeReconstructor Reconstructor,
    CrustEvolutionResult CrustResult,
    IReadOnlyList<double>? PresentationElevations,
    IReadOnlyList<double>? PresentationCrustThickness,
    IReadOnlyList<CellCrustFeature>? PresentationFeatures);
```

`WorldCrustRunSpec.Resolve(...)` is the single place that maps the graph family
and world options into seed, frequency, plates, rates, recipe, snapshot ticks,
and rotation-reference convention. `WorldCrustMaterializer.MaterializeAsync(...)`
is the single place that calls `CrustPipeline.RunAsync`.

### 5.2 Important blocker

Authored plate/rate/recipe inputs must be persisted in the graph family, not only
passed as ephemeral `WorldGenerationRequest.Parameters`. Presentation fetches
reconstruct their document from the family plus cached product metadata. If graph
generation can receive request-only `plates`, `continentalPlates`, or rates, the
presentation cannot later resolve the same `WorldCrustRunSpec`.

Implementation should first verify where those authored values originate:

- If they are graph-node parameters, the spec resolver can read them.
- If they are request-only parameters, the authoring path must persist them into
  the graph family before the materializer refactor can guarantee consistency.

## 6. Boundary slices

The existing cutaway wedge is a radial planet section. It shows broad interior
strata, not plate interaction mechanics. The requested convergent/divergent/
transform slices need a different product:

```text
PlateBoundaryArc + nearby cells + CellBoundaryField + BoundaryProfileShape
        |
        v
BoundarySectionDocument
  kind, selected arc, normal axis, sample distances,
  surface profile, crust/lithosphere bands, labels/handles
        |
        v
BoundarySectionRenderer
```

Initial slice behavior:

- Convergent: trench on subducting side, uplift or volcanic arc on overriding
  side, optional slab guide line.
- Divergent: axial rift notch, flanking swell, young crust band.
- Transform: narrow scarp band with lateral shear indicators.

This belongs in presentation/app contracts first. It should reuse existing
boundary profile math instead of inventing a second tectonic-shape system.

### 6.1 Minimal contract

Suggested home: `project/contracts/App.World.Rendering/Composition/BoundarySectionDocument.cs`,
beside `CutawayWedge` and `CutawayStratumProfile`. That assembly is shared,
Godot-free, and already references the world contracts needed for
`PlateBoundaryKind` and `GlobeVec3`.

Implementation note (2026-07-03): the first shipped slice placed
`BoundarySectionDocument` in `project/contracts/App.World/BoundarySectionDocument.cs`
instead. `App.World.Rendering` already references `App.World`; once
`PlanetPresentationDocument` carries boundary sections, putting the document in
`App.World.Rendering` would create a project-reference cycle. The document stays
Godot-free and uses its own `BoundarySectionBand`/`BoundarySectionColor` DTOs
while builders may still map from `CutawayStratumProfile` internally.

Do not add a second tectonic enum. Reuse `PlateBoundaryKind`.

```csharp
public readonly record struct BoundarySectionSample(
    double SignedDistanceRad,
    double ElevationMetres,
    double CrustThicknessMetres,
    PlateBoundaryKind FeatureKind);

public sealed record BoundarySectionDocument(
    int PlateA,
    int PlateB,
    PlateBoundaryKind Kind,
    GlobeVec3 Origin,
    GlobeVec3 NormalAxis,
    IReadOnlyList<BoundarySectionSample> Samples,
    IReadOnlyList<StratumBand> InteriorBands,
    double Exaggeration,
    double PlanetRadiusMetres,
    string? LabelOverride);
```

The builder should reuse `ConvergentPolarity`, `CellBoundaryField`, and
`BoundaryProfileShape`. The renderer should not call those topography helpers
directly; it consumes `BoundarySectionDocument` and only lifts the data into
Godot geometry.

## 7. Visual target for dry crust

The Matt Keeter tiny-planet reference is a useful rendering target: icosphere
surface, noise-offset terrain, jitter, and faceted dry crust. The project already
has the required pieces:

- `fantasim-cartography`: `GlobeSurface`, `GlobeSurfaceBuilder`, `NoiseRelief`,
  `FlatNormals`.
- `fantasim-app-godot`: `GlobePlateSurfaces` builds watertight plate caps with
  seeded peaks.

Therefore the immediate visual refactor is not a new terrain engine. It is:

1. Keep `GlobePlateSurfaces` as the geometry source.
2. Change active `PlanetPresentationBinder` terrain meshes to use `FlatNormals`
   for world and hypsometric terrain modes.
3. Keep smooth normals available for diagnostic or future ocean/atmosphere modes.

## 8. Phased refactor plan

### Phase 0: Documentation and review

- Land this document.
- Dispatch read-only agent reviews focused on:
  - materializer boundary and product/presentation consistency
  - safe removal or freezing of inactive `App.World.Seam`
  - boundary slice contract shape

### Phase 1: Low-risk visual cleanup

- In active `PlanetPresentationBinder`, switch dry terrain mesh normals from
  `SmoothNormals` to `FlatNormals`.
- Add tests for mesh-normal selection if feasible at the Godot-free boundary.
- Do not touch crust truth or graph semantics in this phase.

### Phase 2: Materialization consolidation

- Add `WorldCrustRunSpec`.
- Add `WorldCrustMaterializer`.
- Move `GlobeReconstructor.RunCrustSnapshot` internals behind the materializer,
  or have it delegate to the materializer.
- Make `WorldFunctionProvider.GenerateCrustAsync` use the same spec/materializer
  as `Service.BuildPlanetPresentationRuntime`.
- Update tests so generated product metadata and presentation document agree on
  seed, frequency, snapshot ticks, plate count, cell count, and summary tick.
- Add a direct consistency test proving `WorldFunctionProvider.GenerateCrustAsync`
  and `Service.GetPlanetPresentationAsync(tick)` resolve the same spec for the
  default graph family.

### Phase 3: Naming cleanup

- Rename comments and docstrings that call app-derived thickness "truth".
- If a public DTO cannot rename yet, clarify semantics:
  `CellCrustThickness` means app-derived thickness until a world-owned field
  exists.
- Keep `SyntheticCrustLayer` visibly synthetic or replace it with an engine-backed
  producer when `fantasim-world` adds real thickness/elevation fields.

### Phase 4: Boundary section product

- Add a Godot-free `BoundarySectionDocument` contract.
- Add a builder that samples one selected `PlateBoundaryArc`.
- Add a presentation renderer for convergent/divergent/transform sections.
- Keep it separate from the radial cutaway wedge.

### Phase 5: Legacy path cleanup

- Decide whether `App.World.Seam` is:
  - deleted,
  - moved to tests/sandbox,
  - or revived as the collectible presentation implementation.
- Until that decision, do not add new features to `GlobeView`.

## 9. Verification gates

Minimum verification for refactors:

- `dotnet test` for `App.World.Tests`, `App.World.Composition.Tests`, and
  cartography globe tests when touched.
- For Godot presentation changes, run the app/export verification path already
  used by the project and capture a screenshot when practical.
- Product/presentation consistency tests must assert that graph generation and
  `GetPlanetPresentationAsync(tick)` derive from the same spec.

## 10. Open decisions

1. Should `WorldFunctionProvider.GenerateCrustAsync` stop supporting its standalone
   3-plate default, or keep it only behind explicit test/demo parameters?
2. Should `CellCrustThickness` remain on `PlanetPresentationDocument`, or should
   the app expose a more explicitly derived field name before public consumers
   depend on it?
3. Should `App.World.Seam` be removed now, or kept as a reference until
   `PlanetPresentationBinder` has copied its useful flat-normal behavior?
4. Should boundary slices be shown as an overlay panel, a world-space floating
   section, or a layer-specific view in the node/timeline UI?

## 11. Immediate recommendation

Do Phase 0 and Phase 1 first. They are low risk and directly support the visual
direction. Then do Phase 2 before adding boundary slices or hydrosphere, because
otherwise those new features will attach to the current split product/presentation
model and make the duplication harder to unwind.
