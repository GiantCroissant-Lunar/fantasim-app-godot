# Session record — Plan 5a: real boom-hud track/section timeline face (hot-reloadable bundle) MERGED

> **Date:** 2026-06-22 · **Repo:** `fantasim-app-godot` · **Result:** the vestigial trackless `AnimationTree`
> is replaced by a REAL track/section timeline face — authored as a **boom-hud** document in **C#**, shipped in a
> **hot-reloadable `timeline` bundle**, and **windowed-verified** (renders, live-tracks the regime, hot-reloads).
> App `main` @ **`0371e3f`** (was `be85478`). 8 commits, 5/5 App.Timeline unit tests, final opus review clean.

## TL;DR — the arc
A prior session shipped Plan 4's timeline transport as a **trackless `AnimationTree` skeleton** — the AnimationPlayer
vocabulary was wired but nothing represented "track-group = sphere, track = layer/field". The user asked to **make
it real**, with four constraints: **uses boom-hud · bundle so it can hot-reload · C# code for UI**. This session
designed it (brainstorming → plan), built it via **subagent-driven development** (5 impl tasks + reviews), and
**verified it live in the exported windowed app** — which (with the final review) caught **three real runtime bugs
the unit tests could not**, all fixed before merge.

## What shipped (`be85478..0371e3f`)
- **`TimelineModel`** (`plugins/App.Timeline/TimelineModel.cs`) — pure band/track layout from the regime schedules
  (proportional `TimelineBand`s clamped to maxTick + active flag; `TimelineTrack`s = union of all layers, active-
  highlighted). Unit-tested (3 tests).
- **`ITimelineController`** (`contracts/App.World/Composition/`) — the shared contract bridging the bundled HUD to the
  resident transport/globe (Tick/MaxTick/IsPlaying/schedules/Play/Pause/SeekTo/`event TickChanged`). Resident adapter
  `TimelineController` (`plugins/App.World.Seam/`) wraps `RegimeTimelineTransport` + `GlobeView`; pumped per-frame via
  a new `RegimeTimelineTransport.TickObserver`.
- **`TimelineViewSource : IViewSource, IDisposable`** (`plugins/App.Timeline/`) — the **boom-hud
  `RuntimeSurfaceDocument`** authored in C#: play/pause `button` + regime/state label → per-sphere `panel` with a
  horizontal row of proportional regime-band `button`s (region-jump) + track `badge`s. Unit-tested (2 tests, tree-walk).
- **`App.Timeline` plugin + scene-less `timeline` bundle** (mirrors `assist`) — `TimelinePlugin` registers the
  `IViewSource` and mounts it; manifest + `collectible-bundles.json` + `Taskfile` `bundle:timeline*` + `bundle:install`
  + `export_presets` preset. ALC-clean (references only shared contracts + `BoomHud.Foundation`).
- **Host wiring** (`hosts/complete-app/Host.cs`) — registers `ITimelineController` in `ComposeWorldView` (sync `_Ready`,
  before the deferred scene-enter) and enters the `timeline` bundle (`EnterAsync("timeline","stage")`).
- **`IViewHost` promoted to `contracts/App.Ui`** (part of the deadlock fix) so collectible plugins can mount via it.

## ⚠️ Durable findings — the windowed verify + final review earned their keep (3 bugs unit tests missed)
1. **Mount deadlock (`04da15d`).** `TimelinePlugin.InitializeAsync` called `IService.ShowAsync("timeline")`, which (since
   `BundleHost` records `_loaded` only AFTER the plugin's `InitializeAsync` returns — gate held @`BundleHost.cs:55`,
   `_loaded` @`:210`) saw `IsLoaded==false` and re-entered `LoadFromDirectoryAsync` → `_gate.WaitAsync` → **deadlock**.
   Symptom: globe + autoplay fine, but the timeline-enter hung at "Loading scene bundle: timeline"; HUD never mounted.
   **Fix:** mount via `IViewHost.Mount` (a `CallDeferred`, no re-load), not `ShowAsync`. **Lesson: a collectible bundle's
   `InitializeAsync` must NEVER re-enter the bundle loader — it runs while the load gate is held and `IsLoaded` is false.**
2. **Per-tick re-render perf (`3475ad6`).** `TimelineViewSource` fired `Changed` on every `TickChanged` → `ViewRenderer`
   regenerated + wrote a `.tscn` to `user://` **every tick** (4280× in one run) → ~9fps, app exited. **Fix:** fire
   `Changed` only on geosphere-**regime** change (+ on play/pause). **Lesson: an `IViewSource.Changed` drives a FULL
   `ViewRenderer.Rebind`; gate it to actual visible changes, never per-frame.**
3. **Cross-ALC subscription leak (`0371e3f`, found by the final opus review).** `TimelineViewSource` subscribed the
   **resident** `ITimelineController.TickChanged` in its ctor but never unsubscribed on unload (`IViewSource` isn't
   `IDisposable`). Each hot-reload leaked a stale subscriber AND — worse — the resident delegate **targeting a
   collectible-ALC instance PINNED the old ALC**, preventing its unload. **Fix:** `TimelineViewSource : IDisposable`
   unsubscribes via a kept delegate; `TimelinePlugin.ShutdownAsync` disposes it first. **Lesson: anything in a
   collectible bundle that subscribes a RESIDENT event must unsubscribe on `ShutdownAsync`, or it pins the ALC.**

## ✅ Verification (exported windowed app)
- `View mounted: timeline`; the HUD renders the per-sphere proportional regime bands + track rows; the active band/
  track follow the regime (stagnant-lid → mobile-plate as autoplay crosses onset). Re-renders: **3**, not 4280.
- **Hot-reload CONFIRMED:** hot-copying a rebuilt `timeline.pck` → `Bundle unloaded → Bundle loaded` → the edited label
  rendered **without restart**; re-tested post-leak-fix with **no error during the new `Dispose`/unsubscribe**.
- 5/5 `App.Timeline.Tests`; full app suite was green pre-branch (119/119).

## Scope note + follow-ups (Plan-5 polish, not bugs)
- **Playhead is regime-level** (the active-band highlight), not a continuous crossing line — the boom-hud basic catalog
  is flexbox (`progressBar` is display-only). The `GlobeView` `HSlider` provides continuous scrub. **Upgrade path:** a
  custom canvas boom-hud component (a `Control` with `_Draw`) for a precise axis + crossing playhead + click-anywhere-seek.
- **Driving via clicks** (play/pause/region-jump buttons) is wired (`Dispatch` → controller) but was not click-tested in
  this run (an unrelated AnyDesk window overlapped the app, blocking computer-use clicks); the logic is unit-tested.
- Still-pending render polish from Plan 4: **boundary-type routing → real terrain + boundary lines** (the globe is flat
  `[-500,-500]` until then), **thermal magma glow**, autoplay magma-phase tuning.
- The **boom-hud-view-in-a-hot-reloadable-bundle pattern** now exists (first of its kind here) — reusable for other live
  views; mind the 3 gotchas above.

## Pointers
- Plan: [`vault/plans/2026-06-22-timeline-face-boomhud-bundle.md`](../plans/2026-06-22-timeline-face-boomhud-bundle.md)
- SDD ledger (per-task reviews + the 3 windowed/review findings): `.git/sdd/progress.md`
- Predecessor: [`vault/handover/2026-06-22-plan4-regimes-onset-timeline-merged.md`](2026-06-22-plan4-regimes-onset-timeline-merged.md)
