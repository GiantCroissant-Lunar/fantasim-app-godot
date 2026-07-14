# fantasim-app-godot vault

Project documentation for the FantaSim Godot host/app. **Write new docs here.**

## Taxonomy
- `architecture/` — evergreen subsystem & design docs (how the app works now).
- `specs/` — dated, concept-lock feature design specs (`YYYY-MM-DD-<name>.md`).
- `plans/` — dated implementation plans (writing-plans output).
- `handover/` — dated session records (`YYYY-MM-DD-<topic>.md`); append-only history, never audited for currency.

Per-branch execution progress is tracked in `.git/sdd/progress.md` (not committed).

## Authority index (audited against code 2026-07-06; banners on every stale doc · index extended 2026-07-11 — delta section below; 07-06 banners unchanged)

**App-internal constitution — read first (mechanically enforced by `App.Architecture.Tests`).**
Cross-repo DOCTRINE authority is the hub
(`fantasim-hub/vault/architecture/doc-authority-map.md` is the tie-breaker; S-006
correction 2026-07-14) — everything below governs THIS app only:
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

## Delta since the 2026-07-06 audit (added 2026-07-11, not re-audited)

**Specs:**
- [layer-presentation-input-parity-canonical-units-directives](specs/2026-07-07-layer-presentation-input-parity-canonical-units-directives.md) — the D1–D8c user-directive arc: layer presentation, input parity, crust thickness, canonical units/odometer vocabulary. ACTIVE directive arc.
- [bundle-oriented-maximalism](specs/2026-07-08-bundle-oriented-maximalism.md) — everything collectible except the loading floor; phase ladder + polarity flip worklist. ACTIVE (phases 0–2.5 shipped; polarity flip pending).
- [common-resident-layer-bundle](specs/2026-07-08-common-resident-layer-bundle.md) — foundation libs served from `common.pck` (Default-ALC resident, never collectible). SHIPPED (phase 2.5).
- [track-embedded-layer-graphs-design](specs/2026-07-08-track-embedded-layer-graphs-design.md) — per-track node-graph views embedded in timeline lanes. SHIPPED (wave 7).
- [track-filmstrip-design](specs/2026-07-08-track-filmstrip-design.md) — image filmstrip previews per track (addendum to track graphs). SHIPPED (waves 9–10).
- [layer-track-registry-design](specs/2026-07-10-layer-track-registry-design.md) — registry-driven timeline lanes; the D5/D7b framing with Unity round-trip in mind. ACTIVE (slices 1+2 shipped).
- [parameter-surface-audit](specs/2026-07-10-parameter-surface-audit.md) — 25-finding audit gating what may enter the v1 JSON schemas. ACTIVE audit ledger (findings #1, #3 resolved).

**Plans (all COMPLETED):**
- [bundle-maximalism-phase0-1](plans/2026-07-08-bundle-maximalism-phase0-1.md) — export self-strip + provisioning + first collectible conversions.
- [phase2-timeline-t3-to-bundle](plans/2026-07-08-phase2-timeline-t3-to-bundle.md) — Timeline T3 orchestrator into the timeline bundle.
- [phase25-common-resident-layer-plan](plans/2026-07-08-phase25-common-resident-layer-plan.md) — 36 assemblies into `common.pck` (IsolatedComponentLoadContext, 4 load-order mechanisms).
- [phase25-loader-design-brief](plans/2026-07-08-phase25-loader-design-brief.md) — loader/packer/strip/version-gate design brief for phase 2.5.
- [wave5-layer-presentation-plan](plans/2026-07-08-wave5-layer-presentation-plan.md) — mantle layer + ratio-locked crust thickness (D1/D3) + timeline UX (D2.2).
- [layer-track-registry-slice1-plan](plans/2026-07-10-layer-track-registry-slice1-plan.md) — registry-driven lanes, slice 1.
- [layer-track-registry-slice2-plan](plans/2026-07-10-layer-track-registry-slice2-plan.md) — stream-discovery seam, source-state restore, laneOrder.
- [planet-presentation-binder-split-plan](plans/2026-07-11-planet-presentation-binder-split-plan.md) — binder 2,636→749: ScrubRefreshCoordinator + shader library + mesh factory + partials.
- [d8b-progressive-resolution-slice1-plan](plans/2026-07-11-d8b-progressive-resolution-slice1-plan.md) — rung-frequency scrub binds, climb-at-rest, drag origins.
- [timelineface-split-plan](plans/2026-07-11-timelineface-split-plan.md) — TimelineFace 1,882→774: FilmstripPreviewController + cache ledger + partials.
- [d42-world-unit-sweep-plan](plans/2026-07-11-d42-world-unit-sweep-plan.md) — Ma wire keys retired, per-tick defaults, tick constants re-derived (MaxTick 11 kb→2 kb).

> Design reference (read-only, richer): `ref-projects/fantasim-app-godot/vault/architecture/` (~30 docs).
