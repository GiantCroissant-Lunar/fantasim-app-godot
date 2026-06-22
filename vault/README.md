# fantasim-app-godot vault

Project documentation for the FantaSim Godot host/app. **Write new docs here.**

## Taxonomy
- `architecture/` — evergreen subsystem & design docs (how the app works now).
- `specs/` — dated, concept-lock feature design specs (`YYYY-MM-DD-<name>.md`).
- `plans/` — dated implementation plans (superpowers `writing-plans` output).
- `handover/` — dated session records (`YYYY-MM-DD-<topic>.md`).

Per-branch execution progress is tracked in `.git/sdd/progress.md` (not committed).

## Key architecture docs
- [service-tier-architecture](architecture/service-tier-architecture.md) — T1–T4 service tiers (Godot only in T4 seams).
- [cross-alc-rules](architecture/cross-alc-rules.md) — collectible-ALC / bundle isolation rules.
- [render-surface-and-motion](architecture/render-surface-and-motion.md) · [rendering-and-lod](architecture/rendering-and-lod.md).
- [node-graph-paradigm](architecture/node-graph-paradigm.md) · [iii-graph-runtime](architecture/iii-graph-runtime.md).
- [world-generation-cartography-flow](architecture/world-generation-cartography-flow.md) · [akka-ecs-integration](architecture/akka-ecs-integration.md) · [multi-scene-di-scoping-review](architecture/multi-scene-di-scoping-review.md).

## Most recent feature (worked example of the spec → plan → handover flow)
- spec: [specs/2026-06-22-tscn-timeline-time-advancement-design.md](specs/2026-06-22-tscn-timeline-time-advancement-design.md)
- plan: [plans/2026-06-23-tscn-timeline.md](plans/2026-06-23-tscn-timeline.md)
- handover: [handover/2026-06-23-tscn-timeline-executed-merged.md](handover/2026-06-23-tscn-timeline-executed-merged.md)

> Design reference (read-only, richer): `ref-projects/fantasim-app-godot/vault/architecture/` (~30 docs).
