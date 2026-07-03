# ALC Pin Diagnosis — world bundle reload + planet presentation hot-reload feasibility

**Branch:** `agent/alc-pin-diagnosis` (read-only worktree)
**Date:** 2026-07-03
**Scope:** READ-ONLY diagnosis. No source modified, no commits, no Godot runs. Claims cite `file:line`.

---

## 1. VERDICT

**The "old ALC still pinned" log is a FALSE-NEGATIVE gate, not (primarily) a genuine residual reference pin.** The frame-deferred R3 gate that was designed and xUnit-proven in S2a/S2b (`ReloadPolicy.cs`, `Observable.NextFrame` before each `IsCollected` probe) was **REPLACED** by commit `79bc07e` with a plain `Task.Delay(16ms)` fire-and-forget loop (`BundleHost.cs:294-344`). That commit explicitly removed the `using R3` and the `NextFrame`-before-probe discipline. The S2b handover (`vault/handover/2026-06-25-reload-s2b-windowed-probe-placement-handover.md:44-70`) documented that the OLD in-stack gate "got a false 'still pinned' because transient refs (the async state machine, deferred holders) still pinned the ALC during the check." The current `Task.Delay` loop is better than the old in-stack probe (it fires after `ReloadAsync` returns, with 480ms initial delay), but it runs on a **threadpool thread** — not deferred to the Godot main thread's next frame. If the Godot main thread has not yet processed the deferred callbacks (`Callable.From(...).CallDeferred()` in the binder's `OnResourceRuntimeChanging`), transient references from the in-flight reload state machine and the deferred Godot node cleanup may still be alive when the probe forces GC.

**There is also one GENUINE pin risk specific to the world bundle**: the resident host (`complete-app.csproj`) has a **`ProjectReference` to `App.World.csproj`** (`complete-app.csproj:16`) — the collectible bundle's own plugin assembly. This causes `FantaSim.App.World.dll` to load into the **resident ALC** at startup AND into the **collectible ALC** when the world bundle loads. While the binder's use of `GlobePlateSurfaces` etc. references the RESIDENT copy (not the collectible one), the dual-copy situation is fragile: any resident code path that obtains a collectible-ALC `Service` object and holds it across the reload boundary would pin. The binder's `_generationSubscription` is the one such path, and it IS disposed synchronously — but the margin is thin.

**For planet presentation hot-reload (Question 2):** moving the presentation code into a collectible bundle is feasible but requires solving the static-SceneTree-handoff pin (the binder mounts Godot nodes under the stage Environment), extracting rendering types from the collectible `App.World` into shared contracts, and establishing a mount/unmount protocol through the existing `IBundleSceneRegistry` seam. Risk is MEDIUM-HIGH; a bounded packet is feasible.

---

## 2. EVIDENCE — per suspect

### Suspect A: Resident event subscriptions holding bundle-side delegates after ShutdownAsync
**STATUS: CONFIRMED CLEAN (with timing caveat)**

The `PlanetPresentationBinder` subscribes to `_resource.RuntimeChanging/Changed` (`PlanetPresentationBinder.cs:119-120`) and to `world.SubscribeGenerationChanged(...)` (`PlanetPresentationBinder.cs:216`).

- `OnResourceRuntimeChanging` (`PlanetPresentationBinder.cs:1135-1151`) disposes `_generationSubscription` **synchronously** (line 1141) — the `GenerationChangedSubscription.Dispose()` (`Service.cs:677-685`) nulls its `_owner` and removes the callback from the world Service's `_subscribers` list. This runs BEFORE `BundleHost.UnloadCoreAsync` calls `WorldPlugin.ShutdownAsync`.
- `OnResourceRuntimeChanged` (`PlanetPresentationBinder.cs:1153-1160`) schedules `Rebind()` via `CallDeferred()` — runs on the next Godot frame, after the new bundle loads.
- `ClearActiveRoot()` and `ReleaseNodeGraphView()` are deferred via `Callable.From(...).CallDeferred()` (`PlanetPresentationBinder.cs:1145-1149`). These free Godot nodes and dispose the graph view. They run on the next Godot main thread frame.

