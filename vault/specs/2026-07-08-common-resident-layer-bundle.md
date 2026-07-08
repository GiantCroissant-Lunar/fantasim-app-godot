# Common resident-layer bundle (`common.pck`) — foundation libs as a bundle, not exe cargo

**Status:** DECIDED 2026-07-08 (semantics + sequencing resolved interactively; implementation
NOT started — slotted between phase 2 and the polarity flip of
[2026-07-08-bundle-oriented-maximalism.md](2026-07-08-bundle-oriented-maximalism.md), as its
"phase 2.5" row). Companions:
[architecture/bundle-delivery-and-loading.md](../architecture/bundle-delivery-and-loading.md)
(catalog phases B/C — the versioning half), `shared-assembly-policy.json` (the packing list's
source of truth), `tools/bundles/stage_bundle.py` (`--check-dual`).

## Decision

The plate-projects foundation assemblies (GiantCroissant house libs) move out of the exported
app binary into **`common.pck`: a RESIDENT-LAYER bundle** — extracted and loaded **once, at
boot, into the default (parent) AssemblyLoadContext**, never collectible, never unloaded.
Packaging/delivery granularity, NOT reload granularity.

**Explicit non-goal — a collectible common bundle.** PluginArchi's resolution model is
two-tier: every bundle ALC resolves shared names against the PARENT context only
(`IsolatedLoader` + `SharedAssemblyPolicy`). Bundle→bundle type resolution does not exist, so a
collectible common layer would need a layered-ALC capability in plate-projects/plugin-archi (an
RFC) and would buy reloadability nobody needs for foundation libs — unloading the layer every
bundle binds against means unloading everything. If that capability is ever wanted, it is a
plate-projects decision, not a fantasim workaround.

## Why

1. **The exe shrinks to the true floor** (bundle-maximalism doctrine applied to *packaging*):
   kernel + bundle machinery + engine glue only. Everything else ships as pcks.
2. **Foundation updates without re-export:** a Unify/Cartography/Arch-closure fix becomes a
   `common.pck` swap + app restart instead of `task build:godot:desktop`.
3. **Kills the bundle∩bundle duplication class:** generic plate libs currently ride inside the
   world bundle (UnifyCell.*, UnifyGeometry.*) because world is today's only consumer. The
   moment a second bundle needs UnifyGeometry we get two private copies — a type-split risk
   `--check-dual` does not even scan for (it checks bundle∩host only). In the common layer the
   assembly exists once, as a parent-context type, for every bundle.

## Packing list — derived, not hand-written

`common.pck` contents = **`shared-assembly-policy.json` exactMatches ∪ selected shared prefixes,
MINUS the loader stack**, PLUS the generic plate libs currently staged in domain bundles:

- **Goes to common:** UnifyMaths(+.Numerics/.Abstractions), Arch closure (Arch, Arch.LowLevel,
  Collections.Pooled, CommunityToolkit.HighPerformance, Schedulers), MessagePack closure
  (MessagePack, .Annotations, UnifySerialization.MessagePack.Runtime), UnifyStorage.Abstractions
  + Runtime.LiteDb, Cartography.* trio, BoomHud*, R3/ReactiveUI/DynamicData, Akka*, MessagePipe*,
  Newtonsoft.Json, UnifyEcs.*, TimeDete.*, FantaSim shared domain contracts
  (FantaSim.World.Fields.Contracts/Core, Shared.Contracts, Cross.Abstractions,
  FantaSim.App.World.Rendering, FantaSim.App.*.Contracts), and — promoted OUT of the world
  bundle — UnifyCell.*, UnifyGeometry.*.
- **Stays in the exe (the loading floor):** GodotSharp/engine glue, System./Microsoft. runtime,
  netstandard, PluginArchi.*, ServiceArchi.*, RegistryArchi.*, DependencyArchi.*,
  CrosscutFoundation.* (kernel + the code that loads bundles runs BEFORE any pck exists),
  App.Common, App.Resource(+Bundle.Seam), App.SceneFlow, App.Command, the thin Host, remaining
  T4 seams until their phases move them.
- The stager derives this mechanically (a `layer: "common"` marker per policy entry or a
  `common` section in the policy json — decide at implementation; the policy file stays the
  single source of truth with THREE consumers: Bootstrap, stage_bundle.py, common packer).

## Mechanics (implementation phases, each windowed-gated)

1. **BundleHost `resident-layer` mode:** manifest `bundleType: "resident-layer"` → extract and
   load all managed assemblies into the PARENT context (no plugin group, no collectible ALC,
   no unload path); must load before any collectible bundle and before the host composition
   sequence that uses those libs (Ecs/Arch, presentation contracts/Cartography). Inverted lint:
   resident-layer assemblies must NOT appear in any bundle's collectible exclusions.
2. **Boot restructure:** `Host._Ready` becomes kernel boot → load `common.pck` → compositions →
   scene entry. Compile-time references are unaffected (compilation still uses the nuget graph);
   only runtime load location changes.
3. **Export exclusion:** the excluded DLLs must leave the exported app binary or every one is a
   dual copy again — extend `--check-dual` with exe∩common and bundle∩bundle scans; gate on all
   three being empty.
4. **Version discipline:** the app binary compiles against pinned versions; `common.pck` must
   carry exactly those. This is where the dormant catalog design (bundle-delivery phases B/C)
   activates: the catalog stamps the compatible common version; mismatch = fail-hard at boot.

## Interaction with the polarity flip

After phase 2.5, the flip becomes clean and total: **shared = T1 contracts + the common layer**;
every non-contract FantaSim.App.* prefix leaves the share list; domain bundles carry domain code
only. The flip is an edit to the policy json + one windowed gate, exactly as phase 0 intended.

## Acceptance

- Exported app binary contains no assembly that `common.pck` carries (exe∩common empty).
- No bundle carries an assembly `common.pck` carries (bundle∩common empty) or that another
  bundle carries (bundle∩bundle empty).
- Boot sanity + steady-state world reload `old ALC collected` unchanged.
- A foundation-lib change reaches the running product via `common.pck` swap + restart, no
  re-export (demonstrated once, windowed).
