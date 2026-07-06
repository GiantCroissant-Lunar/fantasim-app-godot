# Dry Crust Adaptive Subdivision Implementation Plan

> **AUDIT (2026-07-06, code-verified):** COMPLETED — tasks 1–4 in code (defaults since evolved: recursive depth 2); Task 5 chunked-LOD plan never created; look refs superseded by the north star. _(See the authority index in `vault/README.md`.)_


> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the workspace `orchestrate-before-implementing` rule and
> `external-agent-delegation` skill); otherwise execute inline with a review checkpoint per
> task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the dry/no-hydrosphere crust read like a rocky displaced planet, then add
conforming adaptive subdivision so high-relief regions can use finer triangles without changing
simulation truth.

**Architecture:** `fantasim-world` remains the truth/model producer and does not learn about render
LOD. `fantasim-cartography` gains a Godot-free adaptive globe-surface builder that refines
triangles from existing vertices/heights while preserving watertight edge conformity. `fantasim-app-godot`
selects the adaptive builder for world-view dry crust and renders the resulting sub-faces through the
existing `ArrayMesh` seam.

**Tech Stack:** .NET 8, C#, Godot 4.7 .NET, `ArrayMesh`, UnifyCell/UnifyGeometry/UnifyMaths 1.0.0,
FantaSim cartography/world packages.

## Global Constraints

- Keep adaptive subdivision Godot-free until the final app presentation seam.
- Do not store subdivision as truth-stream state; derive it from cell geometry, heights, and view options.
- Do not change `fantasim-world` for LOD in this slice.
- Preserve cross-plate watertightness: no T-junctions and no split decisions made independently per plate.
- Preserve current dry/no-hydrosphere default and legacy hydrosphere-present mode.
- Follow RED -> GREEN -> REFACTOR for behavior changes.
- Use exported-app windowed verification for final visual proof because presentation rendering is resident.

---

## Reference Findings

- Attached still image: dry gray rocky body, no water mask, strong radial displacement, faceted
  polygonal silhouette, varied triangle sizes and creases.
