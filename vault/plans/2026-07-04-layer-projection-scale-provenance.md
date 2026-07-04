# Layer Projection Scale Provenance Implementation Plan

> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the `orchestrate-before-implementing` rule + the `external-agent-delegation`
> skill); otherwise execute inline with a review checkpoint per task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Make crust globe projection scale and provenance explicit so the crust-focused layer can use cartography adaptive subdivision without becoming simulation truth.

**Architecture:** `fantasim-world`/App.World keep cell, plate, crust, and boundary facts on the current UnifyCell geodesic tessellation. `fantasim-cartography` remains the projection/adaptive surface builder. `fantasim-app-godot` carries a layer projection profile on the presentation document and uses it to select fixed/adaptive caps and scale labels in the renderer.

**Tech Stack:** .NET contracts and xUnit tests, Godot 4 C# presentation bundle, `FantaSim.Cartography.Globe`, `UnifyCell`/Unify design docs.

## Global Constraints

- Do not move simulation truth off `UnifyCell.GeodesicSphereTessellation` in this slice.
- Do not add S2/H3/hex/Voronoi as truth-grid replacements; they can only be documented as future projection/index adapters.
- Keep App.World.Rendering contracts Godot-free.
- Use TDD: add failing tests before implementation.
- Verify with focused tests, `dotnet unify-build Compile`, exported app/bundle reload when possible, and a fresh screenshot.

---

### Task 1: Add Layer Projection Contract

**Files:**
- Modify: `project/contracts/App.World/PresentationLayers.cs`
- Modify: `project/plugins/App.World/Services/Service.cs`
- Test: `project/tests/App.World.Tests/WorldServiceGenerationProductsTests.cs`

**Interfaces:**
- Produces: `PlanetLayerProjectionProfile` attached to `PlanetPresentationDocument.LayerProjectionProfiles`.
- Consumes: existing `VerticalExaggeration`, `SurfaceSubdivision`, `AdaptiveSubdivisionMaxDepth`, and `AdaptiveSubdivisionEdgeHeightDelta`.

- [ ] **Step 1: Write the failing test**

Add a test that fetches a `PlanetPresentationDocument` and asserts it has a `geosphere.crust` projection profile with:

```csharp
Assert.Equal("UnifyCell.GeodesicSphereTessellation", crust.SourceGrid);
Assert.Equal("physical-metres", crust.SourceUnit);
Assert.Equal("unit-sphere-displacement", crust.DisplacementUnit);
Assert.Equal(document.VerticalExaggeration, crust.MetresToUnitRadius);
Assert.Equal(document.SurfaceSubdivision, crust.SurfaceSubdivision);
Assert.True(crust.PreservesCellProvenance);
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```bash
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --filter FullyQualifiedName~LayerProjection -v minimal --nologo
```

Expected: FAIL because the projection profile API does not exist yet.

- [ ] **Step 3: Implement the minimal contract**

Add a Godot-free record to `PresentationLayers.cs` and populate it in `Service.GetPlanetPresentationAsync` from the existing runtime values. Keep existing scalar properties for compatibility.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the same focused test and expect PASS.

### Task 2: Use Projection Profile In Plate Surface Binding

**Files:**
- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.cs`
- Test: `project/tests/App.World.Tests/PlateCapMeshBuilderTests.cs`
- Optional Test: add a Godot-free resolver test if a resolver is extracted under `project/contracts/App.World.Rendering/Globe/`.

**Interfaces:**
- Consumes: `PlanetPresentationDocument.LayerProjectionProfiles`.
- Produces: crust-focused hypsometric terrain can use adaptive cartography caps and crust vertical scale, with cell provenance preserved through `PlateCap.VertexProvenance`.

- [ ] **Step 1: Write the failing test**

Add or update a Godot-free test that proves the crust projection path selects adaptive caps when the crust profile requests adaptive subdivision, and that midpoint vertices retain provenance and interpolated colors.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```bash
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --filter FullyQualifiedName~Projection -v minimal --nologo
```

Expected: FAIL until the renderer selection uses the projection profile.

- [ ] **Step 3: Implement the minimal renderer selection**

In `PlanetPresentationBinder.BindPlateSurface`, resolve the crust projection profile for `GlobeViewMode.HypsometricTerrain` and allow adaptive subdivision for that focused crust view. Keep `World` view's current sqrt lens unless explicitly represented by a separate profile later.

- [ ] **Step 4: Run focused tests and app compile**

Run:

```bash
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --filter FullyQualifiedName~Projection -v minimal --nologo
dotnet build project/plugins/App.Presentation/App.Presentation.csproj --no-restore -v minimal --nologo
```

Expected: PASS.

### Task 3: Document Grid Interchange Boundary

**Files:**
- Modify: `vault/architecture/globe-surface-lod-scale-and-provenance.md`

**Interfaces:**
- Produces: an explicit note that UnifyCell supports multiple tessellation families as contracts, but this app slice keeps one truth grid and treats alternate grids as projection/index layers until migration is designed.

- [ ] **Step 1: Add documentation**

Add a short section clarifying: geodesic truth grid now; S2 as index; H3 absent; hex/Voronoi possible through future UnifyCell adapters; mixed grids need labelled provenance and cannot be silently combined.

- [ ] **Step 2: Verify docs and code**

Run:

```bash
dotnet unify-build Compile
```

Expected: PASS.

### Task 4: Visual Verification

**Files:**
- Capture screenshot under `build/_artifacts/visual-verification/` or another repo-local verification folder.

**Interfaces:**
- Produces: screenshot evidence that the crust surface is visible with dry exposed relief and adaptive/focused crust scale.

- [ ] **Step 1: Build/install bundle or exported app**

Use the repo Taskfile/unify-build flow already present in the app repo. Keep the exported app open for bundle reload where possible.

- [ ] **Step 2: Capture a fresh screenshot**

Use the real rendered Godot/exported app surface. Do not claim visual correctness from tests alone.

- [ ] **Step 3: Inspect screenshot**

Confirm: dry crust is exposed, plate/boundary relief is visible, no hydrosphere masks the crust, and the globe is not blank or stale.
