# Handover: World → stage scope (collectible `world.pck`) — data bundle built, windowed punch-list

**Date:** 2026-06-25
**Branch:** `feat/world-to-stage-phase1` (off `main`)
**Status:** the collectible World **data** bundle is IMPLEMENTED + COMMITTED + BUILDS + PACKAGES, and the
app **boots clean with it**. The windowed verify found **3 functional gaps** — it is **not yet
functionally complete**. The hard part (ALC dependency partitioning + packaging) is done and proven
safe; the remaining work is a precise punch-list.

---

## TL;DR — resume here

1. `git checkout feat/world-to-stage-phase1` (HEAD = `a101232`).
2. Three fixes remain (details in **§Remaining work**): **wire the world-bundle load**, **restore
   `ITimelineController`**, **enable the remote ingress** for the collect-gate test.
3. Then re-export (`task build:godot:desktop` → `task bundle:world` → `task bundle:install` →
   `task run:exported`) and confirm the **`old ALC collected`** line after a `resource.reload_bundle world`.
4. The **globe rendering is deliberately deferred** to a separate follow-up (see §Deferred).

---

## Goal & context

Make the **World** service a collectible bundle so its lifetime can be **stage-owned** (load/unload with
stage, ALC collects on unload) — the concrete instance of the resident-vs-collectible scope-ownership
model. Design + plan:
- [vault/architecture/service-scope-ownership.md](../architecture/service-scope-ownership.md) — the axis (resident vs collectible, reference-direction invariant, tier×scope, target topology).
- [vault/plans/2026-06-25-world-to-stage-scope.md](../plans/2026-06-25-world-to-stage-scope.md) — the migration plan; its **"Phase 2 — dependency-closure audit RESOLVED"** section is the executable spec.
- [.agent/run/dispatch/world-dep-closure-audit.glm.md](../../.agent/run/dispatch/world-dep-closure-audit.glm.md) — the full 51-assembly closure classification (GLM, validated).

## Durable architecture findings (the learnings — don't re-derive these)

1. **Plugin lifecycle is BUNDLE-driven.** `BundleHost.AddGroupAsync`/`RemoveGroupAsync`
   ([App.Resource.Bundle.Seam/BundleHost.cs:209,232](../../project/plugins/App.Resource.Bundle.Seam/BundleHost.cs:209))
   fire a bundle plugin's `InitializeAsync`/`ShutdownAsync`. The resident `PluginHostBuilder`
   ([Bootstrap.cs](../../project/plugins/App.Common/Bootstrap.cs)) adds **no** resident assemblies — so a
   resident `[Plugin]` is **never discovered**. **A service's lifetime == its bundle's load/unload.**
   "Stage-owned" therefore requires a collectible bundle, not a resident plugin.
2. **The static-handoff pins the ALC.** A resident type setting a static on a collectible-ALC type
   (glm's `WorldPlugin.PendingSceneTree`) is a resident→collectible strong ref → the ALC never collects.
   The globe's `GetTree()` must come from **inside** the bundle (an `Environment.tscn` script), not a
   resident handoff.
3. **World's dependency closure is huge** (~51 assemblies): **19 forced-SHARED** (resident overlap —
   `World.Fields.*`, `UnifyEcs.*`, `UnifyStorage.Abstractions/LiteDb`, `UnifyMaths`, `Arch`, `TimeDete.`,
   `World.Shared.Contracts`), **32 COLLECTIBLE** (World-only engine/Cartography/SurrealDb/MessagePack).
   Full classification in the audit; applied to the policy (see §What was done).
4. **Non-scene bundles don't auto-load.** Scene bundles (`stage`/`assist`/`timeline`) load via
   `SceneFlow.EnterAsync`. Non-scene bundles (`activity`, and now `world`) load only on an **explicit**
   `resource.LoadFromDirectoryAsync(id)` / consumer trigger. **This is gap #1 below.**
5. **The SceneTree is not threaded through SceneFlow** (multi-scene-di-scoping-review Issue 3) — the
   reason the globe needs the in-bundle `Environment.tscn` bridge.

---

## What was done (commits)

