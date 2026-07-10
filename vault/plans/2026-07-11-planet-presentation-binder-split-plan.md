# PlanetPresentationBinder Split — implementation plan (2026-07-11)

> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the `orchestrate-before-implementing` rule + the `external-agent-delegation`
> skill); otherwise execute inline with a review checkpoint per task. Steps use checkbox
> (`- [ ]`) syntax for tracking. Implementer does NOT commit and does NOT run the windowed
> gate — the lead reviews, commits per task, and runs every gate (house pattern, see
> `../handover/2026-07-10-review-and-track-registry-slice1-handover.md` §4 last bullet).

**Goal:** Split `PlanetPresentationBinder.cs` (2,636 lines) into a core reconciliation file plus
7 seams — with ZERO behavior change — so D8b (progressive-resolution scrub) and D5 (compose
graph) land in focused files, not a god class.

**Architecture:** Two-tier split. (Tier A) Real class extractions where coupling is low and D8b
needs the seam: a static shader library, a static pure mesh/color factory, a testable
`ScrubRefreshCoordinator`, and `PlanetTimelineController` to its own file. (Tier B)
`partial class` file splits for the Godot-node view clusters that share mount state
(`_activeRoot`, `_plateSurfaceRoot`, reload lifecycle): plate-surface bind, cutaway/exploded,
mantle views, scene furniture. Partials move members VERBATIM — zero reference changes, zero
regression risk in the just-stabilized reload/ALC paths. Promotion of a partial to a
collaborator class happens later, only when an arc (D5/D8b) actually needs it.

**Why these are the D8b seams:** D8b's rung ladder lands in the scrub cluster
(`HandleScrubAwareHeavyRefresh` → `ScrubRefreshCoordinator`) and maps rung → tessellation via
the `AdaptiveSubdivisionOptions` built in `BindPlateSurface` (moved to the PlateSurface
partial). `RegisterPlayback`'s `Action<long>` onSeek widening is D8b work, NOT this refactor.

**Tech stack:** C# / .NET 8, Godot 4 (GodotSharp), xunit (`project/tests/App.Presentation.Tests`).
Assembly stays `FantaSim.App.Presentation` (world collectible bundle). SDK-style csproj —
new `.cs` files are auto-included; do NOT touch the csproj.

## Global Constraints

- NO behavior change anywhere. Moved bodies are verbatim; only access modifiers and
  qualification (`PlanetShaderLibrary.X`) may change.
- No new csproj, package, or repo. All files land in `project/plugins/App.Presentation/`
  and `project/tests/App.Presentation.Tests/`.
- Edits ONLY under `project/plugins/App.Presentation/` and `project/tests/App.Presentation.Tests/`.
  Do not touch Host.cs, App.World*, App.Timeline*, contracts, configs, or Taskfile.
- ALC house rules: NEVER serialize anonymous types with STJ (use JsonObject); no NEW static
  fields holding types from other assemblies (the existing static Shader caches move file but
  stay in this same bundle assembly — preserved as-is, same lifetime); no delegate may capture
  a service provider at capture time (resolve at execution time).
- Canonical ticks everywhere; no Ma/Ga identifiers or comments.
- File-scoped namespaces; match existing comment density and doc-comment style (cite vault
  paths the way neighboring files do). Keep every existing code comment with the code it
  annotates — comments move with their members.
- Test baseline: 1091 green before Task 1. Suite must be green after EVERY task.
- Line references below are against `PlanetPresentationBinder.cs` at commit `b760ac4`. Ranges
  shift as tasks land — re-locate members by NAME, verify against the listed range order.

---

### Task 1: `PlanetShaderLibrary` (static shader/material library)

