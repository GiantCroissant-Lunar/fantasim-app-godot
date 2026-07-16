# Plan: VISIBLE adaptive LOD — nonuniform resolution the eye can verify (directive 4 slice 1)

**Source:** `vault/specs/2026-07-16-layer-first-presentation-directives.md` §4 + round-2
refinement 4b. **Standing failure this plan exists to end:** the user has asked for adaptive
LOD for months; agents (repeatedly, including 2026-07-16) reported the builder's
adaptive-subdivision MACHINERY as if it were delivered LOD. What renders is uniform
tessellation. The root cause was the absence of a falsifiable gate — this plan's acceptance
FAILS on uniform output, by construction.

**Scope (slice 1 of the adaptive arc):** truth-driven NONUNIFORM refinement, visible and
measurable — refine where the information is (plate boundaries, high relief), stay coarse
where it isn't (plate interiors). View/camera-dependent streaming LOD and chunked/tiled
residency remain the design arc (S2 keys landed 2026-07-16 in unify-topology as its
primitive); do NOT attempt them here.

**What already exists (use, don't rebuild):** fantasim-cartography's
`AdaptiveGlobeSurfaceBuilder` — threshold-driven midpoint refinement with `HeightFinalizer`
+ `DetailSampler` delegates on `AdaptiveSubdivisionOptions` (LOD-roadmap slice 2, reviewed
clean; midpoint invariant holds inductively). `VertexProvenance` records refinement lineage.
The app configures these options when building globe surfaces — THAT configuration is what
currently yields effectively uniform output.

**Design decisions (locked):**
1. Refinement criterion = COARSE CAUSAL CONTEXT, not camera and not history (terrain-diffusion
   adoption, hub deposit §3: conditioning on the causal bundle; "no dependency on query
   history" — the mesh for a given (tick, seed, params, R-budget) is identical regardless of
   how the user got there). Drive the existing threshold mechanism with a per-region criterion
   from `CellFeatures`: boundary proximity/kind/weight and relief magnitude ⇒ deeper
   refinement; interiors stop early.
2. Deterministic identity: refined output is a pure function of
   (tick, seed, declared params incl. R-budget). Re-running the build yields bit-identical
   meshes. If any memoization/cache is introduced, eviction drops value AND processed-marker
   together (deposit §3 invariant) — but prefer NO cache in this slice.
3. A triangle budget is declared (S1 discipline): total triangles bounded; the WIN is
   redistribution — boundary-adjacent density up, interior density down at equal-or-lower
   total cost. No silent budget growth.
4. **Debug legibility surface:** a wireframe/density debug toggle reachable via ingress
   (follow the existing render.* command registration pattern in
   `App.Render.Seam/HostComposition/RenderComposition.cs`) so the nonuniformity is VISIBLE
   in the windowed app and screenshotable. This is part of the slice, not optional.

**Falsifiable acceptance — the gate that has been missing for months:**
- (agent, headless) A density test: build a globe at a mobile-plate tick; partition faces by
  distance-to-nearest-boundary; assert triangle density in the boundary band ≥ 3× the
  interior density, AND total triangles ≤ the declared budget, AND interiors are COARSER than
  today's uniform baseline (pin the baseline first — characterization test before the
  change).
- (agent, headless) Determinism: two independent builds at the same identity → identical
  vertex/index buffers.
- (lead, windowed) Wireframe screenshot showing visibly nonuniform tessellation tracking the
  boundary network; the same screenshot at an interior-only framing shows coarse mesh.
  UNIFORM OUTPUT = SLICE FAILED. No machinery-exists reporting.

**TDD order:** characterization baseline (uniform densities today) → density-ratio test (red)
→ determinism test → criterion implementation via AdaptiveSubdivisionOptions configuration →
debug toggle → green → full suite.

**Code anchors:** app-side surface build configuration (grep `AdaptiveSubdivisionOptions`
consumers under `project/plugins/`), `GlobePlateSurfaces` / `PlateSurfaceMeshFactory`,
`TectonicDetailSampler` (context source), RenderComposition (command registration).

**Out of scope:** camera/view-dependent LOD, tiles/chunks/culling (design arc), mantle-field
grid resolution (adaptive-mantle directive — separate), cartography builder REWRITES (the
builder's existing option surface should suffice — if a targeted builder change is genuinely
required, STOP that sub-task and record the exact needed change in AGENT-SUMMARY.md;
fantasim-cartography is READ-ONLY for this dispatch), engine/truth changes, project.godot.

**Agent constraints:** assigned worktree only; NO commits/pushes; no export/bundle/install
tasks; no vault/ edits; absolute paths for shell ops.
