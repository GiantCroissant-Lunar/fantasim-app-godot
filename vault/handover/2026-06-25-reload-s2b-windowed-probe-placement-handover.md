# Handover — reload S2b windowed-verified: threading FIXED, scene-tier collection NOT (probe placement + likely kernel-registry pin) (2026-06-25, session 3)

**Branch:** `feat/world-to-stage-phase1` · **HEAD:** `3e89077` · **MERGED to local `main`** (FF; `main` == `3e89077`).
**`origin/main` is 33 commits behind — NOT pushed** (held by user pending the collection fix).
**Read alongside:** the living plan `vault/plans/2026-06-25-reload-testability-redesign.md` (Status table),
the prior handover `vault/handover/2026-06-25-reload-testability-redesign-handover.md` (S0-S3B), and memory
`fantasim-bundle-reload-redesign` + `delegation-model-cost`.

## TL;DR — where we are
The reload **threading regression is FIXED and windowed-proven**, and the frame-deferred gate
**instrument** is correct. But the end-to-end windowed verify **DISPROVED** the collection fix:
reloading scene-tier bundles (`assist`, `timeline`) still logs **`old ALC still pinned`**. The next
session's job is to (1) move the collection probe to fire AFTER the full `Exit→Enter`, and (2) almost
certainly hunt a genuine residual pin — most likely the **shared kernel registry holding the bundle's
registered services** (the deferred DI-scope-ownership problem). Everything merged is sound progress,
not a complete reload fix.

## What is DONE + PROVEN this session (committed; `main` == `3e89077`)
- **S3 Step C** (`037296d`): real ALC reload-collection check in the `code-quality` headless host on the
  REAL `GodotFrameProvider.Process` → `Collected=true attempts=1`. Two Godot-runtime fixes vs the xUnit
  template (BOTH IMPORTANT, reusable): (1) `TestViewSourceFactory.GetReferences()` resolves Roslyn refs by
  FILE PATH — under Godot's .NET loader the game assemblies have an EMPTY `Assembly.Location`, so the
  AppDomain+Location harvest drops them (CS0246); scan `AppContext.BaseDirectory` for `*.dll`. (2) the
  collectible ALC needs a `Resolving` handler sharing the host's already-loaded contract assemblies (Godot
  loads game asms in its OWN ALC, not Default) or `asm.GetType(...)` returns null.
- **S4** (`1d075fa`): dropped `FakeCommandService`; `HttpTransportTests` drives the real `Service` +
  `InProcessClient` via a `CapturingService : IService` decorator (preserves the request-forwarding
  asserts — NOT vacuous). 4/4 green.
- **S2b** (`18bb5ad`): the SPLIT fix. (a) **Threading** — `CommandComposition` uses a SYNCHRONOUS
  `IViewHost.UnmountNow` (the handler runs on the Godot main thread via `RemoteBridgeNode`) instead of the
  deferred-TCS `UnmountAndWaitAsync` + `ConfigureAwait(false)`; `ConfigureAwait(false)` also dropped from
  the downstream `SceneFlow.ExitAsync/EnterAsync` + `resource.ReloadAsync` awaits. (b) **Gate timing** —
  `BundleHost.VerifyOldContextCollectedAsync` now `await Observable.NextFrame(ObservableSystem.DefaultFrameProvider)`
  before each `IsCollected(forceGc:true)` (mirrors the xUnit-proven `ReloadPolicy`). `App.Command` stays
  Godot-free (gate lives in the Godot-capable `App.Resource.Bundle.Seam`, via the R3 DEFAULT provider).
  Build + App.Resource.Tests(10/10) + App.Ui.Tests(26/26) green.
- **complete-app autoload** (`c1644b0`): vendored `addons/R3.Godot` + the `FrameProviderDispatcher`
  autoload into `complete-app/project.godot` (sets `ObservableSystem.DefaultFrameProvider` →
  `GodotFrameProvider.Process`; REQUIRED or `NextFrame` has no Godot frame source). complete-app builds 0 errors.
- **Delegation:** S3C + S2b → opencode `ollama/glm-5.2:cloud`; S4 → opencode `ollama/kimi-k2.7-code:cloud`.
  All reviewed + verified by the lead (the glm S3C exited 0 but actually FAILED — the lead fixed the 2
  Godot-runtime bugs). `agy` was not needed this round.

## THE KEY FINDING — windowed verify DISPROVED collection (this is the next session's problem)
Built+exported complete-app, ran it windowed with `remote__enabled=true`, drove `resource.reload_bundle`
for `assist` then `timeline`. Threading is clean (NO `Removing children only allowed from the main thread`).
But the gate verdict, with the exact log order:
```
Bundle unloaded: assist
Hot-reload: old ALC still pinned for bundle assist ...   <- gate fires HERE (warn)
Scene exited: assist                                      <- SceneFlow _active removal AFTER the gate
Loading scene bundle: assist -> Bundle loaded: assist     <- re-enter (NEW ALC) AFTER
Assist tier active -- sharing the app kernel registry #14920772
Scene entered: assist
```
Same for `timeline`. TWO candidate causes (the next session must disambiguate):

