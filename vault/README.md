# fantasim-app-godot vault

Project documentation for the FantaSim Godot host/app. **Write new docs here.**

## Taxonomy
- `architecture/` — evergreen subsystem & design docs (how the app works now).
- `specs/` — dated, concept-lock feature design specs (`YYYY-MM-DD-<name>.md`).
- `plans/` — dated implementation plans (writing-plans output).
- `handover/` — dated session records (`YYYY-MM-DD-<topic>.md`); append-only history, never audited for currency.

Per-branch execution progress is tracked in `.git/sdd/progress.md` (not committed).

## Authority index (audited against code 2026-07-06; banners on every stale doc)

**Constitution — read first (mechanically enforced by `App.Architecture.Tests`):**
- [planet-domain-station-map](architecture/planet-domain-station-map.md) — the mandatory station route (truth stream → node graph → ECS → tiers → bundle seam) + the two gates.
- [attempt-8 recovery roadmap](plans/2026-07-06-attempt8-recovery-roadmap.md) — the ACTIVE master plan (P0–P7, progress log, product outcomes O1–O5).
- [long-term roadmap](plans/2026-07-06-long-term-roadmap.md) — horizons H1–H4; horizons do not open early.
- [service-tier-architecture](architecture/service-tier-architecture.md) · [cross-alc-rules](architecture/cross-alc-rules.md) · [service-scope-ownership](architecture/service-scope-ownership.md) — the two service axes + ALC rules (drift notes inline).
- [node-graph-paradigm](architecture/node-graph-paradigm.md) — graph paradigm & function providers.

**Active plans:** [p1-conformance-gates](plans/2026-07-06-p1-conformance-gates.md) (DONE) · [p2-continents-through-stations](plans/2026-07-06-p2-continents-through-stations.md) (in flight). Open unscheduled defect: **viewport/panel overlap** (sole live remainder of the superseded 07-04 roadmap; P3 candidate).

**Active specs:** [m0-visible-drifting-continents](specs/2026-07-06-m0-visible-drifting-continents.md) (shipped slice) · [planet-look-north-star](specs/2026-07-05-planet-look-north-star.md) (ACTIVE look target) · [tectonic-look-amplification-research](specs/2026-07-04-tectonic-look-amplification-research.md) (detail-sampler model) · [command-transport-ingress](specs/2026-06-24-command-transport-ingress-design.md) (live subsystem).

**Evergreen architecture (survived the audit):**
[globe-surface-lod-scale-and-provenance](architecture/globe-surface-lod-scale-and-provenance.md) (render/LOD authority) ·
[world-generation-consolidation-refactor](architecture/world-generation-consolidation-refactor.md) (world-gen authority) ·
[hot-reloadable-ui-runtime-and-scoped-bindings](architecture/hot-reloadable-ui-runtime-and-scoped-bindings.md) ·
[unified-provider-function-surface](architecture/unified-provider-function-surface.md) ·
[iii-world-augmentation-boundary](architecture/iii-world-augmentation-boundary.md) ·
[iii-graph-runtime](architecture/iii-graph-runtime.md) ·
[iii-external-tool-nodegraph-vplanet](architecture/iii-external-tool-nodegraph-vplanet.md) ·
[multi-scene-di-scoping-review](architecture/multi-scene-di-scoping-review.md) ·
[bundle-delivery-and-loading](architecture/bundle-delivery-and-loading.md) ·
[runtime-geodata-import-boundary](architecture/runtime-geodata-import-boundary.md) ·
[akka-ecs-integration](architecture/akka-ecs-integration.md) (IMPLEMENTED).

**Superseded / historical (banner at top; kept, never deleted):**
`architecture/rendering-and-lod.md`, `architecture/render-surface-and-motion.md` (both narrate the dead GlobeView path), `architecture/world-generation-cartography-flow.md`, `plans/2026-06-23-host-only-keep-host-type.md`, `plans/2026-06-22-timeline-face-boomhud-bundle.md`, `plans/2026-07-04-globe-surface-next-steps-roadmap.md`. Completed plans carry a `COMPLETED` audit banner.

**History & context (handover/):** the attempt ledger (`2026-07-06-half-year-attempt-ledger.md`), the RFC salvage index (`2026-07-06-rfc-salvage-index.md`), the restart handover, and dated session records.

> Design reference (read-only, richer): `ref-projects/fantasim-app-godot/vault/architecture/` (~30 docs).
