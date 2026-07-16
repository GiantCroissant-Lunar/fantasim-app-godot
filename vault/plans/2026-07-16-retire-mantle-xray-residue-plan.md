# Plan: retire the render.mantle x-ray residue into the mantle layer path (directive 2)

**Source directive:** `vault/specs/2026-07-16-layer-first-presentation-directives.md` §2
(user, 2026-07-16): mantle convection is a LAYER, "literally not being stated as x-ray."

**The situation (code-grounded in the spec, verified live 2026-07-16):** two mantle
presentation paths coexist:
- the CORRECT one — wave-5 D1: `timeline.select_layer {"sphereId":"geosphere","layerId":
  "geosphere.mantle"}` → `GlobeViewMode.MantleInterior` → composed interior + separated
  thick crust slabs (no ghost shell);
- the RESIDUE — `render.mantle {"enabled":...}` (M-A x-ray mode): ghost shell + hidden
  terrain presentation, superseded by the 2026-07-07 directives spec but still alive on the
  command surface and still rendering ghost-shell style.

**Goal:** ONE mantle presentation path. The x-ray mode disappears as a concept; the command
survives only as a loud deprecated alias.

**Design decisions (locked for this slice):**
1. `render.mantle {"enabled":true}` becomes a DEPRECATED ALIAS: it activates the
   geosphere.mantle layer selection (the exact same code path as `timeline.select_layer`),
   and its result JSON carries a `"deprecated":"use timeline.select_layer"` note.
   `{"enabled":false}` re-selects the previous/default layer or World view — mirror whatever
   the layer-selection path already does; do not invent new state.
2. The ghost-shell/x-ray-only presentation code (in
   `project/plugins/App.Presentation/PlanetPresentationBinder.MantleViews.cs`,
   `project/plugins/App.Render.Seam/HostComposition/RenderComposition.cs`,
   `project/plugins/App.Render/MantleRequest.cs`, and the `IPlanetPresentation` surface in
   `project/contracts/App.Presentation/IPlanetPresentation.cs`) is REMOVED where it is
   reachable only via the x-ray mode. CAUTION: the volumetric anomaly field (engine
   `MantleAnomalyField`) and any sampling/tuning shared with the layer path STAY — the
   residue is the mode/ghost-shell wrapper, not the field.
3. Loud failure: if mantle layer activation is rejected (layer inactive at the current tick
   or unknown), the alias returns ok:false with a clear message — never a silent no-op (the
   `select_layer` silent-failure gotcha has burned gates twice; do not add a third path).
4. Regime honesty test: at a pre-onset tick (stagnant-lid regime), activating the mantle
   layer presents mantle interior WITHOUT plate slabs — because the plate layer is genuinely
   inactive there, not because a mode hides it. Assert via the composition/state products,
   not screenshots.

**TDD order:**
1. Failing test: `render.mantle` enabled → same composition state as
   `timeline.select_layer geosphere/geosphere.mantle` (assert on the produced
   view-mode/composition decision, not pixels) + deprecation note present in result.
2. Failing test: alias loud-fails (ok:false + message) when activation is rejected.
3. Failing test: pre-onset tick → mantle layer composition contains no plate-slab
   contribution (regime-gated visibility from truth).
4. Implement: reroute the command, delete unreachable x-ray/ghost-shell code, green.
5. Full suite for affected projects green; grep proves no remaining reachable
   ghost-shell path (document the grep in AGENT-SUMMARY.md).

**Out of scope:** `render.exploded` (M-B — untouched); wall lighting, plume look, slab-top
coloring, boundary-process detail (directive-3 look slice); tunnel work; engine changes;
T1 contract changes beyond removing x-ray members IF they are app-contract-local and
unreferenced (if a removal would ripple into bundles' shared closure, keep the member,
mark [Obsolete], and record it in AGENT-SUMMARY.md instead).

**Acceptance (agent-verifiable):** all new + existing tests green via `dotnet test`;
`git status` shows only intended source/test/contract files; grep evidence for
no-reachable-ghost-shell recorded. **Acceptance (lead-only, after review):** windowed —
`render.mantle` drives the SAME visual as layer selection (thick slabs + interior, no ghost
shell); pre-onset seek + mantle selection shows mantle-only interior; deprecation note
observed in ingress result.

**Agent operating constraints:** work ONLY in the assigned worktree; NO commits, NO pushes;
do NOT run export/bundle/install tasks (a user session may hold the exported app open); do
NOT modify `project.godot`, export artifacts, or anything under `vault/`; leave findings +
file list in `AGENT-SUMMARY.md` at the worktree root.
