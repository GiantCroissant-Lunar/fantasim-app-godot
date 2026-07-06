# World Crust Run Spec Refactor Implementation Plan

> **AUDIT (2026-07-06, code-verified):** COMPLETED — WorldCrustRunSpec/Materializer/HydrosphereMode/BoundarySection* + tests. _(See the authority index in `vault/README.md`.)_


> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the `orchestrate-before-implementing` rule + the `external-agent-delegation`
> skill); otherwise execute inline with a review checkpoint per task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Reduce duplicated crust-generation input resolution by introducing one app-side crust run spec before switching graph and presentation to a shared materializer.

**Architecture:** Add an internal `WorldCrustRunSpec` in `App.World/Crust` that owns seed, frequency, target ticks, plates, recipe, rates, boundary profiles, and vertical exaggeration. First switch `WorldFunctionProvider.crust.generate` to consume the spec while preserving current provider behavior; later tasks can align graph defaults with presentation and move the pipeline call behind `WorldCrustMaterializer`.

**Tech Stack:** .NET 8, C# latest, xUnit, `FantaSim.Geosphere.Crust`, `FantaSim.Geosphere.Plate.Topology`, `UnifyCell`, `UnifyMaths`, app-local `WorldGenerationRenderOptions`.

## Global Constraints

- Keep production code Godot-free in `project/plugins/App.World`.
- Use project references as configured by `project/Directory.Build.props` (`UseProjectReferences=true` by default).
- Do not change `fantasim-world` or `fantasim-cartography` in this slice.
- Preserve existing `crust.generate` JSON behavior in Task 1; semantic alignment happens in a later task with its own tests.
- Follow RED -> GREEN -> REFACTOR for every behavior change.

---

### Task 1: Add Shared Run Spec Without Behavior Change

**Files:**
- Create: `project/plugins/App.World/Crust/WorldCrustRunSpec.cs`
- Create: `project/tests/App.World.Tests/WorldCrustRunSpecTests.cs`
- Modify: `project/plugins/App.World/WorldFunctionProvider.cs`

**Interfaces:**
- Consumes: `WorldGenerationRenderOptions`, `OnsetRoster`, `CrustPipeline.RunAsync`, `CrustInitRecipe`, `CrustEvolutionRates`, `Plate`.
- Produces: `internal sealed record WorldCrustRunSpec` with `FromExecutionPayload(JsonObject payload)` and `ForPresentation(WorldGenerationRenderOptions renderOptions, long onsetTick, long referenceTick)`.

- [ ] **Step 1: Write failing tests**

Add tests for:

```csharp
[Fact]
public void FromExecutionPayload_preserves_current_default_provider_contract()
{
    var spec = WorldCrustRunSpec.FromExecutionPayload(new JsonObject());

    Assert.Equal(3, spec.TessellationFrequency);
    Assert.Equal(UnitConverter.MegaAnnumToTickDelta(8.0), spec.EndTick);
    Assert.Equal(0L, spec.StartTick);
    Assert.Equal(0L, spec.RotationReferenceTick);
    Assert.Equal(new[] { spec.EndTick }, spec.SnapshotTicks);
    Assert.Equal(3, spec.Plates.Count);
    Assert.Equal(new HashSet<int> { 0, 1 }, spec.Recipe.ContinentalPlateIds);
}
```

```csharp
[Fact]
public void ForPresentation_uses_render_options_onset_roster_and_onset_rotation_reference()
{
    long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
    var options = WorldGenerationRenderOptions.Default;

    var spec = WorldCrustRunSpec.ForPresentation(options, onsetTick, onsetTick);
    var expected = OnsetRoster.Build(options.Seed, onsetTick, options.TessellationFrequency)
        .SeedPlatesAt(onsetTick);

    Assert.Equal(options.TessellationFrequency, spec.TessellationFrequency);
    Assert.Equal(onsetTick, spec.RotationReferenceTick);
    Assert.Equal(expected, spec.Plates);
    Assert.Equal(new[] { onsetTick }, spec.SnapshotTicks);
}
```

- [ ] **Step 2: Verify RED**

Run:

