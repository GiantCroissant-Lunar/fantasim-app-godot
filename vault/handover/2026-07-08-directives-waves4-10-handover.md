# 2026-07-08 session handover — directive waves 4–10 (input parity → mantle layer → stacked layers → filmstrip timeline)

**For the next session (fresh context). READ ORDER:** this doc →
`vault/specs/2026-07-07-layer-presentation-input-parity-canonical-units-directives.md`
(D1–D8c: every user directive of this arc, verbatim intent + grounding + what shipped; the
ADDENDA D5–D8c at the bottom are the live frontier) → `vault/plans/2026-07-08-wave5-layer-presentation-plan.md`
and `vault/specs/2026-07-08-track-filmstrip-design.md` + `2026-07-08-track-embedded-layer-graphs-design.md`.
Prior context: `vault/handover/2026-07-07-gplates-mantle-look-arc-handover.md`.

**Standing user rules:** provider routing is the USER's call per dispatch ("codex cli gpt 5.5
high" = `codex exec -m gpt-5.5 -c 'model_reasoning_effort="high"' -s workspace-write`; "zai" =
opencode `zai-coding-plan/glm-5.2`, 5h rolling quota, silent-hang on exhaustion — watch log
mtime). **D2.3 doctrine: any "user can X" claim is gated on REAL mouse input (computer-use),
never ingress-only.** Look changes are eye-judged — lead's eye first as demanding critic, the
user's eye final.

## 1. What landed (ALL pushed, app main; suite green at every merge)

| Wave | What |
|---|---|
| 4 | **Input parity root-caused (3 stacked defects):** pcam ThirdPerson config must apply BEFORE AddChild (`_ready` w/ follow_mode=NONE never builds SpringArm); `UiRoot` full-rect container at default MouseFilter=Stop ate EVERY press app-wide (→ Ignore); held-button motion never reaches `_UnhandledInput` → drag tracked in `_Input` (GlobeOrbitControls). `camera.debug` ingress command (rig state, input counters, `probeX/probeY` control-at-point probe — how UiRoot was caught). **Rate calibration** 0.02→0.0035 rad/anchor (OnsetRoster) — NOTE: the constant feeds ConvectionFieldConfig → upwellings moved → the lid RE-FRACTURED (different seed world); six geometry-pinned tests honestly re-pinned (`f530c24`); kind vocabulary emerges ~5.7× later (+100 anchor units). **D4.1** Ma leak fixed (durationTicks/durationLabel/timeScale{rung,ticksPerRung} + guard test). |
| 5 | **D1** `geosphere.mantle` layer → MantleInterior (M-A interior + M-B separated slabs, NO ghost shell) via `MantleInteriorViewComposer`; slab ×4 double-scale caught windowed (children pre-scaled ×2 + composer ×2 → slabRoot 0.5 compensation; unify scaling conventions = follow-up). **D3** `RadialSectionProfile` (crust exag 8 → 0.038R walls; displayed crust:mantle ratio 0.0837 PINNED by test). **D2.2** ruler click/drag + visible playhead handle; the REAL root of "timeline cannot be adjusted": face-initiated seeks never echoed to the face UI (UpdateUI renders `_lastViewSnapshot`; only ingress round-trips one) — SeekTo now moves the snapshot tick. Ruler is GUI-unreachable (LanesList bleeds 37px over it) → scrub input at the face root. |
| 6 | **D5** stacked ACTIVE SET (toggle semantics, `timeline.toggle_layer`, multi-highlight; combo rules in `LayerCompositionDecision*` — Mantle⇒interior+slabs geometry; Plate⇒identity coloring over Crust⇒terrain; Mantle+Crust windowed-verified: terrain-topped slabs). **D6** playhead line grabbable full-height (8px zone, `_Input` capture) + cursor-centered wheel time-zoom (pure math tested). |
| 7 | **D7c first slice:** each track's content = its layer's NODE GRAPH (compact chip strips; chevron-expanded 200px row hosts real GraphEdit filtered to the layer, reusing BoomHud binder + MSAGL). |
| 8 | **D8 smooth scrub:** per-frame coalescing (`TimelineScrubCoalescer`), `TimelineTickOrigin` Standard/ScrubPreview/ScrubCommit through PushTick, previews suppress TickChanged (no crust gen per motion), `ScrubApplyScheduler` ~300ms rest debounce. Verified: 5 heavy refreshes for a 94-boundary sweep (was 1-per-motion). |
| 9+10 | **D8c filmstrips:** compact track content = low-res IMAGE frames (96×48 equirect from LOW-freq data — frequency-aware crust product cache; `LayerFilmstripPreview` API on world IService). Refined: full-range strips, nearest-playhead-first under 3-in-flight throttle w/ generation supersession, zoom-reactive re-plan reusing cache, REAL mantle frames (one 0.75R shell sample of MantleAnomalyField, cold/warm palette). Windowed-verified: plates visibly drift frame-to-frame, continents evolve, mantle field structure moves. |

