# iii-runtime-spine - Work Plan

## TL;DR (For humans)

**What you'll get:** The exported Godot app will boot a connected runtime spine: Akka.NET supervises ECS worlds, the app consumes `fantasim-world` parameters/fields/truth-stream identity, R3 plus `ObservableCollection` project live field state, and `App.Command` becomes the iii orchestration seat above Akka/ECS.

**Why this approach:** `iii` belongs above Akka/ECS as orchestration, not in per-tick simulation math. `fantasim-world` remains pure/runtime-neutral, while `fantasim-app-godot` is the assembly point that wires ECS, projection, app commands, and Godot export behavior.

**What it will NOT do:** It will not require Hermes/Python or the Rust iii bridge for this first exported-app slice. It will not add Akka/R3/Godot/iii dependencies to `fantasim-world`. It will not pack `fantasim-world` to the local NuGet feed yet.

**Effort:** Large
**Risk:** Medium - main risks are cross-repo project-reference wiring, ALC shared-assembly policy, and native iii being deferred behind a seam.
**Decisions I made for you:** local-first `App.Command` orchestration seam now; `YokanProjectsRoot` project references instead of feed packaging; R3/ObservableCollection app-side only; App.Ecs behavioral tests before field systems; Rust iii bridge/Hermes/App.Stage deferred.

Your next move: approve this plan to start Wave 1 implementation, or ask for a high-accuracy plan review first. Full execution detail follows below.

---

> TL;DR (machine): Large, Medium risk - restore `App.Command` + `IWorldOrchestration`, wire `fantasim-world` via `YokanProjectsRoot`, add `App.World`, `App.World.Projection`, App.Ecs field system/determinism tests, Host composition, final build/test/export/smoke gates.

## Scope

### Must have
- `App.Command` T1 contract plus an `IWorldOrchestration` seam where iii sits above Akka/ECS.
- Local in-process orchestration implementation now; Rust `iii-bridge` adapter left behind the seam for a later slice.
- `YokanProjectsRoot` MSBuild property and `UseProjectReferences`-gated references to sibling `fantasim-world` projects.
- `App.World` T1 thin contract plus T3 orchestrator that consumes `World.Parameters`, `World.Fields`, `World.Fields.Core`, `World.TruthStream`, and `World.TruthStream.Core`.
- `App.Ecs` behavioral tests before adding field systems.
- ECS field components plus `ReduceFieldsSystem` using pure `fantasim-world` reducers/catalog.
- Cross-path determinism test: direct reduce equals truth-stream-backed reduce/materialize path for identical contributions.
- `App.World.Projection` T3 with R3 plus `ObservableCollection` projections over app-side DTOs.
- `Host.cs` composition order extended to Resource -> SceneFlow -> Ecs -> World -> Command -> Ui.
- `SharedAssemblyPolicy` includes world/app command prefixes needed for ALC-safe exported app boot.
- Final gates: solution build/test, `task verify`, Godot desktop export, exported/headless smoke if feasible.

### Must NOT have (guardrails, anti-slop, scope boundaries)
- No Akka/R3/Godot/iii dependencies in `fantasim-world`.
- No Hermes/Python required for the exported app.
- No Rust iii bridge build in this plan.
- No pack-to-feed of `fantasim-world` in this plan.
- No `App.Stage` scene-tier bundle in this plan.
- No per-tick iii calls; iii triggers worlds/recipes only.
- No edits to user-owned uncommitted files outside the plan and implementation targets; do not touch `AGENTS.md` or `vault/handover/*` unless separately requested.

## Verification strategy
> Zero human intervention - all verification is agent-executed.

- Test decision: TDD for new seams/systems, tests-after for restored prior-art scaffolding; xUnit plus `Akka.TestKit.Xunit2`.
- Evidence: `.omo/evidence/task-<N>-iii-runtime-spine.<ext>` for builds, tests, export logs, and app smoke logs.
- Final gates: `dotnet build project/FantaSim.sln`, `dotnet test project/FantaSim.sln`, `task verify`, `task build:godot:desktop`, exported/headless app smoke if feasible.
- Boundary audit: confirm `fantasim-world` diff is empty and no new runtime deps were added there.

## Execution strategy