1. **Probe PLACEMENT (timing in the flow).** The frame-deferred probe runs INSIDE
   `BundleHost.UnloadAsync`, which for scene-tier is nested in `SceneFlow.ExitCoreAsync`
   (`App.SceneFlow/Services/Service.cs:70-79` → `_provider.UnloadAsync` at :76, THEN `_active.RemoveAll`
   at :77, THEN logs `Scene exited` at :78). And `SceneFlowProvider.UnloadAsync`
   (`App.SceneFlow/SceneFlowProvider.cs:57-68`) does `activation.Dispose()` (:63) BEFORE `resource.UnloadAsync`
   (:67). So the probe fires before `Scene exited` and before the re-enter.
2. **A GENUINE residual pin (most likely the real cause).** NOTE: `activation.Dispose()` (which should drop
   the SceneFlow Bootstrap/scene refs) runs at Provider.cs:63, BEFORE the gate — yet the frame-deferred
   gate (≈60 frames) STILL reports pinned. That points BEYOND placement to a real strong ref. Prime suspect:
   the bundle's Bootstrap REGISTERS bundle-typed services into the **shared kernel `IRegistry`** ("sharing
   the app kernel registry #..." in the log) and those registrations are NOT removed on unload → the
   resident registry pins the collectible ALC. This is the known **DI-scope-ownership / bundle-disposal**
   problem (deferred to dependency-archi; "manual kernel-forwarding leads to unprovable ALC collection").

## REMAINING WORK (next session) — in order
1. **Disambiguate placement vs real pin.** Quickest test: move the collection probe to fire AFTER the full
   `Exit→Enter`, then re-verify windowed.
   - Design: stop probing inside `BundleHost.UnloadAsync`. Have `BundleHost.UnloadCoreAsync` STASH the
     `PluginUnloadResult` per `bundleId` (add `_lastUnload` dict + `TryGetLastUnloadResult(bundleId)`), and
     add a separate `BundleHost.VerifyCollectedAsync(bundleId)` that does the frame-deferred probe on the
     stashed result. `CommandComposition` calls it AFTER the `Exit→Enter` completes.
   - LAYERING CAVEAT: `CommandComposition` is in `App.Command` (pure, no Godot/R3). It reaches `BundleHost`
     only via `resource.IService` (a `[ServiceContract]` — adding a method triggers ServiceArchi source-gen;
     verify it regenerates cleanly). Keep the actual frame-deferred probe in `BundleHost` (Godot-capable);
     `resource.IService.VerifyCollectedAsync(bundleId)` just delegates. Do NOT pull GodotFrameProvider into
     App.Command.
   - If it then logs `old ALC collected` → it was placement; loop closed. If STILL pinned → go to step 2.
2. **Hunt the residual pin (likely the kernel registry).** Read the bundle Bootstraps
   (`Assist.Bootstrap`, `Timeline*`) + `SceneActivatorBase` (`contracts/App.SceneFlow`): what do they
   `registry.Register<...>(...)` into the shared kernel, and is it `Unregister`'d on `ShutdownAsync`/Dispose?
   Use the now-working frame-deferred gate as the instrument. (Fix-2 TimelineFace statics + Fix-3 World
   command-unregister from `cd8e6db` are already in — so the remaining pin is something else, probably the
   registry.) DI-scope-ownership may need a real fix here, not another manual forward.
3. **Re-verify windowed** (the procedure below) until `old ALC collected` appears for assist + timeline.
4. **Then** decide on pushing `origin/main` (see below).

## Windowed verify procedure (reusable; gotcha-heavy — follow exactly)
Use the `verify-windowed` skill. S2b/this rework is OUT-OF-ALC (resident/seam/contract) → FULL build, not hot-reload.
1. **Fresh worktree** at the target commit: `git worktree add --detach <wt> <sha>`. REQUIRED — the main
   working tree carries the concurrent NodeGraph session's ~37 uncommitted WIP files, which break the
   full-solution / complete-app build.
2. `task -d <wt> build:godot:desktop bundles bundle:install` — BUT this FAILS in a fresh worktree at
   `bundle:timeline:build`: App.Timeline (Godot.NET.Sdk) builds its DLL to `bin/Debug/net8.0`, NOT
   `.godot/mono/temp/bin/Debug` where the task's `cp` looks. WORKAROUND: stage it manually
   (`cp project/plugins/App.Timeline/bin/Debug/net8.0/FantaSim.App.Timeline.dll project/bundles/timeline/`),
   export timeline.pck directly (`<GODOT> --headless --path project/hosts/content-app --export-pack "timeline PCK" <wt>/build/_artifacts/0.1.0/godot/bundles/timeline.pck`),
   then `task -d <wt> bundle:install`. GitVersion falls back to **0.1.0** (artifacts under `build/_artifacts/0.1.0/`).
