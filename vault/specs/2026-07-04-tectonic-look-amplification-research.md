# Research: how plate-tectonics planet generators get their LOOK (noise as amplifier)

**Date:** 2026-07-04 · **Feeds:** Slice 3 architecture doc (globe-surface-lod-scale-and-provenance)
**Trigger:** user rejected the dry-crust diagnostic look (uniform fBm, Amplitude 1000 m / BaseFrequency 16)
but couldn't name the target; this survey grounds the next look decision in what working references do.

## The pattern every credible reference shares

**Structure first; noise is an amplifier conditioned on tectonic context — never the terrain itself.**

1. **Andy Gainey (Experilous, 2014)** — plates get drift axis + spin; boundary **stress**
   (pressure/shear) per boundary point; elevation assigned per stress type/degree with
   **distinct oceanic vs continental base elevations**; interiors interpolated inward from
   boundary elevations. The canonical hobbyist reference.
2. **Red Blob Games, planet generation (2018)** — boundary rule table (land+land converging →
   mountain; land+ocean → mountain/coast; ocean+ocean → coast/ocean; threshold so only ~strong
   convergence raises ranges), then distance-field interpolation. Author **tried simplex-noise
   heightmaps first and abandoned them** — "didn't look interesting".
3. **LeatherBee, Terrain Generation 4 (2018)** — "logical (even scientific) underlying structure";
   noise ONLY masks regularity: 1D/2D simplex perturbs faultlines without moving intersections,
   ±300 m coastal noise, continental shelf slopes from −150 m at faults up to plate elevation.
4. **Cortial, Peytavie, Galin, Guérin — "Procedural Tectonic Planets" (CGF 2019)** — the academic
   capstone: procedural (not physically simulated) plate events — subduction, collision, rifting —
   generate continents, oceanic ridges, ranges, island arcs; then a separate **amplification stage**
   layers procedural noise or real-world DEM exemplars onto the coarse tectonic elevation,
   **conditioned on the tectonic context**. Follow-up: "Real-time hyper-amplification of planets"
   (Visual Computer 2020) — per-landform amplification primitives.
5. **Nick McDonald, SimpleTectonics (2020)** — clustered convection; buoyant height d(1−ρ);
   subduction transfers mass → uplift cascades; frames its own output as "structured, believable
   starting geometry **rather than homogeneous noise**", feeding later erosion.

## Why the current crust diagnostic reads wrong

It is precisely the "homogeneous noise ball" every reference abandons: one isotropic fBm
(freq 16, amp 1000 m) applied everywhere at equal strength over a symmetric ±500 m envelope.
The macro structure EXISTS in FantaSim (BoundaryProfileShape: trench/arc/collision-uplift/
rift-notch/transform-scarp) but the uniform fabric drowns it — amplitude ≥ envelope, no
anisotropy, no land/ocean contrast, detail uncorrelated with tectonic features.

## Direction (maps 1:1 onto plumbing that already exists)

1. **Context-modulated amplification** — replace uniform `DefaultPeaks` with a sampler whose
   amplitude/character depends on tectonic context: ridged noise along convergent belts,
   moderate fBm on continental interiors, weakest on abyssal plains. The per-cell
   `CellFeatures` / feature-weight global gather and boundary distances already exist;
   the just-landed `AdaptiveSubdivisionOptions.DetailSampler` delegate is exactly the
   amplification hook (a closure over the gathered context fields).
2. **Hypsometric bimodality** — separate oceanic vs continental base levels (references use
   distinct plate base elevations + a shelf ramp, e.g. −150 m shelf → plate elevation), not a
   symmetric ±500 m envelope.
3. **Anisotropy along boundaries** — ranges elongate ALONG the boundary tangent (ridged/warped
   noise oriented by the boundary direction), never isotropic bumps.
4. This is Cortial's coarse-model + amplification split, which is FantaSim's own L×R×M doctrine:
   sim truth stays coarse; the look is a render-derived amplification product.

**DefaultPeaks verdict (Slice 1 pending item): superseded.** Neither "keep 1000/16" nor
"revert 300/7" — the uniform-noise model itself is wrong; the knob dissolves into the
context-modulated sampler design above.

## Sources

- https://www.redblobgames.com/x/1843-planet-generation/
- https://experilous.com/1/blog/post/procedural-planet-generation (via secondary summaries)
- https://leatherbee.org/index.php/2018/10/28/terrain-generation-4-plates-continents-coasts/
- https://onlinelibrary.wiley.com/doi/10.1111/cgf.13614 (open PDF: https://hal.science/hal-02136820)
- https://link.springer.com/article/10.1007/s00371-020-01923-4
- https://nickmcd.me/2020/12/03/clustered-convection-for-simulating-plate-tectonics/ ·
  https://github.com/weigert/SimpleTectonics
