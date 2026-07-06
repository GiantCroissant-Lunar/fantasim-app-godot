# Recursive Feature-Aware Adaptive Subdivision Implementation Plan

> **AUDIT (2026-07-06, code-verified):** COMPLETED — recursive default, featureWeightDelta parsing, TectonicDetailSampler hook. _(See the authority index in `vault/README.md`.)_


> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the `orchestrate-before-implementing` rule + the `external-agent-delegation`
> skill); otherwise execute inline with a review checkpoint per task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Upgrade adaptive globe subdivision from depth-1 height-only refinement to recursive,
feature-aware crust refinement while preserving source-cell provenance and app screenshot
verification.

**Architecture:** `fantasim-cartography` owns the recursive, Godot-free adaptive surface builder.
`fantasim-app-godot` remains the presentation integrator: it derives per-cell crust refinement
weights from existing typed crust features, gathers them to plate vertices using the same topology
as elevation, passes them through cartography options, and verifies the focused crust view in the
exported app.

**Tech Stack:** .NET 8, xUnit, FantaSim Cartography Globe, UnifyMaths.Numerics 0.1.5, Godot 4 .NET
host via the existing exported app pipeline.

## Global Constraints

- TDD required: every production behavior change starts with a failing focused test.
- Do not change simulation truth identity: adaptive subdivision is render geometry only.
- `SourceTriangleIds` must remain parallel to generated triangles and map back to source cells.
- `VertexProvenance` must remain sufficient for render attributes to resolve every generated
  vertex without fallback colors.
- Feature-aware refinement must consume existing `PlanetPresentationDocument.CellFeatures`; do not
  introduce a new truth stream.
- Build through repo-native commands: focused `dotnet test`, then `dotnet unify-build Compile`,
  then exported-app screenshot verification when app code changes.

---

## File Map

- `fantasim-cartography/project/contracts/Cartography.Globe/AdaptiveSubdivisionOptions.cs`
  carries recursive-depth and optional per-vertex feature-threshold inputs.
- `fantasim-cartography/project/contracts/Cartography.Globe/AdaptiveGlobeSurface.cs`
  documents provenance semantics for recursive generated vertices.
- `fantasim-cartography/project/plugins/Cartography.Globe.Core/AdaptiveGlobeSurfaceBuilder.cs`
  performs recursive conforming edge-split subdivision.
- `fantasim-cartography/project/tests/Cartography.Globe.Core.Tests/AdaptiveGlobeSurfaceBuilderTests.cs`
  proves recursion, source triangle mapping, watertightness, and recursive provenance.
- `fantasim-app-godot/project/contracts/App.World/PresentationLayers.cs`
  exposes crust adaptive feature-threshold metadata.
- `fantasim-app-godot/project/contracts/App.World.Rendering/Globe/GlobePlateSurfaces.cs`
  derives per-plate feature refinement weights from per-cell weights.
- `fantasim-app-godot/project/contracts/App.World.Rendering/Globe/PlateCapMeshBuilder.cs`
  resolves recursive midpoint provenance for terrain colors.
- `fantasim-app-godot/project/contracts/App.World.Rendering/Globe/LayerProjectionProfileResolver.cs`
  forwards the feature threshold into rendering.
- `fantasim-app-godot/project/plugins/App.World/GenerationGraph/WorldGenerationRenderOptions.cs`
  parses authored feature-threshold options.
- `fantasim-app-godot/project/plugins/App.World/Services/Service.cs`
  carries the feature threshold into `PlanetPresentationDocument`.
- `fantasim-app-godot/project/plugins/App.Presentation/PlanetPresentationBinder.cs`
  builds typed-feature refinement weights and passes them to adaptive caps.
- `fantasim-app-godot/project/tests/App.World.Tests/*`
  covers projection metadata, render options, feature-weight gather, and recursive color provenance.

## Task 1: Recursive Cartography Subdivision

