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
| **P4** | Motion character (rates/axis coherence to Scotese pace) + Play sweep demo + attempt-#8 verdict against the success criteria | planned after P3 | TBD |

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
- **2026-07-06 P1 planned + dispatched:** see plan doc; packet to opencode/kimi.
- *(next entries appended here as packets land, with gate evidence per entry)*
