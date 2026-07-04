# Boundary Section Visibility Hardening Implementation Plan

> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the `orchestrate-before-implementing` rule + the `external-agent-delegation`
> skill); otherwise execute inline with a review checkpoint per task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Make convergent, divergent, and transform boundary-section slices visible and testable in the crust presentation path.

**Architecture:** Keep section data generation in `App.World` and Godot rendering in `App.Presentation`. Extract the binder's section-visibility rule into a small internal helper so the crust-layer behavior is covered without launching Godot. Add renderer structure coverage for the existing panel/slab geometry, then verify the exported app.

**Tech Stack:** Godot.NET.Sdk 4.7.0, .NET 8, xUnit, `FantaSim.App.World` contracts, Godot `ArrayMesh`/`MeshInstance3D` presentation seam.

## Global Constraints

- Use TDD: every production behavior change starts with a failing focused test.
- Keep section generation Godot-free; renderer code may use Godot types.
- Do not change `fantasim-world`; the needed boundary kind and crust feature data already reaches app-godot.
- Commit each meaningful completed slice with Conventional Commits.
- Verify with exported app bundle reload and screenshot, not only unit tests.

---

### Task 1: Crust Layer Section Visibility

**Files:**
- Create: `project/plugins/App.Presentation/BoundarySectionVisibility.cs`
- Create: `project/tests/App.Presentation.Tests/BoundarySectionVisibilityTests.cs`
- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.cs`

**Interfaces:**
- Consumes: `GlobeViewMode`.
- Produces: `BoundarySectionVisibility.ShouldShow(bool showsPlateFeatures, GlobeViewMode viewMode)`.

- [ ] **Step 1: Write the failing test**

```csharp
[Theory]
[InlineData(GlobeViewMode.World, true)]
[InlineData(GlobeViewMode.HypsometricTerrain, true)]
[InlineData(GlobeViewMode.PlateIdentity, false)]
[InlineData(GlobeViewMode.Inactive, false)]
public void ShouldShow_keeps_sections_visible_for_world_and_crust_views(GlobeViewMode viewMode, bool expected)
{
    Assert.Equal(expected, BoundarySectionVisibility.ShouldShow(showsPlateFeatures: true, viewMode));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --filter FullyQualifiedName~BoundarySectionVisibilityTests -v minimal --nologo`

Expected: FAIL because `BoundarySectionVisibility` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
internal static class BoundarySectionVisibility
{
    public static bool ShouldShow(bool showsPlateFeatures, GlobeViewMode viewMode)
        => showsPlateFeatures && (viewMode == GlobeViewMode.World || viewMode == GlobeViewMode.HypsometricTerrain);
}
```

Replace the binder inline predicate with this helper.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --filter FullyQualifiedName~BoundarySectionVisibilityTests -v minimal --nologo`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add vault/plans/2026-07-04-boundary-section-visibility-hardening.md project/plugins/App.Presentation/BoundarySectionVisibility.cs project/plugins/App.Presentation/PlanetPresentationBinder.cs project/tests/App.Presentation.Tests/BoundarySectionVisibilityTests.cs
git commit -m "fix(presentation): show boundary sections in crust view"
```

### Task 2: Boundary Section Renderer Structure

**Files:**
- Create: `project/tests/App.Presentation.Tests/BoundarySectionRendererTests.cs`
- Modify: `project/plugins/App.Presentation/BoundarySectionRenderer.cs` only if the red test exposes an actual renderer bug.

**Interfaces:**
- Consumes: `BoundarySectionDocument`, `BoundarySectionRenderer`.
- Produces: renderer instances with at most three section panels, each with visible strata/accent mesh children.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Renderer_mounts_at_most_three_section_panels_with_meshes()
{
    var renderer = new BoundarySectionRenderer(new[]
    {
        Section(0, 1, PlateBoundaryKind.Convergent, subductingPlateId: 1),
        Section(2, 3, PlateBoundaryKind.Divergent),
        Section(4, 5, PlateBoundaryKind.Transform),
        Section(6, 7, PlateBoundaryKind.Convergent, subductingPlateId: 7),
    });

    Assert.Equal(3, renderer.GetChildCount());
    foreach (var panel in renderer.GetChildren().OfType<Node3D>())
    {
        Assert.NotNull(panel.GetNodeOrNull<MeshInstance3D>("Strata"));
        Assert.NotNull(panel.GetNodeOrNull<MeshInstance3D>("Accent"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --filter FullyQualifiedName~BoundarySectionRendererTests -v minimal --nologo`

Expected: FAIL if renderer structure is not test-accessible or missing expected nodes.

- [ ] **Step 3: Implement the minimal renderer fix if needed**

If the current renderer already passes, do not change production code. If it fails due to missing panel names or children, keep the fix constrained to stable child naming or mesh creation.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --filter FullyQualifiedName~BoundarySectionRendererTests -v minimal --nologo`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add project/plugins/App.Presentation/BoundarySectionRenderer.cs project/tests/App.Presentation.Tests/BoundarySectionRendererTests.cs
git commit -m "test(presentation): cover boundary section renderer panels"
```

### Task 3: Exported App Verification

**Files:**
- No source files expected.

**Interfaces:**
- Consumes: exported app, installed `world.pck`, remote commands.
- Produces: screenshot showing crust view with boundary-section panels visible.

- [ ] **Step 1: Run focused and full tests**

Run:
```bash
dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj -v minimal --nologo
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj --no-restore -v minimal --nologo
```

- [ ] **Step 2: Compile through UnifyBuild**

Run:
```bash
dotnet tool restore
dotnet unify-build Compile
```

- [ ] **Step 3: Rebuild/install world bundle**

Run:
```bash
task bundle:world
task bundle:install
```

- [ ] **Step 4: Launch exported app and capture screenshot**

Run exported app with remote enabled, seek to `100000000`, select `geosphere.crust`, capture `/tmp/fantasim-boundary-sections-crust-20260704.png`, and inspect it.

- [ ] **Step 5: Confirm clean git state**

Run:
```bash
git status --short
```

Expected: clean.
