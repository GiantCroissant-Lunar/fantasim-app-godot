# Handover — bundle hot-reload, DI scoping, and the ALC-collection gate

**Date:** 2026-06-24 · **Branch:** `main` · **Base commit:** `9b8ad2b` (working tree NOT committed)
**Status:** Issue 4 DONE + windowed-verified; reload command DONE (works, gate not yet sound);
design + spec APPROVED and ready to implement. Next session: implement the PluginArchi diagnostic.

---

## TL;DR

We started from "should we refactor App.Command's static composition root / adopt VContainer?" and
ended with a full bundle hot-reload redesign. Landed: the **SceneActivatorBase** refactor (Issue 4,
windowed-verified) and a **`resource.reload_bundle`** command. Designed + reviewed + locked: a
**two-concern bundle delivery model** with an Addressables-style catalog, and a **WeakReference
ALC-collection gate** that requires a small, approved **PluginArchi API addition** (the immediate
next task). Also: made `codex` the lowest-priority delegation CLI.

## How we got here (arc)

1. **DI / static composition root.** The real hot-reload hazard isn't the `static` keyword — it's
   what the resident `IRegistry` (ServiceArchi, flat) retains, plus event subscriptions. "Dropped
   ref != ALC unloaded" — you need a `WeakReference`.
2. **`*-archi` check.** `dependency-archi` IS the VContainer-concept lib (container-agnostic
   parent/child `DependencyScope` topology + MS.DI adapter) — but it is **NOT wired into fantasim**.
3. **Actual fantasim architecture** (corrected by user): three layers — ServiceArchi `IRegistry`
   (flat resident) + nested MS.DI `ServiceProvider`s via **App.SceneFlow** (`app-root → stage →
   {assist,timeline}`, hand-rolled parent/child) + **PluginArchi** ALC lifecycle. App.Common/Stage/
   Assist are the DI-binding "scenes"; App.Command is a service.
4. **ref-projects** resolves more: uses DependencyArchi declaratively, has `cross-alc-rules.md`,
   and an RFC for the dependency-archi child-scope-singleton gap.
5. **Issue 4** (SceneActivatorBase) implemented + windowed-verified.
6. **Watcher is wrong.** `ResourcePckWatcher`/`WatchResource` mixes runtime/editor, breaks on
   remote/S3, and is only wired for view bundles anyway → replace with a command/ingress trigger.
7. **Two-concern model + Addressables catalog** designed, cross-model reviewed (opencode/GLM-5.2),
   findings folded in.
8. **ALC-collection gate**: the sound signal is a `WeakReference`, not `Directory.Delete`. yokan
   can't reach PluginArchi's `IsContextCollected` → needs an approved PluginArchi API addition.

---

## DONE this session (verification status)

- **Issue 4 — `SceneActivatorBase`** (DONE, **windowed-verified**). Hoisted the duplicated
  shared-kernel forwarding + child-scope build/teardown out of the three scene activators into one
  base in `contracts/App.SceneFlow`; collapsed the 3 identical `*Activation` classes into one shared
  `SceneActivation`. 5 headless tests green; all 3 bundles compile (0 warnings). **Windowed run
  confirmed** stage/assist/timeline all active sharing kernel `#51782583` across ALCs, no errors.
- **`resource.reload_bundle` command** (DONE, **works**; gate NOT sound yet). Registered in
  `CommandComposition`; triggers `App.Resource.IService.ReloadAsync` → real unload+reload via the
  remote ingress (`fantasim-cmd.py cmd resource.reload_bundle '{"bundleId":"assist"}'` → `ok:true`,
  log showed `Bundle unloaded: assist` → `Bundle loaded: assist`). **BUT no `old ALC collected`**
  evidence — yokan can't verify collection (see gotchas).
- **SceneFlow tests** — replaced the placeholder `App.SceneFlowSmokeTests.cs` (`Assert.True(true)`)
  with 3 real scoping tests + added 2 `SceneActivatorBase` tests (kernel-sharing, child-before-parent
  teardown, inactive-parent guard, forwarding, child-scope disposal).
- **3 design docs** (see Artifacts).
- **Skill: `codex` → lowest priority** in `external-agent-delegation` (new "Selection Priority"
  section; routing/cost lines fixed). Live via the symlinked deployment.
- **2 chips spawned:** `task_69b28d06` (audit placeholder tests), `task_1987f8bf` (fix stale
  plugin-archi CLAUDE.md).

### File changes — THIS session (uncommitted, on `main`)

