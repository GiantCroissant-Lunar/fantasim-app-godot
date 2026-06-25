# Handover — reload ALC-collection: testability-first redesign (2026-06-25, session 2)

**Branch:** `feat/world-to-stage-phase1` · **HEAD:** `714232f` · everything below is COMMITTED & green.
**Read alongside:** the living plan `vault/plans/2026-06-25-reload-testability-redesign.md`
(has the Status table) and the prior handover `vault/handover/2026-06-25-reload-gate-timing-and-mainthread.md`
(the windowed-verify disproof that started this redesign).

## TL;DR — where we are
The original "stuck reload" is NOT a missing DAG and NOT just reference pins. The real blockers
(proven by a windowed verify) were **(1) gate-timing** — the collection check ran synchronously
inside the live reload call stack and got a FALSE "still pinned"; **(2) main-thread discipline** —
the reload's scene teardown (`RemoveChild`) must run on the Godot main thread. We pivoted to a
**testability-first redesign**: a pure-C# `ReloadPolicy` whose collection probe is **deferred to the
next frame** (over an `R3.FrameProvider`), provable in plain xUnit (`FakeFrameProvider`) AND in a
headless Godot host (`GodotFrameProvider.Process`). The whole foundation is now proven & committed.
What remains is wiring the proven policy into the real reload path + the end-to-end headless check.

## What is DONE + PROVEN (committed, green)
- **S0 scoping** (`.agent/run/reports/s0-reload-scoping.md`): R3 1.3.1, plugin-archi test template,
  pin = `ViewRenderer._source` (managed), real command Service is Godot-free.
- **S1 — real-ALC collection tests** (`0f13fc6`), plain xUnit, no Godot/export:
  `project/tests/App.Resource.Tests/{SimpleViewHost,TestViewSourceFactory,ReloadCollectionTests}.cs`.
  Roslyn-emits a real `IViewSource` into a collectible ALC; a real-but-simple in-memory `IViewHost`
  holds it. `Held_...NotCollected` (held → ALC pinned) + `Dropped_...Collected` (drop → ALC collects).
  Re-verify: `dotnet test project/tests/App.Resource.Tests/App.Resource.Tests.csproj --filter ReloadCollectionTests` → 2/2.
- **S2a — pure `ReloadPolicy` + frame-deferred gate** (`9e81ef6`):
  `project/plugins/App.Resource/ReloadPolicy.cs` (PURE net8.0, no Godot) +
  `project/tests/App.Resource.Tests/ReloadPolicyGateTests.cs`.
  Signature: `Task<ReloadResult> ReloadAsync(string bundleId, Func<CancellationToken,Task> unmount,
  Func<CancellationToken,Task<PluginUnloadResult?>> unloadReload, int maxAttempts=8, CancellationToken ct=default)`
  where `ReloadResult(bool ProbeAvailable, bool Collected, int Attempts)`. Sequence:
  `unmount → unloadReload → (loop) await Observable.NextFrame(frameProvider).FirstAsync() → probe.IsCollected(forceGc:true)`.
  The `NextFrame`-before-probe IS the gate-timing fix. Test drives it with `R3.FakeFrameProvider`
  + a pump loop (`fake.Advance(); await Task.Yield();` until the task completes). Re-verify:
  `dotnet test ... --filter ReloadPolicyGateTests` → 1/1.
- **S3 host Steps A+B** — `project/hosts/code-quality/` (a NEW Godot 4.7 .NET host the USER created;
  I scaffolded it):
  - Step A (`892956c`): `code-quality.csproj` + `CodeQualityRunner.cs` (main_scene → `_Ready` runs
    checks → `GetTree().Quit(exitCode)`) + `Main.tscn`. Boots headless, exits 0.
  - Step B (`714232f`): vendored `addons/R3.Godot` (R3 1.3.1, the whole addon) + the
    `FrameProviderDispatcher` autoload + `<PackageReference Include="R3" Version="1.3.1" />`. Headless
    smoke confirms `await Observable.NextFrame(GodotFrameProvider.Process)` advances under `--headless`.
  - Run it: `dotnet build project/hosts/code-quality/code-quality.csproj -c Debug` then
    `<GODOT> --headless --quit-after 600 --path project/hosts/code-quality` (GODOT =
    `/Users/apprenticegc/Work/lunar-horse/tools/Godot_mono.app/Contents/MacOS/Godot`). Exit 0 +
    look for `[code-quality] ... advanced (Step B addon smoke OK)` in the output.

Proof chain: pin/collect is real (S1) → frame-deferred gate fixes the false-negative (S2a,
FakeFrameProvider) → the real Godot frame source advances headless (S3 A+B). All the risky
unknowns are retired.

## On the branch: BROKEN vs SOUND (important)
- `cd8e6db` ("sever resident pins") carries the **Fix-1 REGRESSION**: the reload handler does
  `await viewHost.UnmountAndWaitAsync(...).ConfigureAwait(false)` (a deferred-TCS unmount), which
  punts the rest of the reload (`ExitAsync → BundleSceneHost.RemoveScene → Node.RemoveChild`) onto a
  threadpool thread → "Removing children only allowed from the main thread". **This must be reworked
  in S2b** (replace with a synchronous main-thread unmount; the policy then owns the frame-deferred
  probe). `IViewHost.UnmountAndWaitAsync` (added in cd8e6db) is the regressing mechanism — likely
  delete it.
- Fix-2 (`TimelineFace._ExitTree` UnbindCrossTarget + null statics) and Fix-3 (`Command
  IService.Unregister` + `WorldPlugin.ShutdownAsync`) from `cd8e6db` are SOUND and threading-safe —
  keep them.