**Files:**
- Create: `project/plugins/App.Presentation/PlanetShaderLibrary.cs`
- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.cs`
- Test: `project/tests/App.Presentation.Tests/PlanetShaderLibraryTests.cs`

**Interfaces:**
- Produces: `internal static class PlanetShaderLibrary` with
  - `public const string MantleIsosurfaceOpaqueShaderCode` (from lines 1123–1136)
  - `public const string MantleIsosurfaceTranslucentShaderCode` (1138–1156)
  - `public const string MagmaShaderCode` (2073–2136)
  - `public const string StagnantShaderCode` (2137–2210)
  - `public const string HypsoPlateShaderCode` (2211–2303)
  - `public const string AtmosphereRimShaderCode` (2304–2319)
  - `public static Shader MagmaShader { get; }`, `StagnantShader`, `HypsoPlateShader`,
    `AtmosphereRimShader` — same `??=` lazy pattern over private static backing fields
    (move 2320–2323 + 2340–2343)
  - `public static Material HypsoPlateMaterial { get; }` (move 2328 + 2345)
  - `public static readonly Material ExplodedCrustDarkMaterial` (move 2330–2338, comment included)
  - `public static ShaderMaterial BuildIsosurfaceMaterial(Color tint, float emission, float alpha, int priority)` (move 1102–1122)
  - `public static ShaderMaterial BuildMagmaMantleMaterial()`, `BuildStagnantMantleMaterial()`,
    `public static StandardMaterial3D BuildBaseMantleMaterial()` (move 2364–2375)

- [ ] **Step 1: Write the failing tests**

```csharp
// project/tests/App.Presentation.Tests/PlanetShaderLibraryTests.cs
namespace FantaSim.App.Presentation.Tests;

public sealed class PlanetShaderLibraryTests
{
    [Fact]
    public void AllShaderSourcesAreSpatialShaders()
    {
        var sources = new[]
        {
            PlanetShaderLibrary.MantleIsosurfaceOpaqueShaderCode,
            PlanetShaderLibrary.MantleIsosurfaceTranslucentShaderCode,
            PlanetShaderLibrary.MagmaShaderCode,
            PlanetShaderLibrary.StagnantShaderCode,
            PlanetShaderLibrary.HypsoPlateShaderCode,
            PlanetShaderLibrary.AtmosphereRimShaderCode,
        };
        Assert.All(sources, s => Assert.Contains("shader_type spatial;", s));
    }

    [Fact]
    public void HypsoPlateShaderKeepsCutawayWedgeUniformContract()
    {
        // UpdateCutawayPlateShader sets these by name; renaming one silently kills the cutaway.
        foreach (var uniform in new[]
        {
            "u_wedge_active", "u_wedge_axis", "u_wedge_reference",
            "u_wedge_reference_cross", "u_wedge_start_rad", "u_wedge_width_rad",
        })
            Assert.Contains(uniform, PlanetShaderLibrary.HypsoPlateShaderCode);
    }
}
```

(Do not instantiate `Shader`/`Material` in tests — Godot resources need the engine; strings only.)

- [ ] **Step 2: Run to verify failure** — `dotnet test project/tests/App.Presentation.Tests --filter FullyQualifiedName~PlanetShaderLibraryTests` → FAIL: `PlanetShaderLibrary` not defined.
- [ ] **Step 3: Create `PlanetShaderLibrary.cs`** — move the members listed above verbatim
  (usings: `Godot;` only). Keep the look-dev comments with each shader string.
- [ ] **Step 4: Rewire the binder** — delete the moved members; qualify every use
  (`PlanetShaderLibrary.HypsoPlateShader`, `PlanetShaderLibrary.ExplodedCrustDarkMaterial`,
  `PlanetShaderLibrary.BuildIsosurfaceMaterial(...)`, `PlanetShaderLibrary.BuildMagmaMantleMaterial()`, …).
  These stay in the binder unchanged (instance-scoped by design):
  `_magmaMantleMaterial`/`_stagnantMantleMaterial`/`_baseMantleMaterial` caches (2325–2327),
  `ResolveMantleMaterial` (2356–2362), `_hypsoPlateMaterialOverride` + `HypsoPlateMaterialOverride`
  (2347–2354, the W3a per-binder wedge-uniform comment explains why), and the four isosurface
  instance material caches + lazies (1077–1101, they call `PlanetShaderLibrary.BuildIsosurfaceMaterial`).
- [ ] **Step 5: Verify** — `dotnet build project/FantaSim.sln` clean, then full
  `dotnet test project/FantaSim.sln` → 1093/1093 green (1091 + 2 new).

### Task 2: `PlateSurfaceMeshFactory` (static pure builders)

**Files:**
- Create: `project/plugins/App.Presentation/PlateSurfaceMeshFactory.cs`
- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.cs`
- Test: `project/tests/App.Presentation.Tests/PlateSurfaceMeshFactoryTests.cs`