```bash
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --filter FullyQualifiedName~WorldCrustRunSpecTests --no-restore -v minimal --nologo
```

Expected: compile failure because `WorldCrustRunSpec` does not exist.

- [ ] **Step 3: Implement minimal spec**

Create `WorldCrustRunSpec` with payload parsing moved out of `WorldFunctionProvider`. Keep the legacy execution defaults: frequency 3, default three authored plates, default duration 8 Ma, default recipe `Continental(0, 1)`, default rotation reference 0.

- [ ] **Step 4: Verify GREEN**

Run the filtered test command again.

Expected: `WorldCrustRunSpecTests` pass.

- [ ] **Step 5: Switch provider to spec**

Update `WorldFunctionProvider.GenerateCrustAsync` to resolve `WorldCrustRunSpec` once, then pass its fields into `CrustPipeline.RunAsync` and `Summarize`.

- [ ] **Step 6: Verify provider behavior remains green**

Run:

```bash
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --filter FullyQualifiedName~WorldFunctionProviderTests --no-restore -v minimal --nologo
```

Expected: existing provider tests pass unchanged.

### Task 2A: Introduce Core Materializer

**Files:**
- Create: `project/plugins/App.World/Crust/WorldCrustMaterializer.cs`
- Create: `project/tests/App.World.Tests/WorldCrustMaterializerTests.cs`
- Modify: `project/plugins/App.World/WorldFunctionProvider.cs`

**Interfaces:**
- Consumes: `WorldCrustRunSpec`.
- Produces: `WorldCrustMaterialization` with the `GeodesicSphereTessellation`, `PlateTopology`, and single `CrustPipeline.RunAsync` result.

- [ ] **Step 1:** Add a failing test that `WorldCrustMaterializer.MaterializeAsync` produces the same topology/pipeline shape as the direct current provider path.
- [ ] **Step 2:** Implement the materializer as a thin wrapper over `CrustPipeline.RunAsync`.
- [ ] **Step 3:** Switch `WorldFunctionProvider.GenerateCrustAsync` to call the materializer.
- [ ] **Step 4:** Run `WorldCrustMaterializerTests`, `WorldFunctionProviderTests`, and full `App.World.Tests`.

### Task 2B: Add Presentation Projections

**Files:**
- Modify: `project/plugins/App.World/Crust/WorldCrustMaterializer.cs`
- Create or extend: `project/tests/App.World.Tests/WorldCrustMaterializerTests.cs`
- Modify: `project/plugins/App.World/Services/Service.cs`

**Interfaces:**
- Consumes: `WorldCrustMaterialization`.
- Produces: presentation projections for elevations, features, and app-derived thickness.

- [x] **Step 1:** Add a failing equivalence test comparing materializer surface outputs against current `BuildCrustSurfaceData` / `BuildCrustThicknessData`.
- [x] **Step 2:** Move current projection logic without changing semantics.
- [x] **Step 3:** Switch `Service.BuildPlanetPresentationRuntime` to use `WorldCrustRunSpec.ForPresentation` and the materializer.
- [x] **Step 4:** Run `App.World.Tests`, `App.World.Composition.Tests`, and presentation build.

**Status:** Completed in this slice. `Service.BuildPlanetPresentationRuntime` now materializes crust through `WorldCrustMaterializer.MaterializeAsync(WorldCrustRunSpec.ForPresentation(...))` and derives elevations/features/thickness via the new `WorldCrustMaterialization` projection API. The temporary private Service reference helpers and reflection equivalence tests were removed in Task 4 after direct materializer and Service presentation coverage replaced them. No blockers; all focused and full test suites pass.

### Task 3: Align Graph Defaults With Presentation

**Files:**
- Modify: `project/plugins/App.World/Crust/WorldCrustRunSpec.cs`
- Modify: `project/plugins/App.World/WorldFunctionProvider.cs`
- Modify: `project/tests/App.World.Tests/WorldFunctionProviderTests.cs`

**Interfaces:**
- Consumes: `WorldGenerationRenderOptions.Default`, `OnsetRoster`.
- Produces: one graph and presentation default for seed/frequency/plate roster.