### Parallel execution waves
- Wave 1: Tasks 1-4 in parallel after approval.
- Wave 2: Tasks 5-8 after Wave 1.
- Wave 3: Task 9 integration after Wave 2.
- Wave 4: F1-F4 final reviews/QA in parallel where possible.

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| 1 App.Command T1 + orchestration seam | None | 6, 8 | 2, 3, 4 |
| 2 YokanProjectsRoot + package versions | None | 5, 6, 7 | 1, 3, 4 |
| 3 App.Ecs behavioral tests | None | 7 | 1, 2, 4 |
| 4 App.World T1 thin contract | None | 5, 6, 8 | 1, 2, 3 |
| 5 App.World T3 orchestrator | 2, 4 | 6, 7, 8 | 6 |
| 6 App.Command T3 local orchestrator | 1, 5 | 9 | 7, 8 |
| 7 ReduceFieldsSystem + determinism tests | 3, 5 | 9 | 6, 8 |
| 8 App.World.Projection T3 | 4, 5 | 9 | 6, 7 |
| 9 Host composition + policy + UI runtime status | 6, 7, 8 | F1-F4 | None |

## Todos
> Implementation + Test = ONE todo. Never separate.

- [x] 1. Restore `App.Command` T1 contract plus `IWorldOrchestration` seam
  What to do / Must NOT do: Create `project/contracts/App.Command/` using current T1 patterns and prior-art `lunar-horse-002/.../contracts/App.Command` as a reference. Include command DTOs, source-gen client/service partials, `[assembly: PluginSharedContract]`, and new `Orchestration/IWorldOrchestration.cs` with `TriggerAsync` and `HealthAsync`. Must not reference Akka, R3, Godot, iii, Hermes, or App.Agent.
  Parallelization: Wave 1 | Blocked by: None | Blocks: 6, 8
  References: `project/contracts/App.Ecs/App.Ecs.csproj`, `project/contracts/App.Ecs/Services/IService.cs`, `Directory.Packages.props:8`, prior-art `lunar-horse-002/yokan-projects/fantasim-app-godot/project/contracts/App.Command/*`.
  Acceptance criteria: `dotnet build project/contracts/App.Command/App.Command.csproj` exits 0; solution build includes `App.Command`.
  QA scenarios: happy build succeeds; failure scenario proves adding an Akka using to T1 fails boundary expectations. Evidence `.omo/evidence/task-1-iii-runtime-spine.build.log`.
  Commit: Y | `feat(app-command): restore command contract and orchestration seam`

- [x] 2. Add `YokanProjectsRoot` MSBuild property plus R3/DynamicData package versions
  What to do / Must NOT do: Extend root `Directory.Build.props` with a default `YokanProjectsRoot` mirroring existing `PlateProjectsRoot` style. Add central package versions for R3 and DynamicData. Keep `UseProjectReferences` default unchanged. Do not add world package versions or edit `fantasim-world`.
  Parallelization: Wave 1 | Blocked by: None | Blocks: 5, 6, 7
  References: `Directory.Build.props:13`, `Directory.Packages.props`, prior-art project-ref pattern in `ref-projects/fantasim-app-godot/project/contracts/App.World/App.World.csproj`.
  Acceptance criteria: default `dotnet build project/FantaSim.sln` exits 0 with no consumer changes.
  QA scenarios: happy default build green; failure with bad `YokanProjectsRoot` gives clear missing-project MSBuild error once consumers exist. Evidence `.omo/evidence/task-2-iii-runtime-spine.build.log`.
  Commit: Y | `build(msbuild): add YokanProjectsRoot for sibling world refs`

- [x] 3. Replace `App.Ecs` smoke test with Akka TestKit behavioral coverage
  What to do / Must NOT do: Replace `project/tests/App.Ecs.Tests/App.EcsSmokeTests.cs` with real tests for `EcsWorldActor` lifecycle, `EcsSupervisorActor` routing/list/duplicate behavior, supervision, and `UpdateAll` fan-out. Do not add field-system tests here.
  Parallelization: Wave 1 | Blocked by: None | Blocks: 7
  References: `project/plugins/App.Ecs/Actors/EcsWorldActor.cs`, `EcsSupervisorActor.cs`, `EcsMessages.cs`, `project/contracts/App.Ecs/EcsModel.cs`, `vault/architecture/akka-ecs-integration.md`.
  Acceptance criteria: `dotnet test project/FantaSim.sln --filter FullyQualifiedName~App.Ecs.Tests` exits 0 with at least four meaningful tests.
  QA scenarios: happy behavioral tests pass; failure by breaking routing causes a test failure. Evidence `.omo/evidence/task-3-iii-runtime-spine.trx`.
  Commit: Y | `test(app-ecs): add Akka actor behavioral tests`

