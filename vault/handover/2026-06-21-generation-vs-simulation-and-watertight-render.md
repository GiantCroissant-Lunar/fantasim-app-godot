# Session record — Watertight render + peaks, and the generation-vs-simulation reckoning

> **Date:** 2026-06-21 (evening) · **Repos:** `fantasim-app-godot`, `fantasim-cartography` (+ `plate-projects`
> as reference) · **Result:** the globe is **un-shattered** (watertight per-plate caps via a new cartography
> part) and **faceted** (seeded noise peaks) — but the session's real outcome is a **strategic diagnosis of
> why many attempts have never produced a planet that looks like a world.** **ALL WORK IS UNCOMMITTED.**

## TL;DR — the arc
Started from *"the planet doesn't look like [reference image]."* Diagnosed the render (it was a **shattered
mesh** — loose per-cell triangles that crack), moved the surface geometry into `fantasim-cartography` (parts
vs assembly), rebuilt it watertight, added noise relief — then, comparing against real tectonic-plate
generators (World Synth et al.), reached the actual root cause. **The new session's first job is a decision:
generation-first, or keep perfecting the simulation.**

---

## ★ THE HEADLINE — generation vs simulation (read this first)

**The reference tools (World Synth, Astroneer, the Unity cartoon-planet) are one-shot GENERATORS. We built
a time-evolving SIMULATION. That is the whole gap.**

- **World Synth** (read via its writeup): builds the planet **upfront, in seconds — never simulates time.**
  ~**41,000** hex regions. **Many** plates (user knob), **grown organically** (noise-modulated flood-fill →
  naturalistic shapes). Elevation = plate base + boundary collision/rift zones + noise.
- **Us:** **4** plates, **1,280** cells, **regular nearest-seed** shapes. A time-evolving simulation —
  reconstruct positions over canonical time, accumulate fields per tick, hash-chained truth-stream,
  determinism. Elevation at any single tick = **4 uniform plateaus + thin ridges on boundary cells.**

**Why many attempts never landed — two compounding reasons:**
1. **We perfected the part the references skip.** Every attempt poured itself into the *simulation
   foundation* (motion model, reconstruction, truth-stream, canonical time, determinism) and got stuck
   there ("the reconstruction solver exists but is wired into nothing," ~5×). The references build **none**
   of that. The thing that actually makes a planet read as a world — the **generation recipe** — never got
   built. **Smoking gun:** a sophisticated reconstruction + truth-stream engine seeded by **four hand-typed
   plates** (`GlobeReconstructor.DefaultPlates`). Huge engine, toy generator.
2. **Even our generation is far below the genre** (see recipe below).

**Reframe that makes it fixable:** the *look* is a **generation** problem, **separable** from the
simulation. World Synth proves the recipe alone — zero time-stepping — makes a world. So:
1. **Build the generation recipe** (one-shot, like the references) → a planet that looks right at a single
   instant. **This has never been built.**
2. **Then** layer the time-evolving simulation on top (drift, mountains growing over ka) — the thing **none**
   of the references can do. That's the real differentiator.

We've been doing it backwards (simulation first, generation never). **Recommendation: generation-first.**
**OPEN DECISION — the user has not yet chosen.**

## The generation recipe (what "getting it right" needs)
- **Many plates** — dozens (Earth ≈ 7 major + ~10 minor). We have **4**.
- **Organically-grown plate shapes** — noise-modulated growth (World Synth flood-fill). We have **regular
  nearest-seed** regions.
