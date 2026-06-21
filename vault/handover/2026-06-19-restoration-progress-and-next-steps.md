# Handover — restoration progress, current state, and next steps

**Date:** 2026-06-19
**Repo:** `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot`
**Status:** the repo now builds as a restored multi-project architecture with Akka.NET and UnifyECS integrated. The app exports successfully, and a minimal content-only stage bundle exports as a PCK. This is a strong intermediate checkpoint, not the final target architecture.

## 1. What was completed this session

### Phase 0 — dependency-archi fix
In `plate-projects/dependency-archi` we fixed child-scope singleton identity preservation:
- `MicrosoftExtensionsScopeActivationAdapter` now preserves parent singleton identity for child scopes.
- This was committed in the dependency-archi repo as:
  - `b315d1e` — `fix(dependency-archi): use parent.CreateScope() for child scopes to preserve singleton identity`

### Phase 1 — scaffold the multi-project structure
We replaced the original flat single-Godot-project layout with a 4-tier-ready solution layout:

```text
project/
  FantaSim.sln
  contracts/
    App.Resource
    App.SceneFlow
    App.Ecs
    App.Ui
    Cross.Abstractions/Polyfills/IsExternalInit.cs
  plugins/
    App.Common
    App.Resource
    App.Resource.Bundle.Seam
    App.SceneFlow
    App.Ecs
    App.Ui
    App.Ui.Seam
  tests/
    App.Resource.Tests
    App.SceneFlow.Tests
    App.Ecs.Tests
    App.Ui.Tests
  hosts/
    complete-app
    content-app
  bundles/
    stage/
```

We also added:
- `Directory.Build.props`
- `Directory.Packages.props`
- central package management (CPM)
- package feed alignment to `/Users/apprenticegc/Work/lunar-horse/packages/nuget`

### Phases 2-6 — ported real service code
We restored the core architecture from the previous attempts (using the `lunar-horse-002/yokan-projects/fantasim-app-godot` tree as the actual source after the `ref-projects/fantasim-app-godot` directory disappeared).

Restored services:
- `App.Common`
  - `Bootstrap.cs`
  - `AppComposition.cs`
  - `CollectibleBundles.cs`
- `App.Resource`
  - T1 contract
  - T3 orchestrator
  - T4 Godot PCK seam (`BundleHost`, `BundleProvider`, `BundleVfs`, `BundleSceneHost`, `DllExtractor`, `GodotBundleDirectoryResolver`)
- `App.SceneFlow`
  - T1 contract
  - T3 scene-flow orchestration (`SceneFlowProvider`, `EnterAsync`, `ExitAsync`, dynamic parent handling)
- `App.Ui`
  - T1 contract
  - T3 view orchestration
  - T4 Godot view host seam (`ViewHost`, `ViewRenderer`)
- `App.Ecs`
  - T1 contract (written from earlier reference analysis)
  - T3 NEW Akka actor implementation:
    - `EcsSupervisorActor`
    - `EcsWorldActor`
    - `EcsMessages`
    - `Services/Service.cs`

### Akka.NET integration
Akka is now integrated as shared infrastructure in `App.Common.Bootstrap`:
- `ActorSystem.Create("fantasim", ...)`
- registered into the kernel registry
- `SharedAssemblyPolicy` now includes:
  - `Akka`
  - `Newtonsoft.Json`
- `Bootstrap.StopAsync()` terminates the ActorSystem cleanly

### Phase 7 — Host composition root
We removed the original PoC autoload `AkkaHost.cs` and created a real `Host.cs` composition root.

`Host.cs` now:
- activates `AppComposition`
- loads `CollectibleBundles`
- builds the plugin host
- composes the 4 priority services into the kernel:
  - Resource
  - SceneFlow
  - Ecs
  - Ui

The Godot host now autoloads `Host` via `project.godot`.

### Phase 8-9 — bundle pipeline and tooling
We added the first minimal bundle scaffold:
- `project/bundles/stage/manifest.json`
- `project/bundles/stage/scenes/stage_entry.tscn`