**Timing caveat:** the deferred calls run on the Godot main thread. The `QueueOldContextCollectionVerification` probe (`BundleHost.cs:283-292`) runs as a fire-and-forget `Task.Delay` loop on a threadpool thread. If the threadpool task's 480ms initial delay (`BundleHost.cs:303-306`, 30x16ms) elapses before the Godot main thread processes the deferred queue, the probe could see transient refs from the not-yet-processed deferred cleanup. In practice, 480ms is ~30 frames at 60fps, so the deferred calls should have completed — but under load or vsync issues, this is not guaranteed.

### Suspect B: Resident registry registrations not unregistered on shutdown
**STATUS: CONFIRMED CLEAN**

`WorldPlugin.ShutdownAsync` (`WorldPlugin.cs:58-86`):
- Unregisters `world.run_generation_graph` command (`WorldPlugin.cs:67`) — `CommandService.Unregister` removes from `_handlers` dict (`App.Command/Services/Service.cs:65-71`).
- Disposes `_lateArmSubscription` (`WorldPlugin.cs:71`) — removes the `SubscribeGenerationChanged` callback.
- Detaches `PresentationRequested` hook (`WorldPlugin.cs:73,211-218`).
- Disposes `_crustTrigger` (`WorldPlugin.cs:75`) — `CrustGenerationTrigger.Dispose()` unsubscribes from `ITimelineController.TickChanged` (`CrustGenerationTrigger.cs:66`).
- Disposes `_worldCompositionHandle` (`WorldPlugin.cs:79`) — `CompositeDisposable.Dispose()` (`WorldComposition.cs:43-48`) calls `world.Dispose()` (clears `_subscribers`, `Service.cs:648-649`) and disposes both `RegisterOwned` handles (`IService` + `INodeFunctionProvider`).

**Verified by test:** `RegisterOwned<T>.Dispose()` removes the registration from the registry (confirmed via `/tmp/reg_test` — `TryGet<T>` returns null after dispose). The registry does NOT retain a reference to the collectible-ALC `Service` object after shutdown.

### Suspect C: Host binder subscriptions to world-service events (PlanetPresentationBinder.SubscribeGenerationChanged, PresentationRequested, ITimelineController)
**STATUS: CONFIRMED CLEAN**

- `SubscribeGenerationChanged` (`PlanetPresentationBinder.cs:208-231`): the callback delegate captures `this` (resident binder). The world `Service._subscribers` list holds this delegate. The binder disposes `_generationSubscription` synchronously in `OnResourceRuntimeChanging` (line 1141), removing the delegate BEFORE `ShutdownAsync` runs. Additionally, `WorldPlugin.ShutdownAsync` disposes the world `Service` which calls `_subscribers.Clear()` (`Service.cs:649`). **Belt and suspenders.**
- `PresentationRequested` (`WorldPlugin.cs:163`): `WorldPlugin` subscribes `OnPresentationRequestedArm` to the world `Service.PresentationRequested` event. `DetachPresentationArmHook()` (`WorldPlugin.cs:211-218`) unsubscribes in `ShutdownAsync`. This is bundle→bundle (both `WorldPlugin` and `Service` are in the collectible ALC), so even if missed, it wouldn't pin from resident code.
- `ITimelineController` (`PlanetPresentationBinder.cs:109`): the binder registers its RESIDENT `PlanetTimelineController` via `RegisterOwned<ITimelineController>`. The `WorldPlugin`'s `CrustGenerationTrigger` subscribes to `_timeline.TickChanged` (`CrustGenerationTrigger.cs:51`). This is bundle→resident (fine for collection). `CrustGenerationTrigger.Dispose()` unsubscribes (`CrustGenerationTrigger.cs:66`). The binder's `_timelineRegistration` is NOT disposed on world reload (only on app shutdown in `Dispose()`, line 1222) — but `PlanetTimelineController` is RESIDENT, so this doesn't pin the collectible ALC.

### Suspect D: Statics/caches keyed by bundle types
**STATUS: KILLED**

