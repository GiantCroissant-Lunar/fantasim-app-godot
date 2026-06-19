# 2026-06-19: Architecture restoration -- 4-tier services, bundles, Akka.NET adoption

## Context

The current `lunar-horse/yokan-projects/fantasim-app-godot` repo is a flat single-project Akka.NET proof-of-concept (one `.csproj`, one `AkkaHost.cs` autoload). Two previous attempts at the full FantaSim architecture exist in `lunar-horse-002`:

- **ref-projects/fantasim-app-godot**: The gold reference. Full 4-tier service architecture, 12+ services, collectible PCK bundles with hot-reload, extensive vault docs. Uses plain class T3 with manual locking (`lock(_gate)`, `ConcurrentDictionary`).
- **yokan-projects/fantasim-app-godot**: A later evolutionary branch. Adds `App.Command` dispatch surface, Rust gdext bridges, Hermes agent integration, app-command-driven live dev loop. Fewer services (7 contracts) but introduces the command surface pattern.

## Decision

Adopt the 4-tier service architecture and bundle-oriented design from the ref-projects, with Akka.NET integrated as the concurrency/lifecycle/supervision layer for actor-shaped services. Specifically:

1. **T1-T4 tier split** as documented in `vault/architecture/service-tier-architecture.md`. Godot quarantined in T4 seams, contracts are pure C# with no Godot or Akka types.

2. **Akka.NET in T3, not T4.** T4 is always a plain class (Godot main-thread marshalling via `CallDeferred` doesn't benefit from actors). T3 is an actor adapter only when the service has an actor-shaped problem (concurrent stateful entities, supervision/retry, long-running async workflows). Simple services (Ui, SceneFlow, Activity) stay plain class T3.

3. **ECS as the primary actor use case.** One `EcsWorldActor` per Arch world, supervisor actor managing all worlds. Pinned dispatcher per world for parallel single-threaded updates. Replaces the ref-projects' `lock(_gate)` pattern entirely. See `vault/architecture/akka-ecs-integration.md`.

4. **ActorSystem as shared infrastructure.** Composed in `Bootstrap.cs` alongside `IMessageBus`, `IRegistry`, logging. Services inject `ActorSystem` by constructor. No Akka types leak into T1 contracts.

5. **Shared NuGet feed** at `/Users/apprenticegc/Work/lunar-horse/packages/nuget` for all lunar-horse projects (already set up).

6. **UnifyBuild** for Godot export with GitVersion-driven versioning (already set up and verified).

## What was verified before this decision

- Akka.NET 1.5.69 builds and runs inside a Godot 4.6 .NET desktop export (verified: actor round-trip, clean shutdown, export to `.app` + `.zip`).
- UnifyBuild's Godot export pipeline works (fixed the redundant `dotnet publish` + DLL injection bug; Godot's own C# export plugin handles per-arch .NET publishing).
- UnifyECS source reviewed: `IWorld`, `ISystemRunner`, `WorldFactory`, `ArchWorld`, `ArchSystemRunner`, `ArchMultiWorldRunner`. No threading model, no concurrency safety -- the gap Akka fills.
- ref-projects `App.Ecs` reviewed: `EcsWorldContext` with `lock(_gate)` on every method, `ConcurrentDictionary` in `Service`. The locking is the signal that the current design fights concurrency rather than embracing it.

## Documents written

- `vault/architecture/service-tier-architecture.md` -- T1-T4 model with Akka in T3, when to use actors vs plain classes, composition in Host.cs.
- `vault/architecture/cross-alc-rules.md` -- ALC boundary rules, SharedAssemblyPolicy with `Akka` prefix added, actor message residency rules.
- `vault/architecture/akka-ecs-integration.md` -- One actor per world, supervisor, pinned dispatcher, update timing (Tell vs Ask), supervision strategy, project layout.

## Open questions

1. **Foundation dependencies**: How many plate-projects libraries to pull in initially? ServiceArchi, PluginArchi, RegistryArchi, DependencyArchi, CrosscutFoundation, BoomHud are all needed for the full pattern. Start with a minimal subset (ServiceArchi + CrosscutFoundation.Messaging) and grow?
2. **Bundle scope**: Start with the full hot-reloadable PCK bundle system, or begin with resident-only services and add bundles later? The bundle build strategy doc says "promote to unify-build once ~3-5 real bundles exist" -- we're at zero.
3. **First services to implement**: Resource (bundle loader, everything depends on it) and Ecs (the primary Akka use case) seem like the natural starting pair.
4. **Akka as IMessageBus backend**: Akka's EventStream could replace MessagePipe as the `IMessageBus` implementation. Separate decision -- the `IMessageBus` contract stays the same.