- [Coding Adventure: Planetary Fluid Sim](https://www.youtube.com/watch?v=8nIB7e_eds4) at
  13:02-13:20: high-relief dry rocky planet with ridges/spikes and height-driven dark/light material
  contrast. Local reference frames captured under `/tmp/fantasim-ref-videos/fluid-sim-1302-1320.webm`
  and `/tmp/fantasim-ref-videos/fluid-sim-1311.png`.
- [Procedural Planet - Chunked LOD](https://www.youtube.com/watch?v=yK8nJxmXAgo) at 0:54-0:57:
  visible chunked LOD patches with finer/coarser regions. Local reference frame:
  `/tmp/fantasim-ref-videos/chunked-lod-0055.png`.

## Current State

- Dry/no-hydrosphere mode already exists and defaults to absent hydrosphere:
  `project/plugins/App.World/GenerationGraph/WorldGenerationRenderOptions.cs`.
- World-view rocky relief already has seeded peaks and a square-root height lens:
  `project/plugins/App.Presentation/PlanetPresentationBinder.cs` (`WorldPeaks`,
  `WorldHeightExponent`, `WorldHeightScale`).
- Fixed per-plate surface generation already goes through a watertight cartography path:
  `project/contracts/App.World.Rendering/Globe/GlobePlateSurfaces.cs`.
- `fantasim-cartography` currently has `GlobeSurfaceBuilder` and `NoiseRelief`, but no adaptive
  subdivision product.
- Godot `ArrayMesh.AddSurfaceFromArrays(...)` accepts vertex/normal/color/UV/index arrays and also
  an LOD dictionary, but the built-in LOD is per surface and distance-driven; it is not enough for
  chunk-local adaptive refinement. Source:
  `https://docs.godotengine.org/en/stable/classes/class_arraymesh.html`.

## Non-Goals For This Slice

- Do not implement full camera-driven chunk streaming yet.
- Do not replace the crust simulator or plate materializer.
- Do not move dry rocky relief into `fantasim-world`.
- Do not use Godot built-in LOD as the primary adaptive mechanism.

---

### Task 1: Add Cartography Conforming Adaptive Subdivision

**Files:**
- Create: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-cartography/project/contracts/Cartography.Globe/AdaptiveSubdivisionOptions.cs`
- Create: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-cartography/project/contracts/Cartography.Globe/AdaptiveGlobeSurface.cs`
- Create: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-cartography/project/contracts/Cartography.Globe/IAdaptiveGlobeSurfaceBuilder.cs`
- Create: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-cartography/project/plugins/Cartography.Globe.Core/AdaptiveGlobeSurfaceBuilder.cs`
- Create: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-cartography/project/tests/Cartography.Globe.Core.Tests/AdaptiveGlobeSurfaceBuilderTests.cs`

**Interfaces:**
- Consumes: `CartesianPoint3[] vertices`, `int[] triangles`, `double[] heights`,
  `AdaptiveSubdivisionOptions`.
- Produces: `AdaptiveGlobeSurface(GlobeSurface Surface, int[] SourceTriangleIds)` where
  `SourceTriangleIds[t]` maps each generated sub-triangle back to the input triangle.

- [x] **Step 1: Write adaptive tests**

Add tests for:

```csharp
[Fact]
public void BuildAdaptive_keeps_low_slope_triangle_unsubdivided()
{
    var (vertices, triangles) = Fixtures.Octahedron();
    var heights = new double[vertices.Length];
    var builder = new AdaptiveGlobeSurfaceBuilder();

    var result = builder.BuildAdaptive(
        vertices,
        triangles,
        heights,
        new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 0.5));

    Assert.Equal(triangles.Length / 3, result.Surface.TriangleCount);
    Assert.Equal(Enumerable.Range(0, triangles.Length / 3), result.SourceTriangleIds);
}
```

```csharp
[Fact]
public void BuildAdaptive_splits_shared_edge_once_for_both_incident_triangles()
{
    var vertices = new[]
    {
        new CartesianPoint3(1, 0, 0),
        new CartesianPoint3(0, 1, 0),
        new CartesianPoint3(0, 0, 1),
        new CartesianPoint3(-1, 0, 0),
    };
    var triangles = new[] { 0, 1, 2, 1, 3, 2 };
    var heights = new[] { 0.0, 1.0, 0.0, 1.0 };
    var builder = new AdaptiveGlobeSurfaceBuilder();

    var result = builder.BuildAdaptive(
        vertices,
        triangles,
        heights,
        new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 0.25));

    var midpointPositions = result.Surface.Positions
        .GroupBy(p => (Math.Round(p.X, 12), Math.Round(p.Y, 12), Math.Round(p.Z, 12)))
        .Where(g => g.Count() > 1)
        .ToArray();

    Assert.Empty(midpointPositions);
    Assert.Contains(result.Surface.Triangles, index => index >= vertices.Length);
}
```

```csharp
[Fact]
public void BuildAdaptive_subfaces_remember_source_triangle()
{
    var (vertices, triangles) = Fixtures.Octahedron();
    var heights = vertices.Select(v => v.Z > 0 ? 1.0 : -1.0).ToArray();
    var builder = new AdaptiveGlobeSurfaceBuilder();

    var result = builder.BuildAdaptive(
        vertices,
        triangles,
        heights,
        new AdaptiveSubdivisionOptions(MaxDepth: 1, EdgeHeightDeltaThreshold: 0.25));

    Assert.Equal(result.Surface.TriangleCount, result.SourceTriangleIds.Length);
    Assert.All(result.SourceTriangleIds, id => Assert.InRange(id, 0, (triangles.Length / 3) - 1));
}
```

- [ ] **Step 2: Verify RED** — not captured; tests and implementation were added in the same session.

Run:

```bash
dotnet test project/tests/Cartography.Globe.Core.Tests/Cartography.Globe.Core.Tests.csproj --filter FullyQualifiedName~AdaptiveGlobeSurfaceBuilderTests --no-restore -v minimal --nologo
```

Expected: compile failure because the adaptive builder/contracts do not exist.

- [x] **Step 3: Implement minimal contracts**

Create:

```csharp
public sealed record AdaptiveSubdivisionOptions(
    int MaxDepth = 1,
    double EdgeHeightDeltaThreshold = 0.02,
    double Radius = GlobeSurfaceBuilder.DefaultRadius);
```

```csharp
public sealed record AdaptiveGlobeSurface(GlobeSurface Surface, int[] SourceTriangleIds);
```

```csharp
public interface IAdaptiveGlobeSurfaceBuilder : IGlobeSurfaceBuilder
{
    AdaptiveGlobeSurface BuildAdaptive(
        IReadOnlyList<CartesianPoint3> vertices,
        IReadOnlyList<int> triangles,
        IReadOnlyList<double> heights,
        AdaptiveSubdivisionOptions options);
}
```

- [x] **Step 4: Implement depth-1 conforming subdivision**

Use a global edge map keyed by sorted endpoint index `(min, max)`. An edge splits when
`Abs(heights[a] - heights[b]) >= EdgeHeightDeltaThreshold`. Each midpoint is inserted once and
shared by every generated triangle referencing that edge. Handle the four conforming cases:

- 0 split edges -> 1 triangle.
- 1 split edge -> 2 triangles.
- 2 split edges -> 3 triangles.
- 3 split edges -> 4 triangles.

Each emitted sub-triangle writes the original input triangle index into `SourceTriangleIds`.
After generating vertices/triangles/heights, call `GlobeSurfaceBuilder.Build(...)`.

- [x] **Step 5: Verify GREEN**

Run the filtered cartography test command again.

- [x] **Step 6: Run broad cartography verification**

Run:

```bash
dotnet test project/tests/Cartography.Globe.Core.Tests/Cartography.Globe.Core.Tests.csproj --no-restore -v minimal --nologo
```

---

### Task 2: Add App.World Rendering Projection For Adaptive Plate Caps

**Files:**
- Modify: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/contracts/App.World.Rendering/Globe/GlobePlateSurfaces.cs`
- Modify: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/tests/App.World.Tests/GlobePlateSurfacesTests.cs`
- Modify if needed: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/contracts/App.World.Rendering/App.World.Rendering.csproj`

