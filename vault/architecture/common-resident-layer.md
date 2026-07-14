---
source: project/plugins/App.Resource.Bundle.Seam/CommonResidentLayer/CommonResidentLayerBootstrap.cs, project/bundles/common/manifest.json, project/hosts/complete-app/config/shared-assembly-policy.json, project/plugins/App.Common/Bootstrap.cs, project/hosts/complete-app/Host.cs, tools/bundles/strip_common_from_export.py, tools/bundles/stage_bundle.py, vault/handover/2026-07-08-phase25-common-resident-layer-handover.md (authored 2026-07-14)
source-status: current-code documentation
distilled: 2026-07-14
divergence: >
  Cross-links cross-alc-rules.md for SharedAssemblyPolicy share-list mechanics
  (top-level exactMatches/prefixes/excludedExactMatches, R1-R7 unload rules)
  instead of restating them — this doc covers only the delivery mechanism that
  populates the resident component ALC. Does not reproduce the full 36-assembly
  roster (see manifest.json) or stage_bundle.py's --check-dual internals beyond
  what grounds the four load-order mechanisms.
---

# Common Resident Layer (Bundle-Oriented Maximalism, Phase 2.5)

The mechanism that ships ~36 pure-support assemblies in a separate `common.pck`
instead of inside the exported executable, and re-hydrates them into the app's
long-lived component `AssemblyLoadContext` on every boot. This is the app's
most consequential runtime-packaging mechanism and, until this doc, existed
only as a handover + code comments (see Cross-links).

## Doctrine (what must hold)

- **Ownership.** App-runtime ALC/bundle mechanics are owned by this repo's
  vault, not the hub — hub `doc-authority-map.md` "Authority by content type."
  There is no cross-repo doctrine document for this mechanism; this doc is the
  authority for it.
- **Standing invariant this mechanism exists to serve:** type identity is
  ALC-scoped — a contract type loaded in both the resident ALC and a
  collectible ALC produces two distinct runtime types, so cross-ALC casts fail.
  `cross-alc-rules.md` §1-§2 owns the rules for *which* assemblies must be
  shared-resident and why; this doc does not restate that list. What this doc
  owns is *how* the resident component ALC gets those bytes into memory
  without shipping them in the exe.
- **Packaging ≠ hot-reload (PluginArchi two-tier design).** The common layer
  is packaging granularity only, never hot-reload granularity — verbatim from
  the loader's own header comment: "Never touches collectible ALC machinery —
  the common layer is packaging granularity, not hot-reload (spec: PluginArchi
  is two-tier)." Conflating the two is a design error this doc's "Not
  built / open" section flags explicitly.

## Why this exists (memory + type identity across reloads)

Godot's C# export packages the **whole** publish closure — every managed DLL
the host transitively depends on — into the exported app's per-arch data
directories; export presets have no mechanism to exclude individual managed
DLLs (`tools/bundles/strip_common_from_export.py` module docstring). Before
phase 2.5 this meant two costs:

1. **Iteration cost.** Any C# change to a pure-support library (Akka, Arch,
   UnifyMaths, the `.Contracts` family, …) required a full Godot re-export —
   slow and manual — even though nothing about the *bundle* (hot-reload) layer
   changed.
2. **Identity-floor cost.** Collectible bundles need a stable, long-lived copy
   of contract + infrastructure types to bind against, so that when a bundle
   unloads and a new one loads in its place, a host-side cast like
   `is IViewSource` still succeeds against the same runtime type. Packing
   everything into the exe's default publish output doesn't guarantee *which*
   load context ends up serving those types when Godot's own hosting model is
   not `AssemblyLoadContext.Default` (see mechanism 1 below).

`common.pck` solves both: it externalizes the 36-assembly closure out of the
exe into an adjacent, independently-rebuildable package, and the loader
re-serves those exact bytes into the **same** long-lived component ALC every
time the process starts — giving fast iteration on those 36 assemblies
(`task bundle:common` + reinstall + relaunch, no re-export) and a stable
identity floor for every collectible bundle to resolve shared types against.

## Built

### The four load-order mechanisms (institutional knowledge — cost 6 gate cycles)

