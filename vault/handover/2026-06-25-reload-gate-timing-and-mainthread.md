# Handover — reload ALC collection: gate-timing + main-thread constraint (2026-06-25)

## TL;DR
The reference-hygiene fixes were committed on `feat/world-to-stage-phase1`, but a
**windowed verify disproved them**: the collectible-ALC gate still reports
`old ALC still pinned` for every reloaded bundle, AND the Fix-1 handler change
**regressed the reload** by pushing scene teardown off the Godot main thread.
Two deeper problems are the real blockers (the same ones the 2026-06-24 session
flagged): (1) the collection gate is a **false negative** because it checks inside
the live reload call stack; (2) the reload **must run entirely on the Godot main
thread**. Reference hygiene cannot be confirmed until the gate-timing redesign lands.

## Committed this session
- **`cd8e6db`** — Fix 1 (`IViewHost.UnmountAndWaitAsync` + reload-handler view
  unmount/re-mount), Fix 2 (`TimelineFace._ExitTree` → `UnbindCrossTarget()` + null
  the resident statics), Fix 3 (Command `IService.Unregister` + call it in
  `WorldPlugin.ShutdownAsync`). **Fix 1's handler integration is a known regression
  (below). Fix 2 and Fix 3 are sound and threading-safe.**
- **`fa02a5c`** — test doubles implement the new members
  (`FakeViewHost.UnmountAndWaitAsync`, `FakeCommandService.Unregister`).

## Verify infrastructure (now working — reusable)
- **Isolated worktree** at the commit (clean of the concurrent vplanet-codegen WIP)
  + full export. **Gotcha:** `App.Timeline` is a `Godot.NET.Sdk` project; in a fresh
  worktree its DLL lands in `bin/Debug/net8.0`, NOT `.godot/mono/temp/bin`, so
  `bundle:timeline:build`'s `cp` fails — cp from `bin/Debug/net8.0` manually. (This
  is the documented "fresh worktree unreliable for Godot.NET.Sdk" gotcha.)
- **Remote ingress env override is `remote__enabled=true`** — NOT
  `FANTASIM_REMOTE_ENABLED` (the prior session's mistake). Ingress came up in ~4s;
  drive with `python3 tools/fantasim-cmd.py cmd resource.reload_bundle '{"bundleId":"<id>"}'`.
- Drive script: `.agent/run/verify-drive.sh`; app log: `.agent/logs/verify/app.log`.

## The two real blockers

### 1. Gate is a false negative (the measurement instrument is broken)
`assist` (no view, no timeline static, no command — NONE of the three pins) still
reports `old ALC still pinned`. The collection check
(`BundleHost.VerifyOldContextCollectedAsync`) runs **synchronously inside the reload
call stack**; transient references (the live async state machine, `QueueFree`-d but
not-yet-freed Godot nodes, R4 deferred holders) keep the ALC alive during the check
→ false "still pinned" regardless of hygiene. The in-handler 8×50ms poll does NOT
fix this (it still runs within the reload). **REAL FIX: defer the collection check
to AFTER the reload fully returns + N frames, via the App.Remote main-thread
dispatcher / a frame counter.** Until this exists, no pin fix can be confirmed.

### 2. Reload must run on the Godot main thread
Backtrace from the verify:
```
BundleSceneHost.RemoveScene → Node.RemoveChild
  ERROR: Removing children ... only allowed from the main thread
  ... CommandComposition.cs:72 (ExitAsync) reached via ThreadPoolWorkQueue.Dispatch
```
My `await viewHost.UnmountAndWaitAsync(...).ConfigureAwait(false)` (Fix 1) genuinely
yields (the unmount is deferred to a later frame) and `ConfigureAwait(false)` +
`TaskCreationOptions.RunContinuationsAsynchronously` push the continuation onto a
threadpool thread → the **entire** subsequent `ExitAsync → … → RemoveScene` chain
runs off-main, where `RemoveChild` is illegal → botched teardown. The pre-Fix-1
handler had no yielding await before `ExitAsync`, so it stayed on the main thread.
Any main-thread view-unmount must NOT push the rest of the reload off-main.

(Side note: `activity` is shown as a *view* but not tracked as a loaded *bundle*, so
`resource.reload_bundle activity` was a no-op — `Bundle not loaded for reload`. That
path wasn't exercised here.)

## Path forward (to discuss — not yet decided)
- Revert or rework Fix 1's handler integration (regression). Keep Fix 2 + Fix 3.
- Build the **gate-timing redesign FIRST** — it is the instrument; without it the
  value of any pin fix is unobservable. Post-reload, frame-deferred collection
  check on the main thread.
- Then re-verify, and only then judge the hygiene pins.
- Inter-bundle dependency DAG / modern-satsuma (the original question) stays **Phase 2**
  (cascade reload beyond leaves) — confirmed NOT the current blocker.
