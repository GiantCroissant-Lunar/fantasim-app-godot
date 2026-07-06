# Bundle delivery & loading — two concerns + an Addressables-style catalog

> **AUDIT (2026-07-06, code-verified):** CURRENT with drift — `ResourcePckWatcher`/`IService.WatchResource` were NOT deleted (a `SceneTierPckWatcher` was added); hot-reload landed via `App.Resource/ReloadPolicy.cs` (2026-06-25 frame-deferred redesign), not this doc's mechanism; catalog phases B/C unbuilt (only `IiiWorkerBundleCatalog`). _(See the authority index in `vault/README.md`.)_


**Status:** PROPOSED (2026-06-24). Supersedes the file-watcher (`ResourcePckWatcher` /
`IService.WatchResource`) hot-reload trigger. Companions: `cross-alc-rules.md`,
`multi-scene-di-scoping-review.md`.

**Review:** adversarial design review run via opencode/GLM-5.2 (2026-06-24); confirmed
findings folded in below and tagged `[Rnn]`. Log:
`.agent/logs/opencode/bundle-design-review-20260624-142103.log`.

## Why (the watcher is the wrong abstraction)

The current runtime hot-reload trigger is `App.Resource.Service.WatchResource` →
`ResourcePckWatcher` → a `FileSystemWatcher` on a local bundle dir, called from `ViewHost`
per mounted view. Three problems:

1. It bakes a dev-loop concern (reload-on-rebuild) into the runtime `IService` contract.
2. `FileSystemWatcher` is unreliable/unsupported on remote sources (S3, network shares) and
   meaningless for bundles embedded in an exported PCK.
3. It watches the wrong thing — remote-loaded bundles live in a temp file, explicit-path
   loads elsewhere, embedded bundles nowhere on disk.

Fix: split into two concerns with a **catalog** as the contract between them.

## Two concerns

**Concern 1 — publish (outside the runtime).** Build a bundle, place its bytes at a location
(next to exe / shared dir / S3 / CDN), and **publish an updated catalog pointer atomically with
the bundle**, then notify. Pluggable: a Taskfile step (local dev), a CI/CD step, an S3-event
control-plane (prod).

**Concern 2 — adopt (the runtime).** Fetch the catalog, fetch the bundle bytes per scheme,
**verify**, and swap it in. The runtime knows how to *fetch/verify/swap*; it is *told* what/when.

`[R-F8]` The boundary is not "Concern 1 places, Concern 2 loads" — for remote bundles,
*materialization* (download → local temp → mount) is the runtime's job. State it as: **Concern 1
publishes (bytes + catalog) atomically; Concern 2 fetches, verifies, materializes, swaps.** The
catalog publish must be atomic (e.g. write `catalog.json.new`, then rename) so Concern 2 never
sees a catalog pointing at an unplaced bundle.

## The trigger is an ingress (not a watcher)

"Notify the app to reload" generalizes to: any ingress invokes a reload command. The app already
has the ingress-adapter architecture (App.Command: UI / system / remote adapters over one catalog):

- **UI ingress** — a "Reload" button in a dev/ops panel dispatches `resource.reload_bundle`.
- **Remote ingress** — build step / CD calls `fantasim-cmd.py cmd resource.reload_bundle ...` (HTTP :19292).
- **Poll** (optional) — a Concern-1 strategy that GETs a remote catalog version and dispatches
  reloads for changed entries. The *right* "automatic" — an HTTP GET, not a watcher.

All end at one transport-agnostic runtime entry: *"reload address X (from location L, version V)."*

`[R-F9]` **Idempotency contract:** the command carries `{address, version|hash}`. The runtime
fast-rejects when `loaded[address].hash == hash` **before** taking the bundle gate (cheap, no
serialize); only the actual mutation is gated. Concurrent reloads of the same address collapse to
one; concurrent reloads of *dependent* bundles (see `[R-F3]`) must serialize through the scene-flow
gate, not race two gates.

## The catalog (adopt from Unity Addressables, selectively)

**Adopt:** address indirection (logical key, not a path); a versioned **content catalog**
(`address → { location (scheme-tagged), hash, size, deps }`, local baseline + optional remote);
**provider per location scheme** (`file` local, `s3`/`https` remote); **catalog versioning**.
`collectible-bundles.json` (today `bundleId → pluginAssembly`) is the seed — evolve it.

