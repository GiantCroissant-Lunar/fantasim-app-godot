# Planet look — north star (stylized readable planet)

**Date:** 2026-07-05 · **Status:** APPROVED direction (user choice, 2026-07-05)
**Judged against:** every render arc from now on. Supersedes the drifted "Astroneer-style
rocky/faceted body" target baked into earlier comments and the 2026-07-03 plan's rocky-body
reference stills — that drift is why three tuning arcs failed the eye test
(diagnosis + survey: [2026-07-04-tectonic-look-amplification-research](2026-07-04-tectonic-look-amplification-research.md)).

**Target in one line:** a game-render planet (Sebastian-Lague-style): **round limb**,
saturated altitude ramps doing the visual work, exaggerated-but-smooth relief, crisp
boundary belts — **legibility over realism**.

## Hard, testable constraints (planet views: World + crust diagnostic)

1. **Silhouette budget — the limb is a circle.** Total radial displacement (post-lens,
   post-amplification) ≤ **0.5% of base radius** (|d| ≤ 0.005·R), asserted by unit test.
   Earth truth is 0.15%; 0.5% is the stylized allowance. Relief drama moves to shading
   and color, never the limb.
2. **Color-first.** The dry crust is NOT monochrome: hypsometric ramp with distinct,
   saturated bands (basins / plains / highlands / peaks), and a **bimodal base** — ocean-basin
   level tonally separated from continental level (shelf ramp between) even with no water.
   Belt accents (trench/ridge/arc) stay visible on top of the ramp.
3. **Concentrated drama.** Interior fabric amplitude ≤ **0.15×** belt amplitude. Belts are
   thin (1–2 cells), ridged, boundary-aligned — mountain CHAINS, not crumple. Most of the
   surface is calm; that calm is what makes belts read.
4. **Smooth shading in planet views.** Smooth (or blended) normals for World + crust
   diagnostic; flat faceting is reserved for explicitly diagnostic views (PlateIdentity).
5. **Legibility test (windowed screenshot at t=100M, default camera):** a viewer names,
   without help — (a) continents vs basins by tone, (b) mountain chains as lines,
   (c) a round planet. If any of the three fails, the arc fails.

## Anti-goals

- No everywhere-crumple; no silhouette lumps; no monochrome grey; no faceted "low-poly rock"
  reading in planet views. The declared `ReliefAmplification` stays honest but must respect
  the silhouette budget.

## Fit with existing machinery

`TectonicDetailSampler` (context modulation), `HeightFinalizer` (cap lives here),
`HypsometricTint`/`CrustAccentMapper` (ramp + accents), `PlanetLayerProjectionProfile`
(declares the cap alongside `ReliefAmplification`) — no re-architecture required; this spec
re-aims the render-interpretation layer only. Sim truth untouched (L×R×M).
