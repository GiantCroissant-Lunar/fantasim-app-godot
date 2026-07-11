# D8b slice 1 — progressive-resolution scrub (2026-07-11)

> **For the implementing agent:** execute task-by-task, strict TDD where a task lists tests.
> You do NOT commit, do NOT run git write operations, do NOT run the windowed gate — the lead
> reviews by artifacts, commits, and gates. Steps use checkbox (`- [ ]`) syntax.

**Spec:** `../specs/2026-07-07-layer-presentation-input-parity-canonical-units-directives.md`
§D8b (user directives, 2026-07-08): (1) while scrubbing the planet regenerates at a LOW
tessellation rung, following the hand — replacing "freeze heavies until rest"; (2) at rest the
resolution climbs the rung ladder progressively to full, each rung visibly replacing the last;
cancel the climb when a new scrub starts; (4) rungs REUSE the existing tessellation-frequency
ladder — no parallel LOD system. (Directive 3 — low-res track filmstrips — is already served by
the filmstrip pipeline's ViewRung path; OUT of this slice.)

**Goal:** timeline scrubbing rebinds the planet at tessellation frequency 2–3 per applied
preview tick that crosses a content boundary, then steps 3 → full(4) after 300 ms of rest;
a commit or standard seek goes straight to full.

**Architecture:** one forward-tolerant T1 overload (`IService.GetPlanetPresentationAsync(tick,
tessellationFrequency)` as a default interface method, mirroring `ITimelineController.SeekTo`'s
2-arg pattern) → `Service` threads the frequency through `WorldCrustRunSpec.ForPresentation` and
the reconstructor — **both caches are already frequency-keyed** (`CrustProductCacheKey(freq,
snapshotTick)`, reconstructor `(Seed, freq)`), so no cache work is needed. Presentation-side, the
`ScrubRefreshCoordinator` (built for exactly this on 2026-07-11) grows the rung policy, and
`TimelineFace` drag-scrub emits the already-existing `SeekTo(tick, origin)` contract overload.

## Grounding facts (verified against code 2026-07-11 — do not re-derive)

- `WorldGenerationRenderOptions.Default = (Seed: 7, TessellationFrequency: 4)`
  (`project/plugins/App.World/GenerationGraph/WorldGenerationRenderOptions.cs:22`). Full rung = the
  service's configured frequency, NOT a hardcoded 4.
- `Service.ResolveFilmstripFrequency` (Service.cs:903) already maps view rungs → freq 2/3; the
  crust product cache key is `(renderOptions.TessellationFrequency, snapshotTick)` (Service.cs:869)
  and the reconstructor cache key is `(renderOptions.Seed, renderOptions.TessellationFrequency)`
  (Service.cs:1086). Requesting a lower frequency for a cached tick is a NEW cache entry — safe.
- `ITimelineController` (contracts/App.World/Composition/ITimelineController.cs) ALREADY has
  `void SeekTo(long tick, TimelineTickOrigin origin) => SeekTo(tick);` — TimelineFace can emit
  origins with zero contract change. `RegisterPlayback`'s `Action<long>` onSeek is NOT widened in
  this slice (no consumer needs origin on the echo direction).
- `WorldGlobeSnapshot.Frequency` (contracts/App.World/Dto/WorldDtos.cs:85) — the document carries
  its snapshot's frequency; the presentation derives "full" from the standard-path document, never
  from plugin types it can't see.
- `ScrubRefreshCoordinator` (project/plugins/App.Presentation/ScrubRefreshCoordinator.cs, 124
  lines) currently: preview → debounce via `ScrubApplyScheduler` (300 ms rest) → one
  `requestHeavyRefresh`; commit → flush + optional heavy; standard → cancel + optional heavy.
  4 tests in `project/tests/App.Presentation.Tests/ScrubRefreshCoordinatorTests.cs`.
- Binder heavy path: `PlanetPresentationBinder.RefreshPresentationForRegime()` (core file) →
  `world.GetPlanetPresentationAsync(_timeline.Tick)` → `BindDocument`. The bound-surface log line
  lives in `PlanetPresentationBinder.PlateSurface.cs` `BindPlateSurface` ("Planet plate surface
  bound: …").
- TimelineFace drag-scrub: `project/plugins/App.Timeline.Seam/TimelineFace.cs` — registers
  `RegisterPlayback(Play, Pause, SeekTo, …)` at :319; the drag path reaches `_ctl?.SeekTo(…)`
  (single-arg = Standard origin today). G20 applies: drags are captured at `_Input` gated by a
  press flag — find the actual drag-move/release handlers by reading the file, do not guess.

## Global constraints (hard)

- Edits ONLY under: `project/contracts/App.World/Services/IService.cs`,
  `project/plugins/App.World/Services/Service.cs`, `project/plugins/App.Presentation/`,
  `project/plugins/App.Timeline.Seam/TimelineFace.cs`, and
  `project/tests/{App.Presentation.Tests,App.World.Tests}/`. NOTHING else — no .gitignore, no
  Taskfile, no configs, no other contracts.
- NO new csproj/package/repo. No Ma/Ga identifiers — canonical ticks + "frequency"/"rung"
  vocabulary. File-scoped namespaces; doc comments citing this plan's vault path, matching
  neighboring style.
- ALC house rules: no anonymous-type STJ serialization; no new statics caching types from other
  assemblies; delegates resolve providers at execution time.
- Suite baseline 1102 green (verify BEFORE task 1; if red, STOP and report). Full
  `dotnet build project/FantaSim.sln` + `dotnet test project/FantaSim.sln` after EVERY task.
- Prefix every shell command with `cd /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot && `.

---

### Task 1 — T1 overload + Service frequency threading

**Files:** Modify `project/contracts/App.World/Services/IService.cs`,
`project/plugins/App.World/Services/Service.cs`. Test:
`project/tests/App.World.Tests/` (extend the existing Service/presentation test file that
already builds documents; locate by grepping for `GetPlanetPresentationAsync` in tests).

- [ ] **Step 1: failing test.** A document requested at frequency 2 carries
  `GlobeSnapshot.Frequency == 2` while the default-path document carries the configured default;
  a frequency below 2 clamps to 2; a frequency above the configured default clamps to the default.
  Follow the fixture pattern of whatever existing test already calls
  `GetPlanetPresentationAsync(tick)`.
- [ ] **Step 2: contract.** In `IService`, mirror the `ITimelineController.SeekTo` pattern
  exactly:

```csharp
/// <summary>
/// Same as <see cref="GetPlanetPresentationAsync(long)"/> but materializes crust products at the
/// requested tessellation frequency (clamped to [2, configured default]). D8b progressive-
/// resolution scrub (vault/plans/2026-07-11-d8b-progressive-resolution-slice1-plan.md): low
/// rungs follow the hand, the rest climb re-requests at higher rungs. Default implementation
/// ignores the hint so existing fakes compile unchanged.
/// </summary>
PlanetPresentationDocument GetPlanetPresentationAsync(long referenceTick, int tessellationFrequency)
    => GetPlanetPresentationAsync(referenceTick);
```

- [ ] **Step 3: Service implementation.** Override in `Service`: clamp the frequency to
  `[2, <configured options>.TessellationFrequency]`, then run the same path as the 1-arg overload
  with `renderOptions with { TessellationFrequency = clamped }` flowing to BOTH
  `WorldCrustRunSpec.ForPresentation(...)` and `GetCachedGlobeReconstructor(...)` (trace the
  1-arg overload's body and thread the options value it uses — do not duplicate logic; extract a
  private helper taking the options if needed). Also append `, frequency={Frequency}` (the
  effective options frequency) to the existing `"Crust generation triggered"` log line — the
  windowed gate reads it.
- [ ] **Step 4:** full build + suite green. Record counts.

### Task 2 — rung policy in ScrubRefreshCoordinator

**Files:** Modify `project/plugins/App.Presentation/ScrubRefreshCoordinator.cs`,
`project/tests/App.Presentation.Tests/ScrubRefreshCoordinatorTests.cs`.

New behavior (spec directives 1–2), expressed through ONE injected refresh callback that now
carries a rung: change `Action requestHeavyRefresh` → `Action<int?> requestRefresh` where the
argument is a tessellation-frequency override (`null` = full/default path). Semantics:

- **ScrubPreview + heavyRefreshRequested:** immediately invoke `requestRefresh(LowRung)`
  (latest-wins: if a previous preview's refresh was requested this "frame", overwriting is the
  binder's problem — the coordinator just requests; it does NOT debounce these anymore), AND
  record the preview with the scheduler (rest detection stays).
- **ScrubPreview + !heavyRefreshRequested:** record only (light path animates already — D8/P8).
- **Rest flush (scheduler due):** instead of one full refresh, start the CLIMB: invoke
  `requestRefresh(MidRung)`, then after each subsequent `restDelayMs` interval the next higher
  rung, ending with `requestRefresh(null)` (full). Any new `HandleTick` call of ANY origin
  cancels an in-progress climb (the existing CTS pattern extends to the climb sequence).
- **ScrubCommit:** cancel climb + pending rest, then `requestRefresh(null)` if a preview was
  pending or heavyRefreshRequested.
- **Standard:** cancel climb + pending rest; `requestRefresh(null)` iff heavyRefreshRequested
  (today's semantics).

Rungs: `LowRung = 2`, `MidRung = 3` as `internal const int` on the coordinator (they mirror
`Service.ResolveFilmstripFrequency`'s ladder — cite it in the doc comment). Full = `null`
(binder resolves to the standard path).

- [ ] **Step 1: failing tests.** Rewrite/extend the 4 existing tests (they pin OLD semantics —
  update them deliberately, one behavior per test, using the same fakes: queue-drain
  `deferToMainThread`, controllable `nowMs`, `delayAsync` via TaskCompletionSource):
  (a) boundary-crossing preview → immediate `requestRefresh(2)` exactly once, no full refresh;
  (b) rest after previews → climb sequence `[3, null]` in order, nothing more;
  (c) new preview mid-climb cancels the remaining climb steps;
  (d) commit after previews → exactly one `requestRefresh(null)`, climb never runs;
  (e) standard with heavy → exactly one `requestRefresh(null)` (unchanged);
  (f) dispose mid-climb → no further callbacks.
- [ ] **Step 2:** implement; keep the class Godot-free; every awaited delay goes through the
  injected `delayAsync`; every callback through `deferToMainThread`.
- [ ] **Step 3:** the ctor signature change breaks the binder's wiring — update the ONE
  construction site in `PlanetPresentationBinder.cs` minimally in THIS task so the build stays
  green: `requestRefresh: _ => ScheduleRegimeRefresh()` (rung ignored for now; Task 3 threads it
  properly).
- [ ] **Step 4:** full build + suite green.

### Task 3 — binder threads the rung to the world service

**Files:** Modify `project/plugins/App.Presentation/PlanetPresentationBinder.cs` (core),
`project/plugins/App.Presentation/PlanetPresentationBinder.PlateSurface.cs`.

- [ ] **Step 1:** `RefreshPresentationForRegime()` gains an `int? frequencyOverride = null`
  parameter: when non-null call `world.GetPlanetPresentationAsync(_timeline.Tick, frequencyOverride.Value)`,
  else the existing 1-arg call. The coordinator wiring in the ctor becomes
  `requestRefresh: freq => ScheduleRegimeRefresh(freq)` — and `ScheduleRegimeRefresh` gains the
  same optional parameter, stamping the pending override so the deferred
  `RefreshPresentationForRegime` uses the LATEST requested rung (a later full request overwrites
  a pending low one; a later low request overwrites a pending full — last writer wins, one
  deferred refresh either way via the existing `_regimeRefreshPending` dedup).
- [ ] **Step 2:** append `, frequency={Frequency}` (from `document.GlobeSnapshot!.Frequency`) to
  the `"Planet plate surface bound"` log line in `BindPlateSurface` — the gate's primary signal.
- [ ] **Step 3:** full build + suite green (no new tests — the coordinator tests pin the policy;
  the binder threading is exercised by the windowed gate).

### Task 4 — TimelineFace drag emits scrub origins (timeline bundle)

**Files:** Modify `project/plugins/App.Timeline.Seam/TimelineFace.cs`. Tests: extend an existing
Godot-free TimelineFace logic test ONLY if one already covers the drag path; otherwise no new
tests (Godot seam — the windowed gate covers it) and say so in AGENT-SUMMARY.md.

- [ ] **Step 1:** read the drag-scrub handlers (G20: press-flag-gated `_Input` capture). On
  drag-MOVE ticks call `_ctl.SeekTo(tick, TimelineTickOrigin.ScrubPreview)`; on drag-RELEASE call
  `_ctl.SeekTo(tick, TimelineTickOrigin.ScrubCommit)`. Non-drag seeks (play transport, remote
  echo, keyboard) stay exactly as they are. Do NOT touch `ApplyFilmstripPreview` or the disposed
  guard (fixed this morning @825461b).
- [ ] **Step 2:** full build + suite green.

### Task 5 — handoff

- [ ] Final full build + full suite; record counts (expect baseline 1102 + Task 1/2 additions).
- [ ] Write `AGENT-SUMMARY.md` at repo root: per-task files/tests/deviations (with reasons),
  anything discovered the lead must know before gating. Do NOT commit anything.

## Lead acceptance gate (lead-run; NOT the implementer)

T1 contract changed → **full build + re-run** (`task build:godot:desktop` → fresh
`task run:exported`), then `task bundles && task bundle:install` sanity for later hot-reloads.

1. Suite green at the merge commit.
2. Ingress scrub burst at correct scale (G31/G32: single python process, sub-300 ms gaps,
   in-span kb-scale ticks, fresh boot): during previews, `Planet plate surface bound … frequency=2`;
   after rest, binds at `frequency=3` then `frequency=4`; `scrubCommit` → `frequency=4`.
3. Real-mouse drag on the timeline handle (D2 doctrine): planet visibly follows at low res,
   sharpens at rest — screenshot pair vendored; final look verdict is the user's eye.
4. `old ALC collected for bundle world` + `for bundle timeline` on a subsequent hot-reload round.