- [x] **Step 1:** Add failing tests that default graph crust generation reports the same frequency and plate count as `WorldCrustRunSpec.ForPresentation`.
- [x] **Step 2:** Change execution defaults from hard-coded three plates to onset-roster plates.
- [x] **Step 3:** Update provider tests to assert the new shared default contract.
- [x] **Step 4:** Run `App.World.Tests`, `App.World.Composition.Tests`, and presentation build.

**Status:** Completed. `WorldCrustRunSpec.FromExecutionPayload` now falls back to `WorldGenerationRenderOptions.Default.TessellationFrequency` and `OnsetRoster.Build(defaultSeed, PlateOnsetTick, frequency).SeedPlatesAt(PlateOnsetTick)` when the payload does not override `frequency` or `plates`. The graph default thus matches the presentation path's seed/frequency/plate-count contract. Explicit `frequency`, `plates`, `canonicalTick`, `snapshotTicks`, `rotationReferenceTick`, `continentalPlates`, and rate overrides continue to work. Rotation reference semantics remain unchanged (default `0` for graph payloads; `onsetTick` for presentation). No blockers.

### Task 4: Remove Duplicate Service Projection Helpers and Replace Reflection Equivalence Tests

**Files:**
- Modify: `project/plugins/App.World/Services/Service.cs`
- Modify: `project/plugins/App.World/Crust/WorldCrustMaterializer.cs`
- Modify: `project/tests/App.World.Tests/WorldCrustMaterializerTests.cs`
- Modify: `project/tests/App.World.Tests/WorldServiceGenerationProductsTests.cs`
- Modify: `docs/plans/2026-07-03-world-crust-run-spec-refactor.md`

**Interfaces:**
- Consumes: `WorldCrustMaterialization.BuildSurfaceData`, `WorldCrustMaterialization.BuildCrustThickness`.
- Produces: direct behavior coverage of materializer projections; Service no longer carries duplicated private projection helpers.

- [x] **Step 1:** Replace reflection-based equivalence tests in `WorldCrustMaterializerTests` with direct behavior assertions:
  - `BuildSurfaceData(...)` returns non-null elevation and feature arrays whose lengths match the onset globe cell count.
  - At least one feature has non-zero magnitude or non-default kind when features are present at the reference tick.
  - `BuildSurfaceData(...)` returns null arrays for a pre-onset tick.
  - `BuildCrustThickness(...)` returns a non-null array matching the globe cell count with all values finite and non-negative.
  - `BuildCrustThickness(...)` returns null for a pre-onset tick.
- [x] **Step 2:** Add or extend Service-level presentation coverage: `GetPlanetPresentationAsync()` populates `CellElevations`, `CellFeatures`, and `CellCrustThickness` with globe-sized arrays. Service construction via `new Service(new ServiceRegistry())` is straightforward, so no fake production path is needed.
- [x] **Step 3:** Remove duplicate private helpers from `Service.cs`: `BuildCrustSurfaceData`, `BuildCrustThicknessData`, `BuildGlobeGeometryFromSnapshot`, and `ToGeoPoint`. Remove now-unused `using` directives.
- [x] **Step 4:** Update materializer comments to describe `BuildSurfaceData`, `BuildCrustThickness`, and `BuildGlobeGeometryFromSnapshot` as the source-of-truth implementation rather than mirroring private Service helpers.
- [x] **Step 5:** Run focused tests (`WorldCrustMaterializer`, `WorldService`) and broad verification (`App.World.Tests`, `App.World.Composition.Tests`, `App.Presentation` build).

**Status:** Completed. `Service.cs` no longer contains the duplicate private projection helpers. `WorldCrustMaterializerTests` no longer uses reflection to invoke private Service methods. Materializer projection behavior is covered directly, and the existing `WorldServiceGenerationProductsTests.PlanetPresentation_CarriesGlobeSizedCrustArraysAtOnset` asserts that `GetPlanetPresentationAsync()` returns globe-sized `CellCrustThickness`, `CellElevations`, and `CellFeatures` through the real Service construction path. All focused and broad verifiers pass. No blockers.

### Task 5: Route GlobeReconstructor Crust Runs Through the Run Spec