**Interfaces:**
- Consumes: `IAdaptiveGlobeSurfaceBuilder`, `AdaptiveSubdivisionOptions`, existing global
  topology and per-cell elevation envelope.
- Produces: `BuildAdaptiveSurfaces(...)` returning plate caps whose `CellIds` are parallel to
  generated sub-triangles.

- [x] **Step 1: Write failing tests**

Add tests that:

- `BuildAdaptiveSurfaces` produces more triangles than `BuildSurfaces` when high relief crosses a
  shared edge.
- Generated `PlateCap.CellIds` length equals generated `Surface.TriangleCount`.
- Every generated sub-face inherits the original parent cell id.
- Cross-plate boundary midpoint positions match exactly between the adjacent plate caps.

- [x] **Step 2: Verify RED**

Run:

```bash
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --filter FullyQualifiedName~GlobePlateSurfacesTests --no-restore -v minimal --nologo
```

Expected: compile failure because `BuildAdaptiveSurfaces` does not exist.

- [x] **Step 3: Add adaptive builder dependency**

Add a constructor overload or optional dependency:

```csharp
public GlobePlateSurfaces(
    WorldGlobeSnapshot snapshot,
    IGlobeSurfaceBuilder? builder = null,
    NoiseParams? noise = null,
    IAdaptiveGlobeSurfaceBuilder? adaptiveBuilder = null)
```

Use `new AdaptiveGlobeSurfaceBuilder()` when the dependency is null. Keep the existing constructor
behavior unchanged for `BuildSurfaces`.

- [x] **Step 4: Implement `BuildAdaptiveSurfaces`**

Compute the same global vertex metres as `BuildSurfaces`, add cached peak noise to local vertices,
apply the existing height lens, then call the adaptive builder per plate. Map
`AdaptiveGlobeSurface.SourceTriangleIds` back to `plate.CellIds[sourceTriangleId]` so each sub-face
keeps a cell id.

- [x] **Step 5: Verify GREEN**

Run the filtered `GlobePlateSurfacesTests`.

- [x] **Step 6: Run broad app world verification**

Run:

```bash
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --no-restore -v minimal --nologo
dotnet test project/tests/App.World.Composition.Tests/App.World.Composition.Tests.csproj --no-restore -v minimal --nologo
```

---

### Task 3: Render Adaptive Dry Crust In World View

**Files:**
- Modify: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/plugins/App.Presentation/PlanetPresentationBinder.cs`
- Modify: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/plugins/App.World/GenerationGraph/WorldGenerationRenderOptions.cs`
- Modify: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/plugins/App.World/GenerationGraph/WorldGenerationNodeCatalog.cs`
- Modify tests under: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/project/tests/App.World.Tests/`

**Interfaces:**
- Consumes: `WorldGenerationRenderOptions.AdaptiveSubdivision`.
- Produces: world-view `BuildPlateSurface(...)` selects adaptive caps; diagnostic views keep the
  current fixed topology unless explicitly enabled later.

- [x] **Step 1: Add failing option parsing tests**

Extend `WorldGenerationRenderOptionsTests` to parse:

- `surfaceSubdivision = "adaptive"`
- `adaptiveSubdivisionMaxDepth = 1`
- `adaptiveSubdivisionEdgeHeightDelta = 0.02`

- [x] **Step 2: Verify RED**

Run:

```bash
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --filter FullyQualifiedName~WorldGenerationRenderOptionsTests --no-restore -v minimal --nologo
```

- [x] **Step 3: Implement render options**

Add an enum such as:

```csharp
public enum SurfaceSubdivisionMode
{
    Fixed,
    Adaptive
}
```

Add options to `WorldGenerationRenderOptions`, defaulting world view to `Adaptive` only after the
visual gate passes. Register the graph node params in `WorldGenerationNodeCatalog`.

