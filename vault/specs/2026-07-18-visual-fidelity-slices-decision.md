# Visual-fidelity slices V1–V3 — decision record (2026-07-18)

**Status:** Direction USER-DECIDED 2026-07-18 (structured in-session answer). The slice gate
definitions below are the agent's write-up of that decision and are **pending the user's
review**; no implementation may start from this document until the user approves it.

## What was decided

Reviewing the two 2026-07-17 discussions (Claude session `008685ab-8af9-4482-9229-fbd4aaa137ff`
→ `vault/handover/2026-07-17-do-the-ask-agent-defect-handover.md`, and Codex thread
`019f6e3f-8dc6-7b63-98a4-f8a33148b968`, which ended mid-analysis with no next target chosen),
the user chose:

1. **Keep the approved architecture.** `vault/specs/2026-07-17-spherical-plate-material-volume-design.md`
   (spherical plate-material volumes with non-radial boundary deformation) is NOT re-opened.
   The representation is settled; the realization is what fails.
2. **Fix the realization via fidelity slices V1 → V2 → V3** (defined below), each gated by the
   user's eye against the reference registry.
3. **The first visual gate is judged on the whole assembled globe** at the framing of
   `vault/reference/2026-07-17-user-reference-closed-contact-assembled-planet.jpg` — not on a
   single-boundary close-up first.

## Evidence basis (why realization, not representation)

- A0 structural gate PASSED honestly: one production ray returns a distinct overriding plate-7
  interval and a separate hinge-attached down-going plate-2 interval
  (`vault/specs/evidence/2026-07-17-spherical-plate-material-volume-a0-b0/README.md`). The
  under/over anatomy exists in state for the first time.
- Codex's own recorded verdict: "A0 passes structurally … B0 fails visually: the assembled
  globe has amplified relief, but coarse faceting dominates and some trenches resemble open
  cracks. The actual overriding/down-going relationship is still not readable."
- Independent GLM-5.2 review: "scene occlusion — not color or missing geological state — is
  the current failure."
- `TectonicDetailSampler` was deliberately cut from A0/B0 scope (it would have been
  sphere-fixed across ticks); adaptive chunking/zoom detail was deferred out. The detail
  pipeline (design §6) has never been built — it did not fail, it is absent.

Observable differences of the current renders vs the closed-contact reference (facts, per the
no-self-verdicts rule):

1. The triangular simulation-cell grid is visible everywhere at globe distance (flat-shaded
   facets) — violates approved decision #7 / design §7.1 (cells and chunks invisible).
2. Boundaries do not read as raised belts; the focused view shows an open channel at the
   boundary — violates approved decision #1 (completely closed contacts).
3. No broad tectonic forms readable at globe scale (no mountain systems, trench basins,
   collision belts) — design §5 grammar and §6 stages 1–2 not expressed on the surface.
4. Exploded pieces read as flat angular shards with straight jagged edges — design gate 12.1
   requires curved shell regions with readable thickness and undersides.

## The slices

Each slice ships into the EXPORTED WINDOWED app; the acceptance is a paired OS-level
screenshot at the reference framing; the agent reports OBSERVABLE differences only (no
"close"/"matches" language); the user's eye is the sole gate.

### V1 — closed skin

Closed contacts + invisible cells on the assembled globe.

- Smooth-shaded, boundary-concentrated adaptive tessellation of the outer envelope (part of
  design §8 pulled forward — see the sequence amendment below).
- No open seam, channel, or gap at any ordinary contact; boundary presence expressed only as
  crease/step response for now.
- Gate: full assembled globe capture — no visible facet/cell/chunk grid, no open contacts,
  silhouette remains a globe.

### V2 — boundary belts

The deformation grammar (design §5) expressed on the surface, readable at globe distance:
trench basin on the down-going side, orogenic wedge / mountain belt on the overriding side,
volcano chain at the declared setback; divergent ridge shoulders + axial rift; transform fault
corridor. The Vigil anatomy the state already carries, finally visible.

- Gate: full assembled globe capture — boundary belts readable at the reference framing, plus
  the paired whole-globe exploded capture (gate 12.3 semantics) showing curved plate bodies.

### V3 — formed detail

Medium/fine sculpted detail on plate tops (design §6 stages 2–3), deterministic in
plate-material coordinates. The terrain-diffusion derived-only experiment
(`vault/research/2512.08309v4.pdf`; 2026-07-16 evaluation memo) is the candidate formedness
source and slots in here — it does not become canonical terrain physics.

- Gate: full-globe capture retains broad forms with readable medium detail; close capture adds
  fine geometry without relocating broad forms (design gate 12.4).

## Sequence amendment to the replacement design

Design §13 deferred adaptive production extraction to step 4 ("only after A/B acceptance").
The B0 visual gate cannot pass while raw cells are visible, so the tessellation/shading part
of §8 needed for closed-skin rendering moves INTO V1. Full chunk residency, culling,
cancellation, and measured budgets remain deferred until after V1/V2 acceptance. This
amendment is part of what the user approves when approving this document.

## Non-goals (unchanged from the replacement design)

Biome/hydrology, color matching, literal crust-to-core ratios, global uniform voxel planet,
boundary-focused explode mode. Color is not an acceptance concern.

## Next step

On user approval of this document: `writing-plans` for V1 only (no V2/V3 planning yet), then
implementation with the exported-windowed gate. Per the standing instruction from the codex
thread, no large new test expansion.
