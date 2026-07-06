# Globe surface — next-steps roadmap (post adaptive-subdivision review)

> **AUDIT (2026-07-06, code-verified):** SUPERSEDED by `2026-07-06-attempt8-recovery-roadmap.md`; the DefaultPeaks item was dissolved by `specs/2026-07-04-tectonic-look-amplification-research.md`. **LIVE REMAINDER:** Slice 1's viewport/panel overlap defect is still open — re-homed to the attempt-8 roadmap (P3 candidate). _(See the authority index in `vault/README.md`.)_


**Date:** 2026-07-04
**Status:** approved sequencing; each slice gets its own implementation plan when started
**Context:** the recursive feature-aware adaptive subdivision arc landed and was reviewed
(app `b7c306a`, carto `3877cf1`, world `cecfa84`; all clean). This roadmap sequences what
follows. Review findings it encodes: viewport overlap is the top visual defect; provenance
(`VertexProvenance`) and scale (`PlanetLayerProjectionProfile`) contracts now exist and are
the anchors for LOD work; grid-provider abstraction is NOT a goal.

## Decisions (user-approved 2026-07-04)

1. **Boundary-panel layout:** decided *after seeing it* — Slice 1 opens with 2–3 layout
   mockups screenshotted in the exported windowed app; the user picks one before the real
   implementation.
2. **S2/H3 scope:** S2 is **indexing support only** (spatial lookup/culling via
   `UnifyTopology.Sphere.S2`), never core surface topology. No grid-provider
   swap/mix abstraction — that would need its own future design gate. The architecture doc
   (Slice 3) records this in a non-goals section.
3. **DefaultPeaks diagnostic crank** (`Amplitude: 1000 // was 300`, `BaseFrequency: 16`
   contradicting its own doc text): judged **visually during Slice 1's windowed session**;
   comment/values then updated to match the verdict.

## Slices, in order

### Slice 1 — Viewport composition fix (lead session; needs the windowed loop)
Planet and boundary-section panels must stop overlapping.
- Step 1: 2–3 layout mockups (toggleable side panel / docked strip below / permanent split),
  screenshots from the exported windowed app → user picks.
- Step 2: implement the chosen layout as a **deterministic layout contract** — viewport
  rects computed from window size, unit-tested — not another offset nudge (see
  `09c1de0`/`7b13525` for the trial-and-error to avoid).
- Also in this session: DefaultPeaks visual verdict (decision 3).
- Gate: windowed screenshots show no overlap at default + resized window; layout test green.

### Slice 2 — Small cleanups (bounded; delegable per external-agent-delegation)
- Stale "Depth-1 conforming" class doc on `AdaptiveGlobeSurfaceBuilder` (carto).
- Document the unit of `AdaptiveSubdivisionEdgeHeightDelta` (post-lens unit-sphere
  displacement) on `PlanetLayerProjectionProfile` / `AdaptiveSubdivisionOptions`.
- Apply the DefaultPeaks verdict from Slice 1.
- **Midpoint noise resample** (visual win): sample `NoiseRelief` at midpoint base positions
  instead of linearly interpolating heights, so subdivision adds real relief detail.
  Watertight-safe: pure function of position; split decisions still made on parent heights.
  TDD: assert midpoint height ≠ endpoint mean when noise is on, and cross-plate midpoint
  equality still exact.

### Slice 3 — Architecture doc (design work, lead session + user)
`vault/architecture/globe-surface-lod-scale-and-provenance.md` — the design-before-code
gate for Slice 5. Anchors on what exists: `VertexProvenance`, `PlanetLayerProjectionProfile`,
UnifyCell `HierarchicalCellId` (specify canonical corner-based child indexing for stable
identity — emission-order indices change with the split pattern per tick). Non-goals:
grid-provider abstraction; S2 confined to indexing (decision 2).

### Slice 4 — Boundary section semantics (delegable; parallel after Slice 1)
Polarity labels, slab angle/depth scale, transform shear view, divergent ridge/rift view.
Depends only on Slice 1's stable panel home.

### Slice 5 — Real LOD (only after Slice 3 doc approved)
Chunking, scale bands, camera-driven refinement — per the approved doc.

## Execution model
Per orchestrate-before-implementing: Slice 1 stays with the lead session (windowed feedback
loop); Slices 2 and 4 are bounded external-agent tasks (READ external-agent-delegation
SKILL.md before dispatch); Slice 3 is collaborative design; Slice 5 planned via
writing-plans after Slice 3.