- [x] **Step 4: Wire presentation selection**

In `PlanetPresentationBinder.BuildPlateSurface(...)`, call:

```csharp
_plateSurfaces.BuildAdaptiveSurfaces(
    elevations,
    exaggeration: WorldHeightScale,
    heightExponent: WorldHeightExponent,
    options: new AdaptiveSubdivisionOptions(...))
```

for `GlobeViewMode.World` when adaptive mode is enabled. Keep `BuildPlateMesh(...)` unchanged as
much as possible; it should consume the generated `PlateCap` the same way it consumes fixed caps.

- [x] **Step 5: Verify app/presentation build**

Run:

```bash
dotnet build project/plugins/App.Presentation/App.Presentation.csproj --no-restore -v minimal --nologo
dotnet unify-build Compile
```

---

### Task 4: Windowed Visual Verification

**Files:**
- No production file changes expected unless visual verification exposes a defect.
- Update this plan's status section after verification.

**Interfaces:**
- Consumes: exported app, remote screenshot command path.
- Produces: screenshot evidence comparing fixed vs adaptive dry crust.

- [x] **Step 1: Build exported app**

Run:

```bash
task build:godot:desktop
```

Verified on 2026-07-04 with `task build:godot:desktop`, then `task bundle:world bundle:install`
to install the rebuilt `world.pck` into
`build/_artifacts/0.1.2/godot/osx/complete-app.app/Contents/MacOS/bundles/world.pck`.

- [x] **Step 2: Launch exported app with remote enabled**

Run the exported `complete-app` with:

```bash
remote__enabled=true FANTASIM_REMOTE_ENABLED=1 build/_artifacts/0.1.2/godot/osx/complete-app.app/Contents/MacOS/complete-app
```

If bundle-install artifacts are under `build/_artifacts/0.1.74` but the extracted app is under
`0.1.2`, copy the built PCKs into the extracted app before launching, as in the boundary-section
verification handover.

Verified on 2026-07-04 through `task run:exported` under tmux with remote enabled. Runtime log:
`/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/.agent/logs/windowed-verify/adaptive-subdivision-20260704.log`.

- [x] **Step 3: Capture screenshots at the plate-world tick**

Run:

```bash
python3 tools/fantasim-cmd.py cmd timeline.seek '{"tick":105000000}'
python3 tools/fantasim-cmd.py cmd render.screenshot '{"path":"/tmp/fantasim-dry-crust-adaptive-subdivision-20260703.png"}'
```

Verified screenshot path on 2026-07-04:
`/tmp/fantasim-dry-crust-adaptive-subdivision-20260704.png`.

- [x] **Step 4: Accept or tune**

Accept when:

- no hydrosphere/water mask is visible in world view,
- rocky/faceted relief is visible across the body,
- high-relief boundary/orogenic regions show finer geometry than low-relief interiors,
- no cracks appear along plate seams or adaptive split edges,
- boundary-section panels still render and are not occluded by the new surface.

Accepted for this slice on 2026-07-04. Runtime evidence shows the initial inactive mount remains
fixed (`triangles=5120`), then the plate-world tick switches to adaptive with `triangles=12550` and
`meshVertices=37650`. The screenshot shows a dry, faceted crust body with no visible water mask,
visible rocky relief, and no obvious adaptive seam cracks.

---

### Task 5: Plan Chunked LOD As The Next Slice

**Files:**
- Modify: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/vault/architecture/rendering-and-lod.md`
- Create: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/vault/plans/YYYY-MM-DD-chunked-globe-lod.md`

**Interfaces:**
- Consumes: adaptive subdivision proof from Tasks 1-4.
- Produces: a separate plan for explicit chunk `MeshInstance3D` surfaces.

- [ ] **Step 1: Document the decision**

Record that Godot `ArrayMesh` built-in LOD may be useful inside a chunk, but the chunked-LOD
reference requires explicit chunks because built-in LOD is per surface and cannot make only one
region finer.

- [ ] **Step 2: Pick seam strategy**

Choose one before implementing chunked LOD:

- crack skirts: simpler, fits rocky/faceted style,
- boundary morphing: smoother, needs shader/uniform support.

- [ ] **Step 3: Create the chunked-LOD implementation plan**

The plan should be separate from adaptive subdivision because it changes scene structure,
visibility policy, and camera-distance behavior.

## Status

Tasks 1-4 are implemented and verified as of 2026-07-04. The app-side world view now defaults to the
adaptive dry-crust subdivision path after seeking into the plate-world tick, and exported runtime
verification captured both log evidence and screenshot evidence. Task 5 remains open as the next
separate slice for explicit chunked LOD.
