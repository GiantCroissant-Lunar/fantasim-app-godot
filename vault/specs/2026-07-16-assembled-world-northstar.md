# The Assembled World — north-star, re-derived WITH the user (2026-07-16 evening)

This spec replaces every prior agent paraphrase of the look. It was derived in a step-by-step
walkthrough the user led personally; the load-bearing sentences are quoted verbatim. The
reference pixels live in `vault/reference/` (all deposited — none missing).

## The verdict that reframes everything

> "The normal complete sphere could not see how convergent, divergent, transform being
> presented. But the split part with thickness can."

The watertight, seamless composed sphere — the presentation the render stack has been
perfecting for months — is **structurally incapable** of the ask. No amplitude tune fixes it.
The world is an **assembly of solid parts**, not a decorated ball.

## The clauses (user's own construction, from four references)

1. **Split plate slabs, all the time.** The default World view, while time scrubs forward AND
   backward, is a globe of discrete thick plate slabs — "still split parts as time advanced
   forward and/or backward." Not a mode; THE world.
2. **Crust is material.** Slabs have visible, deliberately non-realistic (ratio-locked)
   thickness — tops, side walls, undersides ("this is not realistic scale compare to planet
   core"; reference: `2026-07-16-user-reference-thick-crust-planet.png`).
3. **Mechanics legible from geometry.** "So we can see how mountain, trench is formed. How
   plate A is under plate b and moved." Convergent = visible underride + trench at the dive
   line + mountains piling on the overriding edge; divergent = slabs parting; transform =
   shear along the joint. (Reference: Sketchfab exploded-plates, URL in registry.)
4. **Slab topology is alive.** "Plate could be merged, broken into pieces."
5. **Not a texture — extracted geometry.** "I am not asking to use one 'texture' to present
   it. It is more like marching cube kind of presentation." Solid crust volumes realized as
   meshes from simulation data.
6. **Performance is first-class and shaped by 5.** "Since it is marching cube, the performance
   has to been tuned. That is why I keep mentioned chunked, tiled, adaptive sub division."
   Resolution concentrates at boundaries/interaction zones; interiors coarse; chunked.
7. **Assembled completeness.** The cartoon-planet + gray-geometry references show the target
   *finish*: a world that reads as one complete, chunky, legible thing at a glance — while
   still visibly made of parts (joints never hidden). NOT pixel targets ("referenced image
   only shows the world we want to be assembled").
8. **Excluded takeaways:** water (waterless-worlds lock), biome colors ("we don't get into
   biome so soon").
9. **Terrain-diffusion paper** (`vault/research/2512.08309v4.pdf`): the user's standing
   quality bar for slab-top detail formedness — slots in AFTER the slab skeleton stands
   (derived-only, per the 2026-07-16 evaluation memo).

## Regime coverage (every tick, every regime)

- magma-ocean: no solid slabs — molten sphere (existing emissive surface is the right family).
- stagnant-lid: ONE unbroken thick shell — a single slab with thickness; not a smooth ball.
- mobile-plate: the full split-slab assembly above.
(stagnant-lid currently materializes NO crust surface at all — a known machinery gap,
`CrustGenerationTriggerPolicy.cs` gates crust to mobile-plate only.)

## What exists vs what's missing (constants map: session evidence, Explore result 2026-07-16)

EXISTS and is the right parts bin: per-plate watertight solid slabs (PlateSolidBuilder;
exploded view factor 0 = assembled with thickness at silhouette), slab-top formed relief
(SlabTopReliefProfile, ratio-locked 10× thickness scale, NO world silhouette clamp on that
path), lit strata walls, boundary-band adaptive LOD (3× density), born-rough birth roughness,
engine plate birth/split.

MISSING (the actual work): slab assembly as the DEFAULT World view through time; subduction
underride geometry (A under B); trench/mountain expression at the slab joints; live
merge/break presentation; chunked/adaptive extraction performance architecture; stagnant-lid
single-shell story; retirement of the watertight-sphere-as-world path and its 0.5%R silhouette
clamp doctrine (clamp stays only wherever the watertight sphere itself survives as a
secondary view).

## Process contract

The user said: "generate the world I describe so we can fine tune later... implement and show
me the world so I know you know." Slices ship into the EXPORTED WINDOWED app; every claim is
proven with OS-level screenshots at claim time; the user's eye is the gate; fine-tuning is
iterative against the reference registry.
