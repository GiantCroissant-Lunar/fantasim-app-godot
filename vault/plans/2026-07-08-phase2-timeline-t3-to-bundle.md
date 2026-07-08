# Phase 2 — Timeline T3 into the timeline bundle

> **For agentic workers:** bounded per-task execution; each task commits separately and leaves the
> full solution green (`dotnet build project/FantaSim.sln && dotnet test project/FantaSim.sln`
> PLUS `dotnet build project/hosts/complete-app/complete-app.csproj` — the sln config masks host
> breaks, phase-1 lesson). Seeded by the codex phase-2 analysis (2026-07-08, local log
> `.agent/logs/codex/phase2-timeline-analysis-20260708.log`); this doc is the binding contract.

**Goal:** `FantaSim.App.Timeline` (T3) composes inside timeline.pck's collectible ALC; the host
deletes its rebind machinery and its T3 ProjectReference; the last `--check-dual` allowlist entry
disappears.

**Decisions (resolving the analysis' open questions):**
1. `App.Timeline.Seam` (Godot T4, TimelineFace) STAYS resident this phase — the
   Godot-types-in-bundles rule change is phase 4's verification, not phase 2's.
2. Pure UI-model helpers move to `contracts/App.Timeline` (T1 carries shared DTOs). Any helper
   that turns out to depend on T3 internals moves to the Seam instead — decide per file by
   actual imports, never by copying types into two places.
3. World-rebind lives INSIDE `TimelinePlugin` via resource `RuntimeChanging/Changed` with the
   registration-gated pending pattern (phase-1 proven). No new resident rendezvous contract.
4. Host's `BundleReloadHook` is DELETED entirely in Task 5 (world rebind flows through
   `RuntimeChanged`; verified in the gate via a remote-commanded reload).
5. **(Amended after dispatch round 1, 2026-07-08.)** The "transitional double-compose" invariant
   is WITHDRAWN — it is unsatisfiable once the seam→T3 reference is cut (the seam-resident
   `TimelineComposition` can no longer construct the T3 service; round 1 "solved" this by
   inverting the dependency, a tier violation). Corrected design: `TimelineFace`'s resident
   statics are replaced by a registry-mediated `ITimelineFaceContext` contract (T1) that the
   plugin RegisterOwns and the face resolves at bind time; `DeferredTimelineFace` (pure C#)
   moves into the plugin; resident `TimelineComposition` and the host machinery are deleted on
   the SAME branch (Tasks 2–5 collapse; the branch merges atomically). The statics were the pin
   surface — this design removes it rather than managing it.

**Transitional invariant:** until Task 5, the host STILL boots-composes timeline; the plugin's
composition replaces it (ComposeTimeline-style replace-by-unregister + command Register
replace-by-id). Double composition at boot is wasteful but green and short-lived.

## Pin map (must hold at every commit)

- `_tickChangedHandler` + `_subscribedController` become TimelinePlugin INSTANCE state; TickChanged
  is unsubscribed from the exact controller it was subscribed to (preserve today's
  `_subscribedController` fix) in ShutdownAsync AND on world-rebind severing.
- The three `timeline.*` command closures capture controller+service (bundle-ALC after the move):
  plugin registers them, plugin `Unregister`s all three in ShutdownAsync (WorldPlugin pattern).
- `TimelineFace.ResidentController` (world-ALC object in a resident static): cleared on world
  RuntimeChanging by the plugin, re-set only after the new controller registration exists.
  `ResidentProxy` cleared/unbound on plugin shutdown (its cross target is a documented pin).
- Plugin's subscriptions on the RESIDENT resource service are disposed in ShutdownAsync
  (bundle-handler-on-resident-event is the boom-hud pin class).
- Reload TRIGGER stays resident: `SceneTierPckWatcher` already handles timeline — do not touch.

## Tasks

**Task 1 — contract/helper split** (commit: `refactor(timeline): ITimelineFace + UI models to T1 contracts`)
- Move `ITimelineFace` from `project/plugins/App.Timeline/Providers/ITimelineFace.cs` to
  `project/contracts/App.Timeline/` (namespace unchanged so all consumers compile).
- Move the pure UI helpers TimelineFace consumes (`TimelineScrubCoalescer`, `TimelineModel`,
  `TimelineFilmstrip`, `TimelineTrackLayout`, `LayerTrackGraphProjection`,
  `TimelineTimeFormatter` — verify the list by grepping TimelineFace usings/usages) per
  Decision 2.
- Remove `App.Timeline.Seam`'s ProjectReference to `plugins/App.Timeline`; seam compiles against
  contracts only. `DeferredTimelineFace` keeps implementing the (now-contract) `ITimelineFace`.
- Gate: full suite + host build + `dotnet test project/tests/App.Timeline.Tests/...csproj`.

**Task 2 — plugin-owned composition** (commit: `feat(timeline): TimelinePlugin composes T3 in-bundle`)
- Port `TimelineComposition.ComposeTimeline`'s body into `TimelinePlugin` (instance methods/state,
  `RegisterOwned<FantaSim.App.Timeline.IService>` handle disposed in ShutdownAsync). Controller
  absent at init → compose inert + arm for Task 4's late-bind; log clearly.
