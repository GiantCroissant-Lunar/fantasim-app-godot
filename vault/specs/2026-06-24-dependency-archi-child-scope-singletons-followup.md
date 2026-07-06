# Follow-up — dependency-archi: finish child-scope singleton sharing, then drop manual kernel forwarding

> **AUDIT (2026-07-06, code-verified):** interim-workaround pointers are stale (Stage/Assist activators are now thin shells); library-side landing unverified. _(See the authority index in `vault/README.md`.)_


**Status:** FOLLOW-UP / deferred (2026-06-24). Tracks a `plate-projects/dependency-archi`
change so the app-side multi-scene DI adoption is **not blocked** on it. The app keeps using
the interim manual-forwarding workaround until this lands.

**Parent review:** [`vault/architecture/multi-scene-di-scoping-review.md`](../architecture/multi-scene-di-scoping-review.md) — Issue 1 (High, "First").
**Origin RFC:** `ref-projects/fantasim-app-godot/vault/rfc/rfc-dependency-archi-child-scope-singletons.md`.
**Target file:** `plate-projects/dependency-archi/dotnet/src/DependencyArchi.MicrosoftExtensions/MicrosoftExtensionsScopeActivationAdapter.cs`.

---

## Why this is a follow-up, not a blocker

The app adopts ref's multi-scene parent/child scope concept now using **manual kernel
forwarding** — each `ISceneActivator` re-registers the resolved parent `IRegistry` /
`ILoggerFactory` into its child `ServiceCollection` (see `App.Stage/StageActivator.cs`,
`App.Assist/AssistActivator.cs`). It works, but it is fragile (the review's Issue 1):

- Every new shared kernel singleton must be hand-forwarded in **every** activator; a miss
  silently resolves to `null` or a different instance.
- The child builds a fresh `ServiceProvider`, so it is not a real child of the parent —
  parent scoped/transient services are invisible.
- Disposal is *accidentally* correct (MEDI never disposes `AddSingleton(instance)` singletons,
  so child teardown can't kill the shared kernel) — a MEDI implementation detail, not a contract.

The clean fix lives in the DependencyArchi MEDI adapter, a **separate repo**. It should not
gate the app work, hence this follow-up.

## Current state of the adapter — READ before "fixing"

The class-level XML doc still asserts:

> *"V1 limitation: singleton instances are not shared between parent and child scopes … a
> singleton registered in the parent scope will have a distinct instance in the child scope."*

**That doc is stale.** `ActivateScope` (lines 74–96) already implements RFC option 1
(*resolve-and-forward*): for a child with a `ParentId`, it walks the parent collection and
re-registers each parent **singleton instance** into the child via
`builder.Insert(0, ServiceDescriptor.Singleton(type, instance))` — so singleton **identity is
shared**, and a child registration of the same type overrides (last-wins). The headline
"singletons aren't shared" problem is therefore already solved in code — but under-documented,
contradicted by its own remarks, and (in this app) entirely unexercised because nothing routes
through the adapter yet.

## What actually remains

1. **Reconcile doc ↔ code.** Fix the stale class remark *and* `CreateBuilder`'s "Parent
   descriptors are merged" comment (they are not — only singleton **instances** are, in
   `ActivateScope`). Otherwise a future reader re-"fixes" an already-working path.
2. **Decide option 1 vs option 2.** Option 1 (current) shares only **singletons**; parent
   **scoped/transient** services stay invisible to children. The review recommends **option 2 —
   native `parentProvider.CreateScope()`** — where parent registrations are visible by
   construction and only child-specific services are added on top (idiomatic MEDI, no per-type
   forwarding loop, no shared `_collectionsByScope` dictionary). Decision: keep option 1
   (sufficient for the kernel-singleton use case) or move to option 2 (fuller hierarchy).
3. **Tests.** Prove the RFC acceptance criteria (none exist for the parent/child activation
   path today):
   - a parent-only `AddSingleton<X>` resolves to the **same reference** from a child scope;
   - child-only singletons stay **distinct** per child;
   - disposal stays **child-before-parent**, and disposing a child does **not** dispose parent
     singletons.
4. **Then, in the app:** once the adapter path is trusted + tested, scene activators drop
   manual forwarding and rely on the adapter's parent/child activation — removing the Issue 1
   fragility. (App change, separate from the library change above.)

## Acceptance (from the origin RFC)

- A type `AddSingleton`-registered only in `app-root` resolves to the **same reference** from
  both `app-root` and a child scope's `IServiceProvider`.
- Child-only singletons remain distinct per child.
- Disposal stays child-before-parent; disposing a child does not dispose parent singletons.

## Pointers

- Adapter (forwarding + stale doc): `plate-projects/dependency-archi/dotnet/src/DependencyArchi.MicrosoftExtensions/MicrosoftExtensionsScopeActivationAdapter.cs`
- Scope/parent attr + topology: `…/DependencyArchi.Abstractions/DependencyScopeAttribute.cs`, RFC-0005 (scope topology), RFC-0008 (adapter shape)
- App interim workaround: `project/plugins/App.Stage/StageActivator.cs`, `project/plugins/App.Assist/AssistActivator.cs`
- Parent review (full 5-issue table): `vault/architecture/multi-scene-di-scoping-review.md`