We also added:
- `project/hosts/complete-app/config/collectible-bundles.json`
- `project/hosts/content-app/export_presets.cfg`
- a rewritten `Taskfile.yml` with:
  - `build`
  - `test`
  - `build:godot:desktop`
  - `bundle:stage`
  - `verify`

## 2. Current architecture shape

### Contracts (T1)
Current contract projects exist and build:
- `App.Resource`
- `App.SceneFlow`
- `App.Ecs`
- `App.Ui`

All contract assemblies are pure C# and remain free of Godot or Akka types in their public API.

### T3 service strategy
- `App.Resource`, `App.SceneFlow`, `App.Ui` are plain-class T3 services.
- `App.Ecs` is already implemented as the Akka-backed T3 strategy discussed earlier:
  - one actor system shared at app level
  - one supervisor actor for ECS world lifecycle
  - one world actor per ECS world

### T4 seams
- `App.Resource.Bundle.Seam` and `App.Ui.Seam` are Godot-facing seams.
- T4 remains plain-class / Godot main-thread-marshalled, not actor-based.

## 3. Verification completed

The following commands were run successfully:

### Build
```bash
dotnet build project/FantaSim.sln
```

Result: **Build succeeded** (0 errors).

### Tests
```bash
dotnet test project/FantaSim.sln --no-build
```

Result: all 4 current smoke-test projects passed:
- `App.Resource.Tests`
- `App.SceneFlow.Tests`
- `App.Ecs.Tests`
- `App.Ui.Tests`

### App export
```bash
task build:godot:desktop
```

Result: successful export to:
```text
build/_artifacts/4.0.0/godot/osx/
  complete-app.app
  complete-app.zip
  version.json
```

### Bundle export
```bash
task bundle:stage
```

Result: successful export to:
```text
build/_artifacts/4.0.0/godot/bundles/stage.pck
```

### Full verification task
```bash
task verify
```

Result: solution build + tests + app export + stage bundle export all passed.

### Pre-commit
```bash
pre-commit run --all-files
```

Result: passed.

## 4. Commits made in the main repo

In `fantasim-app-godot`:
- `3ffa5f9` — initial Godot 4.6 .NET project with Akka.NET + UnifyBuild integration
- `1f1e28b` — scaffold 4-tier project structure with contracts, plugins, tests, and solution
- `2152efc` — port service code for all 4 priority services + Akka ActorSystem
- `8a20db0` — migrate AkkaHost PoC to Host.cs composition root with 4 services
- `7808cef` — finalize bundle pipeline, CPM tooling, and verification flow

In `plate-projects/dependency-archi`:
- `b315d1e` — `fix(dependency-archi): use parent.CreateScope() for child scopes to preserve singleton identity`

## 5. Important caveats / what is NOT done yet

### A. `ref-projects/fantasim-app-godot` is gone
The original gold-reference repo under:
- `/Users/apprenticegc/Work/lunar-horse-002/ref-projects/fantasim-app-godot`

was no longer present when porting started. The actual source of truth for the restored code became:
- `/Users/apprenticegc/Work/lunar-horse-002/yokan-projects/fantasim-app-godot`

This means some exact ref-projects patterns (notably the earlier `CompositionModules.cs` pattern) were replaced by the later yokan-projects approach where that file no longer existed.

### B. `App.Common` differs from the original plan
The written plan expected `App.Common` to include:
- `Bootstrap.cs`
- `AppComposition.cs`
- `CompositionModules.cs`
- `CollectibleBundles.cs`

But the actual yokan-projects source only had:
- `Bootstrap.cs`
- `AppComposition.cs`
- `CollectibleBundles.cs`

So `CompositionModules.cs` was not restored because it did not exist in the later source tree we had to use.

### C. `App.Ecs` is newly authored, not ported verbatim
The `App.Ecs` T1 contract + Akka-based T3 were built from the earlier design discussion and from the old reference analysis, not copied from source files in the remaining yokan-projects tree. It builds, but it has only smoke-test coverage so far.

