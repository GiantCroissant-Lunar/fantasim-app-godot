# Plan — reload ALC-collection: testability-first redesign (2026-06-25)

**Status:** IN PROGRESS (living doc — update the Status table as slices land).
**Branch:** `feat/world-to-stage-phase1`. **Prior context:**
`vault/handover/2026-06-25-reload-gate-timing-and-mainthread.md`,
memory `fantasim-bundle-reload-redesign`.

## Problem (what the windowed verify proved)
Collectible-bundle hot-reload never shows `old ALC collected` — two root causes,
one theme (an inherently main-thread, frame-based Godot op forced through an async
HTTP-command pipeline):
1. **Gate is a false negative.** `VerifyOldContextCollectedAsync` checks collection
   *synchronously inside the live reload call stack*; transient refs (the in-flight
   async state machine, R4 deferred holders) pin the ALC during the check. Even
   `assist` (none of the known pins) reports "still pinned".
2. **Reload runs off the main thread.** The Fix-1 `await UnmountAndWaitAsync(...)
   .ConfigureAwait(false)` yielded to the threadpool → `ExitAsync → RemoveScene →
   Node.RemoveChild` ran off-main → "Removing children only allowed from the main
   thread". (REGRESSION committed in `cd8e6db`; must be reworked.)

**And the test gap that hid it:** the unit tests pass against `FakeViewHost`
(`Task.FromResult`, models nothing real). A fake of a Godot seam can never catch a
Godot-threading/GC bug. The pins are *managed references* and the gate is
`WeakReference`+GC — both pure .NET — so they are testable for real without Godot.

## Principles (agreed 2026-06-25)
- **Real but simple over fakes** — even in integration tests. A real-but-simple
  impl that exhibits the real behavior (holds/drops a managed ref) beats a mock.
- **Headless integration is fine.** ALC collection needs the Godot *runtime*, not a
  *window*. Export is a few minutes (not a blocker).
- **R3 for frame scheduling.** Cysharp R3 is already in the app; its `FrameProvider`
  is the injectable seam (real Godot frames in prod; a simple manual ticker in tests).
- **Delegation:** ollama-cloud dispatch uses **`ollama/glm-5.2:cloud`** or
  **`ollama/kimi-k2.7-code:cloud`** ONLY. Do NOT use gemini/`agy` (expensive).

