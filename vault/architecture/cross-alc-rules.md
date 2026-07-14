# Cross-ALC rules

> **AUDIT (2026-07-14, code-verified — supersedes the 2026-07-06 note):** §1, §2, §3, §4, and
> §5-R1 were wrong on their load-bearing claims and are rewritten below. Resident = Godot's
> **`IsolatedComponentLoadContext`** (component ALC), not `AssemblyLoadContext.Default`
> (`App.Resource.Bundle.Seam/CommonResidentLayer/CommonResidentLayerBootstrap.cs`,
> `App.Common/Bootstrap.cs`). Share policy is externalized to
> `project/hosts/complete-app/config/shared-assembly-policy.json` after the **2026-07-11 polarity
> flip** — the broad `FantaSim.App.` prefix is gone, T1 contracts are enumerated `exactMatches`,
> `DynamicData` is deliberately bundle-local. R1 ("no Godot types in a bundle assembly") no longer
> holds: the `world` collectible bundle ships `FantaSim.App.Presentation`, built with
> `Godot.NET.Sdk` (T4, per `App.Presentation.csproj`), per `collectible-bundles.json`. Still true
> from the 2026-07-06 note: `App.Agent` does not exist; `App.Remote` carries no actors;
> `IIiiInvoker` lives in `plugins/App.Iii/IIiiInvoker.cs` — but it is **not** a `.Contracts`
> assembly and is **not** covered by any current `exactMatches`/`prefixes` entry (see §3/§4).
> _(See the authority index in `vault/README.md`.)_


**Status:** PROPOSED; §1, §2 and §5-R1 below now describe shipped 2026-07-11 mechanics, not a
proposal. Other sections remain proposed/ref-projects-derived. Adapted from the ref-projects
cross-ALC rules (confirmed 2026-06-10) with additions for Akka.NET actor system residency.

---

## 1. The two assembly worlds

### Resident: the component ALC (not Default)

Godot hosts `complete-app.dll` and its whole managed dependency graph in its own
**`IsolatedComponentLoadContext`** ("the component ALC") -- **not** `AssemblyLoadContext.Default`.
Two call sites establish this:

- `App.Common/Bootstrap.cs` resolves its own load context via
  `AssemblyLoadContext.GetLoadContext(typeof(Bootstrap).Assembly)` (falling back to `Default` only
  if that returns null) and parents the `PluginHostBuilder` on it in `Bootstrap.BuildPluginHost`.
- `App.Resource.Bundle.Seam/CommonResidentLayer/CommonResidentLayerBootstrap.cs` hooks the
  **same component ALC's** `Resolving` event -- not `Default.Resolving` -- to serve the ~36-assembly
  `common.pck` resident layer on demand. Its own code comment states the reason plainly: Godot
  hosts `complete-app.dll` and its whole dependency graph in an `IsolatedComponentLoadContext`,
  and that context's fallback chain never consults `Default.Resolving`.

These assemblies (the host's direct/transitive dependency graph, plus whatever
`CommonResidentLayerBootstrap` resolves on demand from `common.pck`) live for the process lifetime.
The SharedAssemblyPolicy (§2) tells PluginArchi which assemblies to resolve from this **component**
ALC when a collectible bundle asks for them -- preserving type identity across the boundary so a
bundle-registered `IViewSource` cast succeeds on the host side. The two-worlds model below still
holds; only the identity of the resident context changes, from Default to the component ALC.

### Collectible bundle ALCs

Each bundle with a `pluginAssembly` gets its own collectible ALC (created by `PluginHost.AddGroupAsync`). When the bundle unloads, `RemoveGroupAsync` initiates `AssemblyLoadContext.Unload()` -- but the ALC only collects once the GC confirms zero strong references into it.

**Why this matters:** type identity is ALC-scoped. If a contract type (e.g. `IViewSource`) loads in both the resident (component) ALC and a collectible ALC, the host's `is IViewSource` cast fails -- same name, different runtime type. The SharedAssemblyPolicy prevents this by routing contract loads to the shared resident ALC.

---

## 2. SharedAssemblyPolicy

**Source of truth:** `project/hosts/complete-app/config/shared-assembly-policy.json` -- parsed by
`App.Common/SharedAssemblyPolicyConfig.cs` and consumed by `Bootstrap.BuildPluginHost`. The same
JSON is also consumed by `tools/bundles/stage_bundle.py` for bundle staging, so it is the single
source for both runtime and build tooling. This section describes the shape of the policy; the
JSON's own `comment` field carries the current per-entry rationale and is the thing to read for the
latest detail -- do not let this table drift out of sync with it.