**Files:**
- Modify: `fantasim-cartography/project/plugins/Cartography.Globe.Core/AdaptiveGlobeSurfaceBuilder.cs`
- Modify: `fantasim-cartography/project/contracts/Cartography.Globe/AdaptiveGlobeSurface.cs`
- Test: `fantasim-cartography/project/tests/Cartography.Globe.Core.Tests/AdaptiveGlobeSurfaceBuilderTests.cs`

**Interfaces:**
- Consumes: `AdaptiveSubdivisionOptions.MaxDepth`, `EdgeHeightDeltaThreshold`, `Radius`.
- Produces: `BuildAdaptive(...)` that accepts `MaxDepth >= 0`, recursively subdivides up to that
  depth, preserves shared-edge watertightness per level, and emits `VertexProvenance.Midpoint`
  endpoints as generated-surface vertex indices that can be recursively resolved.

- [ ] **Step 1: Write failing recursion test**

Add a test named `BuildAdaptive_MaxDepthTwo_RecursivelySplitsChildEdges`:

```csharp
[Fact]
public void BuildAdaptive_MaxDepthTwo_RecursivelySplitsChildEdges()
{
    var vertices = new[]
    {
        new CartesianPoint3(1, 0, 0),
        new CartesianPoint3(0, 1, 0),
        new CartesianPoint3(0, 0, 1),
    };
    var triangles = new[] { 0, 1, 2 };
    var heights = new[] { 0.0, 4.0, 0.0 };

    var depthOne = Builder.BuildAdaptive(
        vertices,
        triangles,
        heights,
        new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 1.0));
    var depthTwo = Builder.BuildAdaptive(
        vertices,
        triangles,
        heights,
        new AdaptiveSubdivisionOptions(MaxDepth: 2, EdgeHeightDeltaThreshold: 1.0));

    Assert.True(depthTwo.Surface.TriangleCount > depthOne.Surface.TriangleCount);
    Assert.Equal(depthTwo.Surface.TriangleCount, depthTwo.SourceTriangleIds.Length);
    Assert.All(depthTwo.SourceTriangleIds, id => Assert.Equal(0, id));
    Assert.Contains(depthTwo.VertexProvenance, p =>
        p is VertexProvenance.Midpoint mp
        && (mp.EndpointA >= vertices.Length || mp.EndpointB >= vertices.Length));
}
```

- [ ] **Step 2: Verify RED**

Run:

```bash
dotnet test project/tests/Cartography.Globe.Core.Tests/Cartography.Globe.Core.Tests.csproj --filter FullyQualifiedName~BuildAdaptive_MaxDepthTwo_RecursivelySplitsChildEdges -v minimal --nologo
```

Expected: fail because `MaxDepth > 1` is rejected.

- [ ] **Step 3: Implement recursive subdivision**

Replace the single-pass subdivision in `AdaptiveGlobeSurfaceBuilder.BuildAdaptive` with a bounded
level loop:

- validate `MaxDepth >= 0`;
- remove the `MaxDepth > 1` rejection;
- start from input vertices/heights/triangles/source ids/provenance;
- for each depth level, split any edge whose current endpoint height delta meets
  `EdgeHeightDeltaThreshold`;
- create shared midpoint vertices once per current edge per level;
- midpoint provenance references the current endpoint vertex indices;
- carry each generated triangle's original source triangle id through every level;
- stop early when a level emits no new midpoint.

- [ ] **Step 4: Verify GREEN**

Run the focused test command from Step 2. Expected: pass.

- [ ] **Step 5: Run cartography adaptive suite**

Run:

```bash
dotnet test project/tests/Cartography.Globe.Core.Tests/Cartography.Globe.Core.Tests.csproj --filter FullyQualifiedName~AdaptiveGlobeSurfaceBuilder -v minimal --nologo
```

Expected: all adaptive builder tests pass.

- [ ] **Step 6: Commit**