```
M  project/contracts/App.SceneFlow/App.SceneFlow.csproj        (+ MEDI + Logging.Abstractions)
?? project/contracts/App.SceneFlow/SceneActivatorBase.cs       (new base)
?? project/contracts/App.SceneFlow/SceneActivation.cs          (new shared activation)
M  project/plugins/App.Stage/StageActivator.cs                 (extends base)
D  project/plugins/App.Stage/StageActivation.cs
M  project/plugins/App.Assist/AssistActivator.cs               (extends base)
D  project/plugins/App.Assist/AssistActivation.cs
M  project/plugins/App.Timeline/TimelineActivator.cs           (extends base)
D  project/plugins/App.Timeline/TimelineActivation.cs
M  project/plugins/App.Command/HostComposition/CommandComposition.cs   (resource.reload_bundle)
M  project/plugins/App.Command/App.Command.csproj              (+ contracts/App.Resource ref)
M  project/tests/App.SceneFlow.Tests/App.SceneFlowSmokeTests.cs        (real SceneFlowScopingTests)
?? project/tests/App.SceneFlow.Tests/SceneActivatorBaseTests.cs
M  project/tests/App.SceneFlow.Tests/App.SceneFlow.Tests.csproj        (+ ServiceArchi.Core, MEDI)
?? vault/architecture/bundle-delivery-and-loading.md
?? vault/specs/2026-06-24-dependency-archi-child-scope-singletons-followup.md
?? vault/specs/2026-06-24-pluginarchi-alc-collection-diagnostic.md
?? vault/handover/2026-06-24-bundle-hot-reload-di-scoping.md   (this file)
```
Also (workspace root, NOT git-tracked): `.agent/skills/04-tooling/external-agent-delegation/SKILL.md`
edited; `.agent/run/dispatch/*.txt` + `.agent/logs/{opencode}/...` dispatch prompts/logs.

### ⚠️ Working tree also has UNRELATED uncommitted changes (NOT this session)

The tree was not clean when this session started. These are **not** mine — do not attribute or
blind-commit them; review/segregate (path-scoped commits) before committing anything:
```
M  project/hosts/complete-app/Host.cs        D  project/hosts/complete-app/Host.Gpu.cs(.uid)
M  project/plugins/App.Assist/Bootstrap.cs   ?? project/plugins/App.Assist/GpuSmokeChecks.cs
M/D/?? project/tests/App.Resource.Tests/*    (App.ResourceSmokeTests.cs -> ResourceServiceTests.cs)
M/D/?? project/tests/App.Ui.Tests/*          (App.UiSmokeTests.cs -> UiServiceTests.cs)
```
(The test files look like the placeholder-audit work; provenance unconfirmed.)

---

## Key decisions (with rationale)

- **Trigger = ingress, not a file-watcher.** Remove `IService.WatchResource`/`ResourcePckWatcher`.
  A "reload" is invoked by any ingress (UI button / build step / CD / catalog poll), all dispatching
  `resource.reload_bundle`. Works regardless of where bundles live (local/S3/CDN/embedded).
- **Two concerns:** (1) publish — build + place bytes + publish catalog atomically + notify
  (outside runtime); (2) adopt — fetch + verify + swap (runtime). Catalog is the contract.
- **Catalog = Addressables, selectively:** address indirection + versioned content catalog
  (`address → {scheme-tagged location, hash, deps}`, local + remote) + provider-per-scheme. Evolve
  `collectible-bundles.json`. Skip asset-GUID/build-pipeline; defer reference-counting until shared bundles.
