# App.Remote command ingress — executed + windowed-verified

> 2026-06-24 · repo: fantasim-app-godot · main @ 9a66b14
> design: [command-transport-ingress-design](../specs/2026-06-24-command-transport-ingress-design.md)

## What shipped (main)

1. **`e62b407`** — took over a parked HostComposition pushdown (another session left it uncommitted ~10h). Verified before committing: `dotnet build` 0 errors, full suite 230/230. 13 `Compose*` bodies moved from `Host.cs`/`Host.Gpu.cs` into per-plugin `HostComposition/*.cs` via a `HostCompositionContext { AppComposition, IRegistry, ILoggerFactory }` in App.Common; `Host.cs` ~866 → ~396 lines.
2. **`cc97d07`** — design spec + the two refactor plans.
3. **`9a66b14`** — App.Remote integration: `App.Remote` (T3 HTTP transport) + `App.Remote.Seam` (T4 main-thread bridge) + `App.Remote.Tests` + `tools/fantasim-cmd.py`, plus a `RemoteIngressComposition` T4 module called **last** in `Host._Ready` (stands up `RemoteBridgeNode` as the `IMainThreadDispatcher`, then composes the transport). Build 0 errors; full suite **234/234**.

## Windowed verify (the gate) — PASSED

Exported app `0.1.67`, launched `FANTASIM_REMOTE_ENABLED=1`, driven via `tools/fantasim-cmd.py`:

- Listener bound `127.0.0.1:19292` (~8s). Console (crosscut `ILogger`, `info:` format): `Remote HTTP transport listening...` + `registered: Remote ingress`.
- `health` → `{ok:true, commands:4}`.
- `status` → catalog `[iii.ping, pipeline.run_text_to_3d, world.orchestrate, world.run_generation_graph]`.
- `cmd world.run_generation_graph {}` → `CommandResult{ok:false, "payload malformed"}` = correct response to an empty test payload; proves POST → auth → **main-thread marshal** → handler → result round-trip.
- Expected non-issue: `initial scene entry failed: No scene activator registered for 'stage'` — bundles not installed for this focused verify (`task bundle:install` for a full run). Unrelated to the remote ingress.

## Follow-ups (clean base now; dispatchable to agy/opencode)

- Swap `RemoteOptions` to crosscut `Config` (design §3.8) — JSON base + Env override; token stays Env.
- App-wide refactors: [logging-gdprint-to-ilogger](../plans/2026-06-24-logging-gdprint-to-ilogger.md), [config-adopt-crosscut](../plans/2026-06-24-config-adopt-crosscut.md) (config plan has a pack-Config-packages prerequisite).
- Optional fast-follow: named-pipe / WebSocket transports behind `ITransport` (+ `IMessageBus` streaming).
- The `feat/command-remote-ingress` branch + `.worktrees/fantasim-app-remote` worktree can be cleaned up (their content is now on main).