```bash
git add project/contracts/Cartography.Globe/AdaptiveGlobeSurface.cs \
        project/plugins/Cartography.Globe.Core/AdaptiveGlobeSurfaceBuilder.cs \
        project/tests/Cartography.Globe.Core.Tests/AdaptiveGlobeSurfaceBuilderTests.cs
git commit -m "feat(cartography): support recursive adaptive subdivision"
```

## Task 2: Recursive Provenance in App Mesh Colors

**Files:**
- Modify: `fantasim-app-godot/project/contracts/App.World.Rendering/Globe/PlateCapMeshBuilder.cs`
- Test: `fantasim-app-godot/project/tests/App.World.Tests/PlateCapMeshBuilderTests.cs`

**Interfaces:**
- Consumes: recursive `VertexProvenance.Midpoint` endpoints that may point to generated vertices.
- Produces: terrain color resolution that recursively averages endpoint colors until it reaches
  `Original` vertices, with cycle/out-of-range protection falling back to `MissingTerrainColor`.

- [ ] **Step 1: Write failing recursive-color test**

Add a test named `BuildTerrain_RecursiveAdaptiveMidpointsResolveInterpolatedColors` that builds a
depth-2 adaptive cap and asserts every generated midpoint color is resolved without the missing
color fallback.

- [ ] **Step 2: Verify RED**

Run:

```bash
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --filter FullyQualifiedName~BuildTerrain_RecursiveAdaptiveMidpointsResolveInterpolatedColors -v minimal --nologo
```

Expected: fail because midpoint endpoints beyond the base color array currently fall back.

- [ ] **Step 3: Implement recursive color resolver**

Change `ResolveTerrainColor` so `VertexProvenance.Midpoint` calls a recursive helper with a visited
set and memo cache. `Original` vertices copy base colors; `Midpoint` vertices average recursively
resolved endpoint colors; invalid or cyclic provenance returns `MissingTerrainColor`.

- [ ] **Step 4: Verify GREEN and focused mesh tests**

Run:

```bash
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --filter FullyQualifiedName~PlateCapMeshBuilder -v minimal --nologo
```

Expected: all plate cap mesh builder tests pass.

- [ ] **Step 5: Commit**

```bash
git add project/contracts/App.World.Rendering/Globe/PlateCapMeshBuilder.cs \
        project/tests/App.World.Tests/PlateCapMeshBuilderTests.cs
git commit -m "fix(world): resolve recursive adaptive vertex colors"
```

## Task 3: Typed-Feature Refinement

**Files:**
- Modify: `fantasim-cartography/project/contracts/Cartography.Globe/AdaptiveSubdivisionOptions.cs`
- Modify: `fantasim-cartography/project/plugins/Cartography.Globe.Core/AdaptiveGlobeSurfaceBuilder.cs`
- Modify: `fantasim-app-godot/project/contracts/App.World/PresentationLayers.cs`
- Modify: `fantasim-app-godot/project/contracts/App.World.Rendering/Globe/GlobePlateSurfaces.cs`
- Modify: `fantasim-app-godot/project/contracts/App.World.Rendering/Globe/LayerProjectionProfileResolver.cs`
- Modify: `fantasim-app-godot/project/plugins/App.World/GenerationGraph/WorldGenerationRenderOptions.cs`
- Modify: `fantasim-app-godot/project/plugins/App.World/Services/Service.cs`
- Modify: `fantasim-app-godot/project/plugins/App.Presentation/PlanetPresentationBinder.cs`
- Test: `fantasim-cartography/project/tests/Cartography.Globe.Core.Tests/AdaptiveGlobeSurfaceBuilderTests.cs`
- Test: `fantasim-app-godot/project/tests/App.World.Tests/GlobePlateSurfacesTests.cs`
- Test: `fantasim-app-godot/project/tests/App.World.Tests/WorldGenerationRenderOptionsTests.cs`
- Test: `fantasim-app-godot/project/tests/App.World.Tests/LayerProjectionProfileResolverTests.cs`

**Interfaces:**
- Produces: `AdaptiveSubdivisionOptions.VertexFeatureWeights` and
  `FeatureWeightDeltaThreshold`, both render-only.