**Interfaces:**
- Produces: `internal static class PlateSurfaceMeshFactory` exposing exactly these moved
  statics with their CURRENT signatures (make each `internal` in the new class; bodies verbatim):
  - `BuildAdaptiveFeatureWeights` (1798–1821)
  - `BuildTectonicDetailSampler` (1822–1846)
  - `BuildCellAppearance` (1847–1899)
  - `BuildCellCenters` (1900–1916)
  - `ToColor(RampColor)` (1918)
  - `BuildPerPlateVertexColors` (1923–1932)
  - `ToV3(CartesianPoint3)` (1934)
  - `BuildContinentsCellColors` (1936–1948)
  - `BuildFractionContourFrontier` (1950–1979)
  - `BuildCellNeighborsFromSharedVertices` (1981–2038)
- Consumes: nothing binder-instance — every one of these is already `static`. If one of them
  references a binder constant (e.g. `WorldHeightExponent`), move that constant too and
  re-point the binder at the factory copy. Do NOT duplicate constants.

- [ ] **Step 1: Write the failing tests** (characterization — pin CURRENT behavior)

```csharp
// project/tests/App.Presentation.Tests/PlateSurfaceMeshFactoryTests.cs
using FantaSim.Cartography.Shared;

namespace FantaSim.App.Presentation.Tests;

public sealed class PlateSurfaceMeshFactoryTests
{
    [Fact]
    public void BuildCellCentersNormalizesCentroidsAndSkipsOutOfRangeIds()
    {
        // One valid unit-ish cell, one out-of-range id, one degenerate (zero centroid) cell.
        var valid = MakeCell(cellId: 0,
            c0: new CartesianPoint3(1, 0, 0), c1: new CartesianPoint3(0, 1, 0), c2: new CartesianPoint3(0, 0, 1));
        var outOfRange = MakeCell(cellId: 7,
            c0: new CartesianPoint3(1, 0, 0), c1: new CartesianPoint3(0, 1, 0), c2: new CartesianPoint3(0, 0, 1));
        var degenerate = MakeCell(cellId: 1,
            c0: new CartesianPoint3(1, 0, 0), c1: new CartesianPoint3(-1, 0, 0), c2: new CartesianPoint3(0, 0, 0));

        var centers = PlateSurfaceMeshFactory.BuildCellCenters(2, new[] { valid, outOfRange, degenerate });

        Assert.Equal(2, centers.Length);
        Assert.NotNull(centers[0]);
        var c = centers[0]!.Value;
        var len = Math.Sqrt((c.X * c.X) + (c.Y * c.Y) + (c.Z * c.Z));
        Assert.Equal(1.0, len, precision: 9);   // unit-normalized
        Assert.Null(centers[1]);                 // degenerate centroid skipped
    }

    [Fact]
    public void BuildContinentsCellColorsUsesHalfFractionThresholdAndDefaultsToOcean()
    {
        var fractions = new Dictionary<int, double> { [0] = 0.75, [1] = 0.49 }; // cell 2 absent
        var colors = PlateSurfaceMeshFactory.BuildContinentsCellColors(3, fractions);

        Assert.Equal(3, colors.Length);
        Assert.Equal(ContinentsPalette.ToneFor(isLand: true, isFrontier: false), colors[0]);
        Assert.Equal(ContinentsPalette.ToneFor(isLand: false, isFrontier: false), colors[1]);
        Assert.Equal(ContinentsPalette.ToneFor(isLand: false, isFrontier: false), colors[2]);
    }
}
```

`MakeCell` is a small local helper constructing whatever `GlobeCell` requires (fill remaining
required members with defaults). If `GlobeCell` construction genuinely can't be done without
engine state, replace that ONE test with the Continents-colors test only and record why in
AGENT-SUMMARY.md — do not fake it. Adjust `ContinentsPalette.ToneFor` argument names to the
real signature (check the source; drop named args if they differ).

- [ ] **Step 2: Run to verify failure** — `dotnet test project/tests/App.Presentation.Tests --filter FullyQualifiedName~PlateSurfaceMeshFactoryTests` → FAIL: `PlateSurfaceMeshFactory` not defined.
- [ ] **Step 3: Create the factory** — move the ten members verbatim; usings copied from the
  binder as needed (`FantaSim.Cartography.Globe`, `FantaSim.Cartography.Shared`, `Godot`, …).