- Headless lifecycle tests in `project/tests/App.Timeline.Tests/` mirroring
  `PresentationPluginTests` (factory/registry seams; no Godot calls in tests).
- Resident `TimelineComposition` class stays (host still calls it) — deleted in Task 5.

**Task 3 — command lifecycle** (commit: `feat(timeline): plugin owns timeline.* commands`)
- The plugin registers `timeline.seek` / `timeline.select_layer` / `timeline.toggle_layer` (port
  the closures; they capture plugin-instance service/controller) and `Unregister`s all three in
  ShutdownAsync. Test: after ShutdownAsync, the command service no longer lists them.

**Task 4 — world-rebind protocol** (commit: `feat(timeline): plugin self-manages world reloads`)
- Plugin subscribes resource `RuntimeChanging/Changed`. World changing → sever (unsubscribe
  TickChanged, clear `TimelineFace.ResidentController`, unregister playback via the face path,
  mark pending). World changed → only when `IsLoaded("world")` AND
  `TryGet<ITimelineController>()` is non-null: recompose, rebind face, push current controller
  tick (replaces Host's `SeekAsync` bridge). Subscriptions disposed in ShutdownAsync.
- Tests: pending-flag not consumed while registration absent; sever clears the static.

**Task 5 — host deletion** (commit: `feat(bundles): host sheds timeline T3 (phase 2)`) — SEPARATE
DISPATCH after Tasks 1–4 are reviewed.
- Delete from Host.cs: `using FantaSim.App.Timeline.Seam`, `_timelineReloadPending`, timeline
  branches in both resource-event handlers, boot `ComposeTimeline` call, EnterInitialScenes'
  timeline compose, `HandleTimelineBundleReloaded`, `RebindTimelineFaceAndPushCurrentView`,
  `BundleReloadHook` + `RegisterBundleReloadHook` (Decision 4; `HandleWorldBundleReloaded` keeps
  only `BindPlanetPresentation`).
- Cut `plugins\App.Timeline\App.Timeline.csproj` from complete-app.csproj (transitive-drop check:
  compare host output before/after; pin any non-timeline loss).
- Delete resident `TimelineComposition.cs`. Remove the `("timeline", "FantaSim.App.Timeline.dll")`
  entry from `KNOWN_DUAL_COPIES` in `tools/bundles/stage_bundle.py`; `--check-dual` must pass with
  an EMPTY allowlist.
- Restage timeline (`python3 tools/bundles/stage_bundle.py timeline`); audit additions like
  phase 1 (new non-shared deps of the T3 closure appear — justify each or promote).

**Task 6 — windowed gate (lead session, NOT delegated):** full export → boot sanity → timeline
hot-reload ×2 (`old ALC collected for bundle timeline`, scrub/select/toggle work after) → world
reload (`old ALC collected for bundle world`, timeline stays usable — the plugin's rebind) →
remote-commanded `resource.reload_bundle` for world (proves hook deletion safe) → handover + merge.