**Skip / defer:** Unity asset-GUID / `AssetReference`; the Addressables build pipeline; groups/
labels/profiles. **Reference counting** is deferred *only for un-shared bundles* — see `[R-F3]`.

### Proposed catalog schema (v1, minimal)

```json
{
  "catalogVersion": "1",
  "minCatalogVersion": "1",
  "bundles": [
    { "address": "assist", "pluginAssembly": "FantaSim.App.Assist.dll",
      "location": { "scheme": "file", "path": "bundles/assist.pck" },
      "hash": "<sha256>", "dependsOn": [] }
  ]
}
```

`[R-F7]` **Catalog integrity & rollback:** the per-bundle `hash` is circular if the *catalog
itself* is untrusted (a tampered remote catalog supplies both the bundle and its hash). For the
remote path: serve the catalog over a pinned TLS cert or sign it against a root hash bundled in the
app, and enforce a monotonic `minCatalogVersion` floor to refuse downgrade/replay. State the threat
model explicitly (bit-rot vs MITM vs rollback). For the local-dev path this is moot.

## The runtime loader (Concern 2)

One catalog-driven path: **resolve address → fetch via provider → verify → swap.**

- **Resolve** — look up `address` in the active catalog → location + hash.
- **Fetch (2a)** — provider for `location.scheme` returns local PCK bytes (download for remote).
  `[R-F12]` Remote fetch MUST verify the downloaded bytes' SHA256 against the catalog `hash`
  **before** writing the path `LoadAsync`/`LoadPck` consumes — `LoadRemoteAsync` does not do this today.
- **Swap (2b)** — see below.

### Swap details (the hard part)

`[R-F4]` **Stage-then-swap, never unload-then-pray.** Fetch + verify the new bundle to a temp path
*before* unloading the old. Only unload once the new bundle is staged + verified. On load failure
after unload, retain the old bundle's temp dir + ALC (do not queue it for cleanup) and re-load
last-known-good; log `reload failed, old bundle retained`.

`[R-F2]` **Parent reload cascades.** `SceneFlow.ExitCoreAsync` exits children before the parent, so
reloading `stage` tears down `assist`+`timeline`, and re-`Enter(stage)` does **not** restore them
(no auto-discovery — `multi-scene-di-scoping-review` Issue 5). **v1 scope: leaf-scene reloads only**
(assist/timeline). A parent reload is a documented full-subtree operation: capture the active
subtree, Exit target, Enter target, then re-Enter each captured child with its original parent.
Name the scene-scoped state lost on re-enter (camera, lighting, assist convo, timeline cursor).

- **Scene tiers** swap via **`SceneFlow.Exit → Enter`** (held under `SceneFlow.Service._gate` across
  the whole transition; `BundleHost._gate` is taken only transiently per load/unload).
  `[R-F1]/[R-F13]` The pin to release is the child provider held by `SceneFlowProvider._active`
  (bundle-typed `Bootstrap`) — the activator itself is dropped by `StagePlugin.ShutdownAsync`
  disposing its `RegisterOwned` handle on `RemoveGroupAsync`. The probe (below) confirms which.
- **View bundles** swap via the existing `BundleHost`/`ViewRenderer` path.
- `[R-F15]` **`CacheMode.ReplaceDeep` (R3) is mandatory** in the swap, or `ResourceLoader.Load`
  returns the OLD cached scene/script even after the ALC turns over — reload would show stale
  content while the gate reports success. Verify "window shows updated content," not just collection.

### Collection verification — WeakReference, NOT `Directory.Delete`

`[R-F5]` **Critical correction to ref's `ScheduleTempCleanupSweep`.** Ref proves collection by
"the temp-dir delete succeeded." That is **unsound on macOS/Linux** (our dev OS): `unlink` removes
a still-mapped DLL's dir entry and *succeeds* whether or not the ALC released it — a false positive.
The collection gate MUST be a `WeakReference` to the bundle's `AssemblyLoadContext` (PluginArchi's
`IsolatedLoader.IsContextCollected(forceGC)` already does exactly this) + assert `!IsAlive` after a
forced GC. Temp-dir deletion stays as best-effort cleanup, logged *separately* ("temp dir cleaned"),
never as the collection proof.

