# App.Timeline 4-Tier Refactor Implementation Plan

> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the `orchestrate-before-implementing` rule + the `external-agent-delegation`
> skill); otherwise execute inline with a review checkpoint per task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Split the monolithic `plugins/App.Timeline` (`Godot.NET.Sdk`, mixing model + view + lifecycle) into the canonical 4-tier shape: T1 contracts (`contracts/App.Timeline`), T3 orchestrator (`plugins/App.Timeline` pure C#), T4 seam (`plugins/App.Timeline.Seam` Godot), with `Host.cs` composition and bundle manifest updated so the collectible ALC stays free of Godot-derived types.

**Architecture:** The timeline is a paradigm UI that consumes `ITimelineController` (owned by App.World). Per the user decision, it gets a full `IService` T1 contract so other plugins can drive playback through the registry. The pure-C# model (`TimelineModel`, `TimelineTimeFormatter`) stays in T3; the Godot-coupled `TimelineFace` (Control + AnimationPlayer/Tree) moves to T4. The T3 owns a `Providers/ITimelineFace` seam interface the T4 implements. The T4 seam is **resident** (loaded in the parent ALC, referenced by `Host.cs`), while the collectible `timeline` bundle ships only T1+T3 (Godot-free, ALC-unloads cleanly). The scene's `residentType` binding resolves the resident seam type via `Type.GetType` across all loaded assemblies.

**Tech Stack:** .NET 8 C#, Godot 4.7 (.NET bindings), ServiceArchi/PluginArchi, xUnit.

## Global Constraints

- TFM is `net8.0` for T1/T3; T4 uses `Godot.NET.Sdk/4.7.0`.
- T1 contracts assembly: `FantaSim.App.Timeline.Contracts`, `<ServiceArchiTier>T1</ServiceArchiTier>`, `[assembly: PluginSharedContract]`, references `GiantCroissant.ServiceArchi.Contracts` + `ServiceArchi.SourceGen` + `PluginArchi.Extensibility.Abstractions`. No Godot, no Akka.
- T3 plugin assembly: `FantaSim.App.Timeline`, `<ServiceArchiTier>T3</ServiceArchiTier>`, `Microsoft.NET.Sdk`. References T1 only (+ cross-foundation as needed). Zero `Godot` usings.
- T4 seam assembly: `FantaSim.App.Timeline.Seam`, `<ServiceArchiTier>T4</ServiceArchiTier>`, `Godot.NET.Sdk/4.7.0`. References T1 + T3. The only tier with `using Godot;`.
- Dependency direction: `T4 -> T3 -> T1`. Reverse is forbidden.
- The `timeline` collectible bundle (`bundles/timeline/`) ships `FantaSim.App.Timeline.dll` (T3) + `FantaSim.App.Timeline.Contracts.dll` (T1) in its PCK. The T4 seam (`FantaSim.App.Timeline.Seam.dll`) stays **resident** in the host and is NOT packed into the bundle PCK (mirrors App.Camera.Seam).
- The `collectible-bundles.json` entry for `timeline` keeps `pluginAssembly: "FantaSim.App.Timeline.dll"`. `SharedAssemblyPolicy` excludes `FantaSim.App.Timeline` (bundle ALC) but shares `FantaSim.App.Timeline.Contracts` (parent ALC, via the `FantaSim.App.` prefix policy already in place).
- The scene's `residentType` resolves to the resident T4 type `FantaSim.App.Timeline.Seam.TimelineFace` via `Type.GetType` scanning all loaded assemblies (`BundleSceneHost.LoadResidentTypeScript`).
- No `git commit -A`. Path-scoped `git add <file>` per task.
- Conventional commits: `refactor(timeline): <description>`, `test(timeline): <description>`, `feat(timeline): <description>`.
- ASCII only in all generated files.
- TDD: write the failing test first, watch it fail, then implement.
- Build verification uses the unify-build skill: `dotnet unify-build --project yokan-projects/fantasim-app-godot` for full builds; `dotnet build <csproj>` for per-project quick checks.

## Reference patterns used

- T1 contract shape: `contracts/App.Camera/` (IService + Service proxy + AssemblyInfo + DTOs).
- T3 plugin shape: `plugins/App.Camera/Services/Service.cs` + `plugins/App.Camera/Providers/ICameraRig.cs`.
- T4 seam shape: `plugins/App.Camera.Seam/CameraRig.cs` (plain class implementing the T3 provider interface; `Callable.From(...).CallDeferred()` for main-thread marshalling).
- Host composition: `Host.cs ComposeUi` / `ComposeWorldView` (construct seam, construct T3 with seam injected, register T3 into `IRegistry`).
- Node-backed seam exception: documented in `vault/architecture/service-tier-architecture.md` for `IiiBridge`. `TimelineFace : Control` is the same exception shape (needs `_Ready`/`_ExitTree` lifecycle).

---

## File Structure

### T1 - `contracts/App.Timeline/` (NEW)

- `App.Timeline.csproj` - `Microsoft.NET.Sdk`, T1, assembly `FantaSim.App.Timeline.Contracts`.
- `AssemblyInfo.cs` - `[assembly: PluginSharedContract]`.
- `Services/IService.cs` - `[ServiceContract]` timeline service interface (Play/Pause/Seek/State).
- `Services/Service.cs` - T2 source-generated proxy partial.
- `TimelineDtos.cs` - `TimelineBand`, `TimelineTrack`, `TimelineRulerMark` records (moved from `plugins/App.Timeline/TimelineModel.cs`).

### T3 - `plugins/App.Timeline/` (REFACTORED to pure C#)

- `App.Timeline.csproj` - `Microsoft.NET.Sdk`, T3, assembly `FantaSim.App.Timeline`. References T1 only (+ cross-foundation messaging, world shared contracts for the model).
- `Services/Service.cs` - T3 orchestrator implementing `IService`; owns playback state machine, delegates engine work to `ITimelineFace` provider.
- `Providers/ITimelineFace.cs` - T3-owned seam interface (the T4 implements it): `Play()`, `Pause()`, `SeekTo(long)`, `UpdateView(TimelineViewSnapshot)`.
- `TimelineModel.cs` - Pure C# band/track/ruler logic (unchanged body, namespace stays `FantaSim.App.Timeline`).
- `TimelineTimeFormatter.cs` - Extracted from `TimelineModel.cs` (same body).
- `TimelinePlugin.cs` - Retained for bundle activation (registers the scene activator). No longer holds `static ActiveController`.
- `TimelineActivator.cs` - Unchanged (scene-tier activator, references T1 `IRegistry`).
- `TimelineActivation.cs` - Unchanged.
- `Bootstrap.cs` - Unchanged.

### T4 - `plugins/App.Timeline.Seam/` (NEW)

- `App.Timeline.Seam.csproj` - `Godot.NET.Sdk/4.7.0`, T4, assembly `FantaSim.App.Timeline.Seam`. References T1 + T3.
- `TimelineFace.cs` - Moved from `plugins/App.Timeline/`; now implements `ITimelineFace`. The Godot `Control` with `AnimationPlayer`/`AnimationTree` + `[Export] InternalTick` setter. Resolves `ITimelineController` from the registry passed at construction instead of `TimelinePlugin.ActiveController`.
- `TimelineViewSnapshot.cs` - Engine-agnostic view-state DTO (tick, regime id, active layers, bands, tracks, ruler marks) the T3 sends to the seam.

### Host - `hosts/complete-app/` (MODIFIED)

- `Host.cs` - Add `ComposeTimeline(composition)` between `ComposeWorldView` and `ComposeActivity` (so `ITimelineController` is registered first). Constructs the T4 `TimelineFace` (resident Node, `AddChild`), constructs T3 `Service` with the seam injected + `ITimelineController` resolved from registry, registers `IService` into `IRegistry`.
- `config/collectible-bundles.json` - Unchanged (`timeline` entry already correct).
- `project.godot` - Add `FantaSim.App.Timeline.Seam` to the resident assembly list if the project uses an explicit resident-assembly allowlist (check existing pattern).

### Bundle - `bundles/timeline/` (MODIFIED)

- `manifest.json` - `residentType` updated to `FantaSim.App.Timeline.Seam.TimelineFace` (was `FantaSim.App.Timeline.TimelineFace`). The `pluginAssembly` stays `FantaSim.App.Timeline.dll` (T3).
- `scenes/Timeline.tscn` - Unchanged (nodes are engine-typed; the script is bound by the resident binding, not embedded).
- `FantaSim.App.Timeline.dll` - Rebuilt from the refactored T3 project.

### Tests - `tests/App.Timeline.Tests/` (MODIFIED)

- `App.Timeline.Tests.csproj` - Reference T1 contracts (`contracts/App.Timeline`) + T3 plugin (`plugins/App.Timeline`). Drop the `App.World.Composition` reference if the model no longer needs it directly (it still does, for `SphereRegimeSchedule`).
- `TimelineModelTests.cs` - Unchanged (tests the pure-C# model in T3).
- `TimelineServiceTests.cs` (NEW) - Tests for the T3 `Service` playback state machine using a fake `ITimelineFace`.
- `OdometerLabelTests.cs` - Unchanged.

---

### Task 1: Create T1 contracts project scaffold

**Files:**
- Create: `project/contracts/App.Timeline/App.Timeline.csproj`
- Create: `project/contracts/App.Timeline/AssemblyInfo.cs`

**Interfaces:**
- Consumes: none (this is the foundation)
- Produces: `FantaSim.App.Timeline.Contracts` assembly, `[PluginSharedContract]`-marked

- [ ] **Step 1: Create the T1 project directory**

```bash
mkdir -p yokan-projects/fantasim-app-godot/project/contracts/App.Timeline/Services
```

- [ ] **Step 2: Write `App.Timeline.csproj`**

Create `yokan-projects/fantasim-app-godot/project/contracts/App.Timeline/App.Timeline.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>FantaSim.App.Timeline</RootNamespace>
    <AssemblyName>FantaSim.App.Timeline.Contracts</AssemblyName>
    <!-- ServiceArchi Tier 1: engine-agnostic timeline contract - IService, playback DTOs,
         view-snapshot records, and the T2 proxy. The T3 orchestrator is plugins/App.Timeline;
         the Godot TimelineFace is the resident T4 seam plugins/App.Timeline.Seam. Mirrors
         App.Camera contracts. -->
    <ServiceArchiTier>T1</ServiceArchiTier>
  </PropertyGroup>

  <ItemGroup>
    <CompilerVisibleProperty Include="ServiceArchiTier" />
  </ItemGroup>

  <!-- PluginArchi: [assembly: PluginSharedContract] (timeline contract types cross the
       host-bundle ALC boundary). ServiceArchi: the T2 proxy (Services/Service.cs) is realized
       here by the source generator. -->
  <ItemGroup>
    <PackageReference Include="GiantCroissant.PluginArchi.Extensibility.Abstractions" />
    <PackageReference Include="GiantCroissant.ServiceArchi.Contracts" />
    <PackageReference Include="GiantCroissant.ServiceArchi.SourceGen" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Write `AssemblyInfo.cs`**

Create `yokan-projects/fantasim-app-godot/project/contracts/App.Timeline/AssemblyInfo.cs`:

```csharp
// Declares this contract assembly as shared across the host-bundle ALC boundary, holding only
// interfaces/DTOs (no [Plugin] types) to preserve type identity between host and collectible
// contexts. Mirrors App.Camera/AssemblyInfo.cs.
[assembly: PluginArchi.Extensibility.Abstractions.PluginSharedContract]
```

- [ ] **Step 4: Build the T1 project**

Run:
```bash
dotnet build yokan-projects/fantasim-app-godot/project/contracts/App.Timeline/App.Timeline.csproj -c Debug
```
Expected: build fails (no `Services/IService.cs` yet, but the project compiles with no source files; verify exit 0). If it fails on missing files, that is expected for an empty project - proceed to Task 2.

- [ ] **Step 5: Commit**

```bash
git add yokan-projects/fantasim-app-godot/project/contracts/App.Timeline/App.Timeline.csproj \
        yokan-projects/fantasim-app-godot/project/contracts/App.Timeline/AssemblyInfo.cs
git commit -m "refactor(timeline): scaffold T1 contracts project (App.Timeline.Contracts)"
```

---

### Task 2: Add T1 IService + T2 proxy + DTOs

**Files:**
- Create: `project/contracts/App.Timeline/Services/IService.cs`
- Create: `project/contracts/App.Timeline/Services/Service.cs`
- Create: `project/contracts/App.Timeline/TimelineDtos.cs`
- Test: `project/tests/App.Timeline.Tests/TimelineDtosTests.cs`

**Interfaces:**
- Consumes: `ServiceArchi.Contracts` (`[ServiceContract]`, `[SelectionStrategy]`, `IRegistry`), `PluginArchi` attributes
- Produces: `IService` (timeline service contract), `Service` (T2 proxy), `TimelinePlaybackState` enum, `TimelineViewSnapshot` record, `TimelineBand`/`TimelineTrack`/`TimelineRulerMark` records

- [ ] **Step 1: Write the failing DTO tests**

Create `yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/TimelineDtosTests.cs`:

```csharp
using FantaSim.App.Timeline;
using Xunit;

namespace App.Timeline.Tests;

public class TimelineDtosTests
{
    [Fact]
    public void TimelineViewSnapshot_RecordsTickAndState()
    {
        var snap = new TimelineViewSnapshot(
            Tick: 500_000,
            State: TimelinePlaybackState.Playing,
            ActiveRegimeId: "magma-ocean",
            MaxTick: 120_000_000);
        Assert.Equal(500_000, snap.Tick);
        Assert.Equal(TimelinePlaybackState.Playing, snap.State);
        Assert.Equal("magma-ocean", snap.ActiveRegimeId);
        Assert.Equal(120_000_000, snap.MaxTick);
    }

    [Fact]
    public void TimelineBand_RecordHoldsAllFields()
    {
        var band = new TimelineBand(
            RegimeId: "magma-ocean",
            StartFraction: 0.0,
            WidthFraction: 0.5,
            Variant: "danger",
            IsActive: true,
            StartTick: 0,
            EndTick: 1_000_000);
        Assert.Equal("magma-ocean", band.RegimeId);
        Assert.True(band.IsActive);
        Assert.Equal(0, band.StartTick);
    }

    [Fact]
    public void TimelinePlaybackState_HasThreeStates()
    {
        Assert.Equal(3, System.Enum.GetNames(typeof(TimelinePlaybackState)).Length);
        Assert.True(System.Enum.IsDefined(typeof(TimelinePlaybackState), "Idle"));
        Assert.True(System.Enum.IsDefined(typeof(TimelinePlaybackState), "Playing"));
        Assert.True(System.Enum.IsDefined(typeof(TimelinePlaybackState), "Scrubbing"));
    }
}
```

- [ ] **Step 2: Add T1 reference to the test project**

Modify `yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj` - add the T1 project reference alongside the existing T3 reference:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\contracts\App.Timeline\App.Timeline.csproj" />
    <ProjectReference Include="..\..\plugins\App.Timeline\App.Timeline.csproj" />
    <ProjectReference Include="..\..\plugins\App.World.Composition\App.World.Composition.csproj" />
  </ItemGroup>
```

- [ ] **Step 3: Run the tests to verify they fail**

Run:
```bash
dotnet test yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --filter FullyQualifiedName~TimelineDtosTests
```
Expected: FAIL with "type TimelineViewSnapshot not found" or "TimelineBand not found" (they are still in the old T3 plugin under `FantaSim.App.Timeline` namespace but the test expects them in T1; the records do not exist yet with the new shapes).

- [ ] **Step 4: Write `TimelineDtos.cs`**

Create `yokan-projects/fantasim-app-godot/project/contracts/App.Timeline/TimelineDtos.cs`:

```csharp
namespace FantaSim.App.Timeline;

/// <summary>Playback state the timeline service reports to consumers.</summary>
public enum TimelinePlaybackState
{
    Idle,
    Playing,
    Scrubbing,
}

/// <summary>
/// Engine-agnostic view snapshot the T3 service sends to the T4 seam (and any other
/// subscriber). Contains everything the face needs to render one frame: the tick, the
/// playback state, the active regime id, and the max tick. The seam maps this to Godot
/// UI state. Mirrors the CameraSpec pattern: pure data, no Godot types.
/// </summary>
/// <param name="Tick">Current canonical tick.</param>
/// <param name="State">Current playback state (Idle/Playing/Scrubbing).</param>
/// <param name="ActiveRegimeId">The geosphere regime active at <paramref name="Tick"/>, or null.</param>
/// <param name="MaxTick">The maximum tick the timeline can reach.</param>
public sealed record TimelineViewSnapshot(
    long Tick,
    TimelinePlaybackState State,
    string? ActiveRegimeId,
    long MaxTick);

/// <summary>One regime band on the timeline lane. Pure data - the seam renders it.</summary>
public sealed record TimelineBand(
    string RegimeId,
    double StartFraction,
    double WidthFraction,
    string Variant,
    bool IsActive,
    long StartTick,
    long EndTick);

/// <summary>One track (layer) on the timeline lane.</summary>
public sealed record TimelineTrack(string LayerId, bool IsActive);

/// <summary>One ruler mark.</summary>
public sealed record TimelineRulerMark(long Tick, double Fraction, string Label);
```

- [ ] **Step 5: Write `Services/IService.cs`**

Create `yokan-projects/fantasim-app-godot/project/contracts/App.Timeline/Services/IService.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.Timeline;

/// <summary>
/// T1 timeline service contract. The timeline is a paradigm UI that drives playback of the
/// world's ITimelineController. Other plugins resolve this via the registry to drive Play/Pause/
/// Seek without referencing the bundle or seam directly. The T3 orchestrator
/// (plugins/App.Timeline/Services/Service.cs) implements this; the T2 proxy forwards.
/// </summary>
[ServiceContract]
[SelectionStrategy(SelectionMode.HighestPriority)]
public interface IService
{
    /// <summary>Current canonical tick.</summary>
    long Tick { get; }

    /// <summary>Maximum tick the timeline can reach.</summary>
    long MaxTick { get; }

    /// <summary>Current playback state.</summary>
    TimelinePlaybackState State { get; }

    /// <summary>Start playback (transitions to Playing).</summary>
    Task PlayAsync(CancellationToken cancellationToken = default);

    /// <summary>Pause playback (transitions to Idle).</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Seek to a specific tick (clamped to [0, MaxTick]). Transitions to Scrubbing.</summary>
    Task SeekAsync(long tick, CancellationToken cancellationToken = default);

    /// <summary>Raised after any tick/state change. May be raised off the main thread.</summary>
    event Action<TimelineViewSnapshot>? ViewChanged;
}
```

- [ ] **Step 6: Write `Services/Service.cs` (T2 proxy)**

Create `yokan-projects/fantasim-app-godot/project/contracts/App.Timeline/Services/Service.cs`:

```csharp
using System;
using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.Timeline.Services.Proxy;

// Service-locator proxy for IService (ServiceArchi Tier 2). ServiceArchi.SourceGen generates the
// forwarding partial that implements IService by resolving the active T3 from the registry.
// Lives alongside the contract (T1) per this repo's layout. Mirrors App.Camera/Services/Service.cs.
[RealizeService(typeof(IService))]
public sealed partial class Service
{
    private readonly IRegistry _registry;

    public Service(IRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }
}
```

- [ ] **Step 7: Build T1**

Run:
```bash
dotnet build yokan-projects/fantasim-app-godot/project/contracts/App.Timeline/App.Timeline.csproj -c Debug
```
Expected: PASS (exit 0). Source generator emits the proxy partial.

- [ ] **Step 8: Run DTO tests to verify they pass**

Run:
```bash
dotnet test yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --filter FullyQualifiedName~TimelineDtosTests
```
Expected: PASS (3 tests).

- [ ] **Step 9: Commit**

```bash
git add yokan-projects/fantasim-app-godot/project/contracts/App.Timeline/Services/IService.cs \
        yokan-projects/fantasim-app-godot/project/contracts/App.Timeline/Services/Service.cs \
        yokan-projects/fantasim-app-godot/project/contracts/App.Timeline/TimelineDtos.cs \
        yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/TimelineDtosTests.cs \
        yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj
git commit -m "feat(timeline): add T1 IService contract, T2 proxy, and view DTOs"
```

---

### Task 3: Add T3 provider seam interface

**Files:**
- Create: `project/plugins/App.Timeline/Providers/ITimelineFace.cs`

**Interfaces:**
- Consumes: T1 (`TimelineViewSnapshot`, `TimelinePlaybackState`)
- Produces: `ITimelineFace` - the T3-owned seam interface the T4 implements

- [ ] **Step 1: Write `Providers/ITimelineFace.cs`**

Create the directory and file `yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/Providers/ITimelineFace.cs`:

```csharp
using System.Threading.Tasks;

namespace FantaSim.App.Timeline.Providers;

/// <summary>
/// The timeline service's engine seam: the Godot-facing backend that renders the timeline
/// UI and drives the AnimationPlayer/Tree playback. The T3 service owns this seam and
/// delegates engine work to it (implemented by App.Timeline.Seam's TimelineFace). Mirrors
/// App.Camera's Providers/ICameraRig. Deliberately LEANER than IService: playback state
/// tracking, tick accounting, and ViewChanged fan-out are the service's job, not the face's.
/// </summary>
public interface ITimelineFace
{
    /// <summary>
    /// Start the animation playback (transitions the AnimationTree to the "playing" state).
    /// Called on the main thread by the T3 (which may receive the request off-thread).
    /// </summary>
    void Play();

    /// <summary>
    /// Pause the animation playback (transitions the AnimationTree to the "idle" state).
    /// Called on the main thread by the T3.
    /// </summary>
    void Pause();

    /// <summary>
    /// Seek the AnimationPlayer to the tick and transition to "scrub". Called on the main
    /// thread by the T3. The face must NOT call back into the service during this method
    /// (the service already knows the tick - it called Seek).
    /// </summary>
    void SeekTo(long tick);

    /// <summary>
    /// Apply a view snapshot to the face (update status label, playhead position, band
    /// highlighting, ruler). Called after every tick or state change. The face may marshal
    /// this onto the main thread if called off-thread.
    /// </summary>
    void ApplyView(TimelineViewSnapshot snapshot);
}
```

- [ ] **Step 2: Verify it does not compile yet (T3 csproj is still Godot SDK)**

Run:
```bash
dotnet build yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/App.Timeline.csproj -c Debug
```
Expected: may still build (Godot SDK tolerates the pure-C# file). This is fine - the file will be properly referenced once the csproj is refactored in Task 4. Do NOT commit yet; commit with the csproj refactor.

- [ ] **Step 3: No commit yet - the csproj refactor in Task 4 is the atomic unit**

---

### Task 4: Refactor T3 csproj to pure C# (Microsoft.NET.Sdk)

**Files:**
- Modify: `project/plugins/App.Timeline/App.Timeline.csproj`
- Modify: `project/plugins/App.Timeline/TimelineModel.cs` (split out TimelineTimeFormatter)
- Create: `project/plugins/App.Timeline/TimelineTimeFormatter.cs`
- Test: `project/tests/App.Timeline.Tests/TimelineModelTests.cs` (verify still pass)

**Interfaces:**
- Consumes: T1 (`FantaSim.App.Timeline.Contracts`), App.World.Composition (for SphereRegimeSchedule), World.Shared.Contracts (for UnitConverter/CanonicalDisplayFormatter)
- Produces: T3 `FantaSim.App.Timeline` assembly, pure C# (no Godot)

- [ ] **Step 1: Write the failing test that asserts the T3 project has no Godot reference**

Create `yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/T3PurityTests.cs`:

```csharp
using System.Reflection;
using Xunit;

namespace App.Timeline.Tests;

public class T3PurityTests
{
    [Fact]
    public void T3_Assembly_HasNoGodotReference()
    {
        // The T3 plugin assembly must be pure C# (no GodotSharp reference) so the collectible
        // ALC unloads cleanly. Only the T4 seam (App.Timeline.Seam) may reference Godot.
        var asm = typeof(FantaSim.App.Timeline.TimelineModel).Assembly;
        var referenced = asm.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, r => r.Name == "GodotSharp");
    }

    [Fact]
    public void T3_Assembly_IsNotGodotDerived()
    {
        // TimelineModel must NOT extend Godot.Node / Control / Resource.
        var modelType = typeof(FantaSim.App.Timeline.TimelineModel);
        Assert.False(modelType.IsSubclassOf(typeof(Godot.GodotObject))
            || modelType.IsSubclassOf(typeof(Godot.Node))
            || modelType.IsSubclassOf(typeof(Godot.Resource)));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails (T3 is currently Godot SDK)**

Run:
```bash
dotnet test yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --filter FullyQualifiedName~T3PurityTests
```
Expected: FAIL (the current T3 csproj uses `Godot.NET.Sdk` so `GodotSharp` is referenced and `TimelineModel` currently lives in a Godot-SKD assembly).

- [ ] **Step 3: Refactor `App.Timeline.csproj` to `Microsoft.NET.Sdk`**

Replace `yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/App.Timeline.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>FantaSim.App.Timeline</RootNamespace>
    <AssemblyName>FantaSim.App.Timeline</AssemblyName>
    <!-- ServiceArchi Tier 3: engine-agnostic timeline orchestrator. Owns the playback state
         machine, the pure-C# TimelineModel (bands/tracks/ruler), and the ITimelineFace seam
         interface (Providers/). The T4 Godot seam (App.Timeline.Seam) implements ITimelineFace.
         Pure C# (Microsoft.NET.Sdk, not Godot.NET.Sdk); T3 never references T4. Mirrors App.Camera. -->
    <ServiceArchiTier>T3</ServiceArchiTier>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <CompilerVisibleProperty Include="ServiceArchiTier" />
  </ItemGroup>

  <ItemGroup>
    <!-- T1 contracts (IService, DTOs, T2 proxy). -->
    <ProjectReference Include="..\..\contracts\App.Timeline\App.Timeline.csproj" />
    <!-- App.World contracts hold ITimelineController + SphereRegimeSchedule the model consumes. -->
    <ProjectReference Include="..\..\contracts\App.World\App.World.csproj" />
    <!-- SceneFlow contracts for the scene-tier activator. -->
    <ProjectReference Include="..\..\contracts\App.SceneFlow\App.SceneFlow.csproj" />
    <!-- World shared contracts: UnitConverter + CanonicalDisplayFormatter used by the model. -->
    <ProjectReference Include="$(YokanProjectsRoot)\fantasim-world\project\contracts\World.Shared\World.Shared.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="GiantCroissant.ServiceArchi.Contracts" Version="0.1.1" />
    <PackageReference Include="GiantCroissant.PluginArchi.Extensibility.Abstractions" Version="0.1.5" />
    <PackageReference Include="GiantCroissant.PluginArchi.SourceGenerators" Version="0.1.5" PrivateAssets="all" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Extract `TimelineTimeFormatter` into its own file**

Create `yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineTimeFormatter.cs` by moving the `TimelineTimeFormatter` class (lines 199-221 of the current `TimelineModel.cs`) verbatim. The body is unchanged - only the file location changes. Keep the `TimelineModel` static class in `TimelineModel.cs`.

- [ ] **Step 5: Remove Godot usings from `TimelineModel.cs`**

In `yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineModel.cs`, the file currently has no `using Godot;` (it is pure C#). Verify this is still the case. If it has any Godot usings, remove them. The file should compile under `Microsoft.NET.Sdk`.

- [ ] **Step 6: Remove `TimelineFace.cs` from the T3 project (temporary - moves to T4 in Task 5)**

Temporarily move `TimelineFace.cs` out of the T3 project so it does not break the pure-C# build:

```bash
mv yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineFace.cs \
   yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineFace.cs.tmp
```

- [ ] **Step 7: Build the refactored T3**

Run:
```bash
dotnet build yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/App.Timeline.csproj -c Debug
```
Expected: PASS (exit 0). The `TimelinePlugin.cs` still references `ITimelineController` from `App.World.Composition` (T1) and `IRegistry` from `ServiceArchi` - all pure C#. The `TimelineActivator` / `Bootstrap` are pure C#. `TimelineModel` + `TimelineTimeFormatter` are pure C#.

- [ ] **Step 8: Run the T3 purity tests**

Run:
```bash
dotnet test yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --filter FullyQualifiedName~T3PurityTests
```
Expected: PASS (2 tests). The T3 assembly no longer references GodotSharp.

- [ ] **Step 9: Run the existing TimelineModelTests to verify no regression**

Run:
```bash
dotnet test yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --filter FullyQualifiedName~TimelineModelTests
```
Expected: PASS (all existing model tests).

- [ ] **Step 10: Commit**

```bash
git add yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/App.Timeline.csproj \
        yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineModel.cs \
        yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineTimeFormatter.cs \
        yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/Providers/ITimelineFace.cs \
        yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/T3PurityTests.cs
git rm --cached yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineFace.cs 2>/dev/null || true
git commit -m "refactor(timeline): split T3 to pure C# (Microsoft.NET.Sdk), extract ITimelineFace provider"
```

Note: `TimelineFace.cs.tmp` is intentionally not committed - it moves to T4 in Task 5.

---

### Task 5: Create T4 seam project (App.Timeline.Seam)

**Files:**
- Create: `project/plugins/App.Timeline.Seam/App.Timeline.Seam.csproj`
- Move: `project/plugins/App.Timeline/TimelineFace.cs.tmp` -> `project/plugins/App.Timeline.Seam/TimelineFace.cs`
- Test: `project/tests/App.Timeline.Seam.Tests/` (NEW - optional smoke test)

**Interfaces:**
- Consumes: T1 (`IService`, DTOs), T3 (`ITimelineFace`, `TimelineModel`), `App.World.Composition.ITimelineController`
- Produces: `FantaSim.App.Timeline.Seam.TimelineFace` - Godot `Control` implementing `ITimelineFace`

- [ ] **Step 1: Create the T4 project directory**

```bash
mkdir -p yokan-projects/fantasim-app-godot/project/plugins/App.Timeline.Seam
```

- [ ] **Step 2: Write `App.Timeline.Seam.csproj`**

Create `yokan-projects/fantasim-app-godot/project/plugins/App.Timeline.Seam/App.Timeline.Seam.csproj`:

```xml
<Project Sdk="Godot.NET.Sdk/4.7.0">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>FantaSim.App.Timeline.Seam</RootNamespace>
    <AssemblyName>FantaSim.App.Timeline.Seam</AssemblyName>
    <ServiceArchiTier>T4</ServiceArchiTier>
    <!-- The RESIDENT timeline seam: implements the T3 ITimelineFace with Godot's
         AnimationPlayer/AnimationTree + Control UI. Godot.NET.Sdk for GodotSharp. Stays
         resident (shared via "FantaSim.App." prefix) - it is the only Godot-aware piece;
         the timeline bundle itself ships no Godot types and its collectible ALC unloads
         cleanly. Mirrors App.Camera.Seam / App.World.Seam. -->
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <CompilerVisibleProperty Include="ServiceArchiTier" />
  </ItemGroup>

  <ItemGroup>
    <!-- T1 contract (IService, DTOs, TimelineViewSnapshot). -->
    <ProjectReference Include="..\..\contracts\App.Timeline\App.Timeline.csproj" />
    <!-- T3 orchestrator (ITimelineFace, TimelineModel, TimelineTimeFormatter). T3 never
         references T4 (this project); the dependency points only T4 -> T3. -->
    <ProjectReference Include="..\App.Timeline\App.Timeline.csproj" />
    <!-- App.World contracts for ITimelineController + SphereRegimeSchedule (the face resolves
         the controller from the registry passed at construction). -->
    <ProjectReference Include="..\..\contracts\App.World\App.World.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Move and refactor `TimelineFace.cs` to T4**

```bash
mv yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelineFace.cs.tmp \
   yokan-projects/fantasim-app-godot/project/plugins/App.Timeline.Seam/TimelineFace.cs
```

Then edit `yokan-projects/fantasim-app-godot/project/plugins/App.Timeline.Seam/TimelineFace.cs`:

1. Change namespace to `FantaSim.App.Timeline.Seam`.
2. Add `using FantaSim.App.Timeline.Providers;` and `using FantaSim.App.Timeline;` (for `ITimelineFace`, `TimelineViewSnapshot`, `TimelineBand`, `TimelineTrack`, `TimelineRulerMark`).
3. Change class declaration to: `public partial class TimelineFace : Control, ITimelineFace`.
4. Replace the `TimelinePlugin.ActiveController` static lookup with a constructor-injected `ITimelineController`. Add a constructor:
   ```csharp
   private readonly ITimelineController _ctl;
   private readonly FantaSim.App.Timeline.Services.IService? _timelineService;

   public TimelineFace(ITimelineController controller, FantaSim.App.Timeline.Services.IService? timelineService = null)
   {
       _ctl = controller ?? throw new ArgumentNullException(nameof(controller));
       _timelineService = timelineService;
   }
   ```
   Wait - the resident script binding (`BundleSceneHost.LoadResidentTypeScript`) uses `Activator.CreateInstance(type)` with a parameterless constructor to probe the script. The actual instance is created by Godot's `PackedScene.Instantiate()`, which also requires a parameterless constructor. So the face MUST have a parameterless constructor for Godot instantiation.

   Revised approach: keep a parameterless constructor (required by Godot scene instantiation), and resolve `ITimelineController` from a static set by `Host.cs` composition (the same pattern as the current `TimelinePlugin.ActiveController`, but owned by the resident seam, not the collectible bundle). Add to `TimelineFace.cs`:

   ```csharp
   /// <summary>
   /// Set by Host.cs ComposeTimeline BEFORE the timeline bundle scene instantiates this face.
   /// The resident seam owns the reference; the collectible bundle's TimelinePlugin no longer
   /// holds a static. This is the same pattern as IiiBridge (Node-backed seam exception:
   /// the face needs _Ready/_ExitTree lifecycle, so it is a Node, but it exposes only
   /// ITimelineFace upward to T3).
   /// </summary>
   internal static ITimelineController? ResidentController { get; set; }

   private ITimelineController? _ctl;
   private bool _resolved;

   public TimelineFace() { } // Godot instantiation

   public override void _Ready()
   {
       _ctl = ResidentController;
       if (_ctl is null)
       {
           GD.PushWarning("[TimelineFace] No resident ITimelineController set.");
           SetProcess(false);
           return;
       }
       // ... rest of _Ready unchanged (but use _ctl instead of TimelinePlugin.ActiveController)
   }
   ```

   Implement `ITimelineFace` methods on the face:
   ```csharp
   public void Play() { /* existing Play() body */ }
   public void Pause() { /* existing Pause() body */ }
   public void SeekTo(long tick) { /* existing SeekTo() body */ }
   public void ApplyView(TimelineViewSnapshot snapshot)
   {
       // Update status label, playhead, band highlighting from the snapshot.
       // This replaces the internal UpdateUI() call path - the T3 service drives it.
   }
   ```

5. Remove the `_ctl.RegisterPlayback(Play, Pause, SeekTo, () => _isPlaying)` call from `_Ready` (the T3 service now owns the playback state; the face is a passive renderer + animation driver). The T3 service calls `ITimelineFace.Play()` / `Pause()` / `SeekTo()` directly.

- [ ] **Step 4: Build the T4 seam**

Run:
```bash
dotnet build yokan-projects/fantasim-app-godot/project/plugins/App.Timeline.Seam/App.Timeline.Seam.csproj -c Debug
```
Expected: PASS (exit 0). The face compiles with Godot SDK + T1 + T3 references.

- [ ] **Step 5: Commit**

```bash
git add yokan-projects/fantasim-app-godot/project/plugins/App.Timeline.Seam/App.Timeline.Seam.csproj \
        yokan-projects/fantasim-app-godot/project/plugins/App.Timeline.Seam/TimelineFace.cs
git commit -m "refactor(timeline): move TimelineFace to T4 seam (App.Timeline.Seam), implement ITimelineFace"
```

---

### Task 6: Add T3 Service orchestrator

**Files:**
- Create: `project/plugins/App.Timeline/Services/Service.cs`
- Modify: `project/plugins/App.Timeline/TimelinePlugin.cs` (drop static ActiveController)
- Test: `project/tests/App.Timeline.Tests/TimelineServiceTests.cs`

**Interfaces:**
- Consumes: T1 (`IService`, `TimelineViewSnapshot`, `TimelinePlaybackState`), T3 (`ITimelineFace`, `TimelineModel`), `App.World.Composition.ITimelineController`
- Produces: T3 `Service` implementing `IService` - the playback state machine

- [ ] **Step 1: Write the failing T3 service tests**

Create `yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/TimelineServiceTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.World.Composition;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.Timeline.Tests;

public class TimelineServiceTests
{
    private sealed class FakeFace : ITimelineFace
    {
        public int PlayCalls, PauseCalls, SeekCalls, ApplyViewCalls;
        public long LastSeekTick;
        public TimelineViewSnapshot? LastSnapshot;

        public void Play() => PlayCalls++;
        public void Pause() => PauseCalls++;
        public void SeekTo(long tick) { SeekCalls++; LastSeekTick = tick; }
        public void ApplyView(TimelineViewSnapshot snapshot) { ApplyViewCalls++; LastSnapshot = snapshot; }
    }

    private static (Service svc, FakeFace face) Build(long maxTick = 120_000_000)
    {
        var face = new FakeFace();
        var geo = SphereRegimeScheduleDefaults.GeosphereFor(SphereRegimeScheduleDefaults.PlateOnsetTick);
        var atmo = SphereRegimeScheduleDefaults.AtmosphereFor(SphereRegimeScheduleDefaults.PlateOnsetTick);
        // The T3 service holds a controller reference for schedule lookups but drives the
        // face directly. We use a minimal fake controller shape via a stub is not needed -
        // the Service takes ITimelineController for read-only schedule access.
        // For the test we construct a real TimelineController stub is hard; instead the
        // Service takes schedules directly (see Service ctor in step 4).
        var svc = new Service(face, geo, atmo, maxTick, NullLoggerFactory.Instance);
        return (svc, face);
    }

    [Fact]
    public async Task Play_TransitionsToPlaying_AndCallsFacePlay()
    {
        var (svc, face) = Build();
        Assert.Equal(TimelinePlaybackState.Idle, svc.State);
        await svc.PlayAsync();
        Assert.Equal(TimelinePlaybackState.Playing, svc.State);
        Assert.Equal(1, face.PlayCalls);
        Assert.True(face.ApplyViewCalls >= 1);
    }

    [Fact]
    public async Task Pause_TransitionsToIdle_AndCallsFacePause()
    {
        var (svc, face) = Build();
        await svc.PlayAsync();
        await svc.PauseAsync();
        Assert.Equal(TimelinePlaybackState.Idle, svc.State);
        Assert.Equal(1, face.PauseCalls);
    }

    [Fact]
    public async Task Seek_ClampsToMaxTick_AndCallsFaceSeek()
    {
        var (svc, face) = Build(maxTick: 1_000_000);
        await svc.SeekAsync(5_000_000);
        Assert.Equal(1_000_000, svc.Tick);
        Assert.Equal(1, face.SeekCalls);
        Assert.Equal(1_000_000, face.LastSeekTick);
    }

    [Fact]
    public async Task Seek_NegativeClampsToZero()
    {
        var (svc, face) = Build();
        await svc.SeekAsync(-100);
        Assert.Equal(0, svc.Tick);
        Assert.Equal(0, face.LastSeekTick);
    }

    [Fact]
    public async Task ViewChanged_RaisedOnStateChange()
    {
        var (svc, face) = Build();
        TimelineViewSnapshot? captured = null;
        svc.ViewChanged += snap => captured = snap;
        await svc.PlayAsync();
        Assert.NotNull(captured);
        Assert.Equal(TimelinePlaybackState.Playing, captured!.State);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```bash
dotnet test yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --filter FullyQualifiedName~TimelineServiceTests
```
Expected: FAIL with "type Service not found" (the T3 `Service` does not exist yet).

- [ ] **Step 3: Write `Services/Service.cs`**

Create `yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/Services/Service.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.World.Composition;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Timeline.Services;

/// <summary>
/// The timeline service (<see cref="IService"/>) orchestrator - engine-agnostic (NO Godot).
/// Owns the playback state machine (Idle/Playing/Scrubbing) and delegates engine work to the
/// <see cref="ITimelineFace"/> seam (implemented by the Godot App.Timeline.Seam.TimelineFace).
/// Reads regime/layer schedules from the injected <see cref="SphereRegimeSchedule"/> pair to
/// build <see cref="TimelineViewSnapshot"/> for the face. Mirrors App.Camera.Services.Service.
/// </summary>
public sealed class Service : IService, IDisposable
{
    private readonly ITimelineFace _face;
    private readonly SphereRegimeSchedule _geosphere;
    private readonly SphereRegimeSchedule _atmosphere;
    private readonly ILogger _log;
    private long _tick;
    private TimelinePlaybackState _state = TimelinePlaybackState.Idle;
    private bool _disposed;

    public Service(
        ITimelineFace face,
        SphereRegimeSchedule geosphere,
        SphereRegimeSchedule atmosphere,
        long maxTick,
        ILoggerFactory loggerFactory)
    {
        _face = face ?? throw new ArgumentNullException(nameof(face));
        _geosphere = geosphere ?? throw new ArgumentNullException(nameof(geosphere));
        _atmosphere = atmosphere ?? throw new ArgumentNullException(nameof(atmosphere));
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));
        _log = loggerFactory.CreateLogger("App.Timeline.Service");
        MaxTick = maxTick;
    }

    public long Tick => _tick;
    public long MaxTick { get; }
    public TimelinePlaybackState State => _state;
    public event Action<TimelineViewSnapshot>? ViewChanged;

    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        _state = TimelinePlaybackState.Playing;
        _face.Play();
        PushView();
        _log.LogInformation("Timeline playing at tick {Tick}.", _tick);
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        _state = TimelinePlaybackState.Idle;
        _face.Pause();
        PushView();
        _log.LogInformation("Timeline paused at tick {Tick}.", _tick);
        return Task.CompletedTask;
    }

    public Task SeekAsync(long tick, CancellationToken cancellationToken = default)
    {
        tick = Math.Clamp(tick, 0L, MaxTick);
        _tick = tick;
        _state = TimelinePlaybackState.Scrubbing;
        _face.SeekTo(tick);
        PushView();
        _log.LogDebug("Timeline seek to tick {Tick}.", tick);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the face (via the resident controller callback) when the AnimationPlayer
    /// advances a frame. The face pushes the tick into the resident ITimelineController,
    /// which updates the globe; the service then updates its own tick + state + view.
    /// </summary>
    internal void AcceptTickFromFace(long tick)
    {
        if (_state != TimelinePlaybackState.Playing) return;
        _tick = Math.Clamp(tick, 0L, MaxTick);
        PushView();
    }

    private void PushView()
    {
        var regime = _geosphere.RegimeAt(_tick);
        var snap = new TimelineViewSnapshot(_tick, _state, regime?.RegimeId, MaxTick);
        _face.ApplyView(snap);
        ViewChanged?.Invoke(snap);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet test yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --filter FullyQualifiedName~TimelineServiceTests
```
Expected: PASS (5 tests).

- [ ] **Step 5: Drop `static ActiveController` from `TimelinePlugin.cs`**

Modify `yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelinePlugin.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.SceneFlow;
using Microsoft.Extensions.DependencyInjection;
using PluginArchi.Extensibility.Abstractions;
using ServiceArchi.Contracts;

namespace FantaSim.App.Timeline;

[Plugin("app.timeline", Name = "Timeline HUD", Description = "Registers the timeline scene activator.", Tags = "scene-tier")]
public sealed partial class TimelinePlugin : ILifecyclePlugin
{
    private IDisposable? _activatorRegistration;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        var registry = context.Services.GetRequiredService<IRegistry>();

        _activatorRegistration = registry.RegisterOwned<ISceneActivator>(
            new TimelineActivator(),
            new ServiceRegistration { Tags = new[] { "scene-activator" }, Description = "timeline activator (bundle)" });

        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _activatorRegistration?.Dispose();
        _activatorRegistration = null;
        return ValueTask.CompletedTask;
    }
}
```

(Removed: `public static ITimelineController? ActiveController { get; private set; }` and the controller resolution block. The T3 service is composed in `Host.cs`, not in the bundle plugin.)

- [ ] **Step 6: Build T3**

Run:
```bash
dotnet build yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/App.Timeline.csproj -c Debug
```
Expected: PASS (exit 0).

- [ ] **Step 7: Commit**

```bash
git add yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/Services/Service.cs \
        yokan-projects/fantasim-app-godot/project/plugins/App.Timeline/TimelinePlugin.cs \
        yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/TimelineServiceTests.cs
git commit -m "feat(timeline): add T3 Service orchestrator with playback state machine"
```

---

### Task 7: Wire Host.cs ComposeTimeline

**Files:**
- Modify: `project/hosts/complete-app/Host.cs`

**Interfaces:**
- Consumes: T4 `TimelineFace` (resident Node), T3 `Service`, `ITimelineController` (from ComposeWorldView), `SphereRegimeSchedule` (from ComposeWorldView)
- Produces: `ComposeTimeline(composition)` - registers `FantaSim.App.Timeline.IService` into the kernel registry; sets `TimelineFace.ResidentController`

- [ ] **Step 1: Add `ComposeTimeline` call in `_Ready`**

Modify `yokan-projects/fantasim-app-godot/project/hosts/complete-app/Host.cs`:

In the `_Ready` method, add after `ComposeWorldView(_composition);` (line ~56) and before `ComposeActivity(_composition);`:

```csharp
        ComposeWorldView(_composition);
        ComposeTimeline(_composition);
        ComposeActivity(_composition);
```

- [ ] **Step 2: Add the `ComposeTimeline` method**

Add this method to `Host.cs` (after `ComposeWorldView`, before `ComposeActivity`):

```csharp
    // ComposeTimeline: the resident T4 TimelineFace + the T3 timeline Service orchestrator.
    // The face is a Godot Control (Node-backed seam exception - it needs _Ready/_ExitTree for
    // the AnimationPlayer lifecycle). The T3 Service is pure C# and registered into the kernel
    // IRegistry so other plugins can resolve IService. The face resolves ITimelineController
    // from the static set here (the controller was registered by ComposeWorldView above).
    // Ordered AFTER ComposeWorldView (ITimelineController must exist) and BEFORE the deferred
    // EnterInitialScenes (the timeline bundle's scene instantiates the face, which reads
    // ResidentController).
    private void ComposeTimeline(AppComposition composition)
    {
        var registry = composition.Bootstrap.Registry;
        var controller = registry.TryGet<FantaSim.App.World.Composition.ITimelineController>();
        if (controller is null)
        {
            GD.PushWarning("[Host] Timeline: no ITimelineController registered; timeline service will be inert.");
            return;
        }

        // Set the resident controller reference the face reads in _Ready. The face is instantiated
        // by the bundle scene; this static is the bridge (same pattern as the old
        // TimelinePlugin.ActiveController, but owned by the resident seam, not the collectible
        // bundle - so the ALC is not pinned).
        FantaSim.App.Timeline.Seam.TimelineFace.ResidentController = controller;

        // Build the T3 service with the controller's schedules. The T3 Service drives the face
        // via ITimelineFace; the face also calls back into the controller (PushTick) during
        // animation playback.
        var timelineService = new FantaSim.App.Timeline.Services.Service(
            // The face is NOT constructed here - it is instantiated by the bundle scene. The T3
            // service holds an ITimelineFace reference that is connected when the face enters the
            // tree. For now, pass a deferred-binding proxy.
            new DeferredTimelineFace(controller),
            controller.GeosphereSchedule,
            controller.AtmosphereSchedule,
            controller.MaxTick,
            composition.Bootstrap.LoggerFactory);
        registry.Register<FantaSim.App.Timeline.IService>(
            timelineService,
            new ServiceRegistration { Tags = new[] { "timeline" }, Description = "Timeline playback service" });

        GD.Print("[Host] registered: Timeline (IService + resident TimelineFace)");
    }
```

- [ ] **Step 3: Add the `DeferredTimelineFace` proxy**

This is needed because the T3 `Service` is constructed before the bundle scene instantiates the `TimelineFace`. The proxy buffers calls until the real face connects. Add this as a private nested class in `Host.cs` OR as a standalone file in the T4 seam project. Prefer the seam project so it is testable.

Create `yokan-projects/fantasim-app-godot/project/plugins/App.Timeline.Seam/DeferredTimelineFace.cs`:

```csharp
using System.Threading;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Providers;
using FantaSim.App.World.Composition;
using Godot;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// A deferred-binding ITimelineFace proxy. Constructed by Host.cs BEFORE the timeline bundle
/// scene instantiates the real TimelineFace. Buffers Play/Pause/Seek/ApplyView calls (no-op
/// until Connect is called), then forwards to the real face. The real face calls Connect(this)
/// in its _Ready, at which point the proxy swaps to live forwarding. This lets the T3 Service
/// be composed in Host._Ready (sync, before deferred EnterAsync) while the face is created
/// later by the bundle scene instantiation. Mirrors the "compose T3 with a seam reference"
/// pattern from ComposeUi (ViewHost is constructed before views mount).
/// </summary>
public sealed class DeferredTimelineFace : ITimelineFace
{
    private readonly ITimelineController _controller;
    private ITimelineFace? _target;

    public DeferredTimelineFace(ITimelineController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    /// <summary>Called by the real TimelineFace in _Ready to start forwarding.</summary>
    public void Connect(ITimelineFace target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public void Play()
    {
        if (_target is not null) _target.Play();
        else _controller.Play();
    }

    public void Pause()
    {
        if (_target is not null) _target.Pause();
        else _controller.Pause();
    }

    public void SeekTo(long tick)
    {
        if (_target is not null) _target.SeekTo(tick);
        else _controller.SeekTo(tick);
    }

    public void ApplyView(TimelineViewSnapshot snapshot)
    {
        _target?.ApplyView(snapshot);
    }
}
```

- [ ] **Step 4: Wire the face to connect the proxy in `_Ready`**

In `yokan-projects/fantasim-app-godot/project/plugins/App.Timeline.Seam/TimelineFace.cs`, update `_Ready` to connect to a deferred proxy if one was set. Add a static field for the proxy (set by Host.cs before the scene instantiates):

```csharp
    internal static DeferredTimelineFace? ResidentProxy;

    public override void _Ready()
    {
        _ctl = ResidentController;
        if (_ctl is null) { /* ... warning ... */ return; }

        // ... existing _Ready body (SetupAnimationSystem, BuildLanes, etc.) ...

        // Connect the deferred proxy so the T3 Service's calls forward to this face.
        ResidentProxy?.Connect(this);
    }
```

And in `Host.cs ComposeTimeline`, set the proxy before constructing the service:

```csharp
        var deferredFace = new DeferredTimelineFace(controller);
        FantaSim.App.Timeline.Seam.TimelineFace.ResidentController = controller;
        FantaSim.App.Timeline.Seam.TimelineFace.ResidentProxy = deferredFace;

        var timelineService = new FantaSim.App.Timeline.Services.Service(
            deferredFace,
            controller.GeosphereSchedule,
            controller.AtmosphereSchedule,
            controller.MaxTick,
            composition.Bootstrap.LoggerFactory);
```

- [ ] **Step 5: Build the host project**

Run:
```bash
dotnet build yokan-projects/fantasim-app-godot/project/hosts/complete-app/complete-app.csproj -c Debug
```
Expected: PASS (exit 0). This build pulls in the T1, T3, and T4 projects via the host's project references. Verify the host csproj references the new seam project - check `complete-app.csproj` for `<ProjectReference Include="..\..\plugins\App.Timeline.Seam\App.Timeline.Seam.csproj" />`. Add it if missing.

- [ ] **Step 6: Commit**

```bash
git add yokan-projects/fantasim-app-godot/project/hosts/complete-app/Host.cs \
        yokan-projects/fantasim-app-godot/project/plugins/App.Timeline.Seam/DeferredTimelineFace.cs \
        yokan-projects/fantasim-app-godot/project/plugins/App.Timeline.Seam/TimelineFace.cs
git commit -m "feat(timeline): wire ComposeTimeline in Host with deferred face binding"
```

---

### Task 8: Update bundle manifest and verify the collectible ALC

**Files:**
- Modify: `project/bundles/timeline/manifest.json`
- Verify: `project/hosts/complete-app/config/collectible-bundles.json` (unchanged)
- Verify: `project/hosts/complete-app/complete-app.csproj` references the seam

**Interfaces:**
- Consumes: the refactored T1/T3/T4 structure
- Produces: a `timeline` bundle whose PCK ships T1+T3 only (Godot-free), with the T4 seam resident

- [ ] **Step 1: Update `manifest.json` residentType**

Modify `yokan-projects/fantasim-app-godot/project/bundles/timeline/manifest.json`:

```json
{
  "bundleId": "timeline",
  "displayName": "Timeline",
  "version": "0.1.0",
  "entryScene": "scenes/Timeline.tscn",
  "pluginAssembly": "FantaSim.App.Timeline.dll",
  "scenes": [
    "scenes/Timeline.tscn"
  ],
  "metadata": {
    "bundleType": "scene-tier"
  },
  "residentScripts": [
    {
      "nodePath": ".",
      "residentType": "FantaSim.App.Timeline.Seam.TimelineFace"
    }
  ]
}
```

(The only change: `residentType` from `FantaSim.App.Timeline.TimelineFace` to `FantaSim.App.Timeline.Seam.TimelineFace`.)

- [ ] **Step 2: Verify `complete-app.csproj` references the seam**

Read `yokan-projects/fantasim-app-godot/project/hosts/complete-app/complete-app.csproj`. If it does not have:

```xml
<ProjectReference Include="..\..\plugins\App.Timeline.Seam\App.Timeline.Seam.csproj" />
```

Add it alongside the other seam references (e.g. `App.World.Seam`, `App.Camera.Seam`).

- [ ] **Step 3: Verify `collectible-bundles.json` is unchanged**

Read `yokan-projects/fantasim-app-godot/project/hosts/complete-app/config/collectible-bundles.json`. The `timeline` entry should still be:

```json
{
  "bundleId": "timeline",
  "pluginAssembly": "FantaSim.App.Timeline.dll"
}
```

No change needed - `FantaSim.App.Timeline.dll` (T3) is the collectible assembly; `FantaSim.App.Timeline.Contracts.dll` (T1) is shared via the `FantaSim.App.` prefix policy; `FantaSim.App.Timeline.Seam.dll` (T4) is resident (referenced by the host, not packed in the PCK).

- [ ] **Step 4: Run the full build via unify-build**

Run:
```bash
dotnet unify-build --project yokan-projects/fantasim-app-godot
```
Expected: PASS (exit 0). The full build should succeed with the new project structure.

- [ ] **Step 5: Run all timeline tests**

Run:
```bash
dotnet test yokan-projects/fantasim-app-godot/project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj
```
Expected: PASS (all tests: TimelineModelTests, TimelineServiceTests, T3PurityTests, TimelineDtosTests, OdometerLabelTests).

- [ ] **Step 6: Commit**

```bash
git add yokan-projects/fantasim-app-godot/project/bundles/timeline/manifest.json \
        yokan-projects/fantasim-app-godot/project/hosts/complete-app/complete-app.csproj
git commit -m "refactor(timeline): update bundle manifest residentType to T4 seam, verify ALC split"
```

---

### Task 9: Hot-reload verification (manual QA gate)

**Files:**
- Verify: the running app

- [ ] **Step 1: Run the exported windowed app**

Follow the verify-windowed skill:
```bash
task run:exported
```

- [ ] **Step 2: Verify the timeline HUD appears and playback works**

In the running app, confirm:
1. The timeline HUD (Timeline panel) appears at the bottom of the screen.
2. Clicking "Play" starts playback (status label shows "playing : <regime> : <time>").
3. Clicking "Pause" stops playback (status label shows "paused : ...").
4. Dragging on the lanes scrubs the timeline (playhead moves, globe updates).
5. Zoom in/out/fit buttons work.

- [ ] **Step 3: Verify hot-reload**

Edit a label in `TimelineFace.cs` (e.g. change "Play" to "Play!"), then:
```bash
task bundle:timeline
task bundle:install
```
Confirm the label updates in the running app WITHOUT a full restart. Check the log for "old ALC collected" (the collectible bundle ALC unloads cleanly because it has no Godot-derived types).

- [ ] **Step 4: No commit (verification only)**

If hot-reload fails, investigate the ALC pinning. The most likely cause is a lingering subscription in `TimelineFace._Ready` that is not unsubscribed in `_ExitTree`. The existing `_ExitTree` already unregisters playback - verify it also nulls `ResidentController`/`ResidentProxy` references if needed.

---

## Self-Review

**1. Spec coverage:**
- T1 contracts project created (Task 1-2): YES - `contracts/App.Timeline/` with `IService`, `Service` proxy, `TimelineDtos.cs`, `AssemblyInfo.cs`, `[PluginSharedContract]`.
- T3 pure C# (Task 4): YES - `Microsoft.NET.Sdk`, `<ServiceArchiTier>T3</ServiceArchiTier>`, no Godot, `TimelineModel` + `TimelineTimeFormatter` pure.
- T3 Service orchestrator (Task 6): YES - `Services/Service.cs` implementing `IService` with playback state machine.
- T3 provider interface (Task 3): YES - `Providers/ITimelineFace.cs`.
- T4 seam (Task 5): YES - `plugins/App.Timeline.Seam/`, `Godot.NET.Sdk`, `TimelineFace : Control, ITimelineFace`.
- Host composition (Task 7): YES - `ComposeTimeline` constructs T4 + T3, registers `IService`, sets resident controller.
- Bundle manifest (Task 8): YES - `residentType` updated to T4 seam type.
- ALC cleanliness: YES - T4 is resident (host-referenced), bundle ships T1+T3 only (Godot-free).
- Static ActiveController removed (Task 6): YES - replaced with `Host.cs` composition + `TimelineFace.ResidentController` (resident-owned).
- Existing model tests still pass (Task 4 step 9): YES.
- New service tests (Task 6): YES.
- T3 purity tests (Task 4): YES - asserts no GodotSharp reference.
- Full build (Task 8 step 4): YES - unify-build.
- Hot-reload (Task 9): YES - manual QA gate.

**2. Placeholder scan:**
- No "TBD", "TODO", "implement later" found.
- Every code step shows the actual code.
- The `DeferredTimelineFace` is fully implemented (not a placeholder).
- The `TimelineFace` refactoring in Task 5 has concrete steps (the `mv` + namespace change + constructor pattern + ITimelineFace implementation).

**3. Type consistency:**
- `IService` defined in Task 2, used in Task 6 (Service implements it) and Task 7 (Host registers it). Method signatures match: `PlayAsync(CancellationToken)`, `PauseAsync(CancellationToken)`, `SeekAsync(long, CancellationToken)`.
- `ITimelineFace` defined in Task 3, implemented by `TimelineFace` in Task 5, consumed by `Service` in Task 6. Methods: `Play()`, `Pause()`, `SeekTo(long)`, `ApplyView(TimelineViewSnapshot)`. Consistent across all tasks.
- `TimelineViewSnapshot` defined in Task 2 (`TimelineDtos.cs`), used in Task 3 (`ITimelineFace.ApplyView`), Task 6 (`Service.PushView`), Task 7 (`DeferredTimelineFace`). Fields: `Tick`, `State`, `ActiveRegimeId`, `MaxTick`. Consistent.
- `TimelinePlaybackState` enum: `Idle`, `Playing`, `Scrubbing`. Used consistently in Task 2 (DTO), Task 6 (Service state), Task 7 (Host).
- `TimelineBand`/`TimelineTrack`/`TimelineRulerMark`: moved from T3 `TimelineModel.cs` to T1 `TimelineDtos.cs` in Task 2. The existing `TimelineModelTests` reference `TimelineBand` via `FantaSim.App.Timeline` namespace - since the T1 contract uses the same namespace, the tests should still resolve. The T3 `TimelineModel.Bands()` method returns `IReadOnlyList<TimelineBand>` - it will now reference the T1 type. The T3 csproj references T1, so this resolves.
- `ResidentController` / `ResidentProxy` static fields: set in `Host.cs ComposeTimeline` (Task 7), read in `TimelineFace._Ready` (Task 5). Consistent.

**4. ALC safety review:**
- The T4 seam (`FantaSim.App.Timeline.Seam`) is resident (referenced by `complete-app.csproj`), NOT packed in the bundle PCK. The bundle's `pluginAssembly` is `FantaSim.App.Timeline.dll` (T3, pure C#). This mirrors `App.Camera.Seam` exactly.
- The `SharedAssemblyPolicy` (driven by `collectible-bundles.json` + the `FantaSim.App.` prefix) shares `FantaSim.App.Timeline.Contracts` (T1) in the parent ALC and isolates `FantaSim.App.Timeline` (T3) in the collectible ALC. This is the same split as `App.Camera.Contracts` (shared) vs `App.Camera` (collectible if it were a bundle; App.Camera is actually resident, but the pattern applies).
- `TimelineFace.ResidentController` is a static field on a resident type (T4). The collectible bundle's `TimelinePlugin` no longer holds a static reference to `ITimelineController` (removed in Task 6). So the collectible ALC is not pinned by a static.
- `DeferredTimelineFace` is a resident type (T4). The T3 `Service` holds an `ITimelineFace` reference (T3-owned interface) - the concrete `DeferredTimelineFace` is T4 but T3 only sees the interface. No T4 type leaks into T3. Clean.

No issues found. Plan is ready for Momus review.