**POLARITY FLIPPED 2026-07-11** (bundle-maximalism phase-0 completion). The old model shared
almost everything by a broad `FantaSim.App.` prefix and excluded bundle assemblies by name. The
new model inverts this: **enumerate what's shared** (T1 contracts + a small resident floor) via
`exactMatches`, and share a short list of infrastructure namespaces via `prefixes`. Everything not
listed is bundle-local by default.

### exactMatches (enumerated shared assemblies)

Every `FantaSim.App.*.Contracts` assembly (T1) is listed by exact name (e.g.
`FantaSim.App.World.Contracts`, `FantaSim.App.Ui.Contracts`). Alongside the contracts, a small
**resident floor** of implementation assemblies is also listed -- shared for structural reasons,
not because they're contracts:

- `FantaSim.App.Common`, `FantaSim.App.Resource`, `FantaSim.App.Resource.Bundle.Seam` --
  **permanent** (target-topology resident line).
- `FantaSim.App.Ecs` (until phase 7), `FantaSim.App.NodeGraph` + `FantaSim.App.Ui.NodeGraph`
  (until phase 3) -- droppable only when the corresponding `complete-app.csproj` `ProjectReference`
  goes; unsharing while the host still loads them directly produces a cross-ALC dual-copy (the
  MessagePack type-split failure class).
- `FantaSim.App.Timeline.Seam`, `FantaSim.App.Command`, `FantaSim.App.Ui.Seam`, `FantaSim.App.Ui`
  -- floor entries pulled in by the tunnel-slice-1 (2026-07-11) resident-to-bundle edge: the
  `world` bundle's `App.Presentation` references their tunnel pure-math + filmstrip-sink classes
  and must resolve the same resident copy, not a bundled duplicate.
- Non-`FantaSim.App.*` entries: `UnifyMaths*`, `UnifyStorage.Abstractions`,
  `UnifyStorage.Runtime.LiteDb`, `LiteDB`, `Arch*`, `MessagePack*`, `Cartography.Globe.*`,
  `Cartography.Shared.Contracts`, `FantaSim.App.World.Rendering`, and a few more -- see the JSON
  for the exhaustive, current list; this doc intentionally does not duplicate it line-for-line.

### prefixes (narrow infrastructure prefixes)

Assemblies whose names start with any of these prefixes load into the resident (component) ALC.

| Prefix | What it covers |
|--------|---------------|
| `System.` | BCL |
| `Microsoft.` | BCL / NuGet libs |
| `Godot`, `GodotSharp` | Godot runtime |
| `netstandard` | Standard lib shim (facade-name resolution for netstandard-targeted shared assemblies like UnifyMaths, which can hit this even with no matching file on disk) |
| `PluginArchi.` | PluginArchi abstraction + hosting |
| `ServiceArchi.` | ServiceArchi contracts + core |
| `RegistryArchi.` | RegistryArchi |
| `DependencyArchi.` | DependencyArchi |
| `CrosscutFoundation.` | Messaging, config, resilience, logging |
| `MessagePipe` | Messaging |
| `BoomHud` | BoomHud runtime surface renderer |
| `Akka` | Akka.NET actor system runtime |
| `Newtonsoft.Json` | Akka HOCON config dependency |
| `R3` | Godot-facing reactive primitives |
| `UnifyEcs.` | UnifyECS abstraction |
| `TimeDete.` | Time/date utility |

