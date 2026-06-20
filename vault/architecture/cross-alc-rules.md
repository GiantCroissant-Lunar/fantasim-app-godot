# Cross-ALC rules

**Status:** PROPOSED. Adapted from the ref-projects cross-ALC rules (confirmed 2026-06-10) with additions for Akka.NET actor system residency.

---

## 1. The two assembly worlds

### Resident shared ALC

The host app (complete-app) and its foundation libraries load into the default AssemblyLoadContext (ALC). These assemblies live for the process lifetime. The SharedAssemblyPolicy tells PluginArchi which assemblies to resolve from the parent (resident) ALC when a collectible bundle asks for them -- preserving type identity across the boundary so a bundle-registered `IViewSource` cast succeeds on the host side.

### Collectible bundle ALCs

Each bundle with a `pluginAssembly` gets its own collectible ALC (created by `PluginHost.AddGroupAsync`). When the bundle unloads, `RemoveGroupAsync` initiates `AssemblyLoadContext.Unload()` -- but the ALC only collects once the GC confirms zero strong references into it.

**Why this matters:** type identity is ALC-scoped. If a contract type (e.g. `IViewSource`) loads in both the resident and a collectible ALC, the host's `is IViewSource` cast fails -- same name, different runtime type. The SharedAssemblyPolicy prevents this by routing contract loads to the shared resident ALC.

---

## 2. SharedAssemblyPolicy

**Source:** `project/plugins/App.Common/Bootstrap.cs`, the `PluginHostBuilder` configuration.

### Share prefixes

Assemblies whose names start with any of these prefixes load into the resident (shared) ALC.

| Prefix | What it covers |
|--------|---------------|
| `System.` | BCL |
| `Microsoft.` | BCL / NuGet libs |
| `Godot`, `GodotSharp` | Godot runtime |
| `netstandard` | Standard lib shim |
| `PluginArchi.` | PluginArchi abstraction + hosting |
| `ServiceArchi.` | ServiceArchi contracts + core |
| `RegistryArchi.` | RegistryArchi |
| `DependencyArchi.` | DependencyArchi |
| `CrosscutFoundation.` | Messaging, config, resilience, logging |
| `FantaSim.App.` | All contract assemblies (e.g. `FantaSim.App.Ui`) |
| `BoomHud` | BoomHud runtime surface renderer |
| `Akka` | Akka.NET actor system runtime |
| `Newtonsoft.Json` | Akka HOCON config dependency |
| `R3` | Godot-facing reactive primitives |
| `ReactiveUI` | Reactive extensions |
| `DynamicData` | DynamicData collections |

> **No new prefix for the iii axis.** `FantaSim.App.Iii.Contracts` (the graph data model + `IIiiInvoker`) is covered by the existing `FantaSim.App.` share prefix. The Rust cdylib is native, not managed -- see §3b.

### Collectible exclusions

The `FantaSim.App.` prefix would also share any bundle implementation assembly whose name starts with that prefix. These must load into their own collectible ALCs:

```
"FantaSim.App.Stage",
"FantaSim.App.Assist",
"FantaSim.App.Ui.ReferenceOverview",
...
```

This list is DATA-DRIVEN from `project/hosts/complete-app/config/collectible-bundles.json`. The assembly name is derived from the `pluginAssembly` field minus the `.dll` extension.

---

## 3. Akka.NET and the ALC boundary

### ActorSystem is resident

The `ActorSystem` is created in `Bootstrap.cs` and lives for the process lifetime. It is NOT collectible. Actor services (App.Agent, App.Remote, App.Ecs) create their actors within this shared system. The `ActorSystem`, `IActorRef`, `Props`, and all Akka infrastructure types are resident-shared via the `Akka` share prefix.

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
- Collectible bundles reference `IIiiInvoker` (in `FantaSim.App.Iii.Contracts`, shared-resident via the `FantaSim.App.` prefix) -- **never** `IiiBridge` (the Node).
- The cdylib itself **cannot hot-reload**. Updating the Rust bridge requires a Godot restart. Bundles that invoke pipelines hold only `IIiiInvoker`, so bundle hot-reload is unaffected (clean unload).

---

## 4. What may and may not cross the boundary

### MAY cross (shared-resident)

- Contract assemblies marked `[assembly: PluginSharedContract]` -- matched by the `FantaSim.App.` share prefix.
- `FantaSim.App.Iii.Contracts` -- graph data model (`GraphDocument`/`GraphNode`/`GraphWire`) + `IIiiInvoker`. Pure C#, bundle-safe.
- CrosscutFoundation (messaging, config, resilience, logging).
- BoomHud runtime surface types.
- Akka.NET runtime types (`Akka.dll`, `Newtonsoft.Json.dll`) -- shared-resident so all actor services see the same `ActorSystem` type identity.
- R3 / ReactiveUI / DynamicData (reactive primitives used by resident seams).
- PluginArchi / ServiceArchi / RegistryArchi abstractions.

### Must NOT cross

- The bundle implementation assemblies themselves (e.g. `FantaSim.App.Ui.NodeGraph`).
- Actor message types (`FantaSim.App.X.Actors.Messages.*`) -- these are T3-internal, resident-only.
- `IActorRef` or any Akka handle type -- never exposed in T1 contracts.
- `IiiBridge` (the Godot `Node`) -- bundles reference `IIiiInvoker` only. The Node-backed seam is resident-only.

---

## 5. Rules for clean collectible unload

Each rule is grounded in real code patterns from the ref-projects. The Akka additions are noted.

### R1. NO Godot-derived types in a bundle assembly

A collectible bundle must be pure C# + contract types. The bundle csproj does not reference the Godot package.

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
