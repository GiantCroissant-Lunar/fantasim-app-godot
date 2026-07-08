# Handover — bundle maximalism phases 0–1 SHIPPED (2026-07-08)

**Read order for a fresh session:** this file →
[specs/2026-07-08-bundle-oriented-maximalism.md](../specs/2026-07-08-bundle-oriented-maximalism.md)
(the decided architecture + phase queue) →
[plans/2026-07-08-bundle-maximalism-phase0-1.md](../plans/2026-07-08-bundle-maximalism-phase0-1.md)
(execution notes embedded per task).

## What shipped (branch `feat/bundle-maximalism-phase0-1`, merged to main)

| Commit | What |
|---|---|
| `ff9a872` | Shared-assembly policy externalized to `config/shared-assembly-policy.json` (verbatim 14 exact + 22 prefixes); `Bootstrap.BuildPluginHost(bundles, policy)`; fail-hard load in Host; new App.Common.Tests (5) |
| `5d78a2c` | Generic stager `tools/bundles/stage_bundle.py` (7 unit tests) driven by the policy json + `collectible-bundles.json` `projects` entries; ALL five bundle build tasks rewired; 120-line inline world script deleted |
| `f633d84` | `IPlanetPresentation` → `contracts/App.Presentation` (`FantaSim.App.Presentation.Contracts`, `[PluginSharedContract]`), namespace unchanged |
| `3ff5152` | `PresentationPlugin` (world-bundle entry, RegisterOwned + main-thread-marshalled shutdown; 2 headless lifecycle tests). ⚠ host build broken at this commit (early csproj swap) — fixed by `6aa5da3` |
| `6aa5da3` | Host resolves the contract only; world reload severing/rebind; `PlanetPresentationOptions` config bridge; world bundle 50→51 assemblies (sole addition = presentation dll) |
| `9714ed7` | **Windowed-gate fixes:** world pck watch moved binder→Host (bundle-owned watcher cancelled its own reload mid-flight); `_worldReloadPending` only consumed once the new `IPlanetPresentation` registration exists |

## Windowed gate evidence (Task 6)

- Boot: `PresentationPlugin: IPlanetPresentation registered` between stage entry and world load;
  planet mounted through the bundle-owned binder; screenshot verified.
- Hot-reload cycle 1 (blue-sun tweak): unload → new plugin registers → `Bundle loaded: world` →
  `Planet presentation mounted` → planet visibly blue. **ALC#1 (boot) stayed pinned** (see below).
- Hot-reload cycle 2 (revert): same flow, warm look restored, **`Hot-reload: old ALC collected
  for bundle world`** — the gate line.
- Scrub after two reloads: `timeline.seek` → 400 ka, regime flipped to stagnant-lid, planet
  re-rendered. Controller rebind chain (`world.presentation.rebound` → `timeline.composition.rebound`)
  works.

## Two defects the gate caught (both fixed in `9714ed7`)

1. **Self-cancelling reload.** `PlanetPresentationBinder` owned `WatchResource("world")`. Resident,
   fine; in-bundle, the reload's unload phase disposes the binder → disposes the watcher → cancels
   the in-flight reload token → the LOAD half silently never runs (planet vanished, no error
   logged). Watch ownership is bundle MACHINERY and moved to the resident Host — same pattern as
   IiiComposition's iii watch. **Rule of thumb for phases 2+: whatever triggers a bundle's reload
   must not live in that bundle.**
2. **Premature pending-flag consumption.** Multi-bundle installs interleave RuntimeChanged events;
   `IsLoaded("world")` is true for the OLD copy during its own unload window. The host now consumes
   `_worldReloadPending` only when the NEW bundle's `IPlanetPresentation` registration exists.

## Known residual — RESOLVED same day (boot ALC pin)

~~The FIRST world reload after app boot leaves the boot ALC pinned.~~ **FIXED + windowed-gated
(two consecutive world reloads both log `old ALC collected for bundle world`; seek healthy after).**

**Actual root (gcroot on the live app, ClrMD):** NOT the suspected TimelineComposition
unsubscribe. `timeline.pck` had been shipping a **fossil `FantaSim.App.Timeline.dll`**
(pre-`2c770b2`, Jun 23) because the bundle's `collectible-bundles.json` `output` entry still
pointed at the dead Godot.NET.Sdk dir (`.godot/mono/temp/bin/Debug`) after the csproj moved to
Microsoft.NET.Sdk (`bin/Debug/net8.0`). The fossil's `TimelinePlugin.ActiveController` static
(a design deleted from source two weeks earlier) captured the BOOT world ALC's
`PlanetTimelineController` at scene entry and never re-resolved — pin chain: timeline ALC
LoaderAllocator statics → boot controller → boot world ALC, for the life of the app. Later
reload generations were never captured, hence the boot-only signature.