- [ ] **Step 4: Rewire the binder** — qualify all call sites (`PlateSurfaceMeshFactory.BuildCellAppearance(...)` etc.).
  NOTE: the binder has a second `ToV3(GlobeVec3)` at 2377 — that one STAYS in the binder;
  only `ToV3(CartesianPoint3)` moves. Check every `ToV3` call site resolves to the right overload.
- [ ] **Step 5: Verify** — clean build + full suite green (1095 = 1093 + 2, or 1094 per Step-1 note).

### Task 3: `ScrubRefreshCoordinator` (the D8b seam)

**Files:**
- Create: `project/plugins/App.Presentation/ScrubRefreshCoordinator.cs`
- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.cs`
- Test: `project/tests/App.Presentation.Tests/ScrubRefreshCoordinatorTests.cs`

**Interfaces:**
- Produces:

```csharp
/// <summary>
/// Owns scrub-origin heavy-refresh policy: previews debounce through ScrubApplyScheduler,
/// commits flush, standard ticks cancel. Extracted from PlanetPresentationBinder 2026-07-11
/// (vault/plans/2026-07-11-planet-presentation-binder-split-plan.md); D8b's progressive
/// resolution rung ladder lands here.
/// </summary>
internal sealed class ScrubRefreshCoordinator : IDisposable
{
    public ScrubRefreshCoordinator(
        ScrubApplyScheduler scheduler,
        Action requestHeavyRefresh,
        Action<Action> deferToMainThread,
        Func<long> nowMs,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null);

    public void HandleTick(long tick, TimelineTickOrigin origin, bool heavyRefreshRequested);
    public void Cancel();
    public void Dispose();
}
```

- Consumes: existing `ScrubApplyScheduler` (unchanged), `TimelineTickOrigin` (unchanged).

- [ ] **Step 1: Write the failing tests**

```csharp
// project/tests/App.Presentation.Tests/ScrubRefreshCoordinatorTests.cs
namespace FantaSim.App.Presentation.Tests;

public sealed class ScrubRefreshCoordinatorTests
{
    private static ScrubRefreshCoordinator Make(
        out List<string> log, long restDelayMs = 300L, Func<long>? nowMs = null)
    {
        var events = new List<string>();
        log = events;
        long clock = 0;
        return new ScrubRefreshCoordinator(
            new ScrubApplyScheduler(restDelayMs),
            requestHeavyRefresh: () => events.Add("heavy"),
            deferToMainThread: a => a(),                       // synchronous in tests
            nowMs: nowMs ?? (() => clock),
            delayAsync: (_, ct) => Task.CompletedTask);        // rest delay elapses instantly
    }

