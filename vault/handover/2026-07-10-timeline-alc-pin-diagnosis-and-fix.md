# Timeline-bundle ALC pin — diagnosis + fix (2026-07-10, evening session)

Resolves the open follow-up from
[2026-07-10-world-alc-pin-diagnosis-and-fix.md](2026-07-10-world-alc-pin-diagnosis-and-fix.md):
`Hot-reload: old ALC still pinned for bundle timeline` fired 1-in-~6 timeline reloads during
multi-pck installs. Diagnosed with a CPU-load-amplified live repro + dotnet-dump + the ClrMD
pin-hunter + `clrstack -all`. **THREE distinct mechanisms found and fixed** — each surfaced when
the windowed gate on the previous fix failed and forced another dump-at-failure round. All three
are real defects; C is almost certainly the base-rate 1-in-6, A and B additive under interleave.

## Mechanism A — rogue recompose (pin class 5, timeline edition) — `043b765`

`TimelinePlugin` reacts to WORLD reloads: `RuntimeChanging("world")` severs + arms
`_worldRebindPending`; any later `RuntimeChanged` (bundle-agnostic — EVERY bundle's reload raises
it, from the world watcher's THREADPOOL thread) recomposes via `TryConsumePendingWorldRebind`.
Defects: (1) guard = `IsLoaded("world") && TryGet<ITimelineController>() != null` — satisfied by
the OUTGOING registration mid-unload, so an interleaved Changed from another bundle recomposed
against the dying world controller (log signature: `TimelinePlugin: IService registered` BEFORE
`Bundle unloaded: world`); (2) no synchronization with its own MAIN-thread `ShutdownAsync`, so an
in-flight recompose could re-register face context / IService / 4 commands / TickChanged AFTER
shutdown's cleanup — the dying generation stays rooted by resident structures.

**Fix:** `_lifecycleGate` + `_shutdown` latch + `_outgoingController` WeakReference identity
guard (rebind only against a DIFFERENT controller instance). Deadlock audit: command service
invokes handlers outside its lock; registry never calls back into the plugin. RED→GREEN:
`TimelinePluginLifecycleRaceTests` (gated-controller fake freezes the recompose mid-compose
across shutdown; outgoing-registration guard). Post-fix logs show every recompose waiting for
`Bundle unloaded: world`.

## Mechanism B — resident filmstrip closures capture the provider Func — `d3e312c`

Gate on A still failed (no rogue recompose in the log). Dead-referencer walk of the dump:
resident `TimelineFace+<>c__DisplayClass140_0` (StartFilmstripRequest's Task.Run + CallDeferred
closures; the deferred queue holds GCHandles) → provider Func (compiled into the timeline
bundle, `ComposeTimeline` display class) → TimelinePlugin/Services.Service. The face captured
the provider AT REQUEST time, so queued filmstrip work rooted the outgoing generation.

**Fix:** `StartFilmstripRequest` resolves `_filmstripPreviewProvider` at EXECUTION time and
`ClearResidentContext` supersedes the filmstrip queue — a severed face drops the old generation
immediately. `App.Timeline.Seam` is RESIDENT → full re-export.

## Mechanism C — in-flight renders root both ALCs via their call stacks — `a5c939b`

Gate on A+B still failed. The live dump (instant dumper armed on the verdict line) with
`clrstack -all` showed two threadpool threads still EXECUTING at verdict time:

```
FantaSim.Geosphere.Asthenosphere.Convection.MantleAnomalyField.EvaluateAt(Vector3D)
FantaSim.App.World.Services.Service.BuildMantleFilmstripPreview(...)
FantaSim.App.World.Services.Service.GetLayerFilmstripPreview(...)
FantaSim.App.Timeline.TimelinePlugin+<>c__DisplayClass25_0.<ComposeTimeline>b__1(...)
FantaSim.App.Timeline.Seam.TimelineFace+<>c__DisplayClass140_0.<StartFilmstripRequest>b__0()
```

A filmstrip render executes bundle code on a threadpool thread: its STACK roots the outgoing
timeline ALC (provider frame) and the outgoing world ALC (the Service instance the call started
on) until the render completes — longer than the 32-frame forced-GC probe whenever renders are
slow (mantle per-pixel field evaluation; loaded machine). Self-heals on completion, which is why
every post-verdict dump showed unreachable-but-uncollected victims. Under a saturated threadpool
this also silenced the world pck watcher for the rest of the run (its debounce continuations
starved behind the stuck renders — a secondary reload-degradation).

**Fix (cooperative cancellation through the chain):**
`App.World.IService.GetLayerFilmstripPreview(request, CancellationToken = default)` honored per
pixel row and between heavy build stages; `ITimelineFaceContext.FilmstripPreviewProvider` gains
the token; `TimelineFace` owns `_filmstripCts`, renewed per bind, cancelled in
`ClearResidentContext` — a sever unwinds in-flight renders within one pixel row.
`OperationCanceledException` drops the frame; the next bind re-requests visible slots.

## Windowed proof (exported app, full re-export, 6-spinner CPU load)

12 consecutive `task bundle:install` rounds with 6 busy-loop CPU spinners running (the amplifier
that re-fired the pin at round 8 pre-fix, round 4 with fix A only, round 3 with A+B):

```
timeline: old ALC collected x22, still pinned x0   (2 reloads/round: stage cascade + own pck)
world:    old ALC collected x12, still pinned x0
stage/assist/activity: all collected, x0 pinned
filmstrip renders: 0 failures, 0 MissingMethodException (contract change type-safe end to end)
```

Pre-fix baseline under the identical amplifier: timeline pinned at round 8 (run 3) and, on the
partial fixes, rounds 4 and 3 — plus a world-only pin (run 3, round 4). Post-fix: zero pins in
~24 timeline unloads and 12 world reloads.

## Repro / gate technique (new, keep)

Natural `task bundle:install` rounds on an idle machine (24 reloads) and command-ingress
timeline-reload churn against world/activity pck churn (~1,300 reloads) never re-fired the pin —
churn ≠ load. What works: **6 busy-loop CPU spinners + natural bundle:install rounds** — load
widens interleave windows AND stretches renders past the probe. Pre-fix failures: round 8 (run 3),
round 4 (run 4, fix A only), round 3 (run 6, fixes A+B). For live roots, arm an **instant dumper**
(`tail -f | grep -m1 "still pinned for bundle timeline"` → `dotnet-dump collect` ×2 for the
macOS dup-key retry) BEFORE the gate rounds — post-verdict dumps taken a minute late only show
healed (unreachable) victims.

## Tooling additions (pin-hunter recipe)

- Pin-hunter prints victim ADDRESSES (one per type) so `refs`/`inspect` can chase chains.
- **When GCRoot finds NO paths for still-loaded old-gen modules: (a) the pin may have healed
  (walk DEAD referencer chains with `refs <addr>` hop-by-hop), and (b) check `clrstack -all`
  for bundle-code frames FIRST — thread-stack roots of in-flight calls never showed in the
  ClrMD GCRoot walk on these macOS dumps.** Both were needed this session.
- macOS ClrMD dup-key load failure hit twice more → always collect a second dump immediately.
- Session tooling: scratchpad `pinhunter/` of session b1b3986a; dumps `timeline-pin-run3.dmp`,
  `timeline-pin-run4b.dmp`, `timeline-pin-live.dmp` (the clrstack one).
- Gotcha: `task build:godot:desktop` piped through `tail` masks its exit code, and a concurrent
  `dotnet build` during export causes a per-arch dll mismatch StripError — export with the
  solution build quiesced.

## Open follow-ups (out of scope)

- `iii` bundle: `collection probe skipped (unloadInitiated=False)` every round — the iii reload
  path never initiates a diagnostic unload, so it has NO ALC-collection gate at all.
- Audit rule worth a sweep: any RESIDENT queue/Task/CallDeferred capturing a bundle-compiled
  delegate, and any LONG-RUNNING synchronous call into bundle code without a cancellation seam,
  is a pin class. Filmstrip was today's instance; scrub/present paths may hide more.
- Run-5 anomaly (unexplained, benign): one cascade re-enter showed `Scene entered: timeline`
  without a bundle load, and the next timeline reload's unload found the bundle already gone
  (`probe skipped, unloadResult=False`). Worth a look if scene/bundle state ever drifts again.