**Gone since the flip:** the broad `FantaSim.App.` prefix, `ReactiveUI`, `DynamicData`.
`DynamicData`'s removal is **deliberate**, not cleanup -- its only consumer is bundle-local
`App.World.FieldView` and no resident copy ever existed, so it now stages into `world.pck` (per the
JSON's own comment, this also fixes a latent resolution gap).

> **iii axis has no covering share entry today.** The old doc claimed
> `FantaSim.App.Iii.Contracts` was "covered by the existing `FantaSim.App.` share prefix." That
> assembly does not exist -- `IIiiInvoker` lives directly in `plugins/App.Iii/IIiiInvoker.cs`,
> assembly `FantaSim.App.Iii`, a T3 project, not a `.Contracts` assembly -- and the broad prefix is
> now gone. `FantaSim.App.Iii` is not in `exactMatches` either. In practice this is moot **today**
> because `App.Iii`/`App.Iii.Seam` are direct `ProjectReference`s of `complete-app.csproj` (always
> resident, loaded as part of the host's own dependency graph) and no collectible bundle currently
> references `IIiiInvoker`. If a bundle ever does, `FantaSim.App.Iii` must be added to
> `exactMatches` first, or the bundle gets an incompatible duplicate type. The Rust cdylib is
> native, not managed -- see §3b.

### Collectible exclusions

Bundle implementation assemblies must load into their own collectible ALCs, not the resident one.
This is enforced by `excludedExactMatches`, populated at runtime from
`project/hosts/complete-app/config/collectible-bundles.json`'s per-bundle `assemblyNames` --
consumed in `Bootstrap.BuildPluginHost` as `excludedExactMatches: collectibleBundles.AssemblyNames`.
Example (`world` bundle): `FantaSim.App.World`, `FantaSim.App.Presentation`,
`FantaSim.App.World.FieldView`, `FantaSim.App.World.Composition`, plus its bundle-local NuGet
closure (`SurrealDb.Net`, `SurrealDb.Embedded.InMemory`, `UnifyStorage.Runtime.SurrealDb`, etc.).

This list is DATA-DRIVEN from `collectible-bundles.json`; the assembly name is derived from the
`pluginAssembly` field (and the `assemblyNames` array for multi-project bundles) minus the `.dll`
extension.

---

## 3. Akka.NET and the ALC boundary

### ActorSystem is resident

The `ActorSystem` is created in `Bootstrap.cs` and lives for the process lifetime. It is NOT
collectible. **Actor-backed services today: `App.Ecs`** (`EcsSupervisorActor`/`EcsWorldActor`,
composed via `EcsComposition.ComposeEcs` in `Host.cs`) **and `App.World`'s truth-writer**
(`ActorTruthEventWriter`, started in `App.World/Services/Service.cs` when the world truth-store
backend is SurrealDB, so writes serialize through the resident `ActorSystem`). `App.Agent` does
not exist in the tree. `App.Remote` (`plugins/App.Remote/HttpTransport.cs` + its
`RemoteComposition`) is plain HTTP command dispatch with zero actor refs -- it has fields named
`ActorKind`/`ActorId` (HTTP caller metadata) but creates or holds no `IActorRef`. The
`ActorSystem`, `IActorRef`, `Props`, and all Akka infrastructure types are resident-shared via the
`Akka` share prefix.

### Actor messages must not cross the boundary

Messages sent to actors (e.g. `ShowView`, `CreateWorld`, `AskRequest`) are internal to T3. They live in `FantaSim.App.X.Actors.Messages` which is part of the T3 orchestrator assembly -- resident, not a contract assembly. These types:

- **MAY** be plain C# records/classes (they never need `[PluginSharedContract]`).
- **Must NOT** be in contract assemblies (T1). Contracts stay method-based.
- **Must NOT** carry Godot types or bundle-implementation types.

### What this means for bundle code

A collectible bundle that calls `IService.ShowAsync(viewId)` goes through the T2 proxy -> T3 adapter -> actor mailbox. The bundle never sees `IActorRef`, `Props`, or actor messages. It sees only the method-based `IService` contract. The actor is an implementation detail of the resident T3.

### 3b. Native gdextensions and ALC

The iii Rust bridge (`project/native/iii-bridge/`) is a native gdextension (`cdylib`), engine-loaded via `.gdextension` at Godot startup. It is **invisible to the managed ALC graph** -- `SharedAssemblyPolicy` governs managed assemblies only, so the cdylib is never listed there.

**Rules:**
- The C# side reaches the bridge **only** through Godot `Variant` calls and signals (`ClassDB.Instantiate("IiiClient")`, `Call`, signal connection) -- never direct native interop, never a managed wrapper type that bundle code references.
- The single resident `IiiBridge` node lives in the resident ALC for the process lifetime, composed once in `Host.cs`, and exposes the pure `IIiiInvoker` contract upward.
- Collectible bundles would reference `IIiiInvoker` (`plugins/App.Iii/IIiiInvoker.cs`, assembly
  `FantaSim.App.Iii`) -- **never** `IiiBridge` (the Node). No collectible bundle does so today; see
  the iii-axis note in §2 before adding one.
- The cdylib itself **cannot hot-reload**. Updating the Rust bridge requires a Godot restart. Bundles that invoke pipelines hold only `IIiiInvoker`, so bundle hot-reload is unaffected (clean unload).

---

## 4. What may and may not cross the boundary

### MAY cross (shared-resident)

- T1 contract assemblies (marked `[assembly: PluginSharedContract]`) -- matched by exact name in
  `shared-assembly-policy.json`'s `exactMatches` (no longer a broad prefix -- see §2).
- The resident-floor implementation assemblies enumerated in `exactMatches` (§2) -- e.g.
  `FantaSim.App.Common`, `UnifyMaths*`, `Arch*`, `Cartography.Globe.*`.
- CrosscutFoundation (messaging, config, resilience, logging).
- BoomHud runtime surface types.
- Akka.NET runtime types (`Akka.dll`, `Newtonsoft.Json.dll`) -- shared-resident so all actor services see the same `ActorSystem` type identity.
- R3 (reactive primitives used by resident seams). `ReactiveUI` and `DynamicData` are **not**
  shared -- see §2.
- PluginArchi / ServiceArchi / RegistryArchi abstractions.

### Must NOT cross

- Any bundle implementation assembly not listed in `exactMatches` (e.g. `FantaSim.App.Ui.NodeGraph`,
  `FantaSim.App.World`) -- these load into their own collectible ALC per `excludedExactMatches`
  (§2 Collectible exclusions). `FantaSim.App.Presentation` is the one deliberate T4-in-bundle
  exception -- see §5 R1.
- Actor message types (`FantaSim.App.X.Actors.Messages.*`) -- these are T3-internal, resident-only.
- `IActorRef` or any Akka handle type -- never exposed in T1 contracts.
- `IiiBridge` (the Godot `Node`) -- bundles reference `IIiiInvoker` only (today, no bundle does --
  see §3b). The Node-backed seam is resident-only.

---

## 5. Rules for clean collectible unload

Each rule is grounded in real code patterns from the ref-projects. The Akka additions are noted.

### R1. Godot types MAY live in a bundle's T4 members -- but a resident object must never pin them

The original ref-projects rule ("a collectible bundle must be pure C# + contract types, the bundle
csproj does not reference the Godot package") no longer holds. The live `world` collectible bundle
packs `FantaSim.App.Presentation` -- its csproj (`project/plugins/App.Presentation/App.Presentation.csproj`)
is `Sdk="Godot.NET.Sdk/4.7.0"`, tagged `ServiceArchiTier=T4` -- and it is listed in the `world`
bundle's `assemblyNames` in `collectible-bundles.json`. T4-in-collectible is the shipped pattern.

This works because `GodotSharp` itself stays `prefix`-shared (§2): the Godot types a bundle's T4
members reference resolve to the **same resident `GodotSharp` copy** on both sides of the
boundary, so type identity holds even though the bundle assembly is collectible. What still
matters, unchanged, is R2: no *resident* object may hold a strong reference to a bundle-defined
Godot-derived type (e.g. a bundle's `Node` subclass) past unload, or the ALC pins. Use the `world`
bundle / `App.Presentation` as the worked example when adding a new T4-in-bundle assembly.

### R2. Resident code must not hold strong refs past unload

Resident code (T3, T4) may hold `IActorRef` to actors that manage bundle-related work. On bundle unload, the T3 service must stop or reset those actors (`GracefulStop` or `PoisonPill`). The actor's `PostStop` hook disposes the world/resources. This replaces the ref-projects' manual disposal pattern with actor lifecycle hooks.

### R3. CacheMode.ReplaceDeep is load-bearing for hot-reload

After a rebuilt PCK is re-mounted, a plain cached `Load` would still return the old scene/script. `ReplaceDeep` busts the cache.

### R4. The temp-dir + GC sweep lifecycle

On reload: unload old ALC, queue temp dir for cleanup, re-extract to a fresh temp path, run GC sweep rounds. Actor lifecycle (`PostStop`) hooks into this -- the actor's `PostStop` disposes the `ArchWorld`/`ArchSystemRunner` before the ALC is collected.

### R5. FileSystemWatcher threading rule

The watcher runs on thread-pool threads. For scene bundles, provider work must be marshalled onto the main thread. Actor services that receive reload triggers via messages are already on the actor thread -- no additional marshalling needed for sceneless bundles.

### R6. The BundleHost gate is held across plugin InitializeAsync

Every public mutation acquires `_gate.WaitAsync()`. A plugin must never load another bundle from its `InitializeAsync`.

### R7. Godot has no PCK unload

`ProjectSettings.LoadResourcePack` mounts a PCK into the VFS. There is no API to remove entries. On unload, the VFS entries persist; only the ALC and scene are reclaimed.

---

## 6. Verification

Any claim that hot-reload works (ALC collects cleanly) must be verified in the windowed exported app. Required evidence:

1. Rebuild + repack the bundle, stage the new PCK.
2. The windowed app picks up the change via `FileSystemWatcher` and reloads.
3. The log line `"Hot-reload: old ALC collected -- released and deleted {Dir}"` appears.
4. The app window shows the updated content.

For actor-backed services, additionally verify:
5. Actor `PostStop` log line appears (e.g. `"EcsWorldActor stopped for {WorldId}"`).
6. `ActorSystem.WhenTerminated` has NOT been called (the shared system stays alive; only child actors stop).

For iii-pipeline bundles, additionally verify:
7. After a bundle hot-reload, an in-flight `pipeline.run` still resolves through the **resident** `IiiBridge` (the bridge is not re-created and the cdylib is not reloaded -- only the bundle ALC turns over).

---

## References

- ref-projects cross-ALC rules: `lunar-horse-002/ref-projects/fantasim-app-godot/vault/architecture/cross-alc-rules.md`
- Service tier architecture: `vault/architecture/service-tier-architecture.md`
- Akka + ECS integration: `vault/architecture/akka-ecs-integration.md`