    [Fact]
    public async Task PreviewBurstThenRestRequestsExactlyOneHeavyRefresh()
    {
        var sut = Make(out var log);
        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        sut.HandleTick(200, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        await Task.Yield(); // let the fire-and-forget flush continuation run
        Assert.Single(log, "heavy");
    }

    [Fact]
    public void CommitFlushesPendingRestAndHonorsHeavyRequest()
    {
        var sut = Make(out var log);
        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        sut.HandleTick(150, TimelineTickOrigin.ScrubCommit, heavyRefreshRequested: true);
        // one from the flushed rest refresh + one from the commit's own heavy request —
        // dedup is the binder's _regimeRefreshPending job, not the coordinator's.
        Assert.Equal(2, log.Count(e => e == "heavy"));
    }

    [Fact]
    public void StandardOriginCancelsPendingRestAndOnlyHonorsExplicitHeavy()
    {
        var sut = Make(out var log);
        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        sut.HandleTick(150, TimelineTickOrigin.Standard, heavyRefreshRequested: false);
        Assert.Empty(log);
        sut.HandleTick(160, TimelineTickOrigin.Standard, heavyRefreshRequested: true);
        Assert.Single(log, "heavy");
    }

    [Fact]
    public async Task DisposeSuppressesLateFlush()
    {
        var tcs = new TaskCompletionSource();
        var events = new List<string>();
        var sut = new ScrubRefreshCoordinator(
            new ScrubApplyScheduler(300L),
            () => events.Add("heavy"),
            a => a(),
            () => 0L,
            (_, ct) => tcs.Task);
        sut.HandleTick(100, TimelineTickOrigin.ScrubPreview, heavyRefreshRequested: true);
        sut.Dispose();
        tcs.SetResult();
        await Task.Yield();
        Assert.Empty(events);
    }
}
```

IMPORTANT: before finalizing these tests, verify the assertion baselines against the CURRENT
binder code paths (`HandleScrubAwareHeavyRefresh`/`ScheduleScrubRestRefresh`/
`FlushScrubRestRefresh`/`CancelScrubRestRefresh`, lines 1420–1499) and `ScrubApplyScheduler`'s
actual `RecordPreview`/`ConsumeDue`/`ConsumeCommit` contracts — the tests pin EXISTING
semantics; if an expected count above contradicts the real scheduler contract, fix the TEST
to match the code and note it in AGENT-SUMMARY.md. Behavior change in the move is forbidden.

- [ ] **Step 2: Run to verify failure** — FAIL: `ScrubRefreshCoordinator` not defined.
- [ ] **Step 3: Create the coordinator** — move lines 1420–1499 verbatim into the class:
  `HandleScrubAwareHeavyRefresh` body → `HandleTick`; `ScheduleScrubRestRefresh`,
  `DelayThenFlushScrubRefreshAsync`, `FlushScrubRestRefreshIfDue`, `FlushScrubRestRefresh`
  become private; `CancelScrubRestRefresh` body → `Cancel()`. Substitutions, and ONLY these:
  `ScheduleRegimeRefresh()` → `_requestHeavyRefresh()`; `System.Environment.TickCount64` →
  `_nowMs()`; `Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken)` →
  `_delayAsync(TimeSpan.FromMilliseconds(delayMs), cancellationToken)` where
  `_delayAsync = delayAsync ?? Task.Delay`; `Callable.From(...).CallDeferred()` →
  `_deferToMainThread(() => ...)`; `_disposed` → the coordinator's own `_disposed`
  (set in `Dispose()`, which also cancels/disposes the CTS — same teardown the binder's
  `Dispose` performs today). `_scrubApplyScheduler` / `_scrubRefreshDelay` become fields here.
- [ ] **Step 4: Rewire the binder** — delete moved members + the `_scrubApplyScheduler` and
  `_scrubRefreshDelay` fields; add
  `private readonly ScrubRefreshCoordinator _scrubRefresh;` constructed in the ctor as
  `new(new ScrubApplyScheduler(restDelayMs: 300L), ScheduleRegimeRefresh, a => Callable.From(() => a()).CallDeferred(), () => System.Environment.TickCount64)`.
  `ApplyTimelineTick` line 430 becomes `_scrubRefresh.HandleTick(tick, origin, heavyRefreshRequested);`.
  In `Dispose()`, replace the old scrub-teardown lines with `_scrubRefresh.Dispose();` at the
  SAME position in the teardown order. The Godot `Callable` lambda lives binder-side, so the
  coordinator itself stays Godot-free.
- [ ] **Step 5: Verify** — clean build + full suite green (+4 tests).

### Task 4: `PlanetTimelineController` to its own file

**Files:**
- Create: `project/plugins/App.Presentation/PlanetTimelineController.cs`
- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.cs`

**Interfaces:** unchanged — this is a verbatim file move of an already-separate class.

- [ ] **Step 1: Move** lines 2532–2636 (`internal sealed class PlanetTimelineController` through
  `EmptySchedule`, including the class-level doc comment above it) into the new file. Copy the
  usings the class actually needs (trim the rest). Delete from the binder file.
- [ ] **Step 2: Verify** — clean build + full suite green (existing
  `PlanetTimelineControllerScrubOriginTests` still pass untouched). No new tests: no new code.

### Task 5: partial split — `PlanetPresentationBinder.PlateSurface.cs`