3. Launch windowed (NEVER headless): `remote__enabled=true <wt>/build/_artifacts/0.1.0/godot/osx/complete-app.app/Contents/MacOS/complete-app > <applog> 2>&1` (background).
   Ingress env is **`remote__enabled=true`** (crosscut config `remote:enabled`), NOT `FANTASIM_REMOTE_ENABLED`.
4. Wait for ingress without a foreground `sleep` (it's blocked): `curl -s --retry-connrefused --retry 60 --retry-delay 1 http://127.0.0.1:19292/health`.
5. Drive: `python3 <wt>/tools/fantasim-cmd.py cmd resource.reload_bundle '{"bundleId":"assist"}'` (then `timeline`).
   The reload command AWAITS the gate, so the verdict is in the log when it returns.
6. Read the verdict from `<applog>`: look for `Hot-reload: old ALC collected` (success) vs `still pinned`.
   `pkill -f "complete-app.app/Contents/MacOS/complete-app"` when done.

## Gotchas / decisions (do not relearn)
- **RTK proxy filters `ls` AND `find` output** → FALSE low file counts (saw a real 14-file dir report as
  "1"/"empty"). Verify file presence with `test -f` / `git ls-files` / `rtk proxy <cmd>`, NEVER `ls | wc -l`.
- **Delegation (CORRECTED by the user this session):** the `agy` CLI (Gemini 3.5 Flash Medium,
  `--model gemini-3.5-flash`) is FINE/intended — it was NEVER banned. The only ban is a gemini MODEL via
  opencode's ollama-cloud provider. opencode → `ollama/glm-5.2:cloud` or `ollama/kimi-k2.7-code:cloud`.
  See `delegation-model-cost` memory (rewritten) + the plan/handover delegation notes (corrected).
- **Threading is deadlock-safe by construction:** `RemoteBridgeNode._Process` fire-and-forget-runs the async
  command handler (`action();`), so the handler's `await NextFrame` yields the main thread back to the frame
  loop, the frame ticks `GodotFrameProvider.Process`, and the continuation resumes on the main thread.
- **`exit 0 ≠ done`** — the glm S3C dispatch exited 0 but the runtime check FAILED. Always verify agent work
  by artifacts (build + run), never the wrapper exit code.
- **Concurrent NodeGraph/vplanet session** owns ~37 uncommitted WIP files in the main working tree (App.NodeGraph
  / App.World / App.Ui / vplanet). PATH-SCOPE every `git add`/commit; never `git add -A`, reset, or stash. The
  FF merge to `main` was done via `git branch -f main <feat>` (ref-only, working tree untouched).
- **`UnmountAndWaitAsync` was KEPT** (not deleted) on `IViewHost` to avoid breaking S1/S2a/S3C tests; prod now
  uses the new `UnmountNow`. It can be removed later once nothing references it.

## Git state
- `main` == `feat/world-to-stage-phase1` == `3e89077` (local, FF-merged). `origin/main` 33 behind — NOT pushed.
- Worktrees: primary (feat, has the NodeGraph WIP) · `.worktrees/fantasim-reload-windowed` (detached
  `c1644b0`, has the BUILT desktop export + installed bundles + the timeline-DLL fix — reusable for quick
  re-verify, but at c1644b0 = pre-handover; rebuild at the new HEAD for the rework) · `.worktrees/fantasim-reload-verify`
  (stale `fa02a5c` — removable).

## This session's commits (7, on `feat/world-to-stage-phase1` → `main`)
`037296d` S3C · `1d075fa` S4 · `4396444` docs+delegation-correction · `18bb5ad` S2b code ·
`c1644b0` complete-app R3.Godot autoload · `daa51a9` docs · `3e89077` windowed-verify finding.

## Key paths
- Plan (living, Status table): `vault/plans/2026-06-25-reload-testability-redesign.md`
- Reload path files for the rework: `project/plugins/App.Command/HostComposition/CommandComposition.cs`,
  `project/plugins/App.Resource.Bundle.Seam/BundleHost.cs`, `project/plugins/App.SceneFlow/Services/Service.cs`
  + `SceneFlowProvider.cs`, `project/contracts/App.Resource/Services/IService.cs`.
- Bundle Bootstraps to audit for the registry pin: `project/plugins/App.Assist/*Bootstrap*`,
  `project/plugins/App.Timeline/*`, `contracts/App.SceneFlow/SceneActivatorBase*`.
- Dispatch prompts (reusable): `.agent/run/dispatch/{s3c-reload-collection-check,s4-real-command-service,s2b-wire-reloadpolicy}-prompt.txt`
- Build/run logs: `.agent/logs/windowed-build-*.log`, `.agent/logs/windowed-run-*.log`, `.agent/logs/opencode/*`.