`main`:
- `1b5311b` `feat(iii): vplanet external-tool nodegraph slice` — a prior session's WIP, committed as a checkpoint (unrelated to this work).
- `9ca1f51` `docs(scope): service-scope-ownership architecture + world-to-stage plan`.

`feat/world-to-stage-phase1`:
- `451ac69` `refactor(world): groundwork for collectible world bundle` — Phase-1 restructure (NOT functional on its own; resident plugin had no lifecycle).
- `661ae5b` `docs(world): record resolved Phase-2 dependency-closure spec`.
- **`a101232` `feat(world): collectible world data bundle (world.pck)`** — the current implementation (below).

### `a101232` — the data bundle (11 files)

| File | Change |
|---|---|
| `App.Common/Bootstrap.cs` | `SharedAssemblyPolicy`: removed `"FantaSim.World."` prefix; added `"UnifyEcs."`/`"TimeDete."` prefixes; set the 7 `exactMatches` (resident-forced shared). |
| `config/collectible-bundles.json` | added `world` entry: `pluginAssembly FantaSim.App.World.dll` + `assemblyNames` [`FantaSim.App.World`, `FantaSim.App.World.FieldView`, `FantaSim.App.World.Composition`]. |
| `App.World.Composition.csproj` | `<AssemblyName>FantaSim.App.World.Composition</AssemblyName>` (E.1 — DLL was unprefixed). |
| `App.Command.csproj` | dropped the `App.World` **impl** ProjectReference; kept the contract (E.3). |
| `App.World.csproj` | added PluginArchi Abstractions + SourceGenerators package refs + `contracts/App.Command` ref. |
| `complete-app.csproj` | removed the `App.World.Seam` ProjectReference (E.2). |
| `Host.cs` | deleted the `WorldPlugin.PendingSceneTree = GetTree()` handoff line (E.2). |
| `App.World/WorldPlugin.cs` | **NEW** (renamed from `App.World.Seam/WorldPlugin.cs`): pure-C# data-only `[Plugin("app.world")] ILifecyclePlugin` — `WorldComposition.ComposeWorld` + direct `world.run_generation_graph` registration; no globe/`WorldView`/`CellElevation`. Mirrors `App.Ui.Activity/Plugin.cs`. |
| `App.World.Seam/WorldPlugin.cs` | **DELETED**. |
| `Taskfile.yml` | added `bundle:world:build`, `bundle:world`, the `world.pck` `bundle:install` cp line, `bundle:world` in `bundles:`. |
| `project/bundles/world/manifest.json` | **NEW** world manifest. |
| `content-app/export_presets.cfg` | added the `"world PCK"` preset. |

**Verified:** `dotnet build complete-app` = 0 errors. `task bundle:world` produces `world.pck` (307 KB) with the
3 correctly-named DLLs (E.1 confirmed: `FantaSim.App.World.Composition.dll`). App exports + `bundle:install`
copies `world.pck` next to the exe.

`App.World.Seam` stays in the repo, **unreferenced/dormant** — it holds the globe (`WorldViewComposition` /
`GlobeView` / `TimelineController`) for the deferred follow-up.

---

## Windowed verify — findings (the punch-list)

Ran `FANTASIM_REMOTE_ENABLED=1 <exe>` (windowed) + observed the console. Log: `/tmp/app-run.log`.

**✅ Safe:** app boots clean with the new policy — no `BundleHost` lint throw, no errors;
`stage`/`assist`/`timeline` load + enter normally. The dependency partitioning does **not** break anything.