## REMAINING WORK (next session) — in order
1. **S3 Step C** — the real reload-collection check IN `code-quality`. In `CodeQualityRunner`, build
   the real path: emit/load a test bundle into a collectible ALC (reuse the S1 pattern), mount via a
   real-but-simple host (or move `SimpleViewHost`/`TestViewSourceFactory` to a shared spot the host
   can ref), run `ReloadPolicy.ReloadAsync(..., GodotFrameProvider.Process)`, assert `Collected` →
   set exitCode. This proves the gate against REAL Godot frames (headless analog of S2a). Add the
   needed ProjectReferences to `code-quality.csproj` (App.Resource for ReloadPolicy, contracts/App.Ui,
   PluginArchi.Extensibility.Abstractions, Roslyn).
2. **S2b** — wire `ReloadPolicy` into the real reload path. In `BundleHost`/`CommandComposition`,
   replace the in-stack `VerifyOldContextCollectedAsync` + the Fix-1 off-main unmount with:
   `ReloadPolicy.ReloadAsync(bundleId, unmount: <synchronous main-thread view unmount>,
   unloadReload: <BundleHost unload+reload returning PluginUnloadResult>, GodotFrameProvider.Process)`.
   Inject `GodotFrameProvider.Process` (install the R3.Godot addon into `complete-app` too, or set it
   as the default via the autoload). Keep the WHOLE reload on the main thread (no ConfigureAwait(false)
   before scene ops). Verify via the code-quality headless check (Step C extended to drive a real
   bundle) and/or the windowed app.
3. **End-to-end verify** — reload a real bundle (activity/timeline/assist) and confirm
   `old ALC collected`. Headless via `code-quality` (preferred, automatable) or the windowed app
   (`run:exported` + `remote__enabled=true` + `tools/fantasim-cmd.py cmd resource.reload_bundle '{"bundleId":"<id>"}'`).
4. **S4 cleanup** — drop `FakeCommandService` in `App.Remote.Tests` (real Service is Godot-free; add a
   projref to `App.Command`, instantiate with `ImmediateMainThreadDispatcher` + `NullLoggerFactory` +
   a real `Registry`, update the `HealthAndStatus` assertion to the real command list).
5. **Phase 2 (later)** — inter-bundle dependency DAG (modern-satsuma: reverse-reachability + SCC
   cycle-guard + capture/restore) for NON-leaf cascade reload. NOT the current blocker.

## Gotchas / decisions (do not relearn these)
- **Ingress env var is `remote__enabled=true`** — NOT `FANTASIM_REMOTE_ENABLED` (the prior session's
  wrong var; cost a windowed cycle). It maps to crosscut config `remote:enabled`.
- **`ReloadPolicy` stays PURE** (`App.Resource`, Microsoft.NET.Sdk). Do NOT put it in
  `App.Resource.Bundle.Seam` (Godot.NET.Sdk → drags GodotSharp → not plain-xUnit-testable).
- **The fix = frame-deferred probe + synchronous main-thread unmount.** Do NOT reintroduce a
  deferred-TCS `UnmountAndWaitAsync` with `ConfigureAwait(false)` (that was the regression).
- **Verify agent work yourself** — `exit 0` ≠ done (S1's first dispatch produced ZERO files). Always
  build + run the tests yourself.
- **Delegation:** ollama-cloud dispatch = `ollama/glm-5.2:cloud` OR `ollama/kimi-k2.7-code:cloud`
  ONLY. NEVER gemini/`agy` (user: expensive). `kimi` CLI is quota-dead. Use ABSOLUTE paths in
  `opencode run` (a stray `cd` shifted cwd and broke relative `$(cat ...)`). See
  `.agent/skills/04-tooling/external-agent-delegation`.
- **Concurrent session in the same tree** (vplanet / App.NodeGraph / App.World GenerationGraph) — its
  WIP is uncommitted and breaks `dotnet build FantaSim.sln`. PATH-SCOPE every commit; build ONLY the
  affected projects, never the full solution.
- **Godot.NET.Sdk in a fresh git worktree** outputs its DLL to `bin/Debug/net8.0`, not
  `.godot/mono/temp/bin` — `bundle:timeline:build`'s cp fails there (cp manually). (Only relevant if
  you re-do the windowed worktree export.)

## Key paths
- Plan (living, Status table): `vault/plans/2026-06-25-reload-testability-redesign.md`
- This handover + the disproof: `vault/handover/2026-06-25-reload-*.md`
- Agent reports: `.agent/run/reports/{s0-reload-scoping,s1-collection-tests,s2a-reload-policy}.md`
- Dispatch prompts (reusable): `.agent/run/dispatch/*.txt`
- **Stale verify worktree:** `/Users/apprenticegc/Work/lunar-horse/.worktrees/fantasim-reload-verify`
  (detached at `fa02a5c`, behind HEAD; the windowed-export env + `verify-drive.sh`). `git worktree
  remove` it if not needed, or `git -C <it> checkout <new sha>` + re-export to reuse.

## Commits this session (feat/world-to-stage-phase1)
`cd8e6db` pins(Fix1 regression+Fix2+Fix3) · `fa02a5c` test fakes · `9bb1321` disproof handover ·
`34ecf7d`/`47e7b3a`/`6718d16` plan+S0+R3.Godot · `0f13fc6` S1 (green) · `e9ed95a` plan ·
`9e81ef6` S2a (green) · `91393f5` hold · `892956c` code-quality Step A · `714232f` code-quality Step B.