- `PlanetPresentationBinder` statics: `_magmaShader`, `_stagnantShader`, `_hypsoPlateShader`, `_atmosphereRimShader`, `_hypsoPlateMaterial` (`PlanetPresentationBinder.cs:1075-1083`). These are Godot `Shader`/`Material` objects (engine types, resident). Not bundle-typed.
- `WorldComposition` statics: `GeosphereFieldCatalog` has `static readonly FieldDescriptor` fields (`GeosphereFieldCatalog.cs:24-28`). `FieldDescriptor` is from `FantaSim.World.Fields.Contracts` which is in the shared `exactMatches` (`Bootstrap.cs:114`). Not a pin.
- `OnsetRoster.PlateTopologyStreamIdentity` (`OnsetRoster.cs:39`): `TruthStreamIdentity` from `FantaSim.World.Fields.Contracts` (shared). Not a pin.
- No MessagePack formatter caches or ECS registrations were found that reference collectible-bundle types.

### Suspect E: Godot-side nodes holding C# objects typed from the bundle ALC
**STATUS: CONFIRMED CLEAN (type-identity split protects here)**

The binder creates Godot nodes (`Node3D`, `MeshInstance3D`, `DirectionalLight3D`, `WorldEnvironment`, `Camera3D`, `Label3D`) — all engine types. The `PlateBoundaryFocusRenderer` (`PlateBoundaryFocusRenderer.cs:16`) is a `Node3D` subclass defined in the RESIDENT host. The `_plateSurfaces` field (`PlanetPresentationBinder.cs:79`) is a `GlobePlateSurfaces` — but the binder instantiates it via `new GlobePlateSurfaces(...)` (`PlanetPresentationBinder.cs:587-588`), which resolves to the **RESIDENT copy** of `FantaSim.App.World.dll` (loaded via the `ProjectReference` at `complete-app.csproj:16`). The collectible ALC has its OWN copy. The binder's `_plateSurfaces` does NOT reference the collectible ALC's `GlobePlateSurfaces` type.

**However:** `GlobePlateSurfaces` transitively depends on `FantaSim.Cartography.Globe.Core` (`App.World.csproj:63`), which is NOT in the shared prefixes or `excludedExactMatches`. The resident host gets a resident copy; the bundle gets a collectible copy. The binder's `_plateSurfaces` uses the resident copy. No pin.

### Suspect F: The collection gate itself (probe timing/mechanism)
**STATUS: CONFIRMED — PRIMARY ROOT CAUSE**

The current gate (`BundleHost.cs:283-344`) is a **fire-and-forget `Task.Delay` loop**:
1. `QueueOldContextCollectionVerification` (`BundleHost.cs:283-292`) starts `_ = VerifyOldContextCollectedAfterReloadReturnsAsync(...)` (fire-and-forget).
2. `VerifyOldContextCollectedAfterReloadReturnsAsync` (`BundleHost.cs:294-317`) does `Task.Delay(16ms)` × 30 = 480ms initial delay, then calls `VerifyOldContextCollectedAsync`.
3. `VerifyOldContextCollectedAsync` (`BundleHost.cs:319-344`) does `Task.Delay(16ms)` × 300 with `forceGc:true` each attempt. If not collected after 300 attempts (4.8s), logs "old ALC still pinned" (`BundleHost.cs:341-343`).

**This replaced the S2b frame-deferred R3 gate.** Commit `79bc07e` ("fix(reload): defer old alc collection probe") removed `using R3` and the `Observable.NextFrame` probe, replacing it with the `Task.Delay` loop. The S2b handover (`vault/handover/2026-06-25-reload-s2b-windowed-probe-placement-handover.md:44-52`) explicitly documented that the gate must "defer the probe to the NEXT FRAME via R3" to "release the in-flight async state machine" before probing. The `Task.Delay` approach runs on a threadpool thread and does NOT coordinate with the Godot main thread's frame loop.

**The `ReloadPolicy.cs` (`project/plugins/App.Resource/ReloadPolicy.cs`) still exists and is proven** (S2a tests green), but it is NOT wired into the production reload path. `BundleHost.ReloadAsync` (`BundleHost.cs:93-115`) calls `UnloadCoreAsync` + `LoadCoreAsync` directly, then `QueueOldContextCollectionVerification`. It does NOT call `ReloadPolicy.ReloadAsync`.

### Suspect G: Resident host ProjectReference to collectible App.World.csproj
**STATUS: PLAUSIBLE — FRAGILE ARCHITECTURE**