**Files:**
- Create: `project/plugins/App.Presentation/PlanetPresentationBinder.PlateSurface.cs`
- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.cs`

**Interfaces:** none change — `partial class` file split, members verbatim.

- [ ] **Step 1: Mark the class `internal sealed partial class PlanetPresentationBinder`** in the
  core file.
- [ ] **Step 2: Create the partial file** with header comment
  `// Plate-surface build/bind + Continents membership. Split from PlanetPresentationBinder
  2026-07-11 (vault/plans/2026-07-11-planet-presentation-binder-split-plan.md). D8b maps
  resolution rungs onto the AdaptiveSubdivisionOptions built in BindPlateSurface.` Move
  verbatim: `ApplySurfaceAppearance` (1306–1317), `RebuildPlateSurface` (1318–1357),
  `RefreshContinentsMembership` (1358–1411), `BuildPlateSurface` (1655–1660),
  `BindPlateSurface` (1662–1796), plus the `_last*` bind-cache FIELDS (126–138, with their
  M-B comment) and the `_plateSurfaces` field (57). Fields may move between partial files
  freely — they remain instance state of the same class.
- [ ] **Step 3: Verify** — clean build + full suite green. `git diff --stat` shows only the
  two binder files.

### Task 6: partial split — `PlanetPresentationBinder.CutawayExploded.cs` + `PlanetPresentationBinder.MantleViews.cs`

**Files:**
- Create: `project/plugins/App.Presentation/PlanetPresentationBinder.CutawayExploded.cs`
- Create: `project/plugins/App.Presentation/PlanetPresentationBinder.MantleViews.cs`
- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.cs`

- [ ] **Step 1: CutawayExploded partial** — move verbatim with their comments:
  `UpdateCutaway` (540–552), `UpdateExploded` (554–567), `UpdateCutawayPlateShader` (569–588),
  `RebuildCutawayFaces` (590–611), `RebuildExplodedCrust` (613–640), `BuildExplodedTopDto`
  (716–754), `BuildExplodedSolidDto` (755–781), `BuildExplodedMeshInstance` (782–819),
  `BuildExplodedSolidMeshInstance` (1035–1061), `BuildCutawayFaces` (1157–1200),
  `BuildCutawayFaceSector` (1213–1280), `PolarToCartesian` (1281–1294), and (if present in the
  1035–1200 span) `BuildExplodedSolidCrust`. Also move the W3a/M-B state fields: `_cutawayWedge`,
  `_cutawayAzimuthDeg`, `_cutawayWidthDeg`, `_cutawayFaceRoot`, `_hypsoPlateMaterialOverride` +
  `HypsoPlateMaterialOverride` property (2347–2354), `_explodedActive`, `_explodedFactor`,
  `_explodedCrustRoot` (87–92, 111–114).
- [ ] **Step 2: MantleViews partial** — move verbatim: `UpdateMantle` (642–716 head),
  `RebuildMantleXray` (820–870), `RebuildMantleLayer` (871–948), `BuildMantleXrayRoot`
  (949–985), `BuildCoreSphere` (986–1001), `BuildIsosurfaceNode` (1002–1034),
  `ResolveCrustThicknessMetres` (1062–1076), the isosurface instance-material caches + lazies
  (1077–1101), and the state fields `_mantleXrayRoot`, `_mantleXrayActive`, `_mantleLayerRoot`,
  `_mantleLayerActive`, `_radialProfile` (94–105, 116–120). `ResolvePlanetRadiusMetres`
  (1201–1212) is shared across clusters — it STAYS in the core file.
- [ ] **Step 3: Verify** — clean build + full suite green.

### Task 7: partial split — `PlanetPresentationBinder.SceneFurniture.cs` + core tidy