- [x] 4. Add `App.World` T1 thin app contract
  What to do / Must NOT do: Create `project/contracts/App.World/` with pure app-side DTOs and service proxy: overview, field values, render snapshot, `GetOverviewAsync`, `GetFieldValuesAsync`, `GetRenderSnapshotAsync`, `RunGenerationAsync`, and `GenerationChanged`. Do not expose `fantasim-world` types, Godot, Akka, or R3 in T1.
  Parallelization: Wave 1 | Blocked by: None | Blocks: 5, 6, 8
  References: `project/contracts/App.Ecs/*`, prior-art `ref-projects/fantasim-app-godot/project/contracts/App.World/*`, especially `Composition/FieldValues.cs`.
  Acceptance criteria: solution build exits 0; `dotnet list project/contracts/App.World/App.World.csproj package` shows no `FantaSim.World.*` refs.
  QA scenarios: happy build green; failure by using `FantaSim.World.Fields.FieldId` in T1 DTO is rejected in review/build boundary check. Evidence `.omo/evidence/task-4-iii-runtime-spine.build.log`.
  Commit: Y | `feat(app-world): add thin world contract`

- [x] 5. Add `App.World` T3 orchestrator consuming `fantasim-world` via project refs
  What to do / Must NOT do: Create `project/plugins/App.World/` with conditional project references to sibling `fantasim-world` contracts/plugins using `$(YokanProjectsRoot)`. Implement service that composes `CompositeFieldCatalog`, `FieldReducerRegistry`, and `CatalogValidator` at startup and consumes parameters/truth-stream primitives internally. Do not add R3 here. Do not expose world-lib types through T1.
  Parallelization: Wave 2 | Blocked by: 2, 4 | Blocks: 6, 7, 8
  References: `fantasim-world/project/contracts/World.Fields/*`, `plugins/World.Fields.Core/*`, `contracts/World.Parameters/*`, `contracts/World.TruthStream/*`, `plugins/World.TruthStream.Core/*`, `fantasim-world/vault/architecture/fields-concept.md:104`.
  Acceptance criteria: `dotnet build -p:UseProjectReferences=true project/FantaSim.sln` exits 0; duplicate field id validation test throws as expected.
  QA scenarios: happy build and catalog test pass; failure duplicate `FieldId` module triggers validation. Evidence `.omo/evidence/task-5-iii-runtime-spine.test.trx`.
  Commit: Y | `feat(app-world): compose world fields and truthstream runtime`

- [x] 6. Add `App.Command` T3 local orchestration implementation
  What to do / Must NOT do: Create `project/plugins/App.Command/`, restore prior-art `InProcessClient`/dispatcher patterns, implement trimmed command service plus `LocalOrchestrator` that calls App.World/App.Ecs, and add `IiiBridgeOrchestrator` stub that clearly says the native bridge is deferred. Do not spawn Hermes/Python or reference App.Agent/App.Stage.
  Parallelization: Wave 2 | Blocked by: 1, 5 | Blocks: 9
  References: prior-art `lunar-horse-002/yokan-projects/fantasim-app-godot/project/plugins/App.Command/Clients/InProcessClient.cs`, `Providers/IMainThreadDispatcher.cs`, `Services/Service.cs`, current `project/plugins/App.Common/Bootstrap.cs` registry pattern.
  Acceptance criteria: local orchestrator test drives mock App.World plus App.Ecs and returns success; bridge stub throws documented `NotImplementedException`.
  QA scenarios: happy local orchestration end-to-end test; failure missing App.World registration returns clear error. Evidence `.omo/evidence/task-6-iii-runtime-spine.trx`.
  Commit: Y | `feat(app-command): add local orchestration service`

- [x] 7. Add ECS field components, `ReduceFieldsSystem`, and determinism test
  What to do / Must NOT do: Add T3-internal field contribution/resolved components and `ReduceFieldsSystem` to `App.Ecs`, grouping contributions by `FieldId`, catalog lookup, reducer lookup, canonical sort by tick/producer, reduce, write resolved component, clear contribution buffer. Pin direct reduce equals truth-stream path. Do not expose world-lib component types through T1.
  Parallelization: Wave 2 | Blocked by: 3, 5 | Blocks: 9
  References: `fantasim-world/vault/architecture/fields-concept.md:119`, `project/plugins/App.Ecs/Actors/EcsWorldActor.cs`, `fantasim-world/project/contracts/World.Fields/*`, `World.TruthStream/*`.
  Acceptance criteria: App.Ecs tests pass including reduce and cross-path determinism tests.
  QA scenarios: happy shuffled contributions still deterministic; failure removing canonical sort fails determinism test. Evidence `.omo/evidence/task-7-iii-runtime-spine.trx`.
  Commit: Y | `feat(app-ecs): reduce field contributions in ECS`