`[R-F6]` **Don't stop-the-world the frame loop.** A background `Task.Run` doing
`GC.Collect();WaitForPendingFinalizers();GC.Collect()` every 250ms × 16 induces 60fps hitches.
Drive at most one forced GC after a short settle (the resident holder drops its ref in 1–3 frames),
frame-paced, not a tight loop.

`[R-F11]` yokan's `ReloadAsync` doesn't even capture/enqueue the old temp dir today — the port must
add the enqueue-on-reload step, not just a sweep method.

## What changes

- **Remove** `IService.WatchResource`, delete `ResourcePckWatcher`, drop the `ViewHost` call, update fakes.
- **Add** a `WeakReference`-based ALC-collection gate (`old ALC collected` / `still pinned`) on the
  reload path — replacing the unsound delete-based signal `[R-F5]`.
- **Make** scene-tier reload stage-then-swap `[R-F4]`, leaf-scoped `[R-F2]`, with `ReplaceDeep` `[R-F15]`.
- **Extend** `resource.reload_bundle` payload to `{ address, location?, version? }` with the
  idempotency fast-path `[R-F9]`.
- **Evolve** `collectible-bundles.json` into the catalog + provider abstraction (local + remote),
  with remote hash-verify `[R-F12]` and catalog trust/rollback `[R-F7]`.

## Sequencing

`[R-F10]` **Phases 1–3 are one work item, not independent slices** — slice 1's trigger routes
through the path that leaks scene-tier ALCs until phase 3 fixes it; shipping 1 alone would ship a
trigger that fails the gate. Phases 4–5 are genuinely independent.

- **Phase A (one item): trigger + verification + scene-tier reload**
  1. Remove the watcher; `resource.reload_bundle` becomes the sole trigger; wire `task bundle:<tier>` to it.
  2. Add the `WeakReference` collection gate `[R-F5]` (also the diagnostic for whether a reload collects).
  3. Route scene-tier reload through `SceneFlow.Exit → Enter`, leaf-scoped, stage-then-swap.
  *Not "done" until the gate passes (`old ALC collected`) for a scene tier in the windowed app.*
- **Phase B (independent): catalog + providers** — schema, `file`/`remote` providers, payload `location`/`version`.
- **Phase C (independent): UI ingress + remote/poll** — reload panel; optional remote-catalog poll.

## Open questions

- Catalog format: extend `collectible-bundles.json` in place vs new `catalog.json`? (Lean: extend.)
- Catalog resolution home: `App.Resource.Service` vs a `BundleCatalog`? `[R-F14]` This affects the
  idempotency fast-path — decide before specifying the trigger.
- Remote fetch: a provider interface (`LoadRemoteAsync` becomes the `https` provider).
- Does the UI reload panel ship in a collectible bundle (dogfood) or resident dev tooling?

## Immediate next step — the pin probe

`[R-F1 / single most important]` Before phase-A code, settle empirically what pins a scene-tier ALC:
enter `stage`, exit it, force GC, and check both `_registry.GetAll<ISceneActivator>()` and a
`WeakReference` to the stage ALC/assembly. If the activator is gone and the WeakReference is dead,
the doc's mechanism (child-provider pin, released by `SceneFlow.Exit`) is right; if the WeakReference
is alive, find the real pin first. This probe IS the start of the `[R-F5]` WeakReference gate, so it
is not throwaway.

## References

- `vault/architecture/cross-alc-rules.md` — R1-R7 collectible-ALC discipline.
- `vault/architecture/multi-scene-di-scoping-review.md` — SceneFlow scopes; the scene-tier holder.
- PluginArchi `IsolatedLoader.IsContextCollected(forceGC)` — the WeakReference collection check `[R-F5]`.
- ref `ScheduleTempCleanupSweep` — `ref-projects/.../App.Resource.Bundle.Seam/BundleHost.cs:277`
  (cleanup pattern; its delete-as-proof is unsound on macOS — use it for cleanup, not the gate).
- Unity Addressables — catalog / addresses / `IResourceProvider` / remote delivery (concepts only).
