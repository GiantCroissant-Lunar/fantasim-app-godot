# Handover — bundle maximalism, session 2 (2026-07-08 afternoon)

**Read order for the next session:** this file →
[specs/2026-07-08-bundle-oriented-maximalism.md](../specs/2026-07-08-bundle-oriented-maximalism.md)
(phase table + flip worklist) →
[plans/2026-07-08-phase2-timeline-t3-to-bundle.md](../plans/2026-07-08-phase2-timeline-t3-to-bundle.md)
(phase-2 contract incl. Decision 5) →
[handover/2026-07-08-bundle-maximalism-phase0-1-handover.md](2026-07-08-bundle-maximalism-phase0-1-handover.md)
(the morning session: phases 0–1, reload lessons, boot-pin fix).

## ⚡ RESUME HERE — phase 2 implemented, gates NOT yet run

**Round 2 COMPLETED before session end.** The full Decision-5 implementation sits at
`yokan-projects/fantasim-phase2-clone` branch `feat/bundle-max-phase2`, commit **`cb7adcb`**
(lead-committed; codex cannot commit — its workspace-write sandbox mounts `.git` read-only by
design). What it contains: `ITimelineFaceContext`/`ITimelineFaceProxy` contracts;
`DeferredTimelineFace` moved into the plugin; `TimelinePlugin` owns service/context/proxy
registrations, the three `timeline.*` commands, shutdown severing, and world rebind;
`TimelineFace` resolves context via ONE `ResidentRegistry` fallback static; resident
`TimelineComposition` DELETED; host timeline machinery + `App.Timeline` csproj ref removed;
`--check-dual` allowlist EMPTY; timeline restages as `FantaSim.App.Timeline.dll` only.

**Verified under codex's sandbox:** targeted builds (App.Timeline, Seam, Tests projects) +
`stage_bundle.py timeline` + `--check-dual`. **NOT yet run (sandbox couldn't):** full sln test
suite (VSTest needs a TCP listener — sandbox-denied), explicit `complete-app.csproj` build,
windowed gate.

Next session, in order:
1. In the clone: `dotnet build project/FantaSim.sln && dotnet test project/FantaSim.sln &&
   dotnet build project/hosts/complete-app/complete-app.csproj` — fix forward anything red.
2. REVIEW the `cb7adcb` diff against the plan's Pin map + Decision 5. Non-negotiables: NO
   T3→Seam ProjectReference; every plugin registration/subscription severed in ShutdownAsync;
   world-rebind pending flag consumed only when the new `ITimelineController` registration
   exists. Scrutinize the reported deviations: (a) the one `TimelineFace.ResidentRegistry`
   static (permitted fallback — verify it holds only the resident registry, never bundle
   objects); (b) `stage_bundle.py` now disables dotnet build servers (workaround for the hang);
   (c) timeline tests use local minimal schedules instead of App.World.Composition.
3. Fetch into the main repo: `git -C yokan-projects/fantasim-app-godot fetch
   ../fantasim-phase2-clone feat/bundle-max-phase2:feat/bundle-max-phase2` (never push FROM the
   clone — its origin IS the main repo).
4. WINDOWED GATE (per verify-windowed): full export → boot sanity → timeline hot-reload ×2
   (`old ALC collected for bundle timeline`; seek/select/toggle work after) → world reload
   (timeline stays usable via the plugin's self-rebind; `old ALC collected for bundle world`) →
   remote-commanded `resource.reload_bundle` for world (proves the BundleReloadHook deletion
   safe) → merge to main, delete clone + branch, reword/squash the two wip commits at merge.

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

- **codex can NEVER commit**: workspace-write mounts `.git` read-only by design (clone or
  worktree, doesn't matter). Pattern: codex implements, the LEAD commits after review.
- **codex can NEVER run `dotnet test`**: VSTest opens a local TCP listener, sandbox-denied.
  Budget targeted `dotnet build` gates in prompts; the lead runs the suite.
- codex + git worktree is doubly broken (worktree metadata lives under the main repo's .git):
  use a full LOCAL CLONE; forbid `git push` explicitly (clone's origin is the real repo).
- codex needs `sandbox_workspace_write.writable_roots` for `~/.nuget` + NuGet caches or every
  dotnet restore dies; sibling-repo ProjectReferences ($(YokanProjectsRoot)) can still hit
  denied restore writes — keep clones inside yokan-projects/ and expect test rewrites.
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
