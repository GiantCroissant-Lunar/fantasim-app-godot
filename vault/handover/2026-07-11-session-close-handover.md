# 2026-07-11 session-close handover — the four-arc day

**For the next session (fresh context). READ ORDER:** this doc → per-arc detail only as needed:
[`2026-07-11-parallel-packets-handover.md`](2026-07-11-parallel-packets-handover.md)
(TimelineFace split + D4.2 sweep + rescale) →
[`2026-07-11-d8b-slice1-handover.md`](2026-07-11-d8b-slice1-handover.md) (D8b + G33/G34) →
[`2026-07-11-binder-split-handover.md`](2026-07-11-binder-split-handover.md) (G31/G32).
Prior context: `2026-07-10-review-and-track-registry-slice1-handover.md` (G26–G30, standing
user rules — all still binding).

## 1. Day ledger (ALL pushed, main @ `cce5100`, tree clean, suite green at every commit)

| Arc | Commits | One-liner |
|---|---|---|
| Binder split | `4e13c27` `5c9f619` `ddedf7d` | PlanetPresentationBinder 2,636→749; ScrubRefreshCoordinator + shader library + mesh factory + 4 partials; gated (ALC ×2, visual, scrub-discrimination) |
| Disposed-throw chip | `825461b` | TimelineFace late filmstrip apply exits silently (chip session; lead-verified) |
| **D8b slice 1** | `154b2b4` `eeed4bc` `09b2b1b` `3a39113` | **Progressive-resolution scrub SHIPPED**: freq-2 binds follow the hand, 3→full climb at rest, commit→full; burst 9.67s→1.14s; origin-race (G33) + refresh-echo policy (G34) fixed |
| Decisions | `d3815e9` | Spin-rate = adjustable property (executed same day); mixed-frame DEFERRED (asymmetric per-layer time: plates ~ka, rivers ~jw — binds tunnel dual-time-base design) |
| Spin-rate knob | `8e10a68` `d71d8f4` | ONE property end-to-end, legacy 0.02 const deleted, audit #3 resolved (separate chip session) |
| TimelineFace split | `65a1fc4` `048b9cc` | Core 1,882→774; FilmstripPreviewController + FilmstripCacheLedger + Input/Lanes partials; **tunnel precondition DONE**; 0 disposed-throws through reload |
| D4.2 unit sweep | `4db2c44` `42974db` `8f5f4d3` | Ma wire keys retired loudly; per-tick defaults; **values re-derived: MaxTick ~11 kb → 2 kb** (isolated commit `8f5f4d3`, one-constant revert if the eye rejects it) |
| Session close | `cce5100` + this doc | Handovers + vendored evidence |
| **Evening session (after this doc's first commit)** | `c7530e4`..`0b17dc9` | **Cleanup pile DONE**: double-full-bind FIXED @`8f28cdd` (`PlanetSurfaceBindStamp` content-identity dedupe of the generation-completion chase; gated: fresh-window seeks = 1 bind + "re-bind skipped", ALC 7→14, suite 1146/1146); dead-code orphans removed @`9509e53`; vault README delta @`7f4c4be`; CHANGELOG regenerated @`c7530e4` (cliff.toml footer + limit_commits bugs fixed); 22 worktrees pruned (dirty diffs archived `~/Work/lunar-horse/.agent/run/worktree-prune-archive-20260711/`); artifacts 6.2 G→621 M. **Ollama Cloud WEEKLY QUOTA exhausted mid-arc** — both GLM dispatches stalled silently ~75 min (frozen logs); lead implemented in-session. |

Suite: 1091 → **1132-ish combined** (verify with one run; last full re-run green post-merge).
Every arc windowed-gated; evidence under `vault/specs/evidence/2026-07-11-*/`.

## 2. FIRST THING NEXT SESSION — two USER eye-judgments, one sitting

An exported app may still be running (ingress :19292); if not:
`remote__enabled=true <repo>/build/_artifacts/0.1.2/godot/osx/complete-app.app/Contents/MacOS/complete-app`
(ABSOLUTE path, fresh boot is fine — snapshot series is clean at the new scale).

1. **D8b scrub feel** — real mouse drag on the timeline handle: planet follows at low res,
   sharpens ~300 ms after rest, full on release. Tuning knobs if off: rest delay (300 ms,
   binder ctor), rung consts (ScrubRefreshCoordinator.LowRung/MidRung).
2. **The rescaled 2 kb run** (`8f5f4d3`) — stated intent "1 Gy" honored per user directive,
   but all prior look work happened on the 10-Gy span. Options if wrong: revert ONE constant
   (`Service.MobilePlateWindowTicks`), or raise the INTENT (e.g. 4 Gy Earth-like) now that the
   unit math is honest. Regime proportions changed (stagnant-lid ≈ half the strip).

## 3. Frontier after the eye session (ranked)

1. **Tunnel-timeline / D5–D7b compose arc — design round with the user.** BOTH preconditions
   now exist (track registry 07-10, TimelineFace view/behavior separation 07-11). Inputs:
   user's claude-design export (ref-projects/, gitignored), locked corrections (canonical
   units + odometer, RING control for huge time scaling, add/remove first-class), compose-json
   direction (locked: per-sphere json, domain vocabulary, build at first consumer), and the
   NEW asymmetric per-layer time directive (plates ~ka, rivers ~jw — dual time base is core).
