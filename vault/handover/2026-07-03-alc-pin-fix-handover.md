# ALC Pin FIX — MessagePack shared-resolver cache was the single root (windowed-verified)

**Date:** 2026-07-03 (afternoon, follows the same-day diagnosis report)
**Branch:** `main` — fix landed as `fix(reload): evict MessagePack's collectible-keyed resolver cache on bundle unload`
**Prior doc:** [2026-07-03-alc-pin-diagnosis-report.md](2026-07-03-alc-pin-diagnosis-report.md)

---

## 1. TL;DR

After the frame-deferred gate fix (`fa614f1`) the world "old ALC still pinned" was a **REAL pin**
with exactly **one root**, found by `dotnet-dump gcroot` on the live exported app:

```
strong handle
 -> MessagePack.Resolvers.SourceGeneratedFormatterResolver
      static ConcurrentDictionary<Assembly, IFormatterResolver?> AssemblyResolverCache
 -> stale bundle RuntimeAssembly (world/2 extraction dir)
 -> LoaderAllocator -> the whole PluginGroup_world ALC
```

MessagePack (3.1.7) is deliberately **shared/resident** (the 2026-07-02 cross-ALC type-identity
fix). Its source-gen resolver memoizes `Assembly -> IFormatterResolver` and never evicts, so every
serialize/deserialize touching a bundle-typed value roots that bundle's assembly forever. The
cached keys were `FantaSim.World.TruthStream.Core` and `FantaSim.World.Fields.Stream` (their
`GeneratedMessagePackResolver`s).

**Fix:** `BundleHost.UnloadCoreAsync` now calls `SharedMessagePackCachePurge.EvictCollectibleEntries`
right after `RemoveGroupWithDiagnosticsAsync` — reflects into the private static
`AssemblyResolverCache` and removes **every collectible-keyed entry** (pure memoization; it
repopulates on demand, and evicting live bundles' entries too keeps the predicate independent of
plugin-archi context naming and self-heals accumulated stale generations).

## 2. Windowed verification (exported app, `_artifacts/0.1.2` binary of 15:42)

- world.pck copy → watcher reload → `Hot-reload: evicted 2 collectible-keyed MessagePack resolver
  cache entries on unload of world: FantaSim.World.TruthStream.Core, FantaSim.World.Fields.Stream`
  → **`Hot-reload: old ALC collected for bundle world`** — twice in a row (repeatable).
- timeline via `resource.reload_bundle` (remote ingress) → Exit→Enter →
  **`Hot-reload: old ALC collected for bundle timeline`**. The June idea of moving the probe to
  CommandComposition after Exit→Enter is **moot**: the frame-deferred probe already survives that
  window and reports collected.
- App healthy after 3 reloads: planet renders (magma-ocean tier), node-graph + activity views
  mounted, `timeline.composition.rebound` in the ledger, 0 exceptions in the log.

## 3. Open Questions from the diagnosis report — answered

1. **Does RemoveGroupWithDiagnosticsAsync complete ShutdownAsync before ALC.Unload?** YES —
   verified in plugin-archi source: `PluginGroup.UnloadAsync` awaits `ShutdownLifecyclesAsync`
   before `_loader.UnloadAsync()` (PluginGroup.cs:146-169).
2. **Which thread?** Threadpool (watcher path) — harmless: WorldPlugin.ShutdownAsync does no Godot
   ops; binder cleanup marshals via CallDeferred; probe frame-defers.
3. **Cartography shared-policy expansion** — unchanged, still a future policy decision.
4. **Resident code holding stale TruthStreamIdentity** — the two on-heap instances were
   *consequences* of the MessagePack pin (rooted only through the cache chain), not held by any
   resident service.

## 4. "timeline.pck copy produced no gate line" — explained

**Nothing watches scene-tier pcks.** `WatchResource` is only installed by ViewHost per mounted
view ("world"/"activity"/"iii" + graph view) — timeline is a *scene* under stage, so a pck copy
triggers no reload at all (hence no gate line — not a probe bug). Scene tiers reload only via the
`resource.reload_bundle` command. `BundleHost` now logs when a probe is *skipped*
(`collection probe skipped for bundle ...`), so a silent no-op can't be confused with a missing
gate again. **Follow-up (not done):** a resident scene-tier pck watcher that dispatches
`resource.reload_bundle` on the main thread would make `task bundle:timeline` + `bundle:install`
hot-reload scene tiers the way the verify-windowed loop describes.

## 5. Gotchas / notes for the next session

- **UnifyBuild exports to `build.config.json` `artifactsVersion` = `0.1.2`**, NOT the
  GitVersion-derived dir the Taskfile computes (`0.1.64` here). `task bundle:install` /
  `run:exported` key off GitVersion, so after `build:godot:desktop` the fresh app lives under
  `0.1.2` while pcks go under the GitVersion dir — copy pcks into
  `0.1.2/.../MacOS/bundles/` manually (or fix the version plumbing).
- The export also **wipes `Contents/MacOS/bundles/`** — reinstall pcks after every re-export.
- Diagnosis workflow that worked (repeatable): reproduce pin → `dotnet-dump collect -p <pid>` →
  `dumpheap -type PluginLoadContext` (stale = `_state: 1`) → map stale extraction dir
  (`fantasim_bundles/<pid>-…/world/N`) via `clrmodules`/`dumpdomain` → `dumpmodule -mt` for stale
  modules → intersect with `dumpheap -stat` → `gcroot` survivors. The single-root gcroot of the
  **LoaderAllocator** object is the definitive "how many pins" answer.
- **Long-term cleanliness:** bundle serializers could pass explicit source-gen resolvers
  (composite of their own `GeneratedMessagePackResolver`s) instead of relying on
  `SourceGeneratedFormatterResolver`'s assembly scan — then the shared cache never sees
  collectible assemblies and the reflection purge becomes belt-and-suspenders. Engine-repo change;
  not urgent now that eviction is in.
- Watch for OTHER shared-assembly static caches keyed by collectible Types/Assemblies (Arch
  component registry, MessagePipe, Newtonsoft) — today MessagePack was the only root, but the
  gate + skip-logging will surface any new pin as a `still pinned` line.

## 6. Running state at handoff

- Exported windowed app running: **PID 20873**, log
  `/private/tmp/claude-501/-Users-apprenticegc-Work-lunar-horse/2f7428fd-d093-4523-b9cb-adff89b61524/scratchpad/app-pinfix-run3.log`,
  remote ingress on `:19292` (`tools/fantasim-cmd.py health` OK).
- Last verified evidence: two green world gate lines + one green timeline gate line (grep
  `old ALC collected` in the log).
- The 7 GB diagnosis dump lives in the session scratchpad (`fantasim-pin.dmp`) — disposable.