| # | Mechanism | Why it's necessary | Evidence |
|---|-----------|---------------------|----------|
| 1 | Hook the **component ALC**, not `AssemblyLoadContext.Default` | Godot hosts `complete-app.dll` and its whole managed dependency graph in its own `IsolatedComponentLoadContext`; that context's fallback chain never consults `Default.Resolving`, so hooking `Default` is invisible to real resolution demands | `CommonResidentLayerBootstrap.EnsureLoaded()` resolves `AssemblyLoadContext.GetLoadContext(typeof(CommonResidentLayerBootstrap).Assembly)` (falling back to `Default` only if null) and hooks `componentAlc.Resolving`; `App.Common/Bootstrap.cs.BuildPluginHost` parents `PluginHostBuilder` on the same context |
| 2 | Serve on demand from `Resolving`; **never** eager `LoadFromAssemblyPath` | Eagerly preloading strong-named assemblies registers them in a way the CLR binder rejected for MemberRef resolution from TPA callers — byte-identical MessagePipe/Akka threw `MissingMethodException`, while unsigned Arch happened to work; an assembly *returned from* the `Resolving` event binds to the exact requested identity, the supported extension point | `CommonResidentLayerBootstrap.OnComponentResolving` + the `EnsureLoaded` comment: "NO eager preload — serve on demand through the Resolving hook... An assembly returned FROM the Resolving event is bound to the exact requested identity" |
| 3 | Autoload-registration lock | Godot's script bridge resolves the autoload assembly's (`complete-app.dll`) direct references at script **registration** — before `Host._Ready` runs, i.e. before `EnsureLoaded` can serve anything. Any common candidate referenced there can never move to the boot-time layer this phase | `tools/bundles/stage_bundle.py.host_locked_names()` — byte-scans `complete-app.dll`'s CLI `#Strings` heap for each candidate's assembly name; per the 2026-07-08 handover this currently locks R3 + 7 contracts assemblies as exe cargo |
| 4 | `_Ready` JIT isolation (`NoInlining` split) | JIT-compiling a method resolves **every** type token its body mentions before the first statement executes; if the `EnsureLoaded()` call were not `_Ready`'s only companion statement, the JIT would demand common-layer types before `EnsureLoaded` could serve them | `Host.cs._Ready()` body is exactly two statements — `CommonResidentLayerBootstrap.EnsureLoaded()` then `ComposeAndStart()` (the latter marked `[MethodImpl(MethodImplOptions.NoInlining)]`); the comment cites the literal historical failure this prevents: a `Timeline.Contracts` `FileNotFoundException` thrown "at Host._Ready()" itself |

### The roster and its source of truth

`project/bundles/common/manifest.json` lists 36 assemblies by name + sha256
(`managed.assemblies`) — Akka, Arch/Arch.LowLevel, BoomHud.Abstractions,
Cartography.Globe.\*/Shared.Contracts, Collections.Pooled,
CommunityToolkit.HighPerformance, the `FantaSim.App.*.Contracts` family plus
`FantaSim.App.World.Rendering`, `FantaSim.Cross.Abstractions`,
`FantaSim.World.Fields.*`, `FantaSim.World.Shared.Contracts`, LiteDB,
MessagePack\*, Newtonsoft.Json, Schedulers, `TimeDete.Time.Primitives`,
`UnifyEcs.*`, `UnifyMaths*`, `UnifySerialization.MessagePack.Runtime`,
`UnifyStorage.*`. Selection is driven by `shared-assembly-policy.json`'s
**`common`** sub-object (`exactMatches` + `prefixes`
[`MessagePipe`, `UnifyEcs.`, `TimeDete.`] + a `suffixRules` entry
[`FantaSim.App.` + `.Contracts`] + `detectorGated: [BoomHud.Abstractions]`) —
this is a **separate list** from the top-level `exactMatches`/`prefixes` that
govern collectible-bundle sharing at runtime (`cross-alc-rules.md` §2); the
`common` sub-object governs packaging only, consumed by `stage_bundle.py` to
build `common.pck` and by nothing at runtime. That sub-object's comment (in
`shared-assembly-policy.json` — the generated `manifest.json` carries no
comments) notes `ReactiveUI`/`DynamicData`/`BoomHud.Foundation` were
candidates in the original 2026-07-08 design brief but were dropped —
verified not in the real host closure.

### Boot sequence (`CommonResidentLayerBootstrap.EnsureLoaded`)

1. Locate `<exeDir>/bundles/common.pck` and
   `<exeDir>/config/common-resident-expected.json` via
   `OS.GetExecutablePath().GetBaseDir()` — **not** `AppContext.BaseDirectory`,
   which in a Godot .NET export resolves to the per-arch data directory
   (`Contents/Resources/data_*`), not the exe-adjacent bundle convention (code
   comment).
2. Provisioning-matrix gate: neither file present → unstripped exe or editor
   run, skip silently. `.pck` missing but the expectation file present →
   **boot-fatal** (a stripped exe missing its resident layer). Both present →
   proceed.
3. Mount the pck (`BundleVfs.LoadPck`), read its manifest, hook
   `componentAlc.Resolving += OnComponentResolving`.
4. Extract all 36 DLLs to a temp path (`BundleExtractor.ExtractAllManaged`).
5. Integrity gate, two layers: per-assembly sha256 of extracted bytes vs the
   manifest's declared sha256 (mismatch → boot-fatal); then the manifest's
   identity set vs `common-resident-expected.json` (mismatch or a `.pck`
   present with no expectation file, i.e. half-provisioned → boot-fatal with
   `OS.Alert`).
