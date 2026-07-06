# App Regimes + Onset Wiring + Timeline-Face (Plan 4) Implementation Plan

> **AUDIT (2026-07-06, code-verified):** COMPLETED (merged @03bd394); its GlobeView/AnimationPlayer transport was since replaced by the tscn timeline. _(See the authority index in `vault/README.md`.)_


> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In the rebuilt Godot app, port the 3 geosphere regimes + atmosphere coupling so plate-onset is DERIVED from a hydration curve, wire the onset tick to the engine's `LidFractureAtOnset` (plates born at onset, not Genesis), and add an AnimationPlayer-backed timeline transport that scrubs magma-ocean → stagnant-lid → mobile-plate.

**Architecture:** Additive to the app plus ONE new approved plugin `App.World.Composition` (T3, Godot-free) that holds a curated port of the ref app's regime composition — the regime DTOs (`SphereRegime`/`SphereRegimeSchedule`/`RegimeAt`), the field-composition engine, the 3 geosphere layers (+ synthetic crust) and 2 atmosphere layers, the field catalogs, and `SphereRegimeScheduleDefaults` (which computes the causal onset via the `PrimordialAtmosphereSolver` from sub-plan 4.0). The onset roster comes from the engine's `LidFractureAtOnset.Fracture(...)` folded through `PlateTopologyMaterializer`. The timeline-face is a focused `AnimationPlayer`/`AnimationTree` transport in the T4 `App.World.Seam`, threading `RegimeAt`/`ShowsPlateFeatures` into the existing `GlobeView`.

**Tech Stack:** C# `net8.0`, Godot 4.x (.NET) in the T4 seam only, xUnit, engine via project-references during codev. Depends on **sub-plan 4.0** (atmosphere packages/projects) being merged.

## Global Constraints

- **Additive + exactly ONE new project** (`plugins/App.World.Composition`, user-approved). Pure regime/composition DTOs go into the EXISTING `contracts/App.World`. No other new projects without fresh approval.
- **Curated port, right-sized:** port from `ref-projects/fantasim-app-godot` (read-only — never build/write there) ONLY the regime spine. **DEFER** (do NOT port): `BodyFormationProducer`, `BodyFormationScheduleDefaults`, `ElementGeologyTagProducer`, `LayerStackManifest`(+`Loader`), `SphereRegimeScheduleLoader` (JSON — code-seeded defaults suffice for the scrub). The ported composition field system is self-contained; it parallels the engine's `World.Fields` — do NOT attempt to merge them in this plan (note the duplication as a follow-up).
- **Engine consumption = projectref-for-dev** (the standing doctrine): build with `UseProjectReferences=true` referencing the live `fantasim-world` projects. Packaging the full engine surface + bumping package pins is a RELEASE-GATE follow-up (not in this plan).
- **Godot only in the T4 seam** (`App.World.Seam`); T3 (`App.World`, `App.World.Composition`) stays Godot-free. Render seam uses the existing `GlobeView`/shader path; do not introduce GPU-compute.
- **Reuse Unify** (`UnifyMaths`/`UnifyGeometry`/`UnifyCell`) — do not hand-roll vector/spherical/tessellation primitives.
- **Determinism:** the onset roster is a pure function of `(world seed, onsetTick, tessellation)`; same inputs → identical fold. No `System.Random` in composition/wiring.
- **Verify in the EXPORTED WINDOWED app** (project rule — no headless smoke tests): Task 5 scrubs the real exported build.
- **Build:** `dotnet build project/<App>.sln -p:UseProjectReferences=true` from `yokan-projects/fantasim-app-godot/`. **Commits:** conventional-commit, path-scoped `git add`, end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Branch: `feat/app-regimes-onset-timeline`.

**Curated source map** (ref = `/Users/apprenticegc/Work/lunar-horse/ref-projects/fantasim-app-godot/project`):

