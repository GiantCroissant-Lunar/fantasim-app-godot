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
| S0 scoping | DISPATCHED | opencode `ollama/glm-5.2:cloud` |
| S1 RED test | not started | |
| S2 policy + Fix1 rework | not started | |
| S3 R3 + headless | not started | |
| S4 cleanup | not started | |

## Open unknowns (S0 fills these)
- R3 `FrameProvider` exact type/API + the app's R3 package version.
- plugin-archi test: how it emits/loads a collectible test assembly (Roslyn? fixture?).
- Reload-path file map: where the policy should live (BundleHost vs a new coordinator).
- Real command `Service` usable in `HttpTransportTests` (drop the fake)?
