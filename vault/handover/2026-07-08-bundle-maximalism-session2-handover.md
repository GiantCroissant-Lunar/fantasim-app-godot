# Handover — bundle maximalism, session 2 (2026-07-08 afternoon)

**Read order for the next session:** this file →
[specs/2026-07-08-bundle-oriented-maximalism.md](../specs/2026-07-08-bundle-oriented-maximalism.md)
(phase table + flip worklist) →
[plans/2026-07-08-phase2-timeline-t3-to-bundle.md](../plans/2026-07-08-phase2-timeline-t3-to-bundle.md)
(phase-2 contract incl. Decision 5) →
[handover/2026-07-08-bundle-maximalism-phase0-1-handover.md](2026-07-08-bundle-maximalism-phase0-1-handover.md)
(the morning session: phases 0–1, reload lessons, boot-pin fix).

## ⚡ IN FLIGHT — resume this FIRST

**Codex (gpt-5.5 high) is implementing phase 2 in a local clone:**
`yokan-projects/fantasim-phase2-clone`, branch `feat/bundle-max-phase2`, base = WIP `f918824`
(round-1 salvage, does not build). Log: `<clone>/.agent/logs/codex/phase2-round2-20260708.log`.
At handover time it was ALIVE and progressing (log 5.3 MB and growing; iterating tests toward
its first gated commit). Its contract: three green-gated commits — (1) `refactor(timeline):
registry-mediated face context replaces seam statics`, (2) `feat(bundles): host sheds timeline
T3 (phase 2)`, (3) `chore(bundles): timeline sheds the last --check-dual allowlist entry` —
each gated by sln build + full tests + explicit complete-app build.

Next session:
1. `cd yokan-projects/fantasim-phase2-clone && git log --oneline main..HEAD && git status --short`
   — finished = 3 new commits + clean tree. If codex died mid-run, the log tail says where;
   either re-dispatch (prompt at `<clone>/.agent/run/dispatch/codex-phase2-round2.txt`) or
   finish inline.
2. REVIEW the diff against the plan's Pin map + Decision 5. Non-negotiables: NO T3→Seam
   ProjectReference (round 1's inverted-dependency mistake); every plugin registration/
   subscription severed in ShutdownAsync; world-rebind pending flag consumed only when the new
   `ITimelineController` registration exists; resident TimelineComposition + Host timeline
   machinery + BundleReloadHook deleted; `--check-dual` allowlist EMPTY.
3. Fetch into the main repo: `git -C yokan-projects/fantasim-app-godot fetch
   ../fantasim-phase2-clone feat/bundle-max-phase2:feat/bundle-max-phase2` (do NOT let the clone
   push — its origin IS the main repo).
4. WINDOWED GATE (lead session, per verify-windowed): full export → boot sanity → timeline
   hot-reload ×2 (`old ALC collected for bundle timeline`; seek/select/toggle work after) →
   world reload (timeline stays usable via the plugin's self-rebind; `old ALC collected for
   bundle world`) → remote-commanded `resource.reload_bundle` for world (proves the
   BundleReloadHook deletion safe) → merge to main, delete clone + branch.

## What landed on main this session (all pushed)

| Commit | What |
|---|---|
| `5183681` | Dual-copy audit (user-flagged): 7 world-bundle assemblies were also in host output — promoted to shared (Arch closure, UnifyMaths.Abstractions, UnifySerialization.MessagePack.Runtime, Schedulers; ObjectPool override dropped); world 51→44; `--check-dual` guard born |
| `3a014b5` | Phase 2.5 DECIDED: common RESIDENT-LAYER bundle spec (packaging granularity; collectible common = non-goal, PluginArchi is two-tier) |
| `031d3b1` | Codex adversarial review folded into the 2.5 spec as normative amendments (bootstrap before Activate, dedicated Default-ALC loader, post-export strip, Godot-facing exclusions, catalog first) |
| `c8fd35e` | LiteDB closure bug fixed (shared UnifyStorage.Runtime.LiteDb → unshared LiteDB); polarity-flip worklist recorded (7 undecided FantaSim.App.* bundle deps) |
| `57b27ba` | **E2 RAN — PASS:** stripped Arch.dll, app booted to `composition activated`, failed only at first use → lazy loading confirmed; payload measured: 295 DLLs = 174 runtime + 2 Godot + **119 movable** |
| (E1 commit) | **E1 RAN — FAIL both halves:** Godot-facing script assemblies STAY in the exe; greenfield loader harness SIGSEGVs in exported apps → build the 2.5 loader on BundleExtractor (report archived: handover/2026-07-08-e1-script-binding-experiment-report.md) |
| `30daea8`+ | Phase-2 plan + Decision 5 amendment (face-context contract; transition collapsed) |
| `7ab3f1b` | **Phase-2.5 design brief** (plans/2026-07-08-phase25-loader-design-brief.md): loader in App.Resource.Bundle.Seam/CommonResidentLayer, EnsureLoaded() first statement of _Ready, strip inside IUnifyGodot.ExportDesktopPlatform, expected-catalog version gate, first-cut list, spikes S1–S4 |

Morning session (same day): phases 0–1 shipped + boot-pin fossil-DLL fix — see the phase-0/1
handover.

## Queue after phase 2 merges

1. **Phase 2.5** — write its plan FROM the design brief (S1 first: loader + one stripped
   assembly, completing E2's positive half in the real exported app), then dispatch.
2. **Polarity flip** — worklist in the maximalism spec (7 assemblies need decisions); flip =
   policy-json edit + windowed gate; composes with 2.5 (shared = contracts + common layer).
3. **Frontier resumes** (D8b progressive-resolution scrub) on the hot-reloadable presentation.

## Delegation lessons (hard-won today; also in memory)

- codex + git worktree = broken (worktree metadata lives under the main repo's .git, outside
  the sandbox): use a full LOCAL CLONE; forbid `git push` explicitly (clone's origin is the
  real repo).
- codex needs `sandbox_workspace_write.writable_roots` for `~/.nuget` + NuGet caches or every
  dotnet restore dies.
- codex `dotnet build` calls can hang silently; it recovers, but budget wall-clock for it.
- Background Bash resets cwd between calls — bake absolute paths/`cd` into every dispatch.
- The lead diff review is not optional: round 1 inverted a tier dependency to satisfy an
  unsatisfiable plan invariant (my Decision-5 fix), and reported "green suite" without the host
  building. Verify by artifacts, always.
- opencode/kimi (morning) executed small bounded packets well; codex-high shines on
  analysis/review/design packets — its three read-only reports (phase-2 inventory, 2.5
  adversarial review, policy audit) each changed the plan-of-record.

## Environment notes

- The exported windowed app from the morning gate may still be running (remote ingress :19292;
  `pgrep -f complete-app`); safe to kill — next gate re-launches with the phase-2 build.
- Godot binary: `tools/Godot_mono.app/Contents/MacOS/Godot`; artifacts v0.1.2;
  drive recipe: `remote__enabled=true nohup <exe> > /tmp/... &` + `tools/fantasim-cmd.py`.
- `--check-dual` runs in `task bundle:stagetool:test`; allowlist should be EMPTY after phase 2.