### D. Stage bundle is content-only scaffold right now
The current `stage` bundle is **not yet** a full collectible scene-tier plugin bundle with its own `ISceneActivator` and DI scene scope.

Current state:
- content-only manifest
- content-only `stage_entry.tscn`
- exports as a valid `.pck`

Missing for a real scene-tier bundle:
- `App.Stage` contract/project
- `App.Stage` plugin assembly
- `StageActivator`
- `StagePlugin`
- registration into `collectible-bundles.json`
- scene-tier DI activation path through `App.SceneFlow`

### E. Multi-scene DI bridge is still deferred
We documented that not all `.tscn` are DI containers -- only some scenes should be.

But the following items are still **deferred**:
- `SceneScopeNode`
- `SceneActivatorBase`
- automatic tree-based parent discovery for DI scenes

This means the DI scoping review doc is written, but the implementation of those enhancements is not yet started.

## 6. Next recommended steps

### Priority 1 — real `App.Stage` scene-tier bundle
Implement `App.Stage` as the first real DI scene-tier plugin bundle:
- contract + plugin assembly
- `ISceneActivator`
- `StagePlugin`
- `StageActivation`
- register in `collectible-bundles.json`
- make `App.SceneFlow.EnterAsync("stage")` load/activate it through `App.Resource`

This is the single most important missing piece if the goal is to restore the real bundle-oriented architecture.

### Priority 2 — DI scene scope bridge (`SceneScopeNode`)
For DI scenes that also render Godot content, add the T4 bridge discussed in the review:
- `SceneScopeNode`
- tie `_EnterTree` / `_ExitTree` to DI scope create/dispose
- keep it opt-in (not every `.tscn` becomes a DI scope)

### Priority 3 — replace smoke tests with real behavioral tests
Current tests are only smoke tests. Next session should add:
- `App.Resource` unit tests for bundle load/unload orchestration
- `App.SceneFlow` tests for parent-child enter/exit ordering
- `App.Ecs` actor-level tests (world actor lifecycle, supervisor, snapshot behavior)
- `App.Ui` tests for view load/show/hide behavior

### Priority 4 — review the `Host.cs` composition flow
Current `Host.cs` composes the priority 4 services and builds successfully, but it has not yet been exercised with a real collectible scene-tier plugin (because `App.Stage` is still content-only).

That means the next session should validate:
- `BuildPluginHost` + `CollectibleBundles` registry flow
- scene-tier plugin registration
- `SceneFlowProvider` resolving activators from a loaded bundle

## 7. Working tree / repo status to keep in mind

### Main repo (`fantasim-app-godot`)
At the end of this session, the committed state is good. There may still be `.omo/` runner state files in the working tree -- those are agent-internal and should not be treated as project changes.

### Plate-projects repos modified this session
We also touched several plate-projects repos locally to make this session possible:
- `dependency-archi` -- committed fix
- `service-archi` -- stale `GiantCroissant.Plate.RegistryArchi.*` refs corrected locally
- `crosscut-foundation`, `plugin-archi`, `registry-archi`, `unify-ecs` -- `NuGet.config` adjusted to use the macOS shared feed path

Those local changes in plate-projects are important context for the next session if packaging/build behavior changes.

## 8. Key commands to resume next session

### Verify the main repo state
```bash
dotnet build project/FantaSim.sln
dotnet test project/FantaSim.sln --no-build
task verify
```

### Export app only
```bash
task build:godot:desktop
```

### Export stage bundle only
```bash
task bundle:stage
```

### Main artifacts
```text
build/_artifacts/4.0.0/godot/osx/
build/_artifacts/4.0.0/godot/bundles/stage.pck
```

## 9. References written this session

Architecture docs in `vault/architecture/`:
- `service-tier-architecture.md`
- `cross-alc-rules.md`
- `akka-ecs-integration.md`
- `multi-scene-di-scoping-review.md`

Earlier handover doc:
- `vault/handover/2026-06-19-architecture-restoration-akka-adoption.md`

This file is the updated handover that supersedes the earlier one for actual implementation progress.