**Files:**
- Modify: `project/plugins/App.World/Crust/WorldCrustRunSpec.cs`
- Modify: `project/plugins/App.World/Globe/GlobeReconstructor.cs`
- Modify: `project/tests/App.World.Tests/WorldCrustRunSpecTests.cs`
- Modify: `project/tests/App.World.Tests/GlobeReconstructorTests.cs`

**Interfaces:**
- Consumes: `WorldCrustRunSpec.ForReconstructor`, `CrustPipeline.RunAsync`, reconstructor-owned tessellation/plates.
- Produces: one app-side owner for default crust recipe/rates/rotation-reference configuration while preserving reconstructor gating.

- [x] **Step 1:** Add `WorldCrustRunSpec.ForReconstructor(...)` for callers that already own their plate roster and active snapshot ticks.
- [x] **Step 2:** Switch `GlobeReconstructor.RunCrustFeatures`, `RunCrustEvolution`, and `RunCrustSnapshot` to resolve recipe, rates, tick range, and rotation reference through the spec.
- [x] **Step 3:** Remove the duplicate private `GlobeReconstructor.DefaultRates()` helper.
- [x] **Step 4:** Add factory coverage in `WorldCrustRunSpecTests` and public API gating coverage in `GlobeReconstructorTests`.
- [x] **Step 5:** Run focused and broad verifiers.

**Status:** Completed. `GlobeReconstructor` still owns regime gating, tessellation, and direct `CrustPipeline.RunAsync` execution, but no longer owns duplicate default crust config. The shared spec now owns default `CrustInitRecipe.Continental(0, 1)` and crust evolution rates for the reconstructor path as well as graph/presentation paths. Verifiers passed: `WorldCrustRunSpecTests`, `GlobeReconstructorTests`, full `App.World.Tests`, and `App.World.Composition.Tests`. No blockers.

### Task 6: Add Dry No-Hydrosphere Crust Elevation Mode

**Files:**
- Modify: `project/plugins/App.Ecs/Systems/CellElevationSystem.cs`
- Modify: `project/plugins/App.World/GenerationGraph/WorldGenerationRenderOptions.cs`
- Modify: `project/plugins/App.World/GenerationGraph/WorldGenerationNodeCatalog.cs`
- Modify: `project/plugins/App.World/Crust/WorldCrustRunSpec.cs`
- Modify: `project/plugins/App.World/Crust/WorldCrustMaterializer.cs`
- Modify: `project/plugins/App.World/Cells/CellElevationModel.cs`
- Modify: `project/plugins/App.World/HostComposition/CellElevationComposition.cs`
- Modify: focused tests under `project/tests/App.Ecs.Tests` and `project/tests/App.World.Tests`

**Interfaces:**
- Consumes: graph `hydrosphereMode` option, `WorldGenerationRenderOptions.HydrosphereMode`, `WorldCrustRunSpec.HydrosphereMode`.
- Produces: dry/default crust elevations that expose oceanic crust without sea-level offset or ocean-age deepening, while preserving the legacy wet/oceanic formula behind an explicit mode.

- [x] **Step 1:** Add `CellElevationHydrosphereMode` with `Present` and `Absent` modes. Keep the existing `CellElevationSystem.Derive(sample)` API as the legacy `Present` behavior.
- [x] **Step 2:** Make `Absent` mode compute exposed rocky crust from continental fraction plus uplift/volcanism only, so young and old oceanic crust no longer diverge from hydrosphere/abyssal assumptions.
- [x] **Step 3:** Add `HydrosphereMode` to `WorldGenerationRenderOptions`, default it to `Absent`, and parse graph values such as `dry`, `absent`, `no-hydrosphere`, `present`, and `wet`.
- [x] **Step 4:** Register `hydrosphereMode` on the `world.options` node schema so authored graph edits can validate and pass it through.
- [x] **Step 5:** Thread the option through `WorldCrustRunSpec`, `WorldCrustMaterializer`, `CellElevationModel.Build(...)`, and `CellElevationComposition.Compose(...)`.
- [x] **Step 6:** Add focused tests for formula behavior, graph option parsing, run-spec propagation, materializer output, and presentation model threading.
- [x] **Step 7:** Run focused and broad verifiers.

