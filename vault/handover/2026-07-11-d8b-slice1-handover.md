# 2026-07-11 session handover — D8b slice 1 SHIPPED (progressive-resolution scrub)

**For the next session.** Read order: this doc →
[`../plans/2026-07-11-d8b-progressive-resolution-slice1-plan.md`](../plans/2026-07-11-d8b-progressive-resolution-slice1-plan.md)
(executed) → [`2026-07-11-binder-split-handover.md`](2026-07-11-binder-split-handover.md)
(same-day precursor; G31/G32 stand).

## 1. What landed (all on main, suite 1110/1110)

| Commit | What |
|---|---|
| `4e13c27`/`5c9f619`/`ddedf7d` | Binder split arc (see its handover) |
| `825461b` | TimelineFace disposed-throw silenced (chip session; lead-verified + pushed) |
| (plan) | D8b slice-1 plan |
| `eeed4bc` | **D8b slice 1** — T1 `GetPlanetPresentationAsync(tick, frequency)` default-method overload (SeekTo-pattern), Service clamp [2, configured default] via `GetPlanetPresentationAsyncCore` (crust-product + reconstructor caches were ALREADY freq-keyed), `ScrubRefreshCoordinator` rung policy (LowRung=2 immediate on boundary-crossing preview, climb Mid=3→full at rest, cancel on new scrub), binder `_pendingFrequencyOverride` last-writer-wins threading, `frequency=` in the bind log. Implemented by **opencode `ollama/glm-5.2:cloud`** (user-routed); lead fixes in the same commit: refresh-echo suppression (`_applyingRefreshedDocument`), first climb step AT rest flush, climb token captured before callback (use-after-dispose caught by the new regression test). |
| (fix round) | **Origin delivery + echo policy** — remote `timeline.seek` pushed origin only `if (controller.Tick != tick)` AFTER `SeekAsync`, whose face echo applies the tick as Standard first → origins NEVER reached scrub policy (racy by construction; flipped sides between builds). Fix: push origin FIRST + unconditionally. Coordinator: **Standard/no-heavy = scrub-state NO-OP** (every refresh apply echoes such a tick — cancelling wiped the pending rest after each low-rung refresh). Binder gen-changed subscription skips while `IsScrubActive` (a scrub's own low-rung generation completion must not chase a full fetch mid-drag). |

GLM dispatch notes: faithful, zero out-of-scope edits (G29 didn't recur), and it correctly
no-opped plan Task 4 — drag origins already ship via `TimelineScrubCoalescer` →
`ApplyScrubAction` → `PushTick(tick, action.Origin)` (TimelineFace.cs:768); the plan's SeekTo
grounding fact was wrong. `RegisterPlayback` onSeek widening remains unneeded (no consumer).

## 2. Gate evidence (`../specs/evidence/2026-07-11-d8b-slice1-gate/`)

Fresh-boot exported app, fixed bundles hot-reloaded (`old ALC collected` for world + timeline):

- **Log signature (gate-log-excerpt.txt):** per boundary-crossing preview: one cold freq-2
  generation + exactly one `BIND freq=2` (~1,400 tris vs ~12,000 full); at rest `BIND freq=3`
  (4,590) → `BIND freq=4` (12,290); commit → straight to full; re-scrub over warm rungs =
  freq-2 binds with **zero** generation lines (freq-keyed cache).
- **5-preview burst wall-clock: 9.67 s → 1.14 s** (pre-fix vs post-fix, same sweep — the
  pre-fix number was full-frequency generations stalling the main thread per preview).
- **Screenshots:** `d8b2-lowres.png` (soft low-tessellation planet mid-scrub, playhead 6 kb)
  vs `d8b2-climbed.png` (same tick, sharpened after climb) — the web-image effect.
- Suite 1110/1110 (lead-run). Diff scope: contracts/IService.cs + Service.cs +
  App.Presentation + TimelinePlugin.cs + tests.

**USER'S EYE still owed:** the scrub *feel* under real mouse drag (D2 doctrine). The coalescer
input path is unit-tested and shared downstream with the proven remote path, but the
hand-feel/look verdict is the user's.

## 3. Diagnosis lessons (add to the G-ledger reading list)

- **G33 the remote origin race:** `timeline.seek` = `SeekAsync` (service → face.SeekTo →
  `PushTick(tick)` STANDARD, synchronous) + guarded origin push. Which side wins is
  build/timing-dependent — yesterday's discrimination test passed on the OTHER side of the
  race. Any origin-dependent gate must verify the origin actually ARRIVED (bind frequency,
  not just debounce timing).
- **G34 refresh echoes are everywhere:** document apply echoes a Standard/no-heavy tick
  (face SeekTo echo AND `PlanetTimelineController.UpdateFrom → PushTick`); generation
  completion echoes a deferred no-arg `ScheduleRegimeRefresh()`. Any policy keyed on
  incoming ticks must decide explicitly what an echo means, or it will cancel itself.
  Current policy: echo-shaped ticks are scrub-neutral; heavy ones still count.
- The pre-existing **double full bind** per standard/commit refresh (gen-changed chase,
  visible as `BIND freq=4 ×2`) survives OUTSIDE scrubs — benign but wasteful; candidate
  cleanup, do NOT fold into a scrub arc.

## 4. Follow-ups (ranked)

1. **User eye-judgment session** on scrub feel (real mouse drag; fresh boot per G32).
   Possible tuning knobs already in place: rest delay (300 ms), rung ladder constants.
2. **D8b residue:** mantle x-ray / mantle-layer views ignore rungs (rebuild at full on every
   entry — fine, they're not scrub-coupled); filmstrip previews already low-res via ViewRung
   (directive 3 satisfied pre-slice).
3. Double-full-bind cleanup (G34 note above).
4. Dead-code sweep from the split review (`PlanetShaderLibrary.HypsoPlateMaterial`,
   `PlateSurfaceMeshFactory.ToColor`/`.ToV3` orphans).
5. Carried: D4.2 unit sweep, polarity flip, TimelineFace split, compose-json (locked, build at
   first consumer), SurrealDB slice, vault README index.

## 4b. USER DECISIONS (2026-07-11, this session — close the two parked intents)

- **Spin rate = ADJUSTABLE PROPERTY, not a constant.** Resolves the 0.02-sibling straggler AND
  parameter-audit finding "spinRateRadiansPerMegaAnnum is roster-bypassed (placebo)": wire ONE
  real spin-rate parameter end-to-end — graph knob (WorldGenerationNodeCatalog) → WorldCrustRunSpec
  → OnsetRoster → AND GlobeReconstructor (delete its legacy `SpinRatePerMegaAnnum = 0.02` const;
  consume the property). Default = calibrated 0.0035 (OnsetRoster.DefaultAngularDriftPerMegaAnnum,
  tools/rates/2026-07-07-rate-calibration-report.md). Chip spawned for the wiring slice.
- **Mixed-frame residue = DEFERRED deliberately** (do NOT "fix" it in a cleanup pass). Rationale:
  the timeline will carry ASYMMETRIC per-layer time scales — plate movement at ~ka canonical
  cadence, later layers (e.g. rivers) at ~jw scale — so frame semantics get redesigned when
  per-layer cadences land (ties into the track registry / dual-time-base tunnel direction).
  Until then the current split stands: elevations/fractions smooth at playhead,
  thickness/sections at snapshot frame (≤ one snapshot-spacing lag).

## 5. State at session end

Exported app RUNNING with all D8b code live (hot-reloaded post-fix): log
`/private/tmp/claude-501/-Users-apprenticegc-Work-lunar-horse/ccc76c6b-c659-48b1-892d-28e75e808d7c/scratchpad/d8b-gate-run.log`,
ingress :19292. Note the app binary predates `eeed4bc`'s RESIDENT portions? No — full
`build:godot:desktop` ran AT `eeed4bc` (T1 overload is in the resident contracts) and the fix
round touched only collectible bundles (hot-reloaded). Snapshot series carries gate-sweep
snapshots at 100M-multiples (G32) — fresh boot before eye-judged look work.