**❌ Gap 1 — world bundle never loads.** `world.pck` is installed but nothing triggers its load (finding #4
above). `WorldPlugin` never runs; World never composes. Console shows
`Command (orchestration degraded, 4 commands)` (the `world.*` command is absent because World isn't loaded).

**❌ Gap 2 — timeline regression.** `Timeline: no ITimelineController registered; timeline service will be
inert.` The `ITimelineController` registration lived in `WorldViewComposition` (the globe seam), which is no
longer composed. The data-bundle split entangled a non-globe concern (the timeline controller) with the
deferred globe.

**❌ Gap 3 — remote ingress unreachable.** `FANTASIM_REMOTE_ENABLED=1` did **not** open `:19292`
(connection refused), so `tools/fantasim-cmd.py cmd resource.reload_bundle '{"bundleId":"world"}'` couldn't
fire and the `old ALC collected` gate was **not** exercised.

---

## Remaining work (next session)

### Fix 1 — wire the world-bundle load
Two options:
- **(a) Boot-load (proves COLLECTION fastest):** add `resource.LoadFromDirectoryAsync("world")` after the
  composition sequence in `Host.cs` (after `CommandComposition` so the `world.*` command registers). Gives
  a collectible-but-boot-lifetime World; reload → collect is then testable. Good for proving the ALC gate.
- **(b) Scene bundle under stage (true STAGE-OWNERSHIP):** convert `WorldPlugin` to register a
  `WorldActivator : SceneActivatorBase` (`SceneId="world"`) like `TimelinePlugin`/`StagePlugin`, and add
  `EnterAsync(new SceneRequest("world","stage"))` in `Host.cs:EnterInitialScenes`. World then loads/unloads
  with stage. The activator composes the data services; dispose tied to `ShutdownAsync` on unload.
  Recommend (a) first to confirm the collect gate, then (b) for the actual ownership semantics.

### Fix 2 — restore `ITimelineController`
The controller impl (`TimelineController`) lives in `App.World.Seam` (Godot), which the data bundle excludes.
Decide where it comes from with the globe deferred: a minimal **resident** registration, a tiny stub, or
pull the timeline-controller concern out of `WorldViewComposition` into something that composes without the
globe. (Check `App.World.Seam/HostComposition/WorldViewComposition.cs` for the original `RegisterOwned<ITimelineController>`
and `App.World.Seam/TimelineController.cs`.)

### Fix 3 — enable the remote ingress in the exported app
`FANTASIM_REMOTE_ENABLED=1` didn't open `:19292`. Check `config/app.json` `"remote"` block (it has
`"bind": "127.0.0.1:19292"` — verify an `"enabled"` flag and whether the env var overrides it), and
`RemoteIngressComposition` (`Host.cs:89`). May need `app.json` `remote.enabled=true` (or the right env var)
for `run:exported`. Without it, the reload/collect gate can't be driven programmatically.

### Then — re-verify the collect gate
`task build:godot:desktop` → `task bundle:world` → `task bundle:install` → launch windowed with remote on →
confirm World composes → `fantasim-cmd.py cmd resource.reload_bundle '{"bundleId":"world"}'` → grep console
for **`old ALC collected`** (per [verify-windowed](../../.claude/skills/verify-windowed/SKILL.md)). Watch
for residual pins (the audit's E.4 Akka actor only applies under the SurrealDb backend; default `inmemory`
spawns no actor).

---

## Deferred (separate follow-up) — the globe

The globe rendering (`WorldViewComposition` / `GlobeView`, in the dormant `App.World.Seam`) was deliberately
left out of the data bundle. Reviving it in a collectible context needs: the **`Environment.tscn` in-bundle
SceneTree bridge** (finding #2 — the static handoff pins), `GlobeView` freed on unload, and `App.World.Seam`
packed into the bundle (it's `Godot.NET.Sdk`; the existing scene bundles set the precedent). This is the
user's "Environment sub-scene under stage, planet generated in Environment" intent.

---

## Delegation notes (for the next session's dispatches)

- `zai-coding-plan/glm-5.2` (GLM via opencode) was the **reliable** workhorse all session (best Phase-1 impl,
  the dep-closure audit, the bundle impl). Use it.
- **Avoid:** `kimi-for-coding` (billing-cycle quota EXHAUSTED), `opencode/gemini-*` Zen (auth broken),
  `ollama-cloud/gemini-*` (tool-calling protocol error). The `agy`/`kimi` CLIs were session-locked.
- The windowed `old ALC collected` verify is **lead-only** (agents can't run Godot).

## Key paths
- Plan/spec: `vault/plans/2026-06-25-world-to-stage-scope.md`
- Audit: `.agent/run/dispatch/world-dep-closure-audit.glm.md`
- Impl prompt (reusable): `.agent/run/dispatch/world-bundle-impl-prompt.txt`
- Worktrees (cleanup if stale): `/tmp/world-stage-wt/*`, `/tmp/world-bundle-wt` (`git worktree list`)
