# Visual references

External reference imagery the presentation work steers toward. Each entry records source,
license, and what it is the reference FOR — references calibrate DEFAULT parameters and
acceptance checks, never code constants (fantasy-world principle, see
`vault/specs/2026-07-02-planet-evolution-arc-design.md` §5b).

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
