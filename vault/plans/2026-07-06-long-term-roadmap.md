# FantaSim long-term roadmap — horizons H1–H4

**Date:** 2026-07-06 · **Status:** direction document (each horizon opens only when the previous
one's gates are green; plans are written per-horizon at open time, per session-goal-contract).
**Sources:** the attempt ledger (what 7 eras actually did), the RFC salvage index (what was
designed and never built), and the attempt-8 recovery roadmap (the active H1).
**Prime directive, learned twice** (81 RFCs → "3 plates with arrows"; noosphere-before-geosphere
05-26): **horizons do not open early.** Designing ahead is fine (the salvage index IS that);
building ahead is the documented failure mode.

## H1 — the living planet (ACTIVE = attempt-8 recovery, P1–P7)

The planet reads and moves through the architecture. Owner doc:
`2026-07-06-attempt8-recovery-roadmap.md` (P1 ✅, P2 in flight, P3 calibration, P4 motion
character, P5 convection-driven topology, P6 early-planet era, P7 agent operability + closing
demo). Salvage feeds: RFC-027/029 + RFC-0200-xxx (P5), asthenosphere RFC + G123–G131 (P5/P6),
RFC-064/065 CED designs (P5/P6), BimodalDistribution/hypsometric-curve design (P3),
RFC-048/044/080 truth-vs-aesthetic split (P3), RFC-0024 tunnel timeline + G143 + G149–G151 (P7).
**Exit gate:** the P7 closing demo passes and the user signs off O1–O5.

## H2 — planet systems (water, ice, fire, climate)

The waterless lock (`e3b84ef`) governs the DEFAULT look; H2 adds the SYSTEMS — as truth streams
and products through the same stations — with presentation modes that may show them (hydrosphere
mode already exists in `CellElevationSystem.Derive`; surfacing it is a P3-style calibration arc,
not a re-litigation, WHEN H2 opens with user unlock).

Order within H2 (dependency-driven, all salvage-anchored):
1. **Ocean basins** — RFC-073 (mask, bathymetry, coasts) on the moving crust.
2. **Hydrology** — RFC-031 + the landed G32–G45 lane (rivers, lakes, watersheds).
3. **Climate belts** — G69–G77 (temperature/precipitation/seasonality) + RFC-005's successor.
4. **Cryosphere** — RFC-078 + G117/G118 (glaciation, sea-level coupling).
5. **Volcanism** — RFC-076 (Accepted v1; provinces, ashfall, CO₂ — also feeds P6 retroactively).
6. **Erosion/soil** — G80–G94 (stream-power on the moving topology).
**Gates:** per-system truth-stream tests + windowed product views; the SAME two-gate discipline.
**Exit:** a planet whose surface history (coasts, rivers, ice ages, eruptions) is scrub-visible.

## H3 — life and culture

Strictly after H2's habitability substrate exists (the May lesson: biosphere-before-geosphere
produced ad-hoc thresholds; retrofit via RFC-074).
1. **Biogeochemistry + biomes** — RFC-074 (C/N/P + NPP) replacing the ad-hoc G-lane thresholds;
   RFC-081 disturbance/productivity; G110 habitability baselines fixed.
2. **Species & evolution** — RFC-007 + RFC-066/067 (selection, phenotype projection).
3. **Language & text** — RFC-003/004 primordial language + RFC-016/017 Tracery layer (name/
   narrative generation over world truth).
4. **Culture & politics** — RFC-006; map styling RFC-057.
5. **Noosphere LAST and per-consumer** — RFC-075/G106 stay closed under the 05-26 ruling
   ("noosphere is very case-by-case; a default one won't fit"): the base world stays neutral;
   consumer projects (H4) bring their own noosphere overrides.
**Exit:** a world that names itself — biomes, species, cultures, languages on the scrubbed planet.

## H4 — consumers, oracles, products

What the world is FOR (the ledger's warning: the original consumer vanished in Dec 2025 and the
project drifted producer-only for months).
1. **Oracles / external truth providers** — RFC-0001/0002 (VPLanet, Landlab as optional upstream
   truth via projection/reprojection; the Truth Stack Hierarchy doc); iii workers already carry
   VPLanet plumbing (worker + codegen exist in the app repo today).
2. **Cartography as product** — RFC-030/053/055/056/058 + RFC-046/047 (map documents, projections,
   vector styling, exports); RFC-059 authoring-as-events (user edits become truth).
3. **A game consumer** — the mung-bean ↔ sim-world contract (Dec 2025 design: hash-chain
   integration, session-delta export, R/G realization/gameplay split) as the template — whether
   mung-bean itself or a successor; dungeon-scale sub-L0 layer design exists.
4. **Economic layer for consumers** — RFC-077 ore deposits (Accepted v1) + per-consumer noosphere.
**Exit:** at least one external consumer runs against the world's truth streams.

## Standing constraints across all horizons

- The two gates (conformance + scripted windowed eye gate) apply to EVERY arc, every horizon.
- The station map extends per-domain (each new sphere = same S1→S5 route).
- Settled decisions carry across horizons: waterless default look, plate count emergent,
  S2-indexing-only, no hand-rolled spherical math (Unify*), no USD/DCC (ruled too early 05-28),
  noosphere per-consumer only.
- Storage churn is OVER: SurrealDB-via-unify-storage is the decided backend (5 backends in 7
  months is a ledger anti-pattern, not an invitation).
- Session-goal-contract + prior-attempt audit (ledger, salvage index, circle map) before any plan.
