# TimelineFace split — implementation plan (2026-07-11)

> **For the implementing agent:** execute task-by-task. You do NOT commit, do NOT run git write
> operations, do NOT run the windowed gate — the lead reviews by artifacts, commits, and gates.
> Mirrors the executed binder-split recipe:
> [`2026-07-11-planet-presentation-binder-split-plan.md`](2026-07-11-planet-presentation-binder-split-plan.md).

**Goal:** split `TimelineFace.cs` (1,882 lines) into a core face plus seams — ZERO behavior
change — with the filmstrip machinery extracted as a real `FilmstripPreviewController`
(the 2026-07-10 review's "first cut"; precondition for the tunnel-timeline skin).

**Architecture:** one real class extraction (FilmstripPreviewController + a tiny Godot-free
cache ledger) where D-arcs will land next, `partial class` file splits (verbatim member moves)
for the input and lanes clusters. Line refs against current main (`d71d8f4`) — locate by NAME
when drifted.

## Global constraints (hard)

- ZERO behavior change. Moved bodies verbatim; only the substitutions each task lists.
- Edits ONLY under `project/plugins/App.Timeline.Seam/` and `project/tests/App.Timeline.Tests/`.
  No csproj/package/config/.gitignore edits. SDK csproj auto-includes new .cs files.
- **PRESERVE the 825461b disposed-guard fix**: `StartFilmstripRequest`'s deferred callable and
  `ApplyFilmstripPreview`'s early-outs were JUST fixed to exit silently on a disposed/freed face
  during reload waves. The moved code must keep that behavior byte-for-byte; the controller's
  disposed/alive checks must be at least as strong. Re-read the current bodies carefully.
- ALC house rules: no anonymous-type STJ serialization; no new statics holding cross-assembly
  types; delegates resolve at execution time. Canonical ticks; no Ma/Ga identifiers.
- Suite baseline 1116 green BEFORE Task 1 (verify; if red STOP and report). Full
  `dotnet build project/FantaSim.sln` + `dotnet test project/FantaSim.sln` after EVERY task.
- Prefix every shell command with
  `cd /Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot && `.

### Task 1 — `FilmstripCacheLedger` (Godot-free bookkeeping, TDD)

**Files:** Create `project/plugins/App.Timeline.Seam/FilmstripCacheLedger.cs`;
Test `project/tests/App.Timeline.Tests/FilmstripCacheLedgerTests.cs`.

The current cache is a Dictionary + insertion-order FIFO evicted at a 512 cap (see
`EvictFilmstripCacheIfOverCap`, TimelineFace.cs:1849, and `DisposeFilmstripTextureCache`,
:1831). Extract the BOOKKEEPING (keys, insertion order, cap, eviction choice) into a generic
Godot-free class; textures stay caller-side:

```csharp
/// <summary>
/// Insertion-ordered FIFO ledger for the filmstrip texture cache: tracks keys up to a cap and
/// yields the evictee when full. Godot-free bookkeeping only — the owner maps keys to textures.
/// Split from TimelineFace 2026-07-11 (vault/plans/2026-07-11-timelineface-split-plan.md).
/// </summary>
internal sealed class FilmstripCacheLedger
{
    public FilmstripCacheLedger(int cap);
    public int Count { get; }
    public bool Contains(string key);
    /// <summary>Records a key; returns the evicted oldest key when the cap is exceeded, else null.</summary>
    public string? Record(string key);
    public IReadOnlyCollection<string> Keys { get; }   // insertion order, for dispose-all
    public void Clear();
}
```

Steps: (1) failing tests FIRST — record-under-cap returns null; recording the (cap+1)-th key
returns the OLDEST key and drops it from Keys; duplicate Record of an existing key returns null
and does NOT change its eviction position (match the CURRENT TimelineFace semantics — read the
existing code first and pin what it actually does; if the current code re-inserts on duplicate,
pin THAT instead and say so in AGENT-SUMMARY); Clear empties. (2) implement. (3) suite green.

### Task 2 — `FilmstripPreviewController` (real class extraction)

**Files:** Create `project/plugins/App.Timeline.Seam/FilmstripPreviewController.cs`;
Modify `project/plugins/App.Timeline.Seam/TimelineFace.cs`.

Move VERBATIM (with their comments) into the controller: records `PendingFilmstripFrame` (:37),
`QueuedFilmstripFrame` (:39); methods `BuildFilmstripFramePlaceholder` (:1197),
`RequestFilmstripTexture` (:1230), `PumpFilmstripQueue` (:1275), `StartFilmstripRequest`
(:1296), `FilmstripRequestKey` (:1352), `ApplyFilmstripPreview` (:1355), `PruneFilmstripWaiters`
(:1400), `SupersedeFilmstripGeneration` (:1822), `DisposeFilmstripTextureCache` (:1831),
`EvictFilmstripCacheIfOverCap` (:1849 — now delegating to the Task-1 ledger), plus every field
that ONLY these members touch (texture cache dict, queue, waiters, generation counter, in-flight
bookkeeping — enumerate by grep before moving).

`BuildCompactFilmstrip` (:1145) STAYS in TimelineFace (it lays out lane content and calls the
controller for textures/placeholders).

Controller shape — constructor-injected, no Godot inheritance:

```csharp
internal sealed class FilmstripPreviewController : IDisposable
{
    public FilmstripPreviewController(
        Func<FantaSim.App.World.IService?> resolveWorldService,   // execution-time resolve (ALC rule)
        Func<bool> isFaceAlive,                                   // the 825461b disposed/freed guard
        Action<Action> deferToMainThread,                         // Callable.From(...).CallDeferred wrapper
        ILogger log);
    // public surface = exactly what TimelineFace still calls:
    // RequestTexture(...), Pump(), Supersede(), DisposeCache(), BuildFramePlaceholder(...)
}
```

Substitutions allowed: field access → controller fields; the face's disposed checks →
`isFaceAlive()`; direct service resolution → `resolveWorldService()` (must stay
execution-time, matching the current code's pattern from the 07-10 mechanism-C fix); CallDeferred
→ `deferToMainThread`. TimelineFace keeps one `private readonly FilmstripPreviewController
_filmstrip;` built in the ctor with lambdas closing over `this` (`() => IsInstanceValid(this) &&
IsInsideTree()` — copy the EXACT aliveness predicate the current guards use; do not invent one).
`_ExitTree`/dispose path calls `_filmstrip.Dispose()` at the same point the current teardown
runs `SupersedeFilmstripGeneration` + `DisposeFilmstripTextureCache`.

Suite green after (no new tests — Godot-coupled; the ledger carries the unit coverage; the
windowed gate proves the rest).

### Task 3 — partial split `TimelineFace.Input.cs`

Mark the class `partial`; move verbatim: `_Input` (:573), `TryStartPlayheadLineScrub` (:603),
`TryHandleTimelineWheelZoom` (:621), `TryHandleTimelineMagnifyZoom` (:640),
`IsTimelineZoomPosition` (:653), `OnLanesGuiInput` (:657), `OnFaceGuiInput` (:682),
`FaceToRulerLocalX` (:705), `HandleScrubPress` ×2 (:708/:714), `QueueScrubMotion` ×2
(:722/:728), `HandleScrubRelease` (:736), `TryScrubTick` (:747), `ApplyScrubAction` (:761),
plus input-only state fields (press flags, drag state — enumerate by grep). Keep the G20
drag-capture comments with the code. Suite green.

### Task 4 — partial split `TimelineFace.Lanes.cs` + core tidy

Move verbatim: `UpdateLayout` (:804) through `IsLayerActive` (:1542) EXCEPT the filmstrip
members already moved in Task 2 and except transport/`OnBandPressed`-style handlers that the
core's transport region owns — precisely: `UpdateLayout`, `UpdateTrackContentLayout`,
`BuildLanes`, `BuildLane`, `BuildLaneBands`, `BuildLaneTracks`, record
`TrackContentRenderContext`, `RenderTrackContent`, `RenderFilmstripTrackContent`,
`RenderGraphTrackContent`, `RenderGenericTrackContent`, `UpdateLanesMinimumHeight`,
`ConfigureTrackRowChild`, `ConfigureTrackContent`, `ResolveTrackContentWidth`,
`BuildCompactFilmstrip`, `CompactStripLabel`, `BuildExpandedGraph`, records
`TrackGraphPortItem`/`TrackGraphNodeItem`/`TrackGraphWireItem`, `ResolveGenerationGraphFamily`,
`ResolveTrackRegime`, `HasLayer`, `OnTrackExpandPressed`, `OnTrackPressed`, `IsLayerActive`,
`DisposeTrackBindings`, plus lanes-only fields. Core file keeps: statics/ctor/_Ready/_Process,
resident-context bind/clear + registry subscription, `_ExitTree`, `SetupAnimationSystem`,
transport (`Play`/`Pause`/`SeekTo`/`EchoSeekTo`/`ApplyView`/`TransitionState`/
`OnPlayPausePressed`/`OnBandPressed`/zoom button handlers), `UpdateUI`, `FriendlyLayerLabel`,
`UpdateRuler`, `UpdatePlayheadHandle`, `ZoomToSpanAroundCurrentTick`, `ZoomToSpanAroundLocalX`,
`SetViewRange`, `ScheduleViewRebuild`, shared helpers (`TrackKey`, `SafeNodeName`,
`ClearChildren`, `DisconnectIfConnected`). Prune unused usings in every touched file; do not
reorder surviving members. Suite green. Record `wc -l` of all TimelineFace* + new files in
AGENT-SUMMARY (target: core ≤ 700).

### Task 5 — handoff

Final full build + suite (expect 1116 + Task-1 additions); `AGENT-SUMMARY.md` at repo root
(files/tests per task, every deviation with reason, discoveries for the lead); no commits.

## Lead acceptance gate (lead-run)

All edits in the timeline collectible bundle → hot-reload: `task bundle:timeline &&
task bundle:install` against the running exported app → `old ALC collected for bundle
timeline`; filmstrips still render in lanes (screenshot); drag-scrub still emits
ScrubPreview/ScrubCommit (D8b low-rung binds visible in log during a remote burst); NO
`ObjectDisposedException` during the reload wave (the 825461b guard survived the move); suite
green at the merge commit.
