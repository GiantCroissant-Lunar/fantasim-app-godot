# Host Composition Push-Down — Implementation Plan

> **AUDIT (2026-07-06, code-verified):** COMPLETED (@e62b407; 15 per-plugin HostComposition modules; Host.cs-only) — 'Active' header stale. _(See the authority index in `vault/README.md`.)_


**Status:** Active. **Supersedes** [`2026-06-23-host-only-keep-host-type.md`](./2026-06-23-host-only-keep-host-type.md) — that plan's decision to keep all `Compose*` methods inside `Host` is **reversed** here per the user's 2026-06-23 direction.

**Goal:** Push each of the 13 `Compose*` method bodies out of `hosts/complete-app/Host.cs` + `Host.Gpu.cs` and down into the plugin project that owns that domain. Host keeps only the orchestration sequence (ordering + invoking the modules), lifecycle (`_Ready`/`_Process`/`_Notification`), and the env-guarded demo/smoke entry points (which are Host-owned runtime entry points, not composition).

**Why reverse the prior decision:** Host.cs reached ~866 lines + Host.Gpu.cs ~174; the composition root was indistinguishable from the domain wiring. Pushing the bodies into the owning plugin co-locates each domain's wiring with its implementation and shrinks the host to a thin orchestrator.

## Architecture

**Module shape:** explicit `static` `HostComposition` class per plugin project, invoked directly from `Host._Ready` in fixed order. NOT a contract-discovery system — 13 fixed-order modules do not benefit from a generic `IAppCompositionModule` dispatcher, and explicit calls keep ordering readable, debuggable, and diff-reviewable.

**Context:** `HostCompositionContext` (in `plugins/App.Common`, Godot-free) carries `{ AppComposition, IRegistry, ILoggerFactory }`. Deliberately has **no state bag** — `App.Common` cannot reference `App.World`/`App.Ecs`/`App.GpuCompute` (inverted dependency). Instead, modules RETURN host-owned handles and modules needing earlier handles take them as explicit params. This makes the data flow visible in signatures.

**Godot handles:** T4 modules (Godot-touching) receive `Godot.SceneTree`/`Godot.Node` as explicit parameters — no shared Godot context type, no new T4 project.

**ALC safety:** `Bootstrap.cs:93-119` puts `FantaSim.App.` in `SharedAssemblyPolicy.prefixes`, so every destination plugin assembly is **resident** (not collectible). Zero pinning risk.

## Module Placement

| # | Compose method | Destination | Layer | Returns / Receives |
|---|---|---|---|---|
| 1 | `ComposeResource` | `App.Resource.Bundle.Seam/HostComposition/ResourceComposition.cs` | T4 | receives `(ctx, hostNode, bundles)` |
| 2 | `ComposeSceneFlow` | `App.SceneFlow/HostComposition/SceneFlowComposition.cs` | T3 | `(ctx)` |
| 3 | `ComposeEcs` | `App.Ecs/HostComposition/EcsComposition.cs` | T3 | returns `(IService?, bool)` |
| 4 | `ComposeWorld` | `App.World/HostComposition/WorldComposition.cs` | T3 | `(ctx)` |
| 5 | `ComposeCellElevation` | `App.World/HostComposition/CellElevationComposition.cs` | T3 | returns `(CellElevationModel?, WorldGenerationRenderOptions)` |
| 6 | `ComposeCommand` | `App.Command/HostComposition/CommandComposition.cs` | T3 | `(ctx)` |
| 7 | `ComposeIii` | `App.Iii.Seam/HostComposition/IiiComposition.cs` | T4 | `(ctx, tree, hostNode)` |
| 8 | `ComposeGpu` | `App.GpuCompute.Seam/HostComposition/GpuComposition.cs` | T4 | returns `Service` |
| 9 | `ComposeGpuShader` | `App.GpuShader.Seam/HostComposition/GpuShaderComposition.cs` | T4 | returns `Service` |
| 10 | `ComposeWorldView` | `App.World.Seam/HostComposition/WorldViewComposition.cs` | T4 | `(ctx, tree, hostNode, cellElevation, renderOptions)` |
| 11 | `ComposeTimeline` | `App.Timeline.Seam/HostComposition/TimelineComposition.cs` | T4 | `(ctx)` |
| 12 | `ComposeActivity` | `App.Activity/HostComposition/ActivityComposition.cs` | T3 | `(ctx)` |
| 13 | `ComposeUi` | `App.Ui.Seam/HostComposition/UiComposition.cs` | T4 | `(ctx, tree)` |

**Helpers that move with their method:** `PublishWorldGenerationGraphRun` → `CommandComposition`; `GetWorldRenderOptions`/`ResolveWorldRenderOptions`/`SubscribeWorldGenerationRefresh`/`TryBuildDefaultSphereHandoff` → `WorldViewComposition`.

## Post-Refactor Host

`_Ready` becomes a flat ordered list of 13 module calls threading handles as locals. Host.cs drops from 866 → ~180 lines (composition ~25 lines + lifecycle + ~480 lines of demo/smoke methods which stay Host-owned). Host.Gpu.cs drops from 174 → ~120 (smoke methods stay).

**Demo/smoke methods stay in Host** by design: they are env-guarded runtime entry points that touch the scene tree and call `GetTree().Quit()` — not composition. A follow-up plan may extract them into a `DemoRunner` if Host size is still a concern after this refactor.

## Execution

- Task 0: `HostCompositionContext` in App.Common (lead session — unblocks all modules).
- Tasks 1-2: 9 module slices dispatched to `agy` + `kimi` (1 instance each; ~2 concurrent).
- Task 3: Host shell rewrite (lead session — critical integration).
- Task 4: Build + smoke verify (lead session).

## Invariants

1. Strict ordering preserved (Resource → ... → Ui); `ComposeWorldView` (#10) registers `ITimelineController` that `ComposeTimeline` (#11) resolves.
2. T3 modules (Microsoft.NET.Sdk) MUST NOT reference Godot — replace `GD.Print`/`GD.PushError`/`GD.PushWarning` with `ctx.LoggerFactory.CreateLogger(...)`.
3. Modules MUST NOT retain the context in static fields.
4. Host owns lifetime of returned handles (`_cellElevation`, `_ecs`, etc. disposed in `_Notification`).
5. Each module slice ends with `dotnet build` on its own destination csproj.