| Ref source | App target | Notes |
| --- | --- | --- |
| `contracts/App.World/Composition/{Fields,ILayer,FieldValues,SphereRegimes}.cs` | `contracts/App.World/Composition/` (existing csproj) | pure DTOs |
| `plugins/App.World/Composition/{FieldComposer,FieldValueResolver}.cs` | `plugins/App.World.Composition/` (NEW) | field DAG engine |
| `plugins/App.World/Composition/{GeosphereFieldCatalog,AtmosphereFieldCatalog,GeosphereFieldMath}.cs` | `plugins/App.World.Composition/` | catalogs + math util |
| `plugins/App.World/Composition/{GeosphereMagmaOceanLayer,GeosphereStagnantLidLayer,GeospherePlateLayer,SyntheticCrustLayer}.cs` | `plugins/App.World.Composition/` | 3 geosphere + crust |
| `plugins/App.World/Composition/{AtmosphereBulkLayer,AtmosphereCoupledLayer}.cs` | `plugins/App.World.Composition/` | atmosphere (uses 4.0 packages) |
| `plugins/App.World/Composition/SphereRegimeScheduleDefaults.cs` | `plugins/App.World.Composition/` | schedules + onset search |
| `…/{BodyFormation*,ElementGeologyTagProducer,LayerStackManifest*,SphereRegimeScheduleLoader}.cs` | — | **DEFER** |

---

### Task 1: Reconcile engine consumption (projectref-for-dev to the post-Plan-1–3 surface)

The app's codev project-refs point at the PRE-rename engine (`Geosphere.Plate.Topology`, renamed → `…Generation`) and lack the Plan-2/3 + atmosphere surface. Fix and extend them.

**Files:**
- Modify: `project/plugins/App.World/App.World.csproj` (the `UseProjectReferences=='true'` ItemGroup — recon located it ~lines 80–94)
- Modify: `project/Directory.Build.props` (default `UseProjectReferences` — recon ~lines 13–21)
- Modify: `project/Directory.Packages.props` (engine package pins — recon ~lines 36–48; for the release path)

**Interfaces:**
- Produces: an app that compiles against `FantaSim.Geosphere.Plate.Topology` (LidFractureAtOnset in `…Generation`), `FantaSim.Geosphere.Asthenosphere.Convection` (ConvectionFieldGenerator), `FantaSim.Atmosphere.Genesis.Core`/`.Contracts` (4.0).

- [ ] **Step 1: Read the current refs** — open `App.World.csproj` and confirm the `Condition="'$(UseProjectReferences)' == 'true'"` ItemGroup and its current `ProjectReference` entries (the stale `Geosphere.Plate.Topology\Geosphere.Plate.Topology.csproj`).

- [ ] **Step 2: Update the codev project-references** — in that ItemGroup, replace/extend so it references (paths relative to `$(YokanProjectsRoot)\fantasim-world\project`):

```xml
<!-- engine: post-Plan-1–3 + atmosphere (codev) -->
<ProjectReference Include="$(YokanProjectsRoot)\fantasim-world\project\plugins\Geosphere.Plate.Topology.Generation\Geosphere.Plate.Topology.Generation.csproj" />
<ProjectReference Include="$(YokanProjectsRoot)\fantasim-world\project\plugins\Geosphere.Asthenosphere.Convection\Geosphere.Asthenosphere.Convection.csproj" />
<ProjectReference Include="$(YokanProjectsRoot)\fantasim-world\project\contracts\Geosphere.Asthenosphere\Geosphere.Asthenosphere.csproj" />
<ProjectReference Include="$(YokanProjectsRoot)\fantasim-world\project\plugins\Atmosphere.Genesis.Core\Atmosphere.Genesis.Core.csproj" />
<ProjectReference Include="$(YokanProjectsRoot)\fantasim-world\project\contracts\Atmosphere\Atmosphere.csproj" />
<!-- keep existing: Geosphere.Crust, World.Fields(.Core), World.Parameters, World.TruthStream(.Core), Plate.Topology.Materializer + its contracts -->
```

