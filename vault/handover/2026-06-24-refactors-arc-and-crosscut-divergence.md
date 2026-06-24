# Handover — command-ingress + logging/config refactors + crosscut pack (and an active divergence)

> 2026-06-24 · repo: fantasim-app-godot · origin/main @ `5e690eb` (before this doc commit)
> Covers a long multi-step arc; **read §3 first if you are the other session** (active divergence).

## 1. What shipped (all on origin/main)

| Area | origin/main commit | State |
|---|---|---|
| HostComposition pushdown (13 `Compose*` → per-plugin `HostComposition/*.cs`) | `e62b407` | taken over from a parked session, verified, committed |
| App.Remote **command ingress** (drive the exported windowed app as a normal user over HTTP) | `9a66b14` | **shipped + windowed-verified** |
| Design spec + refactor plans | `cc97d07` | docs |
| App.Remote handover | `a8e09ea` | docs |
| **Logging** refactor (`GD.Print` → crosscut `ILogger`) | `75772b4` | shipped (windowed boot-checked) |
| Contracts `SelectionStrategy` move | `3bb0aae` | **other agent's** commit |
| **Config** refactor (env → crosscut `Config`) + §3.8 `RemoteOptions` swap | `5e690eb` | shipped (windowed boot-checked) |

Cross-repo: **crosscut-foundation** main merged + `Config.* 0.2.2` packed → local feed + pushed (`b456c44`) — see §5.

## 2. The original goal — DONE

"Can an agent operate the exported windowed Godot app as a normal user?" — **yes.**
```bash
# enable via config (Env override), launch the exported app, drive it:
remote__enabled=true <…>/complete-app.app/Contents/MacOS/complete-app &
python3 tools/fantasim-cmd.py status                       # list commands
python3 tools/fantasim-cmd.py cmd world.run_generation_graph '<json>'
```
Optional `remote__token=<secret>` for bearer auth; bind default `127.0.0.1:19292`. Run `task bundle:install` first for full scene boot. Design: [`vault/specs/2026-06-24-command-transport-ingress-design.md`](../specs/2026-06-24-command-transport-ingress-design.md).

## 3. ⚠️ ACTIVE DIVERGENCE — read this if you are the parallel session

A second agent committed in parallel during the config work. As of this handover:
- **`origin/main` = `5e690eb`** (has the config refactor; does NOT have the other agent's `9b8ad2b`).
- The **other session's local main = `9b8ad2b`** (gdext relocation + `Taskfile.yml`; NOT on origin) — diverged **1 ↔ 1** from origin, both off `3bb0aae`.

Both commits touch **disjoint files**, so reconciliation is clean. The parallel session **MUST**:
```bash
git pull --rebase origin main      # replays 9b8ad2b on top of config; no conflicts expected
git push origin main
```
**DO NOT `git push --force`** — it would clobber the config refactor (`5e690eb`) on origin.

History note: the takeover/push flow here twice hit this multi-agent collision (the parked pushdown earlier, this divergence now). When landing on `main`, verify FF and **never chain `git push` after a `merge --ff-only` that may abort** (that pushed the other agent's commit inadvertently — benign that time, but avoid).

## 4. Key decisions / conventions (so they don't get re-litigated)

- **Command core is transport-agnostic.** `App.Command` = pure catalog/execution; `App.Remote` (T3 HTTP transport + `ITransport` seam) + `App.Remote.Seam` (T4 `RemoteBridgeNode` main-thread dispatcher) = ingress that depends inward. Ports-and-adapters; validated vs hexagonal/CQRS-bus/MediatR/MCP. `RemoteIngressComposition` is the T4 HostComposition module.
- **Logging:** the crosscut `LoggingService` **IS** the app's `Microsoft.Extensions.Logging.ILoggerFactory` (`App.Common/Bootstrap.cs`). Plugins depend only on MEL abstractions; only the composition root references `CrosscutFoundation.Logging.*`. Use `ctx.LoggerFactory.CreateLogger(...)`, not `GD.Print` (T4 seams included). One early-boot `GD.Print` stays in `Host.cs` (pre-Bootstrap).
- **Config (option A):** crosscut `Config` (JSON @50 + Env @90). Keys are config-style (`world:showGraph`, `remote:enabled`); **Env override uses `__`** (`world__showGraph=true`, `remote__enabled=true`). Legacy `FANTASIM_*` names are **retired**. `app.json` (`project/hosts/complete-app/config/`) holds defaults. **Secrets** (`remote:token`) live in the Env layer only — never committed JSON.
- **0 `GetEnvironmentVariable`** remain in `project/` (all via crosscut Config).

## 5. crosscut-foundation (cross-repo) — how Config got packed

- `refactor/servicearchi-proxy-rename` merged → main; it had **missed the `ServiceProxy→Service` rename in 2 sample programs** (`Sample.Standalone`, `Sample.WithPlugins`) → build was red. Fixed (`b456c44`), green, pushed.
- **Pack:** `Config.* 0.2.2` produced via `GITVERSION_MAJORMINORPATCH=0.2.2 dotnet unify-build PackProjects` (override — there is **no `v0.2.2` tag**; GitVersion computes a `0.2.0-…` prerelease). `SyncLocalFeed` is **not** configured, so `.nupkg` were **manually copied** to `/Users/apprenticegc/Work/lunar-horse/packages/nuget/`. Artifacts land under `build/_artifacts/0.1.0/nuget/` (artifactsVersion dir; package version is correct).
- `Config.Env`/`Config.CommandLine` **are** packed (the crosscut `CLAUDE.md` note saying otherwise is stale; `build.config.json` lists them).
- Compat: `Config.ServiceArchi 0.2.2` depends on `ServiceArchi.Contracts 0.1.1` = exactly fantasim's pin. No NU1101.

## 6. Delegation notes (what worked)

- **opencode** must use the skill's models: `opencode run --model ollama/kimi-k2.7-code:cloud` (worked for logging + config). `opencode/gpt-5.2-codex` (OpenCode Zen gateway) **failed `Invalid API key`** — do not use.
- **codex** (`gpt-5.5`) built App.Remote well (workspace-write sandbox; its "FAIL" self-reports were sandbox socket denials — verify by your own build/test).
- Always: dispatch in an **isolated worktree off main**, **verify by artifacts** (writes happen? remaining-count drops?), then **own build + tests + windowed boot-check** in the lead session, review the diff, fix forward.

## 7. Follow-ups (none blocking)

- Parallel session: reconcile `9b8ad2b` per §3.
- App.Remote fast-follows (design §9): named-pipe / WebSocket transports behind `ITransport`; `IMessageBus` streaming.
- Cosmetic: the logging refactor left some redundant `[Host]` prefixes inside a few converted messages (category already covers it).
- `RemoteOptions.FromEnvironment` was removed; `tools/fantasim-cmd.py` reads `FANTASIM_REMOTE_BIND`/`TOKEN` for the **client** connection (that's the driver's own env, unchanged).
