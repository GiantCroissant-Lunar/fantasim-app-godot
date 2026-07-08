# Handover — phase 2.5 common resident layer COMPLETE (2026-07-08 evening)

**Merged to main `f35b318`, pushed.** 36 pure-support assemblies (Akka, MessagePack, Arch,
LiteDB, Newtonsoft.Json, UnifyMaths/UnifyEcs families, Cartography, the FantaSim.App.*.Contracts
family, BoomHud.Abstractions, ...) now ship in `common.pck` and load on demand at boot; the
exported exe no longer carries them. **A C# change in any of the 36 no longer requires
re-exporting the app** — `task bundle:common` + reinstall + relaunch replaces the full export.

Read order: [plan + gate-findings amendment](../plans/2026-07-08-phase25-common-resident-layer-plan.md)
→ this file. Prior context: [session-2 handover](2026-07-08-bundle-maximalism-session2-handover.md)
(phase 2), [design brief](../plans/2026-07-08-phase25-loader-design-brief.md).

## What runs now (all windowed-verified)

- `Host._Ready` = exactly `CommonResidentLayerBootstrap.EnsureLoaded()` + NoInlining
  `ComposeAndStart()` — nothing else may EVER be added to `_Ready`'s body (JIT resolves its
  type tokens before statement 1).
- The loader mounts `<exeDir>/bundles/common.pck`, validates the generated
  `config/common-resident-expected.json` (identity = name+sha256, mismatch/half-provision =
  boot-fatal with alert), extracts, and serves DLLs from the **component ALC's `Resolving`
  event** — never eager preload.
- `task build:godot:desktop` self-strips (36 from both per-arch data dirs, verified empty) and
  self-provisions (catalog + common.pck) — a produced app can't be un-provisioned (S4 negative
  test: deleting the catalog is boot-fatal).
- `stage_bundle.py --stage-common` stages from the policy `common` section with two guards:
  GodotSharp byte-scan detector (E1) and `host_locked_names()` (autoload-registration lock).
- `--check-dual` additionally audits bundle∩common and bundle∩bundle; exe∩common is the strip
  tool's own post-verify.

## The four load-order mechanisms (institutional knowledge — cost 6 gate cycles)

1. **Godot hosts the game assembly graph in an `IsolatedComponentLoadContext`, NOT
   `AssemblyLoadContext.Default`.** Anything done to Default (hooks, preloads) is invisible to
   the real callers. Hook the load context of your own assembly.
2. **Never eagerly `LoadFromAssemblyPath` strong-named assemblies as a resolution strategy** —
   runtime requests arrive `PublicKeyToken=null` and the preloaded identity never matches
   (byte-identical MessagePipe/Akka threw MissingMethodException; unsigned Arch worked). Serve
   from the `Resolving` event: its return binds to the exact requested identity.
3. **The autoload script assembly's direct references resolve at script REGISTRATION** (before
   Host instantiation): they can never move to a boot-time-loaded layer. Auto-measured by the
   packer (currently R3 + 7 contracts).
4. **JIT of a method resolves all its body's type tokens before the first statement executes** —
   a loader call must be the method's ONLY companion statement (NoInlining split).

Also: `AppContext.BaseDirectory` in a Godot export = per-arch data dir (use
`OS.GetExecutablePath()`); `GD.Print` is invisible in nohup-captured logs (Console mirrors);
C# string literals are UTF-16 in metadata (`strings` misses them — verify freshness with
utf-16-le byte search).

## Queue

1. **Polarity flip** — worklist in the maximalism spec (7 undecided FantaSim.App.* bundle deps);
   policy-json edit + windowed gate. Post-flip shared = contracts + common layer.
2. **IUnifyGodot upstreaming (D3)** — move the strip from the Taskfile post-step into
   `IUnifyGodot.ExportDesktopPlatform` (plate-projects/unify-build; own version/publish cycle).
3. **Frontier resumes** — D8b progressive-resolution scrub on the hot-reloadable presentation.
4. Optional cleanup: quiet the per-assembly `resolving:` log lines once confidence is high.

## Delegation notes (phase 2.5 round)

- codex gpt-5.5 high implemented plan tasks 1-6+8 faithfully in ~30 min; ALL its dotnet builds
  hung/failed sandboxed (worse than phase 2 — fresh clone needs full restore) but python gates
  ran. Lead fixed one compile error (CS0165 — a PLAN authoring bug: definite-assignment ignores
  [DoesNotReturn]) and all four gate findings above (plan bugs/unknowns, not codex's).
- Pattern held: codex implements, lead commits after review + runs every gate.

## Environment

- Exported app RUNNING for continued work: pid 78706, log
  /tmp/fantasim-windowed-phase25-final.log, evidence `extracted 36 assemblies ... serving on
  demand` + `composition activated`, world/timeline reloads collected. Remote ingress :19292.
- Artifacts v0.1.2; `.agent/logs/codex/` holds the phase-2.5 dispatch + log tail.
