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

## Adversarial review outcome (2026-07-08, codex/gpt-5.5 high, read-only cross-model)

Verdict: **implementable only with these amendments** (full findings:
`.agent/logs/codex/common-bundle-review-20260708.log`). The mechanics section above is
subordinate to this list where they conflict.

1. **Signature-free bootstrap stage.** `Host._Ready` cannot be the load site: the CLR may force
   `Host`'s field/method-signature assemblies at type-load time, and `AppComposition.Activate()`
   constructs the Akka `ActorSystem` in the Bootstrap ctor — common assemblies are needed BEFORE
   any of that runs. A tiny bootstrap entry (no common-layer types in any signature) must install
   an `AssemblyLoadContext.Default.Resolving` hook and load `common.pck` before
   `AppComposition.Activate()` or any composition.
2. **Strict resident-layer loader, separate from PluginArchi.** `BundleHost`/`IsolatedLoader`
   only load into collectible contexts; the common layer needs its own loader: extract, load every
   DLL into `AssemblyLoadContext.Default`, fail-hard on missing/mismatch, never unload. (Confirmed:
   merely extracting to a temp dir does NOT make Default probing find the assemblies — they must be
   loaded, or the Resolving hook must map them.)
3. **Post-export strip step.** `export_presets.cfg` cannot exclude managed DLLs — Godot's C# export
   packages the full `dotnet publish` closure (UnifyBuild's BuildGodot delegates to it). The strip
   runs after export, removes common-layer DLLs from the app's data dirs, and the `--check-dual`
   family gains exe∩common / bundle∩common / bundle∩bundle scans as the acceptance gate.
4. **Godot-facing assemblies stay in the exe for the first cut** (BoomHud.Godot.Runtime, any
   Godot.NET.Sdk assembly with script classes). Whether GodotSharp script registration works for
   Default-loaded, PCK-delivered assemblies is an open experiment — until it passes, only pure
   support DLLs go to common.
5. **The catalog/version gate builds FIRST** (bundle-delivery phases B/C): common bundle identity +
   hash/version compatibility + fail-hard boot validation. No version contract exists today.

Two gating experiments before implementation starts (both small, windowed):
- **E1 (script registration): RAN 2026-07-08 — FAIL (both halves), verdict binding.**
  Godot-facing script assemblies are EXCLUDED from common.pck. Detail that matters: the exported
  experiment hosts crashed (SIGSEGV, CoreCLR `MethodTable::RunClassInitEx`) whenever the
  loader/reflection bootstrap code was present — before `_Ready`, before the pck was even touched
  — while smoke controls ran clean. So E1 never reached the script-binding question; it instead
  demonstrated the fresh-harness loader pattern itself is fragile in exported Godot 4.7 mono apps.
  Consequences: (a) Godot script assemblies stay in the exe, definitively for this phase;
  (b) the phase-2.5 pure-support loader MUST be built on fantasim's daily-proven extraction path
  (`BundleExtractor` + explicit Default-ALC load), spiked inside the real app — not a greenfield
  `Assembly.LoadFrom` harness. Report: session scratchpad `e1-experiment/E1-REPORT.md`.
- **E2 (strip viability): RAN 2026-07-08 — lazy loading CONFIRMED.** `Arch.dll` stripped from a
  copy of the exported app: the app booted COMPLETELY (`Host._Ready` ran, plugin host built, all
  compositions, `composition activated.` logged) and failed only at FIRST USE —
  `UnifyECS.ArchWorld..ctor` inside `EcsWorldActor` creation threw `FileNotFoundException` when
  the first Arch-executing method ran. Consequences:
  - Assembly demand is at first-executed-use on .NET 8/macOS, NOT at `Host` type-load or
    `AppComposition.Activate()`. Amendment 1 RELAXES: an `AssemblyLoadContext.Default.Resolving`
    hook installed as the first statement of `Host._Ready` (before Activate) suffices for
    everything the entry path does not execute earlier. The signature-free micro-bootstrap is
    only needed for the absolute-maximal variant (moving App.Common/kernel out too).
  - The remaining unproven half of E2 is the positive binding (preloaded/hook-resolved copy gets
    used) — standard documented ALC behavior + confirmed mechanism in the codex review; it is
    proven implicitly by implementation step 1 (the loader) at its first windowed run.

**Measured payload (2026-07-08, v0.1.2 export):** 295 managed DLLs in
`data_complete-app_macos_arm64` (93 MB) = 174 .NET runtime-pack (stays, self-contained export) +
2 Godot glue (stays) + **119 user/foundation assemblies — the maximal candidate set for pck
delivery**. End-state exe: runtime pack + GodotSharp + `complete-app.dll` (Host entry + resolver
bootstrap). Iteration economics: full desktop export ≈ minutes; pck restage ≈ seconds.

## Acceptance

- Exported app binary contains no assembly that `common.pck` carries (exe∩common empty).
- No bundle carries an assembly `common.pck` carries (bundle∩common empty) or that another
  bundle carries (bundle∩bundle empty).
- Boot sanity + steady-state world reload `old ALC collected` unchanged.
- A foundation-lib change reaches the running product via `common.pck` swap + restart, no
  re-export (demonstrated once, windowed).
