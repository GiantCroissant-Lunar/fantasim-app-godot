# Refactor: `GD.Print` → crosscut `ILogger` — implementation plan

> status: ready-to-execute 2026-06-24 · repo: fantasim-app-godot · DESIGN/PLAN ONLY (no code yet)
> goal: route every ad-hoc Godot console print through the house structured logger (`ILogger` over the crosscut `LoggingService`), so logging is uniform and reaches the console across the ALC boundary
> sibling: [config-adopt-crosscut](2026-06-24-config-adopt-crosscut.md) (same Bootstrap-composition + seam-injection mechanics) · context: [command-transport-ingress-design](../specs/2026-06-24-command-transport-ingress-design.md) §7
> **COORDINATION:** `Host.cs` / `Host.Gpu.cs` are mid the HostComposition pushdown (`vault/plans/2026-06-23-host-composition-pushdown.md`). If that is still uncommitted when you start, do the **Host.* parts last** (re-inventory them first — composition and some prints move into `*/HostComposition/*.cs`, which already use `ILogger`). The three `.Seam` files are independent — start there.

## 1. Why

The crosscut `LoggingService` **is** the app's `ILoggerFactory` — `App.Common/Bootstrap.cs:35` casts `CrosscutFoundation.Logging.IService` to `ILoggerFactory` and registers it (tagged `"logging"`) so every `ILogger` reaches the console "across the ALC boundary" (Bootstrap comment). 44 `GD.Print*` / `PushError*` calls bypass it — no levels, no categories, no structured fields, not captured by the sink. This plan routes them through `ILogger`.

## 2. Inventory (exact, 2026-06-24) — 44 sites / 5 files

26 `GD.Print`, 12 `GD.PushError`, 8 `GD.PushWarning`, 0 `GD.PrintErr`:

| File | count | logger in scope today? | note |
|---|---:|---|---|
| `project/hosts/complete-app/Host.cs` | 25 | yes (`_composition.Bootstrap.LoggerFactory`) | **re-inventory post-pushdown** — count/lines shift |
| `project/hosts/complete-app/Host.Gpu.cs` | 9 | no (partial of `Host`) | reuse `Host`'s `_log`; post-pushdown |
| `project/plugins/App.World.Seam/GlobeView.cs` | 7 | no | T4 Godot node — needs a resolved logger |
| `project/plugins/App.Iii.Seam/IiiBridge.cs` | 2 | no | T4 Godot node |
| `project/plugins/App.Timeline.Seam/TimelineFace.cs` | 1 | no | T4 Godot node |

## 3. Target pattern

Cache one category logger per type and map by severity:

```csharp
private readonly ILogger _log = loggerFactory.CreateLogger("World.Globe"); // category from the old [globe]/[graph]/[iii] prefix
```

| from | to |
|---|---|
| `GD.Print($"[x] msg {v}")` | `_log.LogInformation("msg {V}", v)` (drop `[x]` — it is the category) |
| `GD.PushWarning(msg)` | `_log.LogWarning(msg)` |
| `GD.PushError(msg)` | `_log.LogError(msg)` — see §5 |

Prefer structured message templates (`"entered scene {SceneId}; loaded={Loaded}"`, args) over string interpolation where the values matter.

## 4. How each file gets a logger

- **`Host.cs`** — already has `_composition.Bootstrap.LoggerFactory`. Add a cached `private ILogger _log` set *immediately after composition activates*; the 1–2 prints **before** that point stay `GD.Print` (§5).
- **`Host.Gpu.cs`** — same `partial class Host`; reuse the same `_log`.
- **Seams (`GlobeView` / `IiiBridge` / `TimelineFace`)** — T4 Godot nodes instantiated by their plugin composition. **First read how each node is constructed**, then give it a factory by the least-invasive route: pass `registry.Get<ILoggerFactory>()` (tagged `"logging"`) from the composing code into the node's ctor/`Init`, and cache `_log`. Do **not** create a new factory — reuse the resident one.

## 5. Gotchas / policy

- **Early boot:** any print before `Bootstrap` builds the factory (e.g. `Host.cs` "composition root starting…") **must stay `GD.Print`** — no factory exists yet. Find the boundary (composition activation) precisely; document which prints remain and why.
- **`GD.PushError`/`PushWarning` have Godot *editor* value** (Debugger panel, red entries). Policy: convert to `_log.LogError`/`LogWarning` for structured logging. For a genuinely fatal/attention error you want to break into the Godot debugger during dev, you **may** keep a `GD.PushError` alongside — but default to `ILogger`-only and never silently drop the error semantics.
- **ALC/bundles:** the resident crosscut factory is shared (`"CrosscutFoundation."` prefix) and works across collectible ALCs — that is the point. Never spin a per-bundle factory.

## 6. Steps (bite-sized, seam-first)

1. `GlobeView.cs` — wire `ILoggerFactory` into its construction path; cache `_log = lf.CreateLogger("World.Globe")`; convert its 7 sites (§3). Build `App.World.Seam`; windowed-verify the globe still renders and the logs appear on the console.
2. `IiiBridge.cs` — same, category `"Iii.Bridge"`, 2 sites.
3. `TimelineFace.cs` — same, category `"Timeline.Face"`, 1 site.
4. **(after the HostComposition pushdown commits)** Re-inventory `Host.cs`; add cached `_log` post-activation; convert the residual sites; keep the pre-factory print(s) as `GD.Print`.
5. `Host.Gpu.cs` — reuse `_log`; convert its sites.
6. Final verify (§7).

## 7. Verify

- `task build` + `task test` green.
- Exported **windowed** app (`task run:exported`): the converted categories appear on the console via the crosscut sink; no behavior change (globe/graph/scenes still work). The `verify-windowed` skill is the gate.
- Guard: `git grep -cE "GD\.(Print|PushError|PushWarning)" -- project/` drops to only the intentional early-boot / editor-kept lines — list which remain and why in the PR.