**Status:** Completed. Default graph and presentation crust rendering now use a dry/no-hydrosphere mode, so the app can show exposed rocky terrain instead of treating oceanic crust as submerged by default. Legacy ocean/deepening behavior remains available via `hydrosphereMode=present` / `wet` and through the existing no-argument ECS API. Verifiers passed: focused `CellElevationSystemTests`, `WorldGenerationRenderOptionsTests`, `WorldCrustRunSpecTests`, `WorldCrustMaterializerTests`, `CellElevationModelTests`; full `App.World.Tests`, full `App.World.Composition.Tests`, full `App.Ecs.Tests`; `dotnet tool restore`; and `dotnet unify-build Compile`. The external OpenCode delegation for this slice stalled in analysis-only mode and produced no code changes, so the lead session completed the implementation directly. No blockers.

### Task 7: Add Boundary-Normal Section Product and Renderer

**Files:**
- Create: `project/contracts/App.World/BoundarySectionDocument.cs`
- Create: `project/plugins/App.World/Topography/BoundarySectionBuilder.cs`
- Create: `project/plugins/App.Presentation/BoundarySectionRenderer.cs`
- Create: `project/tests/App.World.Tests/BoundarySectionBuilderTests.cs`
- Modify: `project/contracts/App.World/PresentationLayers.cs`
- Modify: `project/plugins/App.World/Crust/WorldCrustMaterializer.cs`
- Modify: `project/plugins/App.World/Services/Service.cs`
- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.cs`
- Modify: `project/tests/App.World.Tests/WorldCrustMaterializerTests.cs`
- Modify: `project/tests/App.World.Tests/WorldServiceGenerationProductsTests.cs`

**Interfaces:**
- Consumes: `PlateBoundaryArc`, `CellBoundaryField`, `ConvergentPolarity`, `BoundaryProfileShape`, `BoundaryProfileParameters`, materialized crust state/features.
- Produces: `BoundarySectionDocument` samples for representative convergent/divergent/transform arcs and a Godot `BoundarySectionRenderer` that displays section panels in the active planet presentation.

- [x] **Step 1:** Add RED tests for boundary section shape:
  - convergent subduction preserves polarity and produces trench + overriding-side rise,
  - divergent section has a rift axis below flanks,
  - transform section remains narrow/subtle.
- [x] **Step 2:** Add a contract DTO in `App.World` rather than `App.World.Rendering`, because `App.World.Rendering` already references `App.World`; putting the document there would create a project-reference cycle once `PlanetPresentationDocument` carried it.
- [x] **Step 3:** Implement `BoundarySectionBuilder` as a Godot-free builder that reuses existing polarity and boundary-profile math; no second tectonic enum or duplicate profile system.
- [x] **Step 4:** Add materializer projection and service/document plumbing so `GetPlanetPresentationAsync()` carries boundary sections.
- [x] **Step 5:** Dispatch OpenCode for a bounded presentation renderer file and review the result before binding it.
- [x] **Step 6:** Bind `BoundarySectionRenderer` in `PlanetPresentationBinder` as a world-view panel group separate from the radial W3a cutaway wedge.
- [x] **Step 7:** Run focused and broad verifiers.

**Status:** Completed and windowed-screenshot verified. The app now has a first-class boundary-normal section product, separate from the radial cutaway wedge, and the active planet presentation binds representative convergent/divergent/transform section panels. Verifiers passed: `BoundarySectionBuilderTests`; focused `WorldCrustMaterializerTests` + `WorldServiceGenerationProductsTests`; full `App.World.Tests`; full `App.World.Composition.Tests`; `dotnet build project/plugins/App.Presentation/App.Presentation.csproj --no-restore -v minimal --nologo`; `dotnet unify-build Compile`; and `task build:godot:desktop`. Full exported-app verification used the remote command path to seek the plate-world tick and capture `/tmp/fantasim-boundary-section-windowed-mobile-zorder-20260703.png`; the screenshot shows the three section panels with the convergent slab guide visible. The panels are intentionally first-pass and still visually understated in the full UI, so later polish should focus on placement, scale, and contrast rather than data plumbing.