- Produces: app per-cell typed feature weights where `CellCrustFeature.Kind == 0` is `0`, and typed
  mountain/volcanic/trench/ridge/fault cells approach `1` based on magnitude.
- Produces: per-plate vertex feature weights gathered with the same global topology used for
  elevation.

- [ ] **Step 1: Write failing cartography feature-threshold test**

Add `BuildAdaptive_SplitsWhenFeatureWeightDeltaCrossesThresholdEvenIfHeightIsFlat`.

- [ ] **Step 2: Verify RED**

Run the focused cartography test. Expected: fail because options have no feature weights.

- [ ] **Step 3: Add cartography options and split predicate**

Add nullable `IReadOnlyList<double>? VertexFeatureWeights = null` and
`double FeatureWeightDeltaThreshold = double.PositiveInfinity` to `AdaptiveSubdivisionOptions`.
Validate lengths when provided. Split an edge when either height delta or feature-weight delta
crosses its threshold. Midpoint feature weight is the mean of endpoint weights.

- [ ] **Step 4: Write failing app feature gather/routing tests**

Add tests proving:

- `WorldGenerationRenderOptions` parses `adaptiveSubdivisionFeatureWeightDelta`;
- `PlanetPresentationDocument` carries it in the crust projection profile;
- `GlobePlateSurfaces.BuildAdaptiveSurfaces` can accept per-cell feature weights and split flat
  terrain near sharp feature-weight transitions.

- [ ] **Step 5: Implement app routing**

Derive per-cell feature weights in `PlanetPresentationBinder` from `document.CellFeatures`:

```text
weight = feature.Kind == 0 ? 0.0 : Clamp(0.35 + Log10(1.0 + Max(0, feature.Magnitude)) / 2.0, 0.0, 1.0)
```

Pass weights into `BuildAdaptiveSurfaces`. Keep null weights for documents without cell features.

- [ ] **Step 6: Verify app tests**

Run:

```bash
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --filter "FullyQualifiedName~WorldGenerationRenderOptions|FullyQualifiedName~LayerProjection|FullyQualifiedName~BuildAdaptiveSurfaces" -v minimal --nologo
```

Expected: pass.

- [ ] **Step 7: Commit**

Commit cartography and app changes in separate commits if both repos changed:

```bash
git commit -m "feat(cartography): add feature-aware adaptive splits"
git commit -m "feat(world): refine crust near boundary features"
```

## Task 4: Full Build and Exported-App Screenshot

**Files:**
- No source file changes unless verification finds a bug.

**Interfaces:**
- Consumes: completed Tasks 1-3.
- Produces: build/test proof and screenshot proof from the exported Godot app.

- [ ] **Step 1: Run full focused tests**

Run cartography adaptive tests and app world tests.

- [ ] **Step 2: Run Unify build**

Run from `fantasim-app-godot`:

```bash
dotnet tool restore
dotnet unify-build Compile
```

- [ ] **Step 3: Export/install bundle**

Run:

```bash
task build:godot:desktop
task bundle:world
task bundle:install
```

- [ ] **Step 4: Verify in exported app**

Launch:

```bash
env remote__enabled=true FANTASIM_REMOTE_ENABLED=true task run:exported
```

Then drive:

```bash
python3 tools/fantasim-cmd.py cmd timeline.seek '{"tick":100000000}'
python3 tools/fantasim-cmd.py cmd timeline.select_layer '{"sphereId":"geosphere","layerId":"geosphere.crust"}'
python3 tools/fantasim-cmd.py cmd render.screenshot '{"path":"/tmp/fantasim-recursive-feature-adaptive-20260704.png"}'
```

Expected log includes `view=HypsometricTerrain` and `subdivision=adaptive`, with a triangle count
greater than the previous depth-1 crust screenshot when the graph option depth is set above 1.

- [ ] **Step 5: Final status**

Record the screenshot path and exact verifier commands in the final response.