- **Collection gate = `WeakReference`, NOT `Directory.Delete`** (see gotcha #1).
- **Scene-tier reload = `SceneFlow.Exit → Enter`** (not bare `BundleHost.ReloadAsync`), leaf-scoped.
- **PluginArchi diagnostic = Option A** (approved): a **separate `IPluginHostDiagnostics` interface**
  in `PluginArchi.Extensibility.Abstractions` (where `IPluginHost` actually lives), returning a
  weak-only `PluginUnloadResult`. `IPluginHost` untouched → no consumer breakage.

## 🛑 Critical gotchas the next session MUST know

1. **`Directory.Delete` succeeding ≠ ALC collected on macOS/Linux.** `unlink` of a still-mapped DLL
   succeeds regardless of ALC liveness → ref's `ScheduleTempCleanupSweep` "old ALC collected" is a
   **false positive** on our dev OS. The gate MUST be `WeakReference` + forced GC. (Sweep is fine
   for *cleanup*, never as the collection *proof*.)
2. **Weak-only invariant.** The collection diagnostic must hold **only** a `WeakReference`. Any
   strong ref to the loader/group/ALC makes *the diagnostic itself the pin* → false "still pinned".
3. **SceneFlow holds the scene-tier pin.** `SceneFlowProvider._active` → old `SceneActivation` →
   child provider → bundle-typed `Bootstrap` pins the ALC. A raw `BundleHost.ReloadAsync` leaks it;
   only `SceneFlow.ExitAsync` releases it. (The activator itself is dropped by `StagePlugin.ShutdownAsync`
   disposing its `RegisterOwned` handle.)
4. **Parent-reload cascade.** `SceneFlow.ExitCoreAsync` exits children first, so reloading `stage`
   tears down `assist`+`timeline` and does NOT restore them. Scope reload to **leaf** scenes, or
   capture+restore the subtree.
5. **yokan has no working collection gate today.** ref's `BundleHost.ScheduleTempCleanupSweep` was
   never ported; yokan emits NO `old ALC collected`. The verify-windowed "gate" is currently
   *unverified* in yokan — that's what the PluginArchi diagnostic fixes.
6. **Verify in the EXPORTED WINDOWED app**, not headless/unit-only (project rule; `verify-windowed`
   skill). `task build:godot:desktop` → `task bundles` → `task bundle:install` → run the exe.
   Reload command needs `FANTASIM_REMOTE_ENABLED=1` (ingress on :19292; drive via `tools/fantasim-cmd.py`).
7. **`codex` is now lowest-priority** for delegation; prefer kimi/opencode/kilo/pi/agy.
8. **plugin-archi CLAUDE.md is stale** — says `IPluginHost` is in `Hosting.Abstractions`; it is
   actually in `Extensibility.Abstractions` (`Hosting.Abstractions` is net9.0, holds only validator/
   orderer/orchestrator). Chip `task_1987f8bf` tracks the fix.
9. **`dependency-archi` is NOT wired into fantasim** (it hand-rolls SceneFlow). Its MS.DI adapter has
   a child-scope-singleton limitation (option-1 partially in code, class-doc stale). Separate
   follow-up spec exists.

---

## NEXT STEPS (ordered)

1. **IMMEDIATE — implement the PluginArchi ALC-collection diagnostic** (spec APPROVED, Option A):
   `vault/specs/2026-06-24-pluginarchi-alc-collection-diagnostic.md`. New `IPluginHostDiagnostics` +
   weak-only `PluginUnloadResult` in `PluginArchi.Extensibility.Abstractions`; `PluginHost` implements
   `RemoveGroupWithDiagnosticsAsync` (drops all strong refs, returns weak-only result; `IsCollected(forceGc)`
   = one bounded `Collect→WaitForPendingFinalizers→Collect`). **Acceptance = 2 tests:** real unload →
   `IsCollected(forceGc:true)` eventually true; retained strong ref → stays false. Additive (minor
   version bump, not breaking). **User pref: lead-session, or dispatch to opencode/agy (NOT codex)
   with those 2 tests mandated + lead verifies.** This is a `plate-projects` change — user gave
   conditional go pending these conditions; confirm before editing plate-projects.
2. **Then — yokan consumes it:** `BundleHost` casts to `IPluginHostDiagnostics`, owns the bounded
   frame-paced poll, logs `old ALC collected` / `still pinned`, gates/degrades the reload.
3. **Bundle-delivery Phase A** (one work item, NOT independent slices): remove the watcher → make
   `resource.reload_bundle` the sole trigger → wire `task bundle:<tier>` to it → scene-tier reload via
   `SceneFlow.Exit→Enter` (leaf-scoped, stage-then-swap, `CacheMode.ReplaceDeep`). Not "done" until
   the gate passes for a scene tier in the windowed app.
4. **Phase B (independent):** catalog + providers (evolve `collectible-bundles.json`; `file`/`remote`
   providers; remote hash-verify; payload `location`/`version`).
5. **Phase C (independent):** UI reload ingress; optional remote-catalog poll.
6. **Separate follow-up:** dependency-archi child-scope-singleton fix
   (`vault/specs/2026-06-24-dependency-archi-child-scope-singletons-followup.md`).
7. **Housekeeping:** commit this session's changes (path-scoped, separate from the unrelated tree
   changes); resolve the 2 chips; consider committing Issue 4 (verified) on its own.

## Artifacts

- **Design:** `vault/architecture/bundle-delivery-and-loading.md` (two-concern + catalog; findings
  tagged `[R-Fnn]`).
- **Approved spec (next task):** `vault/specs/2026-06-24-pluginarchi-alc-collection-diagnostic.md`.
- **Follow-up spec:** `vault/specs/2026-06-24-dependency-archi-child-scope-singletons-followup.md`.
- **Cross-model review (opencode/GLM-5.2):** `.agent/logs/opencode/bundle-design-review-20260624-142103.log`.
- **Pin/gate probe (opencode/GLM-5.2):** `.agent/logs/opencode/alc-gate-probe-20260624-143727.log`.
- **Existing context:** `vault/architecture/cross-alc-rules.md`, `vault/architecture/multi-scene-di-scoping-review.md`.
- **PluginArchi primitive to surface:** `IsolatedLoader.IsContextCollected(bool forceGc)` (public,
  but trapped behind `internal` `PluginGroup.Loader` / `internal sealed PluginHost`).
- **Current contract:** `plate-projects/plugin-archi/.../PluginArchi.Extensibility.Abstractions/IPluginHost.cs:83`
  (`RemoveGroupAsync` returns `ValueTask<bool>` — changing it would be breaking; hence Option A).

## Open questions

- Catalog format: extend `collectible-bundles.json` in place vs new `catalog.json` (lean: extend).
- Catalog resolution home: `App.Resource.Service` vs a `BundleCatalog` (affects idempotency fast-path).
- Remote catalog trust/rollback (signing / pinned TLS / monotonic `minCatalogVersion`).
- Whether to offer the diagnostic for `ReloadGroupAsync` too (likely yes, same shape).