`complete-app.csproj:16` has `<ProjectReference Include="..\..\plugins\App.World\App.World.csproj" />`. The comment (lines 12-15) explains this is for `GlobePlateSurfaces` (watertight mesh caps). This means:
- The resident host assembly has a compile-time dependency on `FantaSim.App.World.dll`, `FantaSim.App.World.Composition.dll`, `FantaSim.Cartography.Globe.Core.dll`, etc.
- At runtime, Godot loads the resident host and its dependencies into the resident ALC. So `FantaSim.App.World.dll` exists in the RESIDENT ALC.
- When the world bundle loads, `BundleHost.LoadCoreAsync` (`BundleHost.cs:184`) checks `_isCollectibleAssembly(assemblyName)` — `FantaSim.App.World` IS in `collectible-bundles.json:23` `assemblyNames`, so it passes the check and loads into the COLLECTIBLE ALC.
- Now there are TWO copies of `FantaSim.App.World.dll`: one resident, one collectible. Type identity is split.

This doesn't directly pin the collectible ALC (the binder uses the resident copy). But it creates a situation where:
- If any resident code ever receives a collectible-ALC `Service` object and holds it, that's a pin. The binder DOES receive the `Service` via `registry.TryGet<WorldService>()` — but it doesn't store it in a field; it only uses it as a method-local variable in `Rebind()` and `RefreshPresentationForRegime()`.
- The `_generationSubscription` holds a ref to the `Service` via `GenerationChangedSubscription._owner` — but this is disposed synchronously in `OnResourceRuntimeChanging`.

**This is fragile but currently not a pin.** It becomes a pin if anyone adds a field that stores the world `Service` or any other collectible-ALC object across a reload boundary.

---

## 3. FIX SKETCH for the pin (minimal, ordered)

### Fix 1 (PRIMARY): Restore the frame-deferred R3 gate in BundleHost

The `ReloadPolicy.cs` exists, is tested, and is proven. Wire it into `BundleHost.ReloadAsync`:

1. In `BundleHost.ReloadAsync` (`BundleHost.cs:93-115`), replace the direct `UnloadCoreAsync` + `LoadCoreAsync` + `QueueOldContextCollectionVerification` sequence with a call to `ReloadPolicy.ReloadAsync`:
   - `unmount`: a synchronous main-thread unmount (call the binder's `OnResourceRuntimeChanging` path — already synchronous for `_generationSubscription`, and the deferred `ClearActiveRoot` runs on the next frame before the probe).
   - `unloadReload`: `UnloadCoreAsync` + `LoadCoreAsync` returning the `PluginUnloadResult`.
   - `frameProvider`: `ObservableSystem.DefaultFrameProvider` (set by the `FrameProviderDispatcher` autoload already installed in `complete-app` per commit `c1644b0`).
2. Remove `QueueOldContextCollectionVerification` and the `Task.Delay` probe loop (`BundleHost.cs:283-344`).
3. Re-add `using R3` to `BundleHost.cs`.
4. The `ReloadPolicy` probe does `await Observable.NextFrame(frameProvider)` before each `IsCollected(forceGc:true)` — this coordinates with the Godot main thread frame loop, ensuring the deferred callbacks have run before probing.

**Risk:** `BundleHost` is in `App.Resource.Bundle.Seam` (Godot.NET.Sdk). `ReloadPolicy` is in `App.Resource` (Microsoft.NET.Sdk, pure). The `BundleHost` already references `App.Resource` (via the project structure). The `R3` package is already referenced by `complete-app` and the `FrameProviderDispatcher` autoload sets `ObservableSystem.DefaultFrameProvider`. The `App.Resource.Bundle.Seam` csproj may need a `PackageReference` to `R3` (it was removed in `79bc07e`).

### Fix 2 (SECONDARY): Remove the resident host's ProjectReference to App.World.csproj

Move `GlobePlateSurfaces` and the rendering helpers (`WorldTerrainRamp`, `HypsometricTint`, `PlateIdentityPalette`, `CrustAccentMapper`, `ProvinceTint`, `VertexTintJitter`, `AtmosphereRimStateMapper`, `VerticalScaleLabel`, `BoundaryStyleMapper`) into a SHARED contract assembly (e.g. `contracts/App.World.Rendering`) or into the existing `contracts/App.World`. Then remove the `ProjectReference` to `App.World.csproj` from `complete-app.csproj:16`. This eliminates the dual-copy situation and the type-identity split.

**This is a prerequisite for Question 2's migration** (moving presentation into a collectible bundle), so doing it first serves both purposes.

### Fix 3 (DEFENSIVE): Make the binder's cleanup fully synchronous

In `PlanetPresentationBinder.OnResourceRuntimeChanging` (`PlanetPresentationBinder.cs:1135-1151`), replace the `Callable.From(() => { ClearActiveRoot(); ReleaseNodeGraphView(); }).CallDeferred()` with direct synchronous calls. The `RuntimeChanging` event fires on the same thread that called `Service.ReloadAsync` — which, for the `ResourcePckWatcher` path, is a threadpool thread. Godot node operations (`RemoveChild`, `QueueFree`) must run on the main thread. So the `CallDeferred` is REQUIRED for thread safety. **Do NOT make this synchronous** — instead, ensure the gate (Fix 1) defers to the next Godot frame, which guarantees the deferred calls have run.

---

## 4. MIGRATION FEASIBILITY — moving planet presentation into a collectible bundle

### 4.1 What would move

**Files/types to move from `project/hosts/complete-app/World/` (resident) into a collectible bundle:**
- `PlanetPresentationBinder.cs` (1225 lines) — the binder, its `PlanetTimelineController`, and all Godot node construction.
- `PlanetGenerationGraphSource.cs` (831 lines) — the graph source for the world generation node-graph view.
- `PlateBoundaryFocusRenderer.cs` (198 lines) — the boundary arc ribbon renderer (a `Node3D` subclass).
- The shader code constants (`MagmaShaderCode`, `StagnantShaderCode`, `HypsoPlateShaderCode`, `AtmosphereRimShaderCode`) — currently inline in the binder.

**Dependencies that must become shared (move to contracts):**
- `GlobePlateSurfaces` (`project/plugins/App.World/Globe/GlobePlateSurfaces.cs`) — currently in collectible `App.World`. If presentation moves to a new bundle, this type must be accessible. Options: (a) keep it in `App.World` and have the presentation bundle depend on the world bundle (cross-bundle ref — complex), (b) move it to `contracts/App.World.Rendering` (shared), or (c) move it into the presentation bundle itself.
- Rendering helpers: `WorldTerrainRamp`, `HypsometricTint`, `PlateIdentityPalette`, `CrustAccentMapper`, `ProvinceTint`, `VertexTintJitter`, `AtmosphereRimStateMapper`, `VerticalScaleLabel`, `BoundaryStyleMapper` (`project/plugins/App.World/Rendering/`). Same options as above.
- `GlobeViewModeResolver`, `RegimeSurfaceResolver`, `MantleSurfaceGate`, `WorldViewContentGate` — already in `contracts/App.World/Composition/` (shared). No move needed.
- `CrustSnapshotTickSeries` — already in `contracts/App.World/GenerationGraph/` (shared). No move needed.
- `FantaSim.Cartography.Globe.Core` (`GlobeSurfaceBuilder`, `IGlobeSurfaceBuilder`) — external package, currently NOT shared. Would need to be added to the shared `exactMatches` or `prefixes` in `Bootstrap.cs`, OR the presentation bundle brings its own copy (type-identity split with the world bundle's copy).

### 4.2 Seams that must exist at the resident side (T1 contracts)

The resident host needs to talk to the presentation bundle through contract interfaces:

1. **`IPlanetPresentationMount`** (new contract, in `contracts/App.World/Presentation/`):
   - `void Mount(PlanetPresentationDocument document, IBundleSceneRegistry sceneRegistry, Node3D stageMount)` — mount the presentation under the stage Environment.
   - `void Unmount()` — synchronously remove all nodes and release all references.
   - `void ApplyTick(long tick)` — drive timeline tick updates.
   - `void Rebind(PlanetPresentationDocument document)` — refresh with a new document.
   - The resident host calls `Mount` after the world bundle loads, `Unmount` before it unloads.

2. **`IPlanetPresentationViewSource`** (new contract) — for the node-graph view registration:
   - `IViewSource ViewSource { get; }` — the presentation bundle owns the `NodeGraphViewSource`.
   - The resident host's `IViewHost` mounts/unmounts it.

3. **`ITimelineController`** (existing contract, `contracts/App.World/Composition/ITimelineController.cs`) — the presentation bundle's `PlanetTimelineController` implements this. The resident host no longer creates it; the bundle registers it via `RegisterOwned<ITimelineController>`.

### 4.3 Mount/unmount protocol that avoids pinning

The static-SceneTree-handoff pin (finding #2 from `vault/handover/2026-06-25-world-to-stage-collectible-bundle.md:41-42`) is the core constraint: a resident type setting a static on a collectible-ALC type pins the ALC. The protocol:

1. **Resident side (`Host.cs`):**
   - On world bundle load: resolve `IPlanetPresentationMount` from the registry (registered by the bundle), call `Mount(document, sceneRegistry, stageMountNode)`. The `stageMountNode` is obtained from `sceneRegistry.GetNodeOrNull("stage", PlanetLayerMountPath)` — this is a Godot `Node` (engine type, no ALC pin).
   - On world bundle `RuntimeChanging`: call `IPlanetPresentationMount.Unmount()` **synchronously**. This frees all Godot nodes and drops all references to bundle-typed objects.
   - On world bundle `RuntimeChanged`: call `IPlanetPresentationMount.Rebind(newDocument)`.

2. **Bundle side (the presentation bundle):**
   - `InitializeAsync`: create the `PlanetPresentationBinder` equivalent, register `IPlanetPresentationMount` and `ITimelineController` in the shared registry.
   - `ShutdownAsync`: call `Unmount()` (free all Godot nodes, dispose graph view, drop all refs), unregister from the registry.
   - **Critical:** the bundle's `Unmount` must run BEFORE the ALC unloads. This is guaranteed by `ShutdownAsync` running inside `RemoveGroupAsync` before `ALC.Unload()`.
   - **Critical:** Godot node operations (`RemoveChild`, `QueueFree`) must run on the main thread. If `ShutdownAsync` runs on a threadpool thread (via `RemoveGroupWithDiagnosticsAsync`), the `Unmount` must marshal to the main thread. The existing `Callable.From(...).CallDeferred()` pattern works for the `RuntimeChanging` path (which fires on the calling thread), but `ShutdownAsync` needs a synchronous main-thread marshal. Options: (a) use `Godot.Callable.From(...).Call()` if already on main thread, (b) use a `Godot.SceneTree.CreateTimer(0)` one-shot, or (c) ensure `RemoveGroupAsync` runs on the main thread (it does for the `ResourcePckWatcher` path via the `RemoteBridgeNode._Process` main-thread dispatch — see S2b handover gotcha about threading).

3. **The `IViewHost.Mount` vs `ShowAsync` re-entrancy:**
   - The node-graph view (`NodeGraphViewSource`) is mounted via `viewHost.Mount(viewId)` (`PlanetPresentationBinder.cs:196`). The `IViewHost` is a RESIDENT service. The bundle calls `Mount` on a resident object — this is bundle→resident, fine for collection. On `Unmount`, the bundle calls `viewHost.UnmountNow(viewId)` (`PlanetPresentationBinder.cs:1194`) and `viewHost.Unmount(viewId)` (line 1195). Both are resident methods. No pin.

### 4.4 Bounded packet breakdown

| Packet | Files | LOC (approx) | Risk |
|--------|-------|------|------|
| P1: Extract rendering types to shared contracts | Move `WorldTerrainRamp`, `HypsometricTint`, `PlateIdentityPalette`, `CrustAccentMapper`, `ProvinceTint`, `VertexTintJitter`, `AtmosphereRimStateMapper`, `VerticalScaleLabel`, `BoundaryStyleMapper` from `App.World/Rendering/` to `contracts/App.World/Rendering/`. Move `GlobePlateSurfaces` from `App.World/Globe/` to `contracts/App.World/Globe/` OR a new `contracts/App.World.Rendering` project. Add `FantaSim.Cartography.Globe.Core` to shared `exactMatches` in `Bootstrap.cs`. | ~800 LOC moved, ~50 LOC config change | LOW — pure move, no logic change. Tests in `App.World.Tests` need csproj ref updates. |
| P2: Remove `complete-app.csproj` ProjectReference to `App.World.csproj` | Remove line 16 from `complete-app.csproj`. Verify the binder still compiles (it should, via P1's contract moves). | 1 line removed | LOW — compile-time check. |
| P3: Create `IPlanetPresentationMount` contract | New file in `contracts/App.World/Presentation/IPlanetPresentationMount.cs`. | ~30 LOC | LOW. |
| P4: Create the presentation bundle | New `project/bundles/presentation/manifest.json`. New `project/plugins/App.Presentation/` project (Godot.NET.Sdk for Godot node access). Move `PlanetPresentationBinder.cs`, `PlanetGenerationGraphSource.cs`, `PlateBoundaryFocusRenderer.cs` from `hosts/complete-app/World/` into `App.Presentation/`. Add `App.Presentation` to `collectible-bundles.json`. | ~2250 LOC moved, ~50 LOC new | MEDIUM — the binder uses Godot APIs (`Node3D`, `MeshInstance3D`, `ShaderMaterial`, `Callable.From`). The bundle must be `Godot.NET.Sdk` (like `App.Timeline` / `App.Stage`). The `PlanetTimelineController` and `ITimelineController` registration move into the bundle. |
| P5: Wire the resident host to the new bundle | In `Host.cs`, replace `new PlanetPresentationBinder(...)` with a resolve of `IPlanetPresentationMount` from the registry after the presentation bundle loads. Move the `LoadWorldBundleAndMountPlanetAsync` logic to load the presentation bundle too. | ~50 LOC changed | MEDIUM — load order matters (world bundle must load before presentation bundle, since the binder needs the world `IService`). |
| P6: Verify hot-reload | `task bundle:presentation` → `task bundle:install` → edit a tier → watch-reload → confirm `old ALC collected`. | N/A | Requires the Fix 1 gate fix first. |

**Total estimated effort:** 2-3 focused sessions. P1 is the largest mechanical task. P4 is the most architecturally significant (moving Godot-typed code into a collectible bundle).

### 4.5 Risks

1. **Godot node lifecycle in a collectible bundle.** The presentation bundle creates Godot nodes (`Node3D`, `MeshInstance3D`, etc.) and mounts them under the stage Environment (a RESIDENT node). When the bundle unloads, `ShutdownAsync` must free ALL these nodes. If any node survives (e.g. a deferred `QueueFree` that hasn't run), the node's C# wrapper object may hold a ref to the collectible ALC's type. **Mitigation:** `Unmount()` must call `RemoveChild` + `QueueFree` on every created node, and the gate must defer to the next frame to let `QueueFree` complete.

2. **`FantaSim.Cartography.Globe.Core` type identity.** If this assembly is shared (added to `exactMatches`), both the world bundle and the presentation bundle use the resident copy — no type-identity split. If NOT shared, each bundle gets its own copy, and passing `GlobeSurface` objects between them fails (different runtime types). **Mitigation:** add `FantaSim.Cartography.Globe.Core` and `FantaSim.Cartography.Shared` to the shared `exactMatches` in `Bootstrap.cs`. This is a dependency-closure expansion that needs the same audit discipline as the original world-bundle closure audit (`.agent/run/dispatch/world-dep-closure-audit.glm.md`).

3. **Cross-bundle dependency.** If the presentation bundle needs types from the world bundle (e.g. `GlobePlateSurfaces` if it stays in `App.World`), unloading the world bundle would need to unload the presentation bundle first. This is the inter-bundle dependency DAG problem (deferred per the S2b handover, "Phase 2 later"). **Mitigation:** P1 moves `GlobePlateSurfaces` into shared contracts, eliminating the cross-bundle dependency. The presentation bundle then depends only on shared contracts + the world `IService` contract interface.

4. **The `ITimelineController` registration lifetime.** Currently the resident binder registers `ITimelineController` and it lives for the app lifetime. If it moves into the bundle, the `WorldPlugin`'s `CrustGenerationTrigger` (in the world bundle) resolves it from the registry — but the presentation bundle must load BEFORE the world bundle's crust trigger arms. Load order: stage → world → presentation → timeline. The `WorldPlugin.InstallCrustTrigger` already handles late arming via `PresentationRequested` and `SubscribeGenerationChanged` (`WorldPlugin.cs:138-167`), so this is compatible.

5. **Static shader caches.** The binder's static `Shader` fields (`PlanetPresentationBinder.cs:1075-1083`) are Godot `Shader` objects. If moved into the bundle, they become collectible-ALC objects. On unload, they must be freed. **Mitigation:** make them instance fields, or null them in `ShutdownAsync`.

---

## 5. OPEN QUESTIONS

1. **Does `PluginArchi.Extensibility.Hosting.PluginHost.RemoveGroupWithDiagnosticsAsync` call `ShutdownAsync` SYNCHRONOUSLY before `ALC.Unload()`?** The monodis decompilation was broken. The entire fix assumes `ShutdownAsync` completes before the ALC starts unloading. If `Unload()` is called concurrently with `ShutdownAsync` (or before it completes), the dispose handles may not have run, leaving registry refs alive. **How to verify:** add a log line at the start and end of `WorldPlugin.ShutdownAsync` and at the `RemoveGroupWithDiagnosticsAsync` return, then check ordering in the windowed log.

2. **Is `RemoveGroupWithDiagnosticsAsync` called on the Godot main thread?** For the `ResourcePckWatcher` path (`Service.cs:244-264`), the watcher's `ScheduleReload` runs on a threadpool thread, calls `_service.ReloadAsync` which calls `Provider.ReloadAsync` → `BundleHost.ReloadAsync`. The S2b handover says threading was fixed via `RemoteBridgeNode._Process` (main-thread dispatch). But the `ResourcePckWatcher` path may bypass the remote bridge. If `RemoveGroupWithDiagnosticsAsync` runs on a threadpool thread, `WorldPlugin.ShutdownAsync` runs there too — and any Godot node operations in the presentation bundle's `ShutdownAsync` would fail with "only allowed from the main thread." **How to verify:** check whether `Service.ReloadAsync` marshals to the main thread, or whether the `ResourcePckWatcher` path needs the same `RemoteBridgeNode` dispatch.

3. **Should `FantaSim.Cartography.Globe.Core` and `FantaSim.Cartography.Shared` be added to the shared `exactMatches`?** This would prevent type-identity splits between the world bundle and a future presentation bundle. But it expands the shared resident ALC's dependency closure. The original world-bundle closure audit (`.agent/run/dispatch/world-dep-closure-audit.glm.md`) classified these as COLLECTIBLE. Making them shared is a policy change that needs its own audit.

4. **Does the `App.World.Composition` assembly (collectible) have any static state that survives `ShutdownAsync`?** `OnsetRoster.PlateTopologyStreamIdentity` (`OnsetRoster.cs:39`) is a `static readonly` field. If the world bundle's ALC unloads and a new one loads, the new ALC's `OnsetRoster` static is re-initialized — no cross-ALC pin. But if any shared (resident) code holds a ref to a `TruthStreamIdentity` created by the old ALC's `OnsetRoster`, that would pin. No such path was found, but a deeper audit of `FantaSim.World.Fields.Contracts` consumers would confirm.

5. **Is the `NodeGraphViewSource` (resident, `App.Ui.NodeGraph`) truly safe?** The binder creates it and registers it via `RegisterOwned<IViewSource>`. The `ReleaseNodeGraphView()` (`PlanetPresentationBinder.cs:1187-1207`) disposes the registration and calls `viewHost.UnmountNow/Unmount`. This is deferred via `CallDeferred`. If the gate probes before this deferred call runs, the `IViewSource` registration in the registry still holds a ref to the `NodeGraphViewSource` — but `NodeGraphViewSource` is a RESIDENT type, so it doesn't pin the collectible ALC. The `_graphSource` (`PlanetGenerationGraphSource`) is also resident. **No pin.** But the `_graphBinding` (`PlanetGenerationTimelineGraphBinding`) holds a ref to `_timeline` (resident `PlanetTimelineController`) and `_graphSource` (resident). All resident. No pin.