Ensure the materializer + topology contracts are referenced too (the onset fold needs `PlateTopologyMaterializer` + `PlateTopologyState` + the events). Add if missing:
```xml
<ProjectReference Include="$(YokanProjectsRoot)\fantasim-world\project\plugins\Geosphere.Plate.Topology.Materializer\Geosphere.Plate.Topology.Materializer.csproj" />
<ProjectReference Include="$(YokanProjectsRoot)\fantasim-world\project\contracts\Geosphere.Plate.Topology\Geosphere.Plate.Topology.csproj" />
```

- [ ] **Step 3: Default to codev for this branch** — in `Directory.Build.props`, set the default to `true` so the windowed export builds against the live engine:

```xml
<UseProjectReferences Condition="'$(UseProjectReferences)' == ''">true</UseProjectReferences>
```

(Flag in the commit body: this is a dev-branch default; the package-pin/release path is a follow-up.)

- [ ] **Step 4: Record the release-path pins (not built here)** — in `Directory.Packages.props`, bump the engine pins to the new `0.1.x` and add the new package ids (so the `UseProjectReferences=false` path is correct once the engine packs them):

```xml
<PackageVersion Include="GiantCroissant.FantaSim.Geosphere.Plate.Topology.Generation" Version="0.1.x" />
<PackageVersion Include="GiantCroissant.FantaSim.Geosphere.Asthenosphere.Convection" Version="0.1.x" />
<PackageVersion Include="GiantCroissant.FantaSim.Geosphere.Asthenosphere" Version="0.1.x" />
<PackageVersion Include="GiantCroissant.FantaSim.Atmosphere.Genesis.Core" Version="0.1.x" />
<PackageVersion Include="GiantCroissant.FantaSim.Atmosphere.Contracts" Version="0.1.x" />
```

(Use the exact version sub-plan 4.0 Task 4 recorded. These are pins only — the package-mode ItemGroup also needs the matching `PackageReference` lines, but the build gate below uses codev.)

- [ ] **Step 5: Build the solution (codev)**

Run: `dotnet build project/<App>.sln -p:UseProjectReferences=true`
Expected: 0 errors. (Resolves the rename + new refs; no cycle — the engine does not reference the app.)

- [ ] **Step 6: Commit**

```bash
git add project/plugins/App.World/App.World.csproj project/Directory.Build.props project/Directory.Packages.props
git commit -m "build(app): projectref the post-Plan-1-3 engine surface + atmosphere (codev)

Fixes the stale Geosphere.Plate.Topology -> .Generation rename; adds Asthenosphere.Convection,
Atmosphere.Genesis.Core/.Contracts, and the topology materializer for onset folding. Branch default
flipped to UseProjectReferences=true; package-mode bump recorded for the release gate.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `App.World.Composition` plugin — regime spine + layers + onset coupling (curated port)

Create the approved plugin and port the right-sized regime composition. Pure DTOs go into the existing `contracts/App.World`.

**Files:**
- Create: `project/plugins/App.World.Composition/App.World.Composition.csproj`
- Create (contracts, existing csproj): `project/contracts/App.World/Composition/{Fields,ILayer,FieldValues,SphereRegimes}.cs`
- Create (plugin): `project/plugins/App.World.Composition/{FieldComposer,FieldValueResolver,GeosphereFieldCatalog,AtmosphereFieldCatalog,GeosphereFieldMath,GeosphereMagmaOceanLayer,GeosphereStagnantLidLayer,GeospherePlateLayer,SyntheticCrustLayer,AtmosphereBulkLayer,AtmosphereCoupledLayer,SphereRegimeScheduleDefaults}.cs`
- Modify: `project/plugins/App.World/App.World.csproj` (ProjectReference → App.World.Composition)
- Test: `project/tests/App.World.Composition.Tests/...`

**Interfaces:**
- Consumes: `FantaSim.Atmosphere.Genesis.Core.{AtmosphereForcing,PrimordialAtmosphereSolver}`, `FantaSim.Atmosphere.Contracts.{AtmosphereState,IAtmosphereStateSolver}` (sub-plan 4.0); `GeosphereFieldCatalog` field ids.
- Produces (the load-bearing contract other tasks rely on):
  - `FantaSim.App.World.Composition.SphereRegime(string RegimeId, long StartTick, long EndTick, IReadOnlyList<LayerId> ActiveLayers, string? DefaultColorByField = null, bool ShowsPlateFeatures = true)` with `bool Contains(long)` and `const long OpenEnd`.
  - `SphereRegimeSchedule(SphereId Sphere, IReadOnlyList<SphereRegime> Regimes)` with `SphereRegime? RegimeAt(long tick)`.
  - `SphereRegimeScheduleDefaults`: `long PlateOnsetTickFor(AtmosphereForcing)`, `static readonly long PlateOnsetTick`, `SphereRegimeSchedule GeosphereFor(long onsetTick)`, `SphereRegimeSchedule AtmosphereFor(long onsetTick)`, `const long MagmaOceanEndTick = 1_000_000`, `const double HydrationOnsetThreshold = 0.99`.

- [ ] **Step 1: Create the plugin csproj** (Godot-free T3; ref App.World.Composition + the new contracts + engine field types as needed; mirror a sibling T3 plugin csproj for ServiceArchi tier attributes if the app uses them)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>FantaSim.App.World.Composition</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\contracts\App.World\App.World.csproj" />
  </ItemGroup>
</Project>
```

