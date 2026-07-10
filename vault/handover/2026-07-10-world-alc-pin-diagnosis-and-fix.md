# World-bundle ALC pin — diagnosis + fix (2026-07-10)

Resolves the slice-2 gate point-5 failure
(`vault/plans/2026-07-10-layer-track-registry-slice2-plan.md`): `Hot-reload: old ALC still
pinned for bundle world after unload (reload degraded)`. Pre-existing (reproduced with pure
slice-1 code, new→new). Diagnosed with dotnet-dump + a rebuilt ClrMD pin-hunter
(`GCRoot.EnumerateRootPaths` over old-generation victims), per the recipe in the
2026-07-03/07-08 pin reports.

## Two distinct roots were found (both fixed)

### Pin class 4 — STJ pooled CachingContext across value-equal options

`LayerTrackRegistryService.AssetReadOptions` (App.World.Composition, world bundle) is a
`static readonly JsonSerializerOptions new(JsonSerializerDefaults.Web) {...}`. Statics die
with their ALC, so this LOOKS safe — but **System.Text.Json (.NET 8) pools CachingContexts
across VALUE-EQUAL options** (`JsonSerializerOptions.TrackedCachingContexts`, 64 weak slots,
lookup via `s_optionsComparer`). Every bundle generation's copy of the static is value-equal,
so generation N+1 adopts the pooled context that still caches generation N's
`JsonTypeInfo`/`RuntimeType` entries → old LoaderAllocator → old ALC pinned.

Dump evidence (world/3 old, world/8 new):

```
options 30017c730 ctx=30017c868 ctx.Options=30017c730   <- gen-3 AssetReadOptions (ctx creator)
options 3002a8d58 ctx=30017c868 ctx.Options=30017c730   <- gen-8 AssetReadOptions, SAME ctx
ctx cache keys: DeclaredLayerEntry/DeclaredLayersDocument/ArchiveOverlayDocument/
                TrackPipelineNode|Wire|Document from BOTH world/3 and world/8 modules
```

**Fix:** `SharedStjCachePurge.ClearReflectionCaches` (App.Resource) invoked from
`BundleHost.UnloadCoreAsync` next to `SharedMessagePackCachePurge`. It reflects into
`System.Text.Json.JsonSerializerOptionsUpdateHandler.ClearCache(null)` — the runtime's own
hot-reload hook (verified against dotnet/runtime release/8.0: iterates
`TrackedOptionsInstances.All`, clears every options' caching context + `_lastTypeInfo` +
`_objectTypeInfo`, flushes the reflection-emit member-accessor cache; every options is
tracked unconditionally in its constructor). Safe memoization — repopulates on next use.
Unit-proven: `App.Resource.Tests/SharedStjCachePurgeTests` reproduces the pooled-context pin
against a Roslyn-emitted collectible assembly (RED without the purge, GREEN with).

RULE UPDATE (extends the b6f93fb rule "no bundle-compiled type may reach a shared
serializer/cache"): a bundle-LOCAL static `JsonSerializerOptions` is NOT local — STJ's pool
unifies it with every value-equal options in the process.

### Pin class 5 — Host rebind race captures the outgoing binder

Second reload of the same session regressed to "still pinned" with a DIFFERENT gcroot: every
path funneled through resident `Host` → gen-8 `PlanetPresentationBinder`. Mechanism
(log-confirmed: "rebind scheduled" at L556 BEFORE "Bundle unloaded: world" at L596, and no
rebind after the new generation registered at L606):

1. World RuntimeChanging arms `_worldReloadPending` and severs `_planetPresentation`.
2. Before `PresentationPlugin.ShutdownAsync` disposes the OLD `IPlanetPresentation`
   registration, ANOTHER bundle's RuntimeChanged event lands (multi-pck installs interleave).
3. Host's guard saw `IsLoaded("world")==true` (old copy, mid-unload) + `TryGet` non-null
   (old registration) → consumed the flag and REBOUND the host to the dying generation's
   binder + render/camera ingress delegates. No second sever exists → pinned forever.
   Round 1 escaped by event-ordering luck; the round-2 signature looks flaky but isn't.

**Fix:** at sever time Host stashes the outgoing presentation in a `WeakReference`
(`_outgoingPresentation`); the rebind guard only consumes the flag when `TryGet` returns a
DIFFERENT instance. (Host.cs `OnResourceRuntimeChanging` / `OnResourceRuntimeChanged`.)

## Windowed proof (exported app, full re-export + `task bundle:install` ×3)

```
round 1 verdict: old ALC collected for bundle world
round 2 verdict: old ALC collected for bundle world
round 3 verdict: old ALC collected for bundle world
TOTALS: collected=3 pinned=0
```

"presentation rebind scheduled" fired once per round (3/3); planet presentation mounted after
every reload; full suite green (1092 incl. the new purge test).

## Open follow-up (out of scope here)

- `old ALC still pinned for bundle timeline` fired ONCE in ~6 timeline reloads during the
  ×3 world rounds (collected the other 5). Same interleaving-class suspect in the timeline
  binding seam; needs its own dump-at-failure session (pin-hunter recipe applies unchanged).

## Tooling note

The ClrMD pin-hunter was rebuilt from the memory recipe (scratchpad, not committed):
module groups by `fantasim_bundles/<pid>/<bundle>/<gen>`, victims = live objects of old-gen
modules, `GCRoot.EnumerateRootPaths`, plus inspect/statics/stjcache/stjmap/refs subcommands.
The `stjmap` subcommand (map every JsonSerializerOptions → its CachingContext) is what proved
the pooling; keep it in the recipe.