- **Higher resolution** — World Synth ~41k cells. We have **1,280** (freq-3). *(User earlier said "not about
  cell count," but the genre is much denser; revisit.)*
- **A rich elevation field** = plate base + boundary **belts** (mountains with *width*, falloff from the
  boundary — not thin one-cell ridges) + **multi-scale noise** + **light erosion**. We have uniform plateaus
  + thin boundary ridges + a single uniform noise octave-set.
- Note the user's framing: *treat the blue as just another elevation band* — this is about the **elevation
  structure**, not biome/land-sea coloring.

---

## What WAS built this session (render plumbing — done, tested, works)

All via subagents, TDD, **not committed**.

1. **`Cartography.Globe` part** (new, in `fantasim-cartography`; Godot-free; parts-vs-assembly):
   - `GlobeSurfaceBuilder.Build(vertices, triangles, heights, radius)` → **watertight** `GlobeSurface`
     (shared-vertex `Positions`, pass-through `Triangles`, `SmoothNormals` per-vertex **and** `FlatNormals`
     per-face — the blocky/smooth toggle), + `GatherVertexHeights(perFace → perVertex mean)`.
   - `NoiseRelief.Sample(unitPos, NoiseParams)` / `.Apply(...)` — deterministic hash-gradient **fBm** (no
     external dep), `NoiseParams(Seed, BaseFrequency, Octaves, Lacunarity, Gain, Amplitude, Ridged)`.
   - **57 tests green.** NOT packed to the feed (publishing a package needs owner approval).
2. **Rename `App.World.Projection` → `App.World.FieldView`** (it's a reactive UI read-model, not a *map*
   projection — collided with cartographic projection). `FieldProjectionService` → `FieldViewService`.
   Behaviour-preserving, history via `git mv`.
3. **Watertight per-plate caps** (the un-shatter):
   - `App.World/Globe/GlobePlateSurfaces.cs` (T3, Godot-free): partitions cells by plate, caches a
     shared-vertex topology per plate (position-dedupe — unify-cell exposes no public index buffer), builds
     each cap via cartography per tick.
   - `App.World.Seam/GlobeView.cs` rewritten: per-plate `MeshInstance3D`, **blocky** (per-face expand at
     watertight `Positions` with `FlatNormals`), removed the GPU-compute displacement + vertex-shader
     fallback, kept the per-plate rotation spine + biome/tectonic colour + sun + mantle + scrubber.
   - App refs cartography via project-references (ungated). **92 app tests green.**
4. **Peaks** wired into `GlobePlateSurfaces`: noise sampled once on each cap's **base (tick-0) positions**
   (so bumps **ride the plate** + stay watertight at shared boundary corners), added to the per-tick
   envelope. Tunable `DefaultPeaks` at **`GlobePlateSurfaces.cs:81`**.

## Current visual state (verified by capture in the windowed app)
- **Un-shattered ✓** — within each plate the surface is coherent (vs the prior exploded-tiles mess).
- **Faceted ✓** — peaks at **300 m was too subtle**; cranked to **1000 m + freq 16** it's clearly bumpy.
  *(`DefaultPeaks` is currently left at the DIAGNOSTIC 1000 m — tune ~650 to keep land/sea legible, or bold
  for pure form.)*
- **Remaining render issues (lower priority than the headline):**
  - **Plate-boundary seams** — per-plate caps gather heights *within* a plate, so a continental edge (high)
    and an ocean edge (low) sit at different radii and don't connect → dark seams + a notch at the 3-plate
    junction. This is the **boundary-geology** step (motion-model A: stitch margins → ridges/trenches).
  - **No organic continents** — land/sea is the 4 plate blobs. = the generation gap above.
  - **Smooth vs blocky** is a 1-line toggle (builder ships both normals); currently blocky.

---

## State — UNCOMMITTED (both repos)
- `fantasim-cartography`: new `Cartography.Globe` (Build + NoiseRelief) projects. *(Also a pre-existing
  `src/→project/` reorg in flight — NOT ours.)*
- `fantasim-app-godot`: `FieldView` rename + render rewrite (`GlobePlateSurfaces`, `GlobeView`, `Host.cs`,
  project refs) + peaks + this doc + `vault/architecture/render-surface-and-motion.md`. Dead file left:
  `complete-app/shaders/relief_displace.glsl` (unused).
- Tests green: cartography 57, app 92 (+ App.World.Tests up to 29). Nothing committed (never requested).

## Pointers
- **Design doc:** [`render-surface-and-motion.md`](../architecture/render-surface-and-motion.md) — the
  cartography split + the watertight/relief/motion design + the gap analysis.
- **References used:** Astroneer (blocky faceted), "Birth of a Planet" (the exact icosphere→displace→colour
  pipeline), Unity cartoon Earth (smooth coherent), **World Synth** `kenny.wtf/posts/world-synth-tectonic-plates/`
  (the generation method — one-shot, ~41k cells, organic plates).
- **Capture command (windowed, self-quits):** `FANTASIM_GLOBE_CAPTURE=/tmp/g.png FANTASIM_GLOBE_TICK=0
  FANTASIM_GLOBE_COLORMODE=0 <Godot> --path project/hosts/complete-app` (build C# first; the `'stage'`
  scene-activator error is unrelated bundle scene-flow).

## Next session — start here
1. **Decide the headline:** generation-first (recommended) vs simulation-first.
2. If **generation-first**: build the generation recipe — many organically-grown plates + higher resolution
   + boundary-belt elevation + multi-scale noise + light erosion. (This is the never-built half.)
3. If continuing the **render** track instead: boundary-geology (close the seams), tune `DefaultPeaks`,
   then organic continents via seeded `continental-fraction` noise.
4. **Commit decision:** everything is uncommitted across two repos — branch + commit before building more.
