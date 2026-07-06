# Spec — PluginArchi: ALC-collection diagnostic for bundle hot-reload

> **AUDIT (2026-07-06, code-verified):** IMPLEMENTED (Option A live: BundleHost `IPluginHostDiagnostics`, weak-only probe) — the 'PROPOSED — awaiting approval' header is stale. _(See the authority index in `vault/README.md`.)_


**Status:** PROPOSED — awaiting approval (this is a `plate-projects/plugin-archi` API change,
not a yokan-only detail). Filed from fantasim-app-godot. On approval this should become a
`plate-projects/plugin-archi/docs/rfcs/` RFC.

**Why:** bundle hot-reload needs a SOUND signal that a collectible bundle's `AssemblyLoadContext`
actually collected after unload. `Directory.Delete` succeeding is NOT that signal — on macOS/Linux
`unlink` of a still-mapped DLL succeeds regardless of ALC liveness (false positive). The sound
signal is a `WeakReference` to the ALC + a forced GC. PluginArchi already has the primitive
(`IsolatedLoader.IsContextCollected(bool forceGc)`), but it is unreachable from consumers:
`IPluginHost`/`IPluginGroup` expose no loader/collection status, `PluginGroup.Loader` is `internal`,
`PluginHost` is `internal sealed`. yokan (which holds only `IPluginHost`) cannot gate on collection.

Companion: `vault/architecture/bundle-delivery-and-loading.md` (the `[R-F5]` gate). This spec
supplies the missing primitive that doc's verification half depends on.

---

## The load-bearing invariant: weak-only retention

The diagnostic must retain **only a `WeakReference`** to the ALC (and, if useful, a `WeakReference`
to a representative assembly). It MUST NOT hold a strong reference to the `IsolatedLoader`,
`PluginGroup`, or `AssemblyLoadContext`.

> If the diagnostic keeps the loader/group/ALC alive to answer the query, **the diagnostic becomes
> the pin** — `IsCollected()` returns false forever and the gate reports "still pinned" on a bundle
> that actually collected cleanly. This is the single most likely way to ship a silently-wrong gate.

Therefore `RemoveGroup...` must, as part of the unload, drop ALL of PluginArchi's own strong refs to
that ALC/loader/group and hand the result object nothing but the `WeakReference`(s).

## API shape — the return-type change is BREAKING

Confirmed: `IPluginHost.RemoveGroupAsync(string, CancellationToken)` returns `ValueTask<bool>`.
Changing it to `ValueTask<PluginUnloadResult>` is **not** source-compatible — it breaks
`if (await host.RemoveGroupAsync(id))`, all mocks/fakes, and all external `IPluginHost` implementers.
Three options, ranked by blast radius:

### Option A (recommended) — separate diagnostics interface, `IPluginHost` untouched

```csharp
// new, in PluginArchi.Extensibility.Abstractions (the package that actually holds IPluginHost; net8.0+netstandard2.1, DI-free)
public interface IPluginHostDiagnostics
{
    // Same unload as RemoveGroupAsync, but returns the weak-only collection probe.
    ValueTask<PluginUnloadResult> RemoveGroupWithDiagnosticsAsync(string groupId, CancellationToken ct = default);
}
```

`PluginHost` (the reference impl) also implements `IPluginHostDiagnostics`. Consumers opt in by cast:
`(host as IPluginHostDiagnostics)?.RemoveGroupWithDiagnosticsAsync(id)`. **Zero breakage** —
`IPluginHost` and its existing `RemoveGroupAsync` are unchanged, so no caller / mock / implementer
breaks. Returns the result/handle directly, so there is **no awkward by-id re-query after removal**
and **no retained per-group map** to get weak-only wrong.

### Option B — add the method to `IPluginHost`

`ValueTask<PluginUnloadResult> RemoveGroupWithDiagnosticsAsync(...)` directly on `IPluginHost`.
Additive for callers, but **breaks every implementer/mock** (must implement it). A default interface
method avoids that for `net8.0` but not reliably for `netstandard2.1` consumers on older runtimes
(Unity/Mono). Simpler surface than A, more breakage.

### Option C — change `RemoveGroupAsync`'s return type, version it as breaking

`ValueTask<PluginUnloadResult> RemoveGroupAsync(...)` where `PluginUnloadResult` carries the old
`bool` (e.g. `UnloadInitiated`). Cleanest single method long-term, but a **breaking change**: bump
the package major/minor (GitVersion), update `PluginHost`, all mocks, and every `if (await ...)`
caller. An `implicit operator bool` softens call sites but does not save implementers.

