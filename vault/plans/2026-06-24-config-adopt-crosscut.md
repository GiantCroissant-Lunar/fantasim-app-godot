# Refactor: adopt crosscut `Config` (JSON + Env), retire ad-hoc env reads — plan

> **AUDIT (2026-07-06, code-verified):** COMPLETED — CrosscutFoundation.Config wired; only bootstrap env reads remain. _(See the authority index in `vault/README.md`.)_


> status: ready-to-execute 2026-06-24 · repo: fantasim-app-godot · DESIGN/PLAN ONLY (no code yet)
> goal: read app settings/toggles from the house `CrosscutFoundation.Config` service (layered **JSON base + Env override**) instead of raw `Environment.GetEnvironmentVariable`
> sibling: [logging-gdprint-to-ilogger](2026-06-24-logging-gdprint-to-ilogger.md) (same Bootstrap-composition + seam-injection mechanics) · context: [command-transport-ingress-design](../specs/2026-06-24-command-transport-ingress-design.md) §3.8
> **COORDINATION:** `Host.cs` / `Host.Gpu.cs` are mid the HostComposition pushdown. If still uncommitted when you start, do the **Host.* parts last** and re-inventory them (some env reads move into `*/HostComposition/*.cs`). `GlobeView` (seam) is independent.

## 0. PREREQUISITE — verify the packages exist (do this first)

The crosscut **Config** packages must be in the local feed (`/Users/apprenticegc/Work/lunar-horse/packages/nuget`). As of 2026-06-24 only `Logging`/`Messaging` crosscut packages are confirmed published; the source exists (`plate-projects/crosscut-foundation/dotnet/src/Config.{Contracts,Core,Json,Env,ServiceArchi}`) but may not be packed. **Step 0:** confirm `GiantCroissant.CrosscutFoundation.Config.{Contracts,Json,Env,ServiceArchi}` resolve; if not, pack+publish from `plate-projects/crosscut-foundation` via `dotnet unify-build` (use the `unify-build` skill) **before** proceeding. Without this the refactor cannot reference the config types.

## 1. Why

crosscut `Config` is a layered system — `Config.Json` (priority 50) + `Config.Env` (priority 90, so **env overrides JSON**) + `Config.CommandLine`, ServiceArchi-wired, read via `IService.Get` / `GetValue<T>(key, default)` / `GetSection` / `GetReloadToken`. The app uses it **nowhere** today; settings come from 15 ad-hoc `Environment.GetEnvironmentVariable` calls. This adopts the house system: discoverable, version-controlled JSON defaults with an Env override to flip a toggle per run. (App.Remote is the first adopter — design §3.8; this generalizes it.)

## 2. Inventory (exact, 2026-06-24) — 15 sites / 3 files

All are dev/smoke/debug **toggles — none are secrets**, so all may live in committed JSON defaults (off) with Env override:

- `project/hosts/complete-app/Host.Gpu.cs`: `FANTASIM_GPU_SMOKE` (28), `FANTASIM_GPUSHADER_SMOKE` (101)
- `project/hosts/complete-app/Host.cs`: `FANTASIM_SHOW_GRAPH` (101), `FANTASIM_GRAPH_PROMPT` (103, 256), `FANTASIM_SHOW_WORLD_GRAPH` (134), `FANTASIM_WORLD_GRAPH_SPHERE` (201), generic reader (219), `FANTASIM_WORLD_GRAPH_TICK` (225), `FANTASIM_WORLD_GRAPH_FOLLOW_TIMELINE` (238), `FANTASIM_GRAPH_TEST` (255), `FANTASIM_WORLD_GRAPH_TEST` (284), `FANTASIM_III_PING` (321) — *re-inventory post-pushdown; lines shift*
- `project/plugins/App.World.Seam/GlobeView.cs`: `FANTASIM_GLOBE_CAPTURE` (172), generic reader (574)

## 3. Wire the Config service in `App.Common/Bootstrap.cs` (symmetric to Logging)

Alongside `RegisterConsoleLogging()` / `RegisterLoggingService()`:

```csharp
_registry.RegisterJsonConfig(/* app config path, priority 50 — confirm the arg from Config.Json.ServiceArchi */);
_registry.RegisterEnvConfig(priority: 90);                 // env overrides json
_registry.RegisterConfigService();
var config = _registry.Get<CrosscutFoundation.Config.IService>();
_registry.Register<CrosscutFoundation.Config.IService>(
    config, new ServiceRegistration { Tags = new[] { "config" }, Description = "Crosscut config (json+env)" });
```

Point the JSON source at `project/hosts/complete-app/config/app.json` (same `config/` dir as `collectible-bundles.json`).

## 4. JSON defaults + key mapping

`project/hosts/complete-app/config/app.json` with safe defaults:

```json
{
  "gpu":   { "smoke": false, "shaderSmoke": false },
  "graph": { "show": false, "prompt": "a small red toy cube", "test": false },
  "world": { "showGraph": false, "graphSphere": null, "graphTick": null, "followTimeline": false, "graphTest": false },
  "iii":   { "ping": false },
  "globe": { "capturePath": null }
}
```

Map each env read → config key, e.g. `GetEnvironmentVariable("FANTASIM_SHOW_WORLD_GRAPH") != "1"` → `config.GetValue("world:showGraph", false)`.

> **Confirm the Env key convention in `Config.Env` source.** The Env layer may map `world:showGraph` ⇄ `FANTASIM_WORLD_SHOWGRAPH`, or accept the existing `FANTASIM_*` names via a configured prefix/alias. If the existing `FANTASIM_*` names must keep working as the env override, configure the Env source's prefix/separator accordingly so current dev habits (and CI) don't break.

## 5. Migrate the reads

1. **`GlobeView.cs`** (seam, independent) — inject/resolve `Config.IService` the same way the logger is provided (sibling plan §4); convert `FANTASIM_GLOBE_CAPTURE` + the generic reader at 574.
2. **(after pushdown commits)** `Host.cs` / `Host.Gpu.cs` — resolve `config = _composition.Bootstrap.Registry.Get<CrosscutFoundation.Config.IService>()`; replace each site per the map; convert the generic helper reader (219) to delegate to `config`.

## 6. Out of scope

- `collectible-bundles.json` / `bundle-directories.json` are **data manifests** (bundle registries) loaded by `App.Common/CollectibleBundles.cs` and `App.Resource.Bundle.Seam/GodotBundleDirectoryResolver.cs` — not settings. Leave them (migrate separately if ever).
- App.Remote's own config is handled at its integration (design §3.8). The remote **token** (the only secret in the app) stays Env / gitignored local — never committed JSON.

## 7. Verify

- Step 0 packages resolve; `task build` + `task test` green.
- **Default run (no env set):** every toggle reads its `false`/default from `app.json` → identical behavior to today with no env set.
- **Override run:** setting the Env layer flips a toggle → identical to today's `FANTASIM_*=1`. Windowed-verify one end-to-end (e.g. world graph).
- Guard: `git grep -c GetEnvironmentVariable -- project/` drops to 0 in the migrated files (only `Config.Env` internals may remain).
