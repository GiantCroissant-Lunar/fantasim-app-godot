# Visual references — the binding registry

External reference imagery the presentation work steers toward. Each entry records source,
license, and what it is the reference FOR — references calibrate DEFAULT parameters and
acceptance checks, never code constants (fantasy-world principle, see
`vault/specs/2026-07-02-planet-evolution-arc-design.md` §5b).

**BINDING RULE (2026-07-16).** Every entry in this registry BINDS the look work whether or
not its pixels are on disk. If a file is listed as MISSING, the reference still exists and
still binds — its verbal spec lives at the cited doc. An agent that cannot find a reference
file must (a) consult this registry, (b) use the verbal spec, and (c) ask the user to deposit
the artifact. Claiming "no reference image exists to align with" is a registry-reading
failure, not a fact about the project. History: user-given references were cited in prose
for months (kenny.wtf since the original waterless lock, mattkeeter since 07-03) while
agents repeatedly rederived the look from training priors — see
`vault/handover/2026-07-16-look-northstar-rederivation-handover.md` for the full post-mortem.

**The acceptance criterion (2026-07-16, user-stated):** bulk everywhere, lumpy silhouette,
chunky legible geometry — at EVERY tick, EVERY regime; no biome, no hydrology. Full clause
list: handover above, §1. Renders are judged against the registry images below, by the
user's eye — agents do not self-certify look.

## Registry

| File | Source | Given / cited | Binds |
|---|---|---|---|
| `mattkeeter-planets-biomes.png` | mattkeeter.com/projects/planets (`biomes.png`) | user, 2026-07-16 directive (verbatim quote in `vault/specs/2026-07-16-layer-first-presentation-directives.md`, Reference 2) | **PRIMARY bulk-at-scale reference**: relief with physical bulk breaking the silhouette; crust thickness at true ratio-locked scale; elevation banding. Biome colors are NOT the takeaway (out of scope). |
| `mattkeeter-planets-full.png` | same page (`full.png`) | supporting | Assembled stylized-world composition (atmosphere halo, clouds). Water visible here is NOT a directive — waterless-worlds lock stands. |
| `kenny-wtf-world-synth-continents.webp` | kenny.wtf/posts/world-synth-tectonic-plates/ | user; cited in the ORIGINAL waterless-worlds lock `e3b84ef` and the 06-21 / 07-02 / 07-03 docs | Believability from ~41k noise-jittered regions + NOAA-style ramp; organic continent shapes; grid invisible. |
| `kenny-wtf-world-synth-boundaries.webp` | same post | same | Plate-boundary legibility on the composed globe (boundaries as coherent belts, not noise). |
| `usgs-vigil-plate-boundaries-cross-section.gif` | pubs.usgs.gov/gip/dynamic/Vigil.html (public domain) | user-selected 2026-07-03 | North-star for crust/terrain CROSS-SECTION anatomy — element→coverage map below. |
| `../specs/assets/2026-07-11-tunnel-timeline/` (esp. `3d-timeline-tunnel-spiral-hero.png`) | user-approved mockups | 2026-07-11 | Tunnel-timeline presentation target (spiral 3D timeline). |
| `../research/2512.08309v4.pdf` | arXiv | user, 2026-07-16 | Standing quality bar for terrain formedness (terrain-diffusion paper); derived-only look-dev experiment per its evaluation memo. |
| `2026-07-16-user-reference-cartoon-planet.webp` | user's procedural-planet-generator screenshot ("CARTOON MODE"; Generate: Scale_factor 1.1, Height shift 1, Mesh Flatness 0.7, smoothing normals 21%; Textures: Water_Level 1, Textures Junction 1.7; Cartoon shader on, Glow/Clouds off) | user; DEPOSITED 2026-07-16 same-day re-give (had been given before and only paraphrased) | **Reading UNDER RE-DERIVATION with the user.** User verbatim 2026-07-16: "referenced image is not meant to be what the world should look exactly like. referenced image only shows the world we want to be assembled." NOT a pixel/measurement target. Binding clauses to be settled in the re-derivation talk — do NOT derive numbers from this image without the user's confirmed reading. Known-excluded takeaways: water (waterless lock), biome colors ("no biome"). |
| `2026-07-16-user-reference-geometry-gray.png` | same tool, untextured mesh render (800×800, flat gray) | user, 2026-07-16 | Shape-only sibling of the above. Same status: reading UNDER RE-DERIVATION — an earlier "match this gray shape exactly" gate written here was a premature agent paraphrase and was retracted the same evening. |
| `sketchfab-exploded-tectonic-plates.jpeg` | sketchfab.com/3d-models/exploded-view-of-tectonic-plates-e9eeeeab3ba844aabf6b9f88b7ea8bc3 (by linajakaite; URL recovered from user 2026-07-16 evening) | user, 2026-07-16 directive; step 1 of the user's assembled-world walkthrough | Plates as discrete thick slabs; incandescent molten seams at EVERY boundary; the assembled globe visibly carries its parts (assembled ↔ exploded interaction). Verbal spec: directives spec, Reference 1. Reading refinement in progress in the re-derivation talk. |

| `2026-07-16-user-reference-thick-crust-planet.png` | user, step 2 of the assembled-world walkthrough (2026-07-16 evening) | user | Crust as THICK material sitting visibly ON the sphere — deliberately unrealistic thickness-to-core scale (user: "not realistic scale compare to planet core"); side walls/cliff edges visible where crust ends; user's combine instruction verbatim: "I want to see the sketchfab demonstrated plate which has crust thickness but splited, so we can see how mountain, trench is formed. How plate A is under plate b and moved." Water/biome colors NOT the takeaway. |

`2026-07-06-p2-continents-drift.gif` is an agent-produced capture (P2 evidence, not a user
reference) — kept here for history.

## usgs-vigil-plate-boundaries-cross-section.gif

"Artist's cross section illustrating the main types of plate boundaries" — José F. Vigil,
*This Dynamic Planet* (USGS, public domain). Source:
https://pubs.usgs.gov/gip/dynamic/Vigil.html

**Status: the NORTH-STAR reference for the crust/terrain view** (user-selected 2026-07-03).
Element checklist → FantaSim coverage:

| Vigil element | Engine data | Presentation status |
|---|---|---|
| Trench + continental volcanic arc (convergent, ocean-continent) | boundary type + polarity, ContinentalFraction, features | P4 convergent profile (in flight) |
| Trench + ISLAND arc (convergent, ocean-ocean) | same + overriding side's low ContinentalFraction | follow-up: arc profile variant by overriding-side crust type |
| Oceanic spreading ridge + axial rift | boundary type, crust age deepening | P4 divergent profile (in flight) |
| Transform (quiet, linear) | boundary type | P4 transform profile (in flight) |
| Continental rift zone ("young plate boundary") | divergent boundary under high ContinentalFraction; engine supports plate birth/split | follow-up: rift-valley variant of divergent profile |
| Hot spot / shield volcano (intraplate, plume-fed) | engine convection field HAS mantle-plume centers | backlog: plume surface expression (hotspot volcano + track) |
| Lithosphere/asthenosphere cutaway, subducting slabs | slab geometry derivable from boundary + polarity | backlog: cross-section/cutaway VIEW mode (big; fits depth axis) |
| Oceanic vs continental crust distinction | ContinentalFraction | partially in hypsometric ramp; explicit in cutaway later |