> Atmosphere layer files `using FantaSim.Atmosphere.Genesis.Core;` — that resolves through `App.World`'s engine refs (Task 1). If the build can't see it, add the atmosphere project/package refs to THIS csproj too (mirror Task 1).

- [ ] **Step 2: Port the contract DTOs** — copy these from ref `contracts/App.World/Composition/` into the app's `contracts/App.World/Composition/`, namespace `FantaSim.App.World.Composition`, no behavior change:
  - `SphereRegimes.cs` (the `SphereRegime`/`SphereRegimeSchedule`/`RegimeAt` shown in Interfaces above — port verbatim)
  - `ILayer.cs` (`SphereId`, `LayerId`, `ILayer`, `IFieldProducer` seams)
  - `Fields.cs` (`FieldId`, `FieldDescriptor`, `FieldDomain`, `FieldValueKind`, …)
  - `FieldValues.cs` (`IFieldComputeContext`, `WorldScalarFieldValues`, …)

Build the contracts project: `dotnet build project/contracts/App.World/App.World.csproj` → 0 errors.

- [ ] **Step 3: Port the catalogs, math, layers, composer** — copy into `plugins/App.World.Composition/` (namespace `FantaSim.App.World.Composition`), curated (strip any deferred-file references):
  - `GeosphereFieldCatalog.cs`, `AtmosphereFieldCatalog.cs`, `GeosphereFieldMath.cs`
  - `FieldComposer.cs`, `FieldValueResolver.cs`
  - `GeosphereMagmaOceanLayer.cs` (produces `surface-temperature-k`, `melt-fraction`), `GeosphereStagnantLidLayer.cs` (`heat-flow-mw-m2`; ctor `(long? plateOnsetTick)`), `GeospherePlateLayer.cs`, `SyntheticCrustLayer.cs`
  - `AtmosphereBulkLayer.cs`, `AtmosphereCoupledLayer.cs`

Build: `dotnet build project/plugins/App.World.Composition/App.World.Composition.csproj` → 0 errors. Fix any reference to a DEFERRED type by removing that path (e.g., if a layer references `ElementGeologyTagProducer`, that wiring is out of scope — stub or omit and note it).

