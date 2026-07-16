# Layer-first presentation directives (user, 2026-07-16)

**Status:** USER DIRECTIVES (concept-lock) — given live during the 2026-07-16 eye sitting
(export 0.1.2 built 15 Jul 18:50, main @735ad8f, PID-bound windowed session; camera stack
re-verified working, mantle x-ray + tunnel view staged). These extend the D1–D8c layer
directives (`vault/specs/2026-07-07-layer-presentation-input-parity-canonical-units-directives.md`)
and the spline-tunnel design (`vault/specs/2026-07-14-spline-tunnel-branch-fork-design.md`).

**Implied sitting verdicts** (recorded so the look ledger stays honest): the tunnel is good
enough to be promoted to THE timeline (directive 1 is a strong positive verdict on the tunnel
direction); the "x-ray" framing of mantle convection is rejected as a concept, not just a name
(directive 2). No explicit verdict was given on bore curvature feel or the voxel-staircase
severity — still owed.

---

## Directive 1 — Tunnel timeline is the DEFAULT; hide the animation timeline

> "Tunnel timeline view should be the default, hide animation timeline, so no matter what
> time we are, we should use tunnel timeline."

- The tunnel is not an alternate visualization: it is **the** time surface, at every tick and
  in every regime. The lane-based animation timeline (Play/Fit + Geosphere/Atmosphere lane
  grid at the bottom of the World view) gets hidden.
- Design questions to settle before implementation (small spec): where the Play/scrub/rate
  affordances live once the lane face is hidden (tunnel doctrine: flying IS scrubbing); how
  layer selection surfaces (corridor sectors already carry active/inactive badges); whether
  the lane face survives behind a developer gate (like `world:showGraph`) rather than being
  deleted.
- Closes the long-open "default-view decision" from the 2026-07-07 GPlates handover ledger.

## Directive 2 — Mantle convection is a LAYER, not an "x-ray" mode

> "Mantle convection should literally not being stated as x-ray, as the world is composed by
> different layer, mantle convection is itself there existed just like plate, crust. And at
> some regime, only mantle convection could be seen where no plate is formed(?)"

- The world is composed of layers; mantle convection **exists** as one of them, peer to plate
  and crust — not a special overlay toggled onto the World view. The current
  `render.mantle {"enabled":...}` mode framing (and the "x-ray" vocabulary from the 2026-07-07
  reference spec) is superseded: mantle presentation should route through the same
  layer-activation path as every other layer (the tunnel corridors already model it as a
  track with ka-active/inactive badges).
- **Regime-gated composition follows truth:** in regimes where no plates have formed
  (magma-ocean, stagnant-lid pre-onset), the mantle-convection layer may be the only
  geosphere-interior layer visible — because the plate layer is genuinely inactive at those
  ticks, not because a mode hides it. The user's "(?)" marks the open question of exactly
  which layers are active per regime — resolve from the regime/track activity model, not by
  hand-listing.
- Consistent with D-doctrine "mantle = LAYER w/ thick-slab crust" (D1–D8c) — this directive
  makes the app's command surface and composition obey what the doctrine already said.

## Directive 3 — Exploded view: crust detail with real thickness

> "We supposed to have one presentation as [Sketchfab: Exploded view of tectonic plates,
> e9eeeeab3ba8] so that we can see detail of how crust forming mountain, trench, volcano, etc.
> Remember that crust should have thickness which shows using its scale such as
> [mattkeeter.com planets biomes image] but we don't get into biome so soon."

- Reference 1 (Sketchfab, viewed 2026-07-16): plates as **discrete thick slabs**, separated
  at boundaries with incandescent molten seams glowing between them; the exploded interaction
  pulls plates apart radially so plate EDGES and undersides are inspectable. The point of the
  presentation: see WHERE and HOW crust builds mountains, trenches, volcanoes — boundary
  processes made legible by separation.
- Reference 2 (Keeter, viewed 2026-07-16): a low-poly planet whose crust relief has real
  physical BULK at planet scale — mountains read as substantial solids breaking the
  silhouette, terrain banded by elevation. The instruction it carries: **crust thickness is
  shown at its true (ratio-locked) scale** — thickness is a property of the slab, not a
  texture. Biomes are explicitly OUT of scope for now ("we don't get into biome so soon").
- Existing machinery: `render.exploded {"factor":0..1}` (M-B, shipped + windowed-verified
  2026-07-07) explodes the crust; D-doctrine already locks ratio-locked thickness. This
  directive extends M-B from "exploded caps" toward per-plate thick slabs + boundary-process
  legibility (the M-B open items were wall lighting + thickness-exaggeration knob).

## Directive 4 — Sphere-categorized layers, culling, chunked/tiled rendering

> "The world is composed by layers, and each layer could be categorized under specific
> sphere (geosphere, hydrosphere, atmosphere, etc.) and as several layers could be active and
> presented, it is best that we have performant approach showing them, if some are covered,
> we cull it out. And we may need to consider chunked (tiled) view."

- Taxonomy: sphere → layers (matches the strata/sphere terminology doctrine and the timeline's
  existing Geosphere/Atmosphere grouping). Multiple layers active and rendered simultaneously
  is the normal case, not the exception.
- Performance model: **occlusion culling between layer shells** — a layer fully covered by an
  opaque outer layer does not pay render cost; plus a **chunked/tiled view** so only visible
  portions of a shell are resident at detail.
- Connections (design inputs, not decisions): S2 cell ids as the spherical tile key
  (unify-topology binding completed 2026-07-16: children/range/contains/cap-cover); the
  R-adaptive subdivision direction (LOD roadmap slice 5) — and the same-day user directive
  that mantle convection joins the adaptive-R scope (coarse↔fine like plate/crust, which also
  addresses the grid-88 voxel staircase seen in the sitting); tiles/chunks are disposable
  derived products (no-cube rule: cache keys, never persisted truth keys); per-track
  representation providers from the tunnel foundation order.
- This is the architecture item of the four — it wants a brainstorming → writing-plans pass
  before any code.

---

## Execution notes

- Order proposed by the lead session: (1) small spec + implementation for the tunnel-default
  switch; (2) mantle-as-layer reframe folded with (3) exploded/thick-crust into the next
  look-dev slice (eye-judged, windowed); (4) into a design session feeding the
  representation-provider / adaptive-R arc. Biomes deferred by explicit user statement.
- The 2026-07-07 mantle-xray reference spec remains the visual target for the convection
  FIELD itself (curtains, plumes, translucency); what this supersedes is its *mode/x-ray
  framing*, per directive 2.