**Recommendation: Option A.** It is the only one that touches nothing existing, and the
return-a-result shape makes the weak-only invariant the natural (not the careful) outcome.

## `PluginUnloadResult` (weak-only, netstandard2.1-safe, DI-free)

```csharp
public sealed class PluginUnloadResult
{
    public string GroupId { get; }
    public bool   UnloadInitiated { get; }   // what the old ValueTask<bool> conveyed

    /// Checks the retained WeakReference. When forceGc, runs ONE bounded sequence:
    ///   GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    /// then returns !weakRef.IsAlive. NO loop, NO retry inside the library.
    public bool IsCollected(bool forceGc = false);
}
```

- One bare `GC.Collect()` can under-report (finalizer ordering), so `forceGc:true` runs the full
  three-call sequence ONCE — bounded, explicit, not a loop.
- The library owns the *primitive* (one check). It does NOT own the *cadence* (see below).

## Host responsibility (yokan) — owns the policy, not the library

The library returns a result; the host decides what to do. yokan's `BundleHost`:

1. On reload's unload step, call `RemoveGroupWithDiagnosticsAsync` and keep the `PluginUnloadResult`.
2. Poll `IsCollected` on a **bounded, frame-paced** cadence (the resident holder drops its ref 1-3
   frames later — `cross-alc-rules` R4), `forceGc:true` on the final check. The retry loop lives
   HERE, never in the library (`[R-F6]`: no stop-the-world GC loop in shared code).
3. Decide: log `old ALC collected: {id}` / `old ALC still pinned: {id}`, mark the reload degraded,
   schedule a later re-check, or fail a verification gate.

This replaces the unsound delete-as-proof. Temp-dir deletion stays as best-effort cleanup, logged
separately — never as the collection signal.

## Required test (the guardrail that catches accidental strong retention)

In PluginArchi's hosting tests, using `TestAssemblyFactory`:

1. `AddGroupAsync` a collectible test assembly; resolve/instantiate a type from it (in a separate
   non-inlined method so the test scope keeps no strong ref to the instance/type).
2. `RemoveGroupWithDiagnosticsAsync` -> capture the `PluginUnloadResult`.
3. Drop all local strong refs.
4. Assert `IsCollected(forceGc: true)` **eventually returns true** within a bounded number of checks.

Plus a **negative test** (proves the gate has teeth): intentionally retain a strong ref to a loaded
type; assert `IsCollected(forceGc: true)` stays **false**. A gate that can't report "still pinned"
is useless. These two tests are the real acceptance criteria — not "build green."

## Non-goals

- The library does NOT own the retry loop or any timed sweep.
- `Directory.Delete` is NOT a collection signal anywhere.
- Option A leaves `IPluginHost.RemoveGroupAsync` semantics unchanged.

## Open questions

- Method/interface name (`IPluginHostDiagnostics.RemoveGroupWithDiagnosticsAsync` is a working name).
- Should the same diagnostic be offered for `ReloadGroupAsync` (which also unloads an ALC)? Likely yes,
  same shape.
- Package placement (DECIDED): `IPluginHostDiagnostics` + `PluginUnloadResult` live in
  **`PluginArchi.Extensibility.Abstractions`** — the package that actually contains `IPluginHost`
  and that consumers (yokan's `App.Resource.Bundle.Seam`) already reference, so opting into
  diagnostics adds **no new package reference**. It is `net8.0;netstandard2.1` + DI-free, which
  suits the weak-only `PluginUnloadResult`. NOTE: the plugin-archi package `CLAUDE.md` claims
  `IPluginHost` is in `Hosting.Abstractions` — that is **stale**; the code has it in
  `Extensibility.Abstractions`, and `Hosting.Abstractions` is `net9.0` holding only validator/
  orderer/orchestrator contracts. Do NOT place the diagnostic there unless we deliberately want
  consumers to take a new net9.0 dependency.

## References

- `vault/architecture/bundle-delivery-and-loading.md` — `[R-F5]` (WeakReference gate), `[R-F6]` (no GC loop).
- `vault/architecture/cross-alc-rules.md` — R4 (deferred holder release).
- PluginArchi `IsolatedLoader.IsContextCollected(bool forceGc)` — the existing primitive to surface.
- Current contract: `plate-projects/plugin-archi/.../PluginArchi.Extensibility.Abstractions/IPluginHost.cs:83`
  (`RemoveGroupAsync` returns `ValueTask<bool>`).
