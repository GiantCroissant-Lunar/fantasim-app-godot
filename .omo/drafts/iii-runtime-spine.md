---
slug: iii-runtime-spine
status: awaiting-approval
intent: unclear
pending-action: execute .omo/plans/iii-runtime-spine.md
approach: app-side iii/App.Command orchestration seam above Akka/ECS; sibling fantasim-world consumed via YokanProjectsRoot project refs; R3/ObservableCollection projection app-side only; Rust iii bridge/Hermes deferred.
---

# Draft: iii-runtime-spine

## Components (topology ledger)

| id | outcome | status | evidence path |
| --- | --- | --- | --- |
| command | `App.Command` T1/T3 local orchestration seam for iii | active | `.omo/evidence/task-1-iii-runtime-spine.build.log`, `.omo/evidence/task-6-iii-runtime-spine.trx` |
| world-refs | App consumes sibling `fantasim-world` via `YokanProjectsRoot` project refs | active | `.omo/evidence/task-2-iii-runtime-spine.build.log`, `.omo/evidence/task-5-iii-runtime-spine.test.trx` |
| ecs | App.Ecs has behavioral tests and field reduction system | active | `.omo/evidence/task-3-iii-runtime-spine.trx`, `.omo/evidence/task-7-iii-runtime-spine.trx` |
| projection | R3 plus ObservableCollection projections over app-side DTOs | active | `.omo/evidence/task-8-iii-runtime-spine.trx` |
| host | Host composes Resource/SceneFlow/Ecs/World/Command/Ui and exports | active | `.omo/evidence/task-9-iii-runtime-spine.export.log` |
| native-iii | Rust iii bridge and Hermes runtime | deferred | none |
| app-stage | real scene-tier `App.Stage` bundle | deferred | none |

## Open assumptions (announced defaults)

| assumption | adopted default | rationale | reversible? |
| --- | --- | --- | --- |
| iii mechanism | Use `App.Command` + `IWorldOrchestration` local implementation now; Rust bridge later | Local repo has no iii source; prior art used a Rust cdylib but exported-app slice should not require Hermes/Python | yes |
| world dependency topology | Use `YokanProjectsRoot` project refs now | Local feed has no `FantaSim.World.*`; project refs unblock implementation without packing | yes |
| R3 placement | App-side T3 projection only | `fantasim-world` must stay runtime-neutral; fields-concept puts R3 on app side | yes |
| field runtime | ECS components + `ReduceFieldsSystem` call pure reducers | Matches `fields-concept.md` runtime mapping | yes |
| App.Stage | Defer | Current stage bundle is content-only; runtime spine can be resident-first | yes |

## Findings (cited - path:lines)

- `fantasim-world/vault/architecture/fields-concept.md:119` maps pure world concepts -> Akka/ECS -> R3 projection -> iii orchestration.
- `fantasim-world/vault/architecture/fields-concept.md:146` says iii sits above Akka and triggers worlds/recipes, not per-tick math.
- `project/hosts/complete-app/Host.cs:14` is the Godot app composition root and currently composes Resource, SceneFlow, Ecs, Ui.
- `project/plugins/App.Common/Bootstrap.cs:33` creates the shared Akka `ActorSystem` and registers it.
- `project/plugins/App.Common/Bootstrap.cs:79` defines ALC shared assembly policy; R3/DynamicData are already anticipated but world/app command prefixes are missing.
- `project/plugins/App.Ui.Seam/ViewRenderer.cs:196` still contains old fallback text `iii + Hermes through App.Command`.
- `project/contracts/App.Ecs/EcsModel.cs:7` and `project/contracts/App.Ecs/Services/IService.cs:8` define the thin method-based App.Ecs T1 shape to preserve.
- `fantasim-world/project/contracts/World.TruthStream/TruthStreamIdentity.cs` defines the stream identity surface.
- `fantasim-world/project/contracts/World.Fields/*` and `plugins/World.Fields.Core/*` define field contracts/reducers/catalog validation.

## Decisions (with rationale)

1. `iii` is represented by `App.Command`/`IWorldOrchestration` first, not by native Rust or Hermes. This makes exported app boot deterministic now while preserving the prior-art native seam.
2. `fantasim-world` remains unchanged and runtime-neutral. All Akka/ECS/R3/iii/Godot integration happens in `fantasim-app-godot`.
3. Project references are used first through `YokanProjectsRoot`; feed packing is deferred until contracts stabilize.
4. App.Ecs behavioral tests are mandatory before field-system changes because the current ECS implementation is new and smoke-tested only.
5. Host composition is the final integration wave, not the first edit, to keep each layer independently buildable.

## Scope IN

- `App.Command` T1/T3 local orchestration seam.
- `App.World` T1/T3 app-side integration around `fantasim-world` contracts/core.
- R3 and `ObservableCollection` projection in app-side T3.
- ECS field components and `ReduceFieldsSystem`.
- Cross-path determinism tests.
- Host composition and export verification.

## Scope OUT (Must NOT have)

- Native Rust iii bridge build.
- Hermes/Python runtime.
- Real `App.Stage` scene-tier bundle.
- Pack-to-feed of `fantasim-world`.
- Any Akka/R3/Godot/iii deps in `fantasim-world`.

## Open questions

- No blocking questions. Defaults above are adopted for an unclear broad brief and surfaced for veto.

## Approval gate

status: awaiting-approval
pending action: start Wave 1 implementation from `.omo/plans/iii-runtime-spine.md` using subagents.