- [ ] **Step 4: Port `SphereRegimeScheduleDefaults.cs`** — copy verbatim (it is the onset coupling). Key surface (already validated against 4.0's solver): `GeosphereFor(onsetTick)` builds magma-ocean `[0,1e6)` (ShowsPlateFeatures=false) → stagnant-lid `[1e6,onset)` (false) → mobile-plate `[onset,∞)` (true); `PlateOnsetTickFor(forcing)` binary-searches the hydration curve (threshold 0.99 → default onset `1e8`).

- [ ] **Step 5: Wire the plugin into App.World** — add to `project/plugins/App.World/App.World.csproj`:
```xml
<ProjectReference Include="..\App.World.Composition\App.World.Composition.csproj" />
```
Add the new plugin + test project to the app solution WITH `ProjectConfigurationPlatforms` entries (Plan-3 lesson — a config-less project is silently skipped by solution build/test).

- [ ] **Step 6: Write the RegimeAt + onset tests**

```csharp
using FantaSim.App.World.Composition;
using FantaSim.Atmosphere.Genesis.Core;
using Xunit;

namespace App.World.Composition.Tests;

public class SphereRegimeScheduleTests
{
    [Fact]
    public void GeosphereFor_BoundariesAndPlateVisibility()
    {
        long onset = SphereRegimeScheduleDefaults.PlateOnsetTick; // 1e8 for default forcing
        var sched = SphereRegimeScheduleDefaults.GeosphereFor(onset);

        Assert.Equal("magma-ocean", sched.RegimeAt(0)!.RegimeId);
        Assert.False(sched.RegimeAt(0)!.ShowsPlateFeatures);
        Assert.Equal("stagnant-lid", sched.RegimeAt(SphereRegimeScheduleDefaults.MagmaOceanEndTick)!.RegimeId);
        Assert.False(sched.RegimeAt(onset - 1)!.ShowsPlateFeatures);
        Assert.Equal("mobile-plate", sched.RegimeAt(onset)!.RegimeId);
        Assert.True(sched.RegimeAt(onset)!.ShowsPlateFeatures);
    }

    [Fact]
    public void StrongerForcing_MovesOnsetEarlier()
    {
        long baseOnset = SphereRegimeScheduleDefaults.PlateOnsetTickFor(new AtmosphereForcing(1.0));
        long strongOnset = SphereRegimeScheduleDefaults.PlateOnsetTickFor(new AtmosphereForcing(2.0));
        Assert.Equal(100_000_000, baseOnset);
        Assert.True(strongOnset < baseOnset);
    }
}
```

- [ ] **Step 7: Run tests + commit**

Run: `dotnet test project/tests/App.World.Composition.Tests/App.World.Composition.Tests.csproj` → PASS.
```bash
git add project/contracts/App.World/Composition/ project/plugins/App.World.Composition/ \
        project/plugins/App.World/App.World.csproj project/tests/App.World.Composition.Tests/ project/<App>.sln
git commit -m "feat(app): App.World.Composition plugin — regimes + geosphere/atmosphere layers + causal onset

Curated port from ref (body-formation, geology-tag, manifest/JSON loaders deferred).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Onset → fracture wiring (plates born at onset, not Genesis)

At the derived onset tick, produce the plate roster via the engine's `LidFractureAtOnset`; before onset the roster is empty.

**Files:**
- Create: `project/plugins/App.World.Composition/OnsetRoster.cs` (a small pure helper)
- Modify: the app's world-build seam that currently produces topology from Genesis — **read it first**: `project/plugins/App.World/Globe/GlobeReconstructor.cs` + `WorldFunctionProvider.cs` (recon flagged these as the current procedural path)
- Test: `project/tests/App.World.Composition.Tests/OnsetRosterTests.cs`

**Interfaces:**
- Consumes engine (Task 1 refs): `FantaSim.Geosphere.Plate.Topology.LidFractureAtOnset.Fracture(GeodesicSphereTessellation, ConvectionFieldGenerator, long onsetTick, TruthStreamIdentity, ConvectionClassifierOptions?)`; `FantaSim.Geosphere.Asthenosphere.Convection.ConvectionFieldGenerator`; `UnifyCell.GeodesicSphereTessellation`; `PlateTopologyMaterializer` + `PlateTopologyState`.
- Produces: `OnsetRoster.PlatesAt(long tick)` → empty before `onsetTick`, the folded N-plate `PlateTopologyState` at/after.

- [ ] **Step 1: Write the failing test** (roster empty before onset; N plates at/after; deterministic)

```csharp
using FantaSim.App.World.Composition;
using FantaSim.Atmosphere.Genesis.Core;
using Xunit;

namespace App.World.Composition.Tests;

public class OnsetRosterTests
{
    [Fact]
    public void Roster_EmptyBeforeOnset_NPlatesAtAndAfter()
    {
        long onset = SphereRegimeScheduleDefaults.PlateOnsetTickFor(AtmosphereForcing.Default);
        var roster = OnsetRoster.Build(worldSeed: 2024, onsetTick: onset, tessellationFrequency: 3);

        Assert.Empty(roster.PlatesAt(onset - 1).Plates);
        Assert.True(roster.PlatesAt(onset).Plates.Count >= 3);
        Assert.Equal(roster.PlatesAt(onset).Plates.Count, roster.PlatesAt(onset + 50_000_000).Plates.Count);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test … --filter OnsetRosterTests` → FAIL (no `OnsetRoster`).

- [ ] **Step 3: Implement `OnsetRoster`** — build the convection field + tessellation, call `LidFractureAtOnset.Fracture` once, fold drafts via the materializer to a `PlateTopologyState`, and gate by tick:

```csharp
using System.Collections.Generic;
using FantaSim.Geosphere.Plate.Topology;                 // LidFractureAtOnset
using FantaSim.Geosphere.Asthenosphere.Convection;       // ConvectionFieldGenerator, ConvectionFieldConfig
using FantaSim.World.TruthStream;                        // TruthStreamIdentity
using UnifyCell;                                         // GeodesicSphereTessellation
// + materializer/state usings per the engine (PlateTopologyMaterializer, PlateTopologyState, FakeEvent-style adapter)

namespace FantaSim.App.World.Composition;

public sealed class OnsetRoster
{
    private readonly long _onsetTick;
    private readonly object _stateAtOnset; // PlateTopologyState (typed once usings resolved)

    private OnsetRoster(long onsetTick, object stateAtOnset) { _onsetTick = onsetTick; _stateAtOnset = stateAtOnset; }

    public static OnsetRoster Build(int worldSeed, long onsetTick, int tessellationFrequency)
    {
        var field = new ConvectionFieldGenerator(new ConvectionFieldConfig { Seed = worldSeed });
        var tess = new GeodesicSphereTessellation(tessellationFrequency);
        var stream = new TruthStreamIdentity("default", "main", 2, "geo.plates.topology", "M0");
        var drafts = LidFractureAtOnset.Fracture(tess, field, onsetTick, stream);
        // fold drafts -> PlateTopologyState via PlateTopologyMaterializer (mirror the engine's
        // OnsetRosterFoldTests FakeEvent adapter in fantasim-world)
        var state = FoldToState(drafts);
        return new OnsetRoster(onsetTick, state);
    }

    public PlateTopologyState PlatesAt(long tick) =>
        tick < _onsetTick ? PlateTopologyState.Empty : (PlateTopologyState)_stateAtOnset;

    // FoldToState: copy the proven fold adapter from
    // fantasim-world/.../OnsetRosterFoldTests.cs (FakeEvent : ITruthEvent + PlateTopologyMaterializer.Apply loop).
}
```

> Read `fantasim-world/project/tests/Geosphere.Plate.Topology.Generation.Tests/OnsetRosterFoldTests.cs` for the exact `FakeEvent`/`PlateTopologyMaterializer.Apply` fold and the `PlateTopologyState` shape; reuse it verbatim. Replace the `object` placeholders with the real `PlateTopologyState` type and add `PlateTopologyState.Empty` if the engine lacks it (a zero-roster instance).

- [ ] **Step 4: Run the test to verify it passes** — `dotnet test … --filter OnsetRosterTests` → PASS.

- [ ] **Step 5: Integrate into the world-build path** — in `GlobeReconstructor`/`WorldFunctionProvider`, replace the Genesis-time plate source so the rendered roster = `OnsetRoster.PlatesAt(currentTick)` and feed `RegimeAt(currentTick)` to the seam (Task 4). Keep the existing watertight-globe render; only the roster source + visibility gating change. Add a focused test if the seam exposes a testable T3 entry; otherwise this is verified in Task 5.

- [ ] **Step 6: Commit**

```bash
git add project/plugins/App.World.Composition/OnsetRoster.cs project/tests/App.World.Composition.Tests/OnsetRosterTests.cs \
        project/plugins/App.World/Globe/ project/plugins/App.World/WorldFunctionProvider.cs
git commit -m "feat(app): plates born at hydration-derived onset via LidFractureAtOnset (empty before onset)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Timeline-face — AnimationPlayer transport + regime threading

A focused `AnimationPlayer`/`AnimationTree` transport over the regime sections, threading regime state into `GlobeView`. T4 seam only.

**Files:**
- Modify: `project/plugins/App.World.Seam/GlobeView.cs` (add regime threading next to `SetTick`)
- Create: `project/plugins/App.World.Seam/RegimeTimelineTransport.cs` (the transport node)

**Interfaces:**
- Consumes: `SphereRegimeSchedule.RegimeAt(long)`, `SphereRegime.{RegimeId,ShowsPlateFeatures,DefaultColorByField}`; the existing `GlobeView.SetTick(long)`.
- Produces: `GlobeView.SetRegime(string regimeId, bool showsPlateFeatures, string? colorByField)`; a transport with play/pause/scrub that advances tick and calls `SetTick` + `SetRegime`.

- [ ] **Step 1: Read the current `GlobeView`** — confirm `SetTick(long)`, the `HSlider` scrubber, `SetColorMode(int)`, and the cap/feature build path (recon: `App.World.Seam/GlobeView.cs`, scrubber `OnScrubberChanged`).

- [ ] **Step 2: Add regime threading to `GlobeView`** — add a method and gate feature rendering:

```csharp
public void SetRegime(string regimeId, bool showsPlateFeatures, string? colorByField)
{
    _currentRegimeId = regimeId;
    _showsPlateFeatures = showsPlateFeatures;          // when false: hide boundary lines/junctions/phenomena
    if (colorByField is not null) ApplyColorBy(colorByField); // magma-ocean -> surface-temperature, lid -> heat-flow
    QueueCapVisibilityRefresh();
}
```
In the cap/feature build, skip plate-feature overlays when `!_showsPlateFeatures` (magma + lid show no plates).

- [ ] **Step 3: Build the AnimationPlayer transport** — `RegimeTimelineTransport : Node` owns an `AnimationPlayer` + `AnimationTree` (state machine: `Idle`/`Playing`/`Scrub`, per the ref `TimelineTunnelLayer` pattern at `ref/.../App.Timeline.Seam/TimelineTunnelLayer.cs:109-123` and `:268-293`). On `_Process`, when Playing, advance `tick` at a configurable ticks/sec across `[0, maxTick]`; each step calls `globeView.SetTick(tick)` and `globeView.SetRegime(RegimeAt(tick)…)`. Scrub maps the existing slider to `tick`. Mark regime-section boundaries (magma→lid→mobile) on the transport bar.

- [ ] **Step 4: Mount the transport** — instantiate `RegimeTimelineTransport`, give it the `GeosphereFor(onsetTick)` schedule + the `GlobeView`, add it to the world-view scene (next to where `GlobeView` is composed — recon: `hosts/complete-app/Host.cs:262-305 ComposeWorldView`).

- [ ] **Step 5: Build** — `dotnet build project/<App>.sln -p:UseProjectReferences=true` → 0 errors. (Godot UI is verified in Task 5, not unit-tested.)

- [ ] **Step 6: Commit**

```bash
git add project/plugins/App.World.Seam/
git commit -m "feat(app): AnimationPlayer regime-timeline transport + GlobeView regime threading

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Windowed verification (exported app)

Prove the scrub in the REAL exported windowed build (project rule: no headless smoke tests).

**Files:** none (verification + a short capture note).

- [ ] **Step 1: Export + launch the windowed app** — build/export per the app's export path (the `complete-app` host; ETC2/ASTC + embedded assemblies + ad-hoc sign per the loak-font gotchas if they apply). Launch the exported binary, not headless.

- [ ] **Step 2: Scrub the timeline and observe**
  - **magma-ocean** `[0,1e6)`: globe glows (colored by `surface-temperature-k`); NO plate boundaries/junctions.
  - **stagnant-lid** `[1e6, onset)`: cooling lid (colored by `heat-flow-mw-m2`); still NO plates.
  - **at onset** (`1e8` default): plates APPEAR (N ≥ 3 caps + boundary lines); `ShowsPlateFeatures` flips true.
  - **mobile-plate** `[onset,∞)`: plates + boundaries persist; watertight render unchanged.

- [ ] **Step 3: Causal check** — set a non-default `AtmosphereForcing(2.0)` and confirm onset moves EARLIER on the transport (plates appear sooner); `1.0` returns to `1e8`.

- [ ] **Step 4: Capture + record** — screenshot each regime; write a short windowed-verify note in `vault/handover/` (date-stamped) with what was observed and any tuning (e.g. magma glow amplitude). Commit the note.

```bash
git add vault/handover/
git commit -m "docs(app): Plan 4 windowed-verify record (magma -> lid -> plates at onset)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**1. Spec coverage** (against the umbrella spec §8 Plan 4 + this session's decisions):
- Port the 3 geosphere regimes + `RegimeAt` → Task 2. ✅
- Atmosphere coupling (onset DERIVED from hydration) → Task 2 (`SphereRegimeScheduleDefaults` + 4.0 solver). ✅
- Wire onset → fracture/emitter → Task 3 (`LidFractureAtOnset` + fold; empty before / N after). ✅
- One new approved plugin `App.World.Composition`, additive otherwise → Tasks 1–2. ✅
- Timeline-face (focused AnimationPlayer transport) → Task 4. ✅
- Windowed verify → Task 5. ✅
- Right-sizing (defer body-formation/geology/manifest/JSON loader) → Global Constraints + source map. ✅

**2. Placeholder scan:** the port tasks reference exact ref source paths + the load-bearing DTO/onset code is inlined; the wiring/timeline tasks give concrete code + tests. `OnsetRoster` flags the one spot the implementer must lift the engine's proven fold adapter (`OnsetRosterFoldTests`) — pointed at the exact file. The `<App>.sln` filename and a couple of recon line numbers are marked "read first / confirm" because the app's exact solution name wasn't captured — the implementer confirms on Step 1 of each task. (Acceptable: these are lookups, not design gaps.)

**3. Type consistency:** `SphereRegime`/`SphereRegimeSchedule.RegimeAt`/`SphereRegimeScheduleDefaults.{PlateOnsetTick,PlateOnsetTickFor,GeosphereFor,MagmaOceanEndTick,HydrationOnsetThreshold}` are used identically in Tasks 2–4 and match the ref source read for this plan. `LidFractureAtOnset.Fracture(...)` signature matches sub-plan 4.0's engine confirmation + Plan 3. `AtmosphereForcing`/`PrimordialAtmosphereSolver` match sub-plan 4.0. ✅

**Dependency:** requires **sub-plan 4.0** (`2026-06-22-engine-atmosphere-genesis-port.md`) merged first — the atmosphere projects must exist for Task 1's refs + Task 2's onset coupling to compile.

**Known follow-ups (not in scope):** the ported composition field system parallels the engine `World.Fields` (possible future dedupe); package-mode pins + engine pack of the full surface (release gate); body-formation, geology-tagging, JSON regime/manifest loaders.
