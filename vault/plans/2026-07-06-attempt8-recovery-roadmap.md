# Attempt #8 recovery — roadmap (master plan)

**Date:** 2026-07-06 · **Status:** ACTIVE — user-approved direction ("let's fix attempt #8").
**Constitution:** `vault/architecture/planet-domain-station-map.md` (the station contracts + two gates).
**Success criteria for attempt #8** (what "achieved the goal" means, judged by the user):
1. The **eye gate** passes on the planet views: land vs ocean by tone, mountain chains as lines,
   a round planet (north-star spec) — AND land masses visibly DRIFT with stable shapes,
   collide/merge over the presented window (Scotese feel).
2. The planet reads **through the architecture**: conformance suite green — the domain flows
   S1→S5 with no proxies (the app's features — bundles, 4-tier, ECS, node graph, truth stream —
   are exercised by the planet, not bypassed).

## Product outcomes — what the user SEES (stated 2026-07-06; the real half-year wants)

| # | Want (user's words) | Delivered by | Status today |
|---|---|---|---|
| O1 | "A planet, not another ball with strips" | P2 (several organic landmasses, stable shapes, drifting) + P3 (legible calibrated tones/relief) | ball with strips |
| O2 | "Plates moving by convection" | P2 partially (poles ARE convection-derived, constant from onset) → **P5 fully** (evolving convection drives pole changes + plate birth/split/merge through the truth stream — the locked emergent-tectonics doctrine) | motion exists, invisible until P2; topology static |
| O3 | "Amplification of the focused layer (crust)" | mechanism EXISTS (layer focus → HypsometricTerrain, adaptive subdivision, TectonicDetailSampler) → **P3 makes it READ** on moving data (measured calibration) | works mechanically, reads muddy/faceted |
| O4 | "Debris cloud / fluid sim forming the early planet" | **P6** — pre-onset era visuals: accretion as an R-product particle visualization driven by coarse truth events (NO hand-rolled SPH; GPU particles or an existing library), then magma-ocean → stagnant-lid era looks | parked 2026-07-06, now re-scoped as P6 |
| O5 | "Agent can operate timeline + node graph to construct/reconstruct at a specific tick" | plumbing EXISTS and was proven this session (remote :19292: seek, select layer, screenshot, run_generation_graph) → **P7 completes + gates it** (full vocabulary: play/speed, node-graph construct/edit/run at tick; documented agent API; unattended end-to-end demo) | partial — the recurring "partially most of the time" |

P1–P4 deliver O1 fully, O2/O3 substantially, O5's foundation. **P5–P7 are required for the full
list** — they are IN this roadmap so attempt #8's definition of done is the user's actual list,
not a subset.

**Orchestration model (user-directed):** lead session (Claude) owns design docs, dispatch,
verification, and THIS progress log. Implementation packets go to external CLIs per
`.agent/skills/04-tooling/external-agent-delegation` once each phase's plan is complete.
Every packet is lead-verified against BOTH gates before its phase advances.

## Phases

| Phase | What | Plan doc | Dispatch |
|---|---|---|---|
| **P0** | Housekeeping: wire remotes, push current state | (none — done inline) | lead |
| **P1** | Conformance gates — mechanical architecture tests | `2026-07-06-p1-conformance-gates.md` | opencode/kimi |
| **P2** | Continents through the stations: organic patch seeding (S1/S2), moving-frame evaluation (S3/S4), fraction-driven Continents view + proxy retirement (S5) | `2026-07-06-p2-continents-through-stations.md` | split: engine packet + app packet |
| **P3** | Calibration by measurement: elevation histogram tool, ramp band fitting, tessellation/jaggies decision, north-star look pass on MOVING data | planned after P2 lands (needs P2's data) | TBD |
| **P4** | Motion character (rates/axis coherence to Scotese pace) + Play sweep demo + mid-arc verdict | planned after P3 | TBD |
| **P5** | Plates moving BY convection, fully: time-varying convection field → evolving Euler poles + plate birth/split/merge as truth events (emergent-tectonics doctrine, 2026-06-22 event-sourced-plate-topology plan is the seed) | planned after P4 | TBD |
| **P6** | Early-planet era (O4): accretion debris/particle visualization as an R-product over coarse truth events (mass growth, moon-forming impact; no hand-rolled SPH), era-correct magma-ocean/stagnant-lid looks, scrub t=0→onset | planned after P4 (parallel-capable with P5) | TBD |
| **P7** | Agent operability surface (O5): complete ingress vocabulary (play/speed, node-graph construct/edit/run at tick, tick-addressed reconstruct), documented agent API; **gate = the attempt-8 closing demo: an unattended agent builds a world via node graph, scrubs accretion→drift, focuses crust amplification, captures a gallery** | planned after P5/P6 | TBD |

Ordering rationale: gates first (P1) so every later packet is policed; the domain fix (P2) next
because P3's calibration is meaningless on frozen/blob data; character tuning (P4) last because
it needs P2's stable shapes and P3's legible tones to judge.

## Standing rules for every phase (from the restart handover §6)

1. Both gates per arc — conformance suite + scripted eye gate; evidence in the phase log below.
2. Prior-attempt audit before proposing anything new (circle map memory + `git log --grep`).
3. One canonical continent field; render proxies banned (P1 enforces).
4. Motion visible in the first screenshot pair of any new/reworked pipeline.
5. Look-iteration budget respected; defer anything that pushes a look change past ~2 min
   (hot-reload the world bundle where possible — the bundle system is a feature, USE it).
6. Calibration by measured histogram, not eye-tuning.
7. Settled decisions require explicit user unlock to revisit (waterless lock `e3b84ef`,
   plate count emergent, S2-indexing-only, …).

## Progress log (lead session keeps this current — the "keep watching" instrument)

- **2026-07-06 P0 DONE:** remotes wired; engine+carto pushed to `attempt-8/main` (previous
  attempt preserved on `main`; histories unrelated — local repos are attempt #8); app main
  pushed through `8c3c6da`. Evidence: `git ls-remote` shows attempt-8/main on both.
- **2026-07-06 P1 DONE (@2c53650):** App.Architecture.Tests with C1–C5 all green; ONE live
  violation found+fixed (C3: parameterless `GetPlanetPresentationAsync()` in `Rebind()` — the
  frozen-onset-frame entry point — now tick-addressed). Dispatched to opencode/kimi,
  lead-verified: `task test` green (449/449 App.World isolated; frame-budget test flakes under
  full parallel load — KNOWN FLAKE, fix in P3). Conformance gate is now LIVE for all later work.
- **2026-07-06 session-goal-contract rule added** (workspace `.agent/rules/`, indexed in
  AGENTS.md) — the institutional fix for why hundreds of goal-pointed sessions didn't compound.
- *(next entries appended here as packets land, with gate evidence per entry)*