**Files:**
- Create: `project/plugins/App.Presentation/PlanetPresentationBinder.SceneFurniture.cs`
- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.cs`

- [ ] **Step 1: SceneFurniture partial** — move verbatim: `BuildVerticalScaleIndicator`
  (522–539), `AddLightingAndCamera` (1535–1580), `ApplyLightingForView` (1581–1590),
  `BuildMantle` (1591–1609), `BuildAtmosphereRim` (1610–1635), `UpdateAtmosphereRim`
  (1636–1654), `BuildProductLayerRoot` (2039–2059), `BuildStatusLabel` (2060–2072),
  `ResolveMantleMaterial` + the three instance mantle-material caches (2325–2327, 2356–2362),
  and fields `_mantle`, `_atmosphereRim`, `_atmosphereRimMaterial`, `_sunLight`,
  `_planetEnvironment`, `_statusLabel` (51–56).
- [ ] **Step 2: Core tidy** — the core file now holds: constants + remaining fields, ctor,
  `ResetRegimeTracking`, `Rebind`, graph-view cluster (`EnsureNodeGraphView`,
  `BuildInitialGraphSource`, `SubscribeGenerationChanged`, `OnLayerSelectionChanged`,
  `ReleaseNodeGraphView`), `BindDocument`, `ApplyTimelineTick`, `ScheduleRegimeRefresh`,
  `RefreshPresentationForRegime`, lifecycle (`OnResourceRuntimeChanging`/`Changed`,
  `TryRebindAfterWorldRuntimeChange`, `ClearActiveRoot`, `ReleasePlateSurfaceRenderer`,
  `Dispose`), shared helpers (`ResolvePlanetRadiusMetres`, `ToV3(GlobeVec3)`, `TryNormalize`,
  `SafeNodeName`, `EmptySchedule` if not moved with the controller). Prune now-unused usings
  in every touched file. Do NOT reorder surviving members.
- [ ] **Step 3: Verify + report** — clean build; full suite green;
  `wc -l project/plugins/App.Presentation/PlanetPresentationBinder*.cs PlanetShaderLibrary.cs PlateSurfaceMeshFactory.cs ScrubRefreshCoordinator.cs PlanetTimelineController.cs`
  recorded in AGENT-SUMMARY.md (target: core file ≤ 1,000 lines; total across files ≈ source
  total ± scaffolding). Confirm with `git grep -n "ShaderCode" project/plugins/App.Presentation/PlanetPresentationBinder*.cs`
  that no shader string remains in any binder file.

### Task 8: handoff

- [ ] Full `dotnet build project/FantaSim.sln` clean and `dotnet test project/FantaSim.sln`
  green one last time; record final counts.
- [ ] Leave `AGENT-SUMMARY.md` at repo root: files added/changed per task, test counts per
  task, every deviation from this plan with the reason (e.g. line ranges that had drifted,
  members discovered to belong to a different cluster), and anything found mid-implementation
  the lead should know before gating (dead members, suspicious couplings, comment rot).
- [ ] Do NOT commit. Do NOT export or run the windowed gate — the lead does.

## Lead acceptance gate (lead-run, after review + per-task commits)

Rebuild-tier decision per `.agent/rules/bundle-hot-reload-verify.md`: all edits are inside the
world collectible bundle → hot-reload path, no full re-export.

1. `task bundle:world && task bundle:install` against the RUNNING exported app →
   `old ALC collected for bundle world` in the app log (×2 rounds — the reload path is the
   riskiest surface of this refactor).
2. Visual sanity after reload (drive recipes, handover §5): `timeline.seek` to a mid-run tick →
   `render.screenshot` — planet renders in World view with relief + status label;
   `render.cutaway {"azimuthDeg":40,"widthDeg":30}` → wedge + cut faces visible in a second
   screenshot; `render.mantle {"enabled":true}` then `{"enabled":false}` → isosurfaces appear,
   surface restores.
3. Scrub-origin regression gate (yesterday's baseline): 6-preview + 1-commit sweep via ingress →
   log signatures show 1 `Crust generation triggered` + 2 `Planet plate surface bound`
   (unchanged from `6624470`'s proof).
4. Full suite green at every commit (`git rebase -x` not needed — verify per-task during review).
5. `git diff b760ac4..HEAD --stat` touches ONLY `project/plugins/App.Presentation/` and
   `project/tests/App.Presentation.Tests/`.

## Self-review notes (plan author, 2026-07-11)

- Spec coverage: the review finding was "split the god class before D8b/D5 land" — all 7 seams
  (shader library, mesh factory, scrub coordinator, controller file, 3 view partials + plate
  partial) trace to the region map re-derived from `b760ac4`; the two D8b landing zones become
  a real class (coordinator) and a dedicated partial (plate surface).
- Deliberate choice: partials over collaborator classes for node-state clusters — zero
  reference changes in the reload/ALC paths stabilized on 07-10. Promotion deferred to the
  arc that needs it. If the user prefers full collaborator extraction now, STOP and re-plan.
- Type consistency: `ScrubRefreshCoordinator` ctor in Task 3 Step 3/4 matches the Interfaces
  block; factory method names in Task 2 tests match the Step-3 move list.
- Known soft spots flagged to the implementer inline: `ToV3` overload split (Task 2 Step 4),
  scheduler-contract verification before pinning coordinator test baselines (Task 3 Step 1),
  `GlobeCell` constructibility (Task 2 Step 1), line-range drift (Global Constraints).