## 2. IMMEDIATE NEXT (the locked frontier, in order)

1. **D8b progressive resolution** (directive locked in spec): scrub at LOW tessellation rung
   (freq 2–3 generation follows the hand), at rest climb the rung ladder to full (web-image
   style; cancel climb on new scrub). Replaces debounce-only D8. Reuse the existing
   freq/adaptive-subdivision ladder — no parallel LOD system. Also: a few heavies still fire
   MID-drag (300ms rest timer vs drag pauses) — knob in `ScrubApplyScheduler`.
2. **D5 full + D7b compose-node arc** (design round first): dissolve derived GlobeViewMode into
   per-layer contributions; composition rules become GRAPH NODES (AnimationTree blend-tree
   semantics — see D7c-corrected research section in the directives spec) surfaced in the
   track-embedded graphs. The `LayerCompositionDecision` table is the interim hardcoding.
3. **D4.2 vocabulary sweep** (Ma/Ga identifiers/comments; engine rename = coordinated repack) +
   **D4.3** CLU/CMU wiring (spatial quantities to canonical display at user-facing surfaces).
4. Mantle look ledger: plume columns still MISSING (eye criterion 2), per-tick interior
   resample while layer active, wall lighting, dark-core readability.
5. Cleanup: ~20 integrated worktrees under `yokan-projects/.worktrees/` (all merged; remove
   after confirming); `PendingConfigurationById` helper may be dead post-wave-4; scaling-
   convention unification (piece builders emit unit scale; only composition roots scale).

## 3. Gotchas NEW this session (G17–G25; 2026-07-06/07 G1–G16 still stand)

- **G17 macOS `seq` emits scientific notation** (1e+08) at tick magnitudes → `{"tick":1e+08}`
  silently rejected by ingress → 41 byte-identical sweep frames TWICE. Use bash arithmetic
  `for ((t=...))`, and verify sweeps by per-frame md5 uniqueness.
- **G18 seek→visible-rebind needs ≥4s pacing** (freq 4): faster barrages leave the rendered
  frame FROZEN for the whole sweep. (D8's origin split helps; full fix = D8b.)
- **G19 wheel events bubble through MouseFilter.Stop controls** — a working wheel proves
  NOTHING about presses reaching a handler.
- **G20 held-button mouse MOTION never reaches `_UnhandledInput`/gui_input reliably** — capture
  drags at `_Input` gated by a press-set flag (pattern now in GlobeOrbitControls AND TimelineFace).
- **G21 full-rect containers with default MouseFilter=Stop eat the app's input** (UiRoot).
  `camera.debug {"probeX","probeY"}` lists every Control that can consume a point — use it.
- **G22 compositor shows STALE frames during rapid synthetic input** — mid-drag zooms lie;
  verify by LOG evidence, re-capture seconds later.
- **G23 codex offline verification is UNRELIABLE**: stale-assets test runs masked compile
  errors 3×; builds fully blocked 2×. Lead ALWAYS rebuilds+retests with network before merge.
  Also: codex cannot commit in worktrees (shared .git index lock) — lead commits.
- **G24 frames/nodes built PRE-ATTACH**: capture node REFERENCES, never NodePaths (`GetPath()`
  errors before the row enters the tree); guard applies with IsInstanceValid+IsInsideTree.
- **G25 `grep -c` exit-1 broke `&&` chains twice more** (G10 recurrence) — never gate chains on
  count-greps; cwd drift (G9) also recurred in a background export (task ran at workspace root).

## 4. Drive recipes (current)

- Launch: `cd fantasim-app-godot && remote__enabled=true [world__showGraph=true] build/_artifacts/0.1.2/godot/osx/complete-app.app/Contents/MacOS/complete-app`
- `camera.debug '{}'` (rig+input counters) / `'{"probeX":N,"probeY":N}'` (controls at point,
  window px). `timeline.toggle_layer {"sphereId":"geosphere","layerId":"geosphere.mantle"}`.
- Real-mouse gates via computer-use: window maps screen→window px at scale ~1/0.69 with the
  window extending past the screen's right edge; ruler band ≈ screen y 570–585.
- Sweep GIF: integer-tick loop, sleep ≥4s, md5-uniqueness check, crop 1000x1000+1600+460.
- Mantle isosurface build after layer toggle: ~60–75s at grid 88 before screenshotting.

## 5. State

App main pushed through the filmstrip-refinement merge; 17-project suite green at every
integration. App.World.Tests ~1-in-3 single-test transient — a spawned chip session is hunting
it independently. Wave-4 run-1 camera boot anomaly unreproduced across 5+ boots (watch item).
No agents running at session end. Memory `fantasim-layer-presentation-directives` is the
resume pointer (D1–D8c status + this handover).
