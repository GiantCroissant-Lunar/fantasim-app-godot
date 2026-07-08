# Handover — bundle maximalism, session 2 (2026-07-08 afternoon)

**Read order for the next session:** this file →
[specs/2026-07-08-bundle-oriented-maximalism.md](../specs/2026-07-08-bundle-oriented-maximalism.md)
(phase table + flip worklist) →
[plans/2026-07-08-phase2-timeline-t3-to-bundle.md](../plans/2026-07-08-phase2-timeline-t3-to-bundle.md)
(phase-2 contract incl. Decision 5) →
[handover/2026-07-08-bundle-maximalism-phase0-1-handover.md](2026-07-08-bundle-maximalism-phase0-1-handover.md)
(the morning session: phases 0–1, reload lessons, boot-pin fix).

## ✅ PHASE 2 COMPLETE — merged to main `e793b53`, windowed-gated (2026-07-08 evening)

The lead review of `cb7adcb` found **three real defects** codex's sandbox could not surface
(it can run neither `dotnet test` nor the windowed app), each fixed as its own commit on the
branch before merge:

1. **`679ce6d`** — 3 failing `TimelinePluginTests`: `ComposeTimeline` created the proxy
   before its internal sever (spurious `RebindResidentContext` on first compose), and
   `SeverTimelineService` disposed only the registration handle, never the
   `TimelineFaceContext` instance — `UnregisterPlayback()` never ran (the plan's playback pin).
2. **`33a7db4`** — windowed-gate finding: the world-rebind ran on the resource watcher's
   thread-pool thread → `UpdateLayout`/`AddChild` off-main (Godot hard error ×9). The deleted
   host machinery had `Callable.From(...).CallDeferred()`; the marshal now lives in
   `TimelineFace.RebindResidentContext` itself (seam owns the Godot constraint; T3 stays pure).
   The plugin's blocking post-rebind `SeekAsync` push was deleted — the face's bind tail
   (`SeekTo(_ctl.Tick)`) already delivers the tick on the main thread after the bind.
3. **`b6f93fb`** — **NEW ALC-PIN CLASS (third instance): anonymous types × shared
   System.Text.Json.** The `timeline.*` handlers serialized `new { ... }` (types compiled into
   the collectible assembly) via the resident default `JsonSerializerOptions`, whose per-Type
   `CachingContext` rooted the bundle's LoaderAllocator forever. ClrMD gcroot on the live app
   named the chain (StrongHandle → JsonSerializerOptions → CachingContext →
   ConcurrentDictionary<Type,CacheEntry> → RuntimeType → LoaderAllocator). Only generations
   that had SERVED a command pinned — untouched ones collected, which made run 1 look flaky.
   Fix: responses built from resident `JsonObject`/`JsonArray`. **Rule: no bundle-compiled
   type may ever reach a shared serializer/cache — grep `JsonSerializer.Serialize(new` in any
   new bundle-tier code.**

**Windowed gate (run 3, all PASS):** boot sanity (plugin composes T3 at boot, 0 errors) →
baseline seek/select/toggle (arming the cache trap) → timeline hot-reload ×2 (`old ALC
collected for bundle timeline` ×2, commands live after each) → world reload via watcher
(plugin severed + recomposed, `old ALC collected for bundle world`) → remote
`resource.reload_bundle` world (hook deletion safe, collected again) → commands still live.
Zero `still pinned`, zero `Adding children` across the run.

Headless gates: sln build, full suite 18 assemblies 0 failures (timeline 72/72), explicit
complete-app build, `stage_bundle.py timeline` (1 assembly), `--check-dual` EMPTY allowlist.

Clone `fantasim-phase2-clone` deleted after merge (codex dispatch + log tail preserved in
`.agent/logs/codex/`); branch deleted. Diagnostic tool: the ClrMD pin-hunter recipe was
rebuilt this session (~60 lines; see the phase-0/1 handover's diagnosis notes).

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
