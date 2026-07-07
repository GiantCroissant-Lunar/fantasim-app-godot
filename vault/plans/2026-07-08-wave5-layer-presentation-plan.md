# Wave-5 plan — layer presentation directives (D1/D3) + timeline UX (D2.2)

**Status: DISPATCHED 2026-07-08.** Executes the directive spec
`vault/specs/2026-07-07-layer-presentation-input-parity-canonical-units-directives.md`
(D1, D2.2, D3) after wave 4 landed input parity (real mouse rotate/zoom, `0d3a378`),
the rate calibration (`e3a0f81` + test re-pins `f530c24`), and the D4.1 Ma-leak fix (`7c79678`).

## Packets

| Packet | Branch | Agent | Scope |
|---|---|---|---|
| W5-A mantle layer + section profile | `wt/2026-07-08-mantle-layer` | opencode zai-coding-plan/glm-5.2 | Stage 1 (D3): `RadialSectionProfile` — one radial source of truth (crust 30 km / lid 90 km / CMB 0.55R), knobs `CrustThicknessExaggeration` (default 8) + `MantleDepthScale` (default 1), ratio-lock pin test; PlateSolidBuilder thickness decoupled from surface relief exaggeration; core-sphere radius from profile. Stage 2 (D1): `geosphere.mantle` layer id → `GlobeViewMode.MantleInterior` at mobile-plate; timeline Mantle track; presentation = M-A interior + M-B separated slabs (explode factor 0.4, NO ghost shell — slabs are the reference frame) via a new focused `MantleInteriorViewComposer` (binder stays thin). |
| W5-B timeline handle | `wt/2026-07-08-timeline-ux` | codex gpt-5.5 high | D2.2: clickable + draggable ruler (reusing HandleScrub mapping), visible grabbable playhead handle, nothing existing breaks. |

Prompts staged in `.agent/run/dispatch/2026-07-08-*.txt` (session-local).

## Acceptance

- W5-A: `task test` green; windowed (lead): select the Mantle track → interior + detached
  thick slabs still reading as a sphere; slab walls visibly thick (≥0.03R at defaults); switch
  back to Crust restores; scrub shows slabs deepening + interior evolving. Eye-judged (mine as
  demanding critic, the user's final). Ratio-lock test pins displayed crust:mantle proportion.
- W5-B: windowed with REAL mouse (D2.3 doctrine): click ruler seeks, drag ruler scrubs, handle
  grabs and drags; band/track/Play buttons unaffected.

## Deferred (explicitly out of this wave)

- D4.2 vocabulary sweep + D4.3 CLU/CMU wiring (own packets; engine-repo API rename requires a
  coordinated repack — do not run concurrently with W5-A's App.World edits).
- BoundarySectionBuilder/cutaway strata consuming the RadialSectionProfile (noted follow-up).
- Rate-distribution seeding (median-centered, p90 tail, from tools/rates report) — changes truth
  authorship; own gated slice + doubt-driven review.
- Known watch items: App.World.Tests ~1-in-3 single-test transient (spawn-task chip filed);
  wave-4 run-1 boot anomaly (camera fine across 4 subsequent boots + forced reload).