## Target architecture
Extract the reload **policy** (pure C#) — `unmount → unload → next-frame(s) →
GC → probe` — depending only on small seams:
- a ref-holding **view host** (drops the bundle `IViewSource`),
- the **ALC probe** = existing `PluginUnloadResult.IsCollected(forceGc)`,
- an R3 **`FrameProvider`** (defer the probe until the reload stack has unwound + a frame).

| Seam | Production | Test |
|---|---|---|
| view host | Godot `ViewHost` (Control/SceneTree, **synchronous** unmount on main) | real-but-simple in-memory host (holds/drops the ref) |
| frame source | R3 Godot `FrameProvider` | simple manual `FrameProvider` (tick by hand) |
| ALC probe | `PluginUnloadResult` (real) | `PluginUnloadResult` over a real collectible ALC |

**The fix falls out of this:** keep the whole reload on the main thread (no
`ConfigureAwait(false)` before scene ops); the view unmount becomes a *synchronous
main-thread call* (no deferred `TaskCompletionSource` → kills the regression); the
collection probe is deferred via the `FrameProvider` to after the stack unwinds.

## Test layers
1. **Plain xUnit (no Godot, no export):** real-but-simple host + real collectible
   ALC (emit a tiny `IViewSource` assembly à la plugin-archi `PluginHostDiagnosticsTests`)
   → asserts pin-when-held, collect-when-dropped, and the frame-deferred gate
   (manual `FrameProvider`). Covers hygiene + collection + gate-timing. GC care
   required (drop refs, `[MethodImpl(NoInlining)]`, bounded force-GC) — copy
   plugin-archi's structure.
2. **One headless integration test:** real Godot `ViewHost`/`SceneTree`/`BundleHost`
   + real ALC, `godot --headless`, reload a bundle, assert collection. Covers the
   Godot seam (main-thread `RemoveChild`, real frames). Needs bundles packed.

## Staged plan (each = a dispatchable slice)
- **S0 (scoping):** confirm R3 `FrameProvider` API+version in the app; the
  plugin-archi collectible-ALC test template; exact reload-path files + where the
  policy lives; whether `HttpTransportTests` can use the real command `Service`.
- **S1 (test-first, RED):** real-but-simple `IViewHost` + plain-xUnit ALC-collection
  test (held→pinned, dropped→collected). No production change yet.
- **S2:** extract the reload **policy** over the 3 seams; rework Fix 1 to a
  synchronous main-thread unmount; defer the probe via `FrameProvider`. Make S1 green.
- **S3:** wire R3 Godot `FrameProvider` + ensure the reload stays on the main thread;
  headless integration test.
- **S4 (cleanup):** drop `FakeCommandService` (use the real Godot-free `Service`);
  remove the dead `UnmountAndWaitAsync` if the policy supersedes it.
- **Re-verify** windowed once green.
- DAG / modern-satsuma inter-bundle cascade = **Phase 2** (not this plan).

## Status
| Slice | State | Notes |
|---|---|---|
| S0 scoping | DONE | report `.agent/run/reports/s0-reload-scoping.md` (glm-5.2) |
| S1 collection harness | DONE | 2/2 pass (verified via `dotnet test`): held->pinned, dropped->collected |
| S2a policy + gate test | DONE | `ReloadPolicy` + gate test green (verified `dotnet test`): frame-deferred probe reports collected |
| S2b wire + Fix1 rework | after S3 host | wire ReloadPolicy into the seam; sync main-thread unmount; verified by the code-quality headless test |
| S3 headless host | IN PROGRESS | code-quality boots headless + R3.Godot addon vendored; `GodotFrameProvider.Process` advances headless [Step A+B done]; next: Step C real reload-collection check |
| S4 cleanup | not started | drop `FakeCommandService` (real Service is Godot-free) |

## S0 findings (RESOLVED 2026-06-25) — see `.agent/run/reports/s0-reload-scoping.md`
- **R3 1.3.1** (CPM `Directory.Packages.props:8`). `R3.FrameProvider` is an abstract class;
  `R3.FakeFrameProvider` (`.Advance()`) is the real manual test source; `Observable.NextFrame(
  frameProvider, ct)` / `TimerFrame(N,...)` are awaitable. **R3 HAS official Godot support**
  (CORRECTED — docs https://github.com/Cysharp/R3#godot; S0 only proved it was not installed
  locally). Not a nuget: copy the `addons/R3.Godot` plugin into the Godot project + enable it.
  It provides `GodotFrameProvider.Process` / `.PhysicsProcess` (+ `GodotTimeProvider.*`) and an
  autoloaded `FrameProviderDispatcher` that sets them as R3 defaults and routes UnhandledException
  to `GD.PrintErr`. So S3 INSTALLS the addon (matching R3 1.3.1) and injects
  `GodotFrameProvider.Process` — NO hand-written provider. R3 today is used only in
  `App.World.FieldView` (pure).
- **Test template** = plugin-archi `TestAssemblyFactory.EmitPluginAssembly` (Roslyn
  `CSharpCompilation.Emit`) + `PluginHostBuilder.AddPluginGroup(...).Build()` (collectible ALC);
  weak-only `PluginUnloadResult` + bounded force-GC poll (10×25ms); no `[MethodImpl(NoInlining)]`
  needed (just don't retain ALC-typed locals).
- **Pin = `ViewRenderer._source`** (managed `IViewSource`), dropped by `Unbind()` → `_source=null`
  (`App.Ui.Seam/ViewRenderer.cs:18,66`). Godot Control nodes hold NO bundle-typed managed refs.
- **Real command `Service`** is Godot-free; ctor `(IMainThreadDispatcher, IRegistry,
  ILoggerFactory, IWorldOrchestration?=null)`; `ImmediateMainThreadDispatcher` is Godot-free →
  S4 can drop `FakeCommandService` (add projref to `App.Command`, update the `HealthAndStatus`
  assertion to the real command list).
- **CORRECTION to the report's layout:** `ReloadPolicy` must NOT live in
  `App.Resource.Bundle.Seam` (that's `Godot.NET.Sdk` → drags GodotSharp → not plain-xUnit-testable).
  Put it in the PURE `plugins/App.Resource` (net8.0) as a class taking delegates/abstractions
  (`Func<Task<bool>>` unmount, `Func<Task<PluginUnloadResult?>>` unloadReload, `R3.FrameProvider`).
  The Godot `.Seam` wires the concrete `BundleHost`/`ViewHost` closures at composition.

## Confirmed file layout (adjusted)
- `plugins/App.Resource/ReloadPolicy.cs` — PURE policy (unmount → unloadReload → `NextFrame` →
  GC-poll → `ReloadResult`). [S2]
- R3 Godot addon: copy `addons/R3.Godot` (matching R3 1.3.1) into the Godot project + enable the
  `FrameProviderDispatcher` autoload; prod injects `GodotFrameProvider.Process`. NO hand-written
  provider. [S3]
- `tests/App.Resource.Tests/{SimpleViewHost,TestViewSourceFactory,ReloadCollectionTests}.cs` —
  real-but-simple host + Roslyn-emitted `IViewSource` + plain-xUnit collection tests. [S1, no new project]
- Headless integration via complete-app `run:headless` (drive `resource.reload_bundle`, assert
  `old ALC collected` in the console log) — NO new project (complete-app runs headless). [S3]
