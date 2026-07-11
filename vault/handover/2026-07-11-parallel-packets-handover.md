# 2026-07-11 session handover — parallel packets: TimelineFace split + D4.2 unit sweep

**For the next session.** Read order: this doc →
[`2026-07-11-d8b-slice1-handover.md`](2026-07-11-d8b-slice1-handover.md) (same-day precursor:
D8b shipped, G33/G34) → the two plans
([TimelineFace](../plans/2026-07-11-timelineface-split-plan.md),
[D4.2](../plans/2026-07-11-d42-world-unit-sweep-plan.md), both executed).

## 1. What landed (all on main; consolidated suite green; single full-export gate)

| Commit | What |
|---|---|
| `048b9cc` | **TimelineFace split** — core 1,882 → 774; `FilmstripPreviewController` (execution-time provider resolve, 825461b disposed guard preserved), Godot-free `FilmstripCacheLedger` (8 TDD tests), `.Input.cs`/`.Lanes.cs` partials. Tunnel-timeline precondition DONE. |
| `42974db` | **D4.2 mechanical sweep** — Ma wire keys retired with LOUD ArgumentException naming the canonical key; per-tick defaults (numerically identical); rung-vocab comments; spin-rate rad/Ma vocabulary EXEMPT per user decision. Zero shipped json used retired keys; classification table was in AGENT-SUMMARY-d42 (folded here: 28 source hits — 11 wire-key, 0 identifier beyond task 1, 7 comment, 2 value, 8 exempt/false-positive; 0 FLAGGED). |
| `8f5f4d3` | **D4.2 value rescale (isolated)** — `MobilePlateWindowTicks` 1B → 100M ("1 Gy" intent), `PlateFeatureFadeInTicks` 5M → 500k ("~5 Ma"). **MaxTick = onset+window: ~11 kb → 2 kb.** |

Both packets implemented by **opencode `ollama/glm-5.2:cloud` IN PARALLEL** — packet 2 ran in a
dedicated git worktree (`.worktrees/fantasim-d42-sweep`, since removed) to avoid concurrent
`dotnet` build-lock collisions on the shared sln; lead cherry-picked its commits onto main.
This worktree-per-packet pattern worked cleanly — reuse it for parallel dispatches.

## 2. Consolidated gate (fresh full export — D4.2 touched T1 contracts)

Evidence: `../specs/evidence/2026-07-11-parallel-packets-gate/`.

- Suite green on the merged tree (lead-run; 1124 timeline-side + 1126 world-side pre-merge,
  combined re-run 0 failures).
- Fresh boot: rescaled run renders coherently — ruler 0→2 kb, playhead 1.90 kb, stagnant-lid ≈
  0–1 kb / mobile-plate ≈ 1–2 kb, filmstrips populate through the SPLIT TimelineFace
  (`combined-rescaled.png`); world look at a standard seek unchanged (`combined-baseline.png`).
- D8b machinery intact post-merge at the new scale: freq=2 preview binds, freq=3 climb steps,
  commit → freq=4 (log; burst pacing artifacts per G31 — the pre-merge discrimination test
  remains the semantic proof).
- Hot-reload round: `old ALC collected` world ×1 + timeline ×2, **0 ObjectDisposedException**
  (825461b guard survived the controller move), 0 pins.

## 3. USER EYE-JUDGMENT NOW OWED (two items, one fresh boot)

1. **D8b scrub feel** (real mouse drag on the timeline handle) — carried from the morning.
2. **The rescaled 2 kb run** (`8f5f4d3`): stated intent "1 Gy" was honored per the user's
   re-derive directive, but weeks of look work happened on the 10-Gy-equivalent span. If the
   eye says the short run is wrong, the revert is ONE constant (`MobilePlateWindowTicks`) —
   or the real fix may be raising the INTENT (e.g. 4 Gy Earth-like) rather than reverting the
   unit math. Regime proportions changed too (stagnant-lid now half the strip).

## 4. Gotchas / notes

- **G35 drive recipes rescaled**: all pre-07-11 gate fixtures used ticks up to 700M — beyond
  the new 200M MaxTick (clamped). In-span sweep now = previews ≤190M; kb scale unchanged
  (1 kb = 100M ticks).
- G28 cwd drift bit twice more this session (stray `vault/` tree at the workspace root —
  moved+cleaned; `task` invoked from wrong dir). ABSOLUTE `cd` prefix on every repo command.
- TimelineFace test csproj uses explicit `<Compile Include>` for plugin sources — adding a
  Godot-free seam class to tests requires that one-line include (TimelineScrubMapper pattern).

## 5. Follow-ups (ranked)

1. The two eye-judgment items above (§3) — everything else is unblocked regardless.
2. Tunnel-timeline / D5-D7b compose arc: BOTH preconditions now exist (track registry 07-10,
   TimelineFace view/behavior separation 07-11). Next design conversation can start.
3. Double-full-bind cleanup (G34, pre-existing).
4. Dead-code sweep from the binder-split review (PlanetShaderLibrary.HypsoPlateMaterial,
   PlateSurfaceMeshFactory.ToColor/.ToV3 orphans) + polarity flip (6/7 decisions) + docs pile
   (vault README index, CHANGELOG, worktree/artifact pruning).

## 6. State at session end

Exported app RUNNING (fresh, all packets live incl. hot-reloaded bundles): log
`/private/tmp/claude-501/-Users-apprenticegc-Work-lunar-horse/ccc76c6b-c659-48b1-892d-28e75e808d7c/scratchpad/combined-gate-run.log`,
ingress :19292 — ready for the §3 eye session as-is (fresh boot, clean snapshot series at the
new scale). Main pushed through this handover's commit.