6. Mark loaded. Every later resolution demand for one of the 36 names is
   served from `OnComponentResolving` via `LoadFromAssemblyPath`; a miss logs
   `"NOT OURS (miss)"` and returns null (falls through to the normal chain).

### Export-time provisioning (`strip_common_from_export.py`)

Runs as a mandatory post-export step (Godot export cannot exclude managed
DLLs itself). Per manifest assembly: hash-compares the DLL across **both**
per-arch data dirs (`arm64`, `x86_64`) — a divergent, RID-specific assembly
cannot be served from one universal `common.pck` and the tool raises rather
than strip it; deletes matching DLLs from both dirs; verifies none remain
(fatal `leftovers` check if any survive); writes
`<MacOS>/config/common-resident-expected.json` (the loader's boot gate);
installs `common.pck` under `<MacOS>/bundles/`. Docstring: "a stripped app
never exists without its layer." Invoked by `task build:godot:desktop`, which
self-strips and self-provisions in one step.

### Staging (`stage_bundle.py --stage-common`)

Candidates = host output DLLs matching the policy's `common` section, minus
two guarded classes:

- **Autoload-registration lock** (mechanism 3 above).
- **E1 Godot-facing detector** (`is_godot_facing`) — byte-scans each candidate
  for the ASCII string `GodotSharp` in its CLI metadata; any match is rejected
  unless the candidate is explicitly listed in `detectorGated`
  (`BoomHud.Abstractions` is the only one today) — Godot-facing assemblies
  must never enter `common.pck`.

## Not built / open

- **IUnifyGodot upstreaming** — moving the strip step from the Taskfile
  post-step into `IUnifyGodot.ExportDesktopPlatform`
  (`plate-projects/unify-build`) is queued in the 2026-07-08 handover, not
  done.
- **Resolution log noise** — `OnComponentResolving` mirrors every resolution
  attempt (hit or miss) to `GD.Print`/`Console.WriteLine`; queued for
  quieting once confidence is high, not done.
- **This layer never hot-reloads.** It is packaging granularity only — a
  change to any of the 36 assemblies requires `task bundle:common` +
  reinstall + a full process relaunch, not a live hot-reload cycle. It is
  therefore **outside** the collectible-bundle hot-reload gate described in
  `cross-alc-rules.md` §6 (`old ALC collected` evidence) — do not expect that
  evidence for a common-layer change; expect a clean relaunch log instead
  (`"extracted 36 assemblies... serving on demand via the component ALC's
  Resolving"`).

## Failure modes each mechanism prevents

| If skipped | Observed failure |
|------------|-------------------|
| Hooking `Default` instead of the component ALC | Resolving handler never fires; every common-layer type demand fails with `FileNotFoundException` because Godot's real assembly graph lives in a different ALC |
| Eager preload instead of `Resolving`-serve | `MissingMethodException` on strong-named assemblies (MessagePipe, Akka) even though the DLL is present on disk — the binder rejects the preloaded identity for MemberRef resolution |
| No autoload-registration lock | A common-layer assembly referenced by the autoload script's own direct references gets demanded at script registration, before `Host._Ready` ever runs — `EnsureLoaded` cannot possibly have served it yet, boot fails |
| No `NoInlining` split in `_Ready` | JIT resolves every type token `_Ready` mentions before statement 1 executes, including tokens from types `EnsureLoaded` was supposed to serve first — reproduced historically as a `Timeline.Contracts` `FileNotFoundException` thrown "at Host._Ready()" |
| No integrity gate | A partially-provisioned or bit-rotted `common.pck` boots silently with mismatched types instead of failing loud at the one moment (boot) where the mismatch is cheap to diagnose |
| No E1 Godot-facing detector | A Godot-referencing assembly could enter `common.pck`, but Godot-facing types must resolve to the single resident `GodotSharp` copy via the `prefixes` mechanism in `cross-alc-rules.md` §2 — routing them through this separate packaging layer instead risks a second, non-identical load path |

## Cross-links

- `cross-alc-rules.md` — the resident-vs-collectible ALC model, the
  `SharedAssemblyPolicy` share lists (top-level `exactMatches`/`prefixes`/
  `excludedExactMatches`), and the R1-R7 clean-unload rules this mechanism
  exists to serve. Read that doc for *what* must be shared and *why*; this doc
  covers only *how* the resident floor gets loaded.
- `vault/handover/2026-07-08-phase25-common-resident-layer-handover.md` — the
  full session record: delegation notes, windowed-verification evidence,
  environment/PID, and the queue this doc's "Not built / open" section
  mirrors.
- `.agent/skills/04-tooling/verify-windowed/SKILL.md` (via `AGENTS.md`) — the
  hot-reload verification loop; note that a common-layer change is verified by
  full relaunch, not that loop.