**Fixes (one commit, `fix(reload): boot ALC pin — timeline.pck shipped a fossil App.Timeline.dll`):**
- `collectible-bundles.json`: timeline `output` → `bin/Debug/net8.0`.
- `stage_bundle.py`: after building, FAIL if the configured output dir ≠ the csproj's real
  msbuild `TargetDir` (this whole fossil-staging class is now a hard error; 2 unit tests).
- `shared-assembly-policy.json`: + `FantaSim.Cross.Abstractions` (the fresh timeline deps
  closure pulls it; `--check-dual` flagged the new dual copy — the audit works).
- `TimelineComposition`: the suspected lead was a REAL adjacent bug (dump showed the stale
  handler in the current controller's `TickChanged` list) — the prior handler is now
  unsubscribed from the controller it was subscribed to (`_subscribedController`), and released
  when composing inert.
- `TimelineFace.BindResidentContext`: on controller swap, clear `_playbackRegistered` after
  unregistering so the NEW controller gets `RegisterPlayback` (Play/Pause callbacks silently
  died after the first reload before this).

**Diagnosis notes for next time:** `dotnet-dump analyze` (and stock ClrMD `LoadDump`) can fail
on a macOS core with "An item with the same key has already been added" — a fresh second dump
loaded fine. Apple's hardened lldb SIGKILLs on loading the SOS plugin (`.lldbinit`) — not a
route on this Mac. A ~40-line ClrMD console app (heap scan for `PluginLoadContext` `_state=1`,
modules grouped by `fantasim_bundles/<pid>/<bundle>/<n>` extraction dir, `GCRoot.EnumerateRootPaths`
on surviving objects) reproduced the full dumpheap→gcroot recipe and named the static box.

## Standing decisions locked this arc

- Domain-grouped bundles; phases 0–2 before the D8b/D5 frontier resumes; Remote will be bundled
  load-first (spec).
- Policy polarity flip (contracts-only sharing) is a **later, gated** edit to
  `shared-assembly-policy.json` — after phase 2, with its own windowed gate.
- Parity finding: old timeline staging silently under-shipped `UnifyMaths.Abstractions.dll`;
  deps-driven staging now ships it (accepted, verified safe).

## Post-merge addendum — dual-copy audit (`5183681`, user-flagged)

7 of the world bundle's 51 assemblies were ALSO in the resident host output (dual copies —
latent type-identity splits). Deps-graph audit confirmed closure violations: shared `Arch` →
Arch.LowLevel/Collections.Pooled/CommunityToolkit.HighPerformance/Microsoft.Extensions.ObjectPool;
shared `Akka` → ObjectPool; shared `UnifyMaths` → UnifyMaths.Abstractions (carried privately by
TWO bundles). All promoted to shared (+ Schedulers + UnifySerialization.MessagePack.Runtime, whose
"bundle ships without it" csproj comment had been false since before phase 1); the stale
ObjectPool collectible override dropped. World bundle 51→44. **`stage_bundle.py --check-dual`**
now audits every bundle against the host output (wired into `bundle:stagetool:test`; exit 1 on
new drift; allowlist carries exactly one known item — the timeline T3 dual copy that phase 2
deletes). Windowed-gated after: clean boot, steady-state `old ALC collected for bundle world`.

**Audit principle for phases 2+:** an assembly present in BOTH a bundle and the host output is
always wrong — either promote it to the shared policy (if the host legitimately ships it) or
find why the host output has it and cut that. `--check-dual` enforces this from now on.

## Next

1. **Phase 2: Timeline T3 → timeline bundle** (deletes the Host rebind machinery; plan via
   writing-plans against the spec). Then the frontier (D8b progressive-resolution scrub) resumes
   on the now-hot-reloadable presentation.
2. Boot-pin chip (above) — independent, any session.
3. Delegation note: kimi-k2.7-code via opencode executed plan tasks 1–4 well (one stop-on-gate
   exactly as instructed — the parity stop was CORRECT behavior); task 5 needed lead-session
   design adaptations (SeamConfigBanTests, options bridge). Budget lead review time for any task
   that touches house arch-test gates.

## Drive recipe (unchanged)

Windowed app: `remote__enabled=true nohup <exe> > /tmp/fantasim-windowed-<ts>.log 2>&1 &`;
`python3 tools/fantasim-cmd.py cmd render.screenshot '{}'` / `timeline.seek '{"tick":N}'`;
world-only reload drill: `task bundle:world && cp build/_artifacts/<v>/godot/bundles/world.pck
<app>/Contents/MacOS/bundles/` (avoids re-triggering every bundle like `task bundle:install` does).