2. Polarity flip — 6 of 7 assembly decisions are the USER's; then policy-json + gate.
3. ~~Cleanup pile~~ DONE by the evening session (see ledger) — small residuals only:
   `LayerCompositionDecision.TerrainRelief` computed-never-consumed (fold into D5/D7b),
   `RegisterPlayback` onSeek still `Action<long>` (widen only when a consumer needs origin),
   pre-commit dotnet-format covers host only.
4. SurrealDB first slice (crust+filmstrip persistence; crust cache still Seed-mis-keyed).

**Routing note for dispatches:** Ollama Cloud weekly quota is EXHAUSTED as of 07-11 evening —
`ollama/*:cloud` dispatches stall silently with frozen logs (~75 min observed). Until reset,
route external work to `zai-coding-plan/glm-5.2` or the in-house Agent tool, per the user's
call; watch the first 10 minutes of any Ollama dispatch for log growth before trusting it.

## 4. Process notes that worked today (reuse)

- **Plan → GLM dispatch → lead gate** loop: 4 arcs through `opencode run --model
  ollama/glm-5.2:cloud` (user-routed), zero out-of-scope edits all day. Non-trivial prompts
  staged under `.agent/run/dispatch/`; logs under `.agent/logs/opencode/`.
- **Worktree-per-packet for parallel dispatches** (avoids concurrent dotnet build locks on the
  shared sln); lead cherry-picks onto main.
- **Multiset line-diff verbatim audit** for split refactors (sort/comm of trimmed lines,
  original vs union-of-new; residue must be only planned substitutions).
- Lead ALWAYS re-runs the suite (G23) and verifies "already implemented / mirrors X" claims
  against source (G30) — both caught real issues today (racy origin delivery, echo cancels).

## 5. Gotcha ledger delta (G31–G35; G1–G30 stand)

- **G31** remote scrub fixtures lie (spawn latency > rest window; tick scale) — use the
  discrimination test, not raw sweep counts.
- **G32** out-of-range seeks pollute the crust snapshot series — fresh boot for eye work.
- **G33** timeline.seek origin race — FIXED (push origin first, unconditional), but the lesson
  stands: origin-dependent gates must verify arrival (bind frequency), not timing.
- **G34** refresh echoes everywhere (face SeekTo echo, UpdateFrom→PushTick, gen-changed
  deferred refresh) — tick-keyed policies must define echo semantics explicitly. Current:
  Standard/no-heavy = scrub-neutral; gen-changed suppressed while IsScrubActive.
- **G35** drive-recipe ticks rescaled: MaxTick now 200M (2 kb); pre-07-11 fixtures used up to
  700M — they clamp now. 1 kb = 100M ticks unchanged.
- G28 (cwd drift) bit 3× more today — absolute `cd` prefix on EVERY repo command, no exceptions.

## 6. Standing user rules (restated, binding)

Provider routing per dispatch is the USER's call ("zai" = opencode zai-coding-plan/glm-5.2,
"ollama cloud" = ollama/glm-5.2:cloud, "sub agent (sonnet 5)" = in-house Agent tool; codex
last resort). Look changes are eye-judged. Real-mouse doctrine for "user can X" claims.
Canonical ticks + odometer everywhere; Ma/Ga only at import bridges (spin-rate rad/Ma authoring
vocabulary is the one declared exception). Never create repos/projects/packages without asking.
External agents never commit — lead reviews by artifacts, commits, runs every gate.