- [x] 8. Add `App.World.Projection` T3 using R3 plus `ObservableCollection`
  What to do / Must NOT do: Create `project/plugins/App.World.Projection/` with R3/DynamicData package refs. Implement a field projection service that listens to App.World changes, emits R3 observable updates, and maintains an `ObservableCollection`/DynamicData-backed collection of view models. No Godot and no `fantasim-world` reactive deps.
  Parallelization: Wave 2 | Blocked by: 4, 5 | Blocks: 9
  References: `fantasim-world/vault/architecture/fields-concept.md:145`, `project/plugins/App.Common/Bootstrap.cs:94` shared policy, App.Ui `IViewSource` pattern if a view source is added.
  Acceptance criteria: projection tests pass: subscribed observable emits after generation change; collection count matches seeded fields.
  QA scenarios: happy emission and collection sync; failure no `GenerationChanged` means test times out/fails. Evidence `.omo/evidence/task-8-iii-runtime-spine.trx`.
  Commit: Y | `feat(app-world): add reactive field projection`

- [x] 9. Wire Host composition, shared policy, and runtime-mode reporting
  What to do / Must NOT do: Extend `Host.cs` with `ComposeWorld` and `ComposeCommand`; order Resource -> SceneFlow -> Ecs -> World -> Command -> Ui. Add `FantaSim.World.`, `FantaSim.App.World.`, `FantaSim.App.Command.` to `SharedAssemblyPolicy`. Add required host project refs. Replace old `ViewRenderer.cs:196` fallback with real orchestration health/runtime mode. Do not remove existing services or spawn external processes.
  Parallelization: Wave 3 | Blocked by: 6, 7, 8 | Blocks: F1-F4
  References: `project/hosts/complete-app/Host.cs:14`, `project/plugins/App.Common/Bootstrap.cs:79`, `project/plugins/App.Ui.Seam/ViewRenderer.cs:196`, `project/hosts/complete-app/complete-app.csproj`.
  Acceptance criteria: solution build green; `task build:godot:desktop` exports; headless/export smoke logs six services and no old runtime fallback.
  QA scenarios: happy export and boot log; failure removing App.Command policy prefix causes ALC mismatch or registry issue. Evidence `.omo/evidence/task-9-iii-runtime-spine.export.log`.
  Commit: Y | `feat(host): compose world command and projection runtime`

## Final verification wave
> Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay before declaring complete.

- [x] F1. Plan compliance audit - verify all must-haves delivered and all must-not boundaries respected.
- [x] F2. Code quality review - verify T1/T3 boundaries, ALC policy, no oversized/sloppy modules, and no world-lib runtime deps.
- [x] F3. Real manual QA - run `task verify` plus exported/headless app smoke showing Resource, SceneFlow, Ecs, World, Command, Ui boot.
- [x] F4. Scope fidelity - confirm Rust iii bridge, Hermes, App.Stage, and persistence are absent from the diff.

## Commit strategy

Atomic commits, one per todo after its local QA passes. Wave 1 commits can land independently. Wave 2 depends on Wave 1. Wave 3 is the integration commit. No commits for `.omo` unless the user explicitly requests commits. Do not commit `AGENTS.md`, handover docs, or `.omo/run-continuation/*` unless separately requested.

## Success criteria

1. `dotnet build project/FantaSim.sln` exits 0 with default props.
2. `dotnet build -p:UseProjectReferences=true project/FantaSim.sln` exits 0 with sibling world refs.
3. `dotnet test project/FantaSim.sln` exits 0, including App.Ecs behavioral tests, reducer determinism tests, App.World catalog tests, App.Command local orchestration tests, and App.World.Projection tests.
4. `task verify` exits 0.
5. `task build:godot:desktop` exports successfully.
6. Exported/headless app smoke logs six composed services and a real orchestration runtime mode, not `iii + Hermes through App.Command` fallback.
7. `fantasim-world` has no diff and no new Akka/R3/Godot/iii dependencies.
