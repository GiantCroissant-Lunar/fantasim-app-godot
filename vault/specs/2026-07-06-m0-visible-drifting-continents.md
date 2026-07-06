# M0 — visible drifting continents (motion-first slice)

**Date:** 2026-07-06 · **Status:** APPROVED design (user decisions D1–D4, 2026-07-06)
**Feel target:** Scotese-style "Earth in 250 My" — continents visibly glide, collide, merge
across the presented window. Motion first; the skin comes later.
**Judged by:** the gates in §4. No look tuning (ramps/belts/relief) is in scope — that is
exactly what consumed the last two months against an invisible-motion pipeline.

## 1. Why this slice (verified diagnosis, 2026-07-06)

Plate motion is ALIVE end-to-end in the engine and reaches the presentation document; it is
invisible only because the user-visible channels are computed in the frozen onset frame:

- Engine: `OnsetRoster` pole rates 0.9–2.0e-2 rad/Ma; `GlobeReconstructor.BuildGlobeAt(onset+20M)`
  reassigns **100% of 5120 cells** vs onset (probe: `project/tests/App.World.Tests/MotionProbeTests.cs`).
- Playhead reaches the document: `PlanetPresentationBinder.cs:697` → `Service.cs:143` →
  `Service.cs:427` (`GlobeSnapshot = BuildGlobeAt(arcTick)`).
- **Loss point:** `Service.cs:418-424` — `globeAtOnset`/`arcsAtOnset` feed CellElevations,
  CellFeatures, thickness, sections; crust pipeline pins `RotationReferenceTick=onsetTick`
  (`WorldCrustRunSpec.cs:100`). World/Hypso views paint per-cell heights+colors → onset-frame.
- Live-app proof: PlateIdentity view (colors BY membership) changes **70.2%** of globe pixels
  100M→120M; the crust view changes **4.9%** (belts brighten in place).
- Time scale: `TicksPerMegaAnnum = 100_000` (fantasim-world `UnitConverter.cs:11`); onset
  `PlateOnsetTick = 100,000,000` = 1000 Ma; window `onset + 20_000_000` = **200 Ma**.

## 2. Decisions (user-approved 2026-07-06)

- **D1 — surface:** new `GlobeViewMode.Continents`, routed from the existing `geosphere.plate`
  timeline track. `PlateIdentity` stays a pure diagnostic, reachable via a config knob
  (`globe:plateView = continents|identity`, default `continents`).
- **D2 — land/ocean source:** the existing per-plate continental designation
  (`continentalPlates` config; default `CrustInitRecipe.Continental(0, 1)` at
  `WorldCrustRunSpec.cs:214`). Zero new truth. Continents = unions of continental plates,
  drifting with membership. The presentation document gains the resolved set
  (`ContinentalPlateIds`) so render and crust pipeline can never disagree.
- **D3 — motion path:** a lightweight membership-only refresh per scrub tick (skip crust
  materialization) + make the timeline Play button sweep onset→maxTick.
- **D4 — boundaries:** frontier-only — derived from the SAME reassigned assignment that colors
  the caps (consistent by construction). The typed-arcs/membership mismatch is follow-up F1,
  NOT in M0.

## 3. Design

### 3.1 View mode
`GlobeViewMode.Continents` added to `project/contracts/App.World/Composition/GlobeViewMode.cs`;
`GlobeViewModeResolver` maps `geosphere.plate` → `Continents` unless `globe:plateView=identity`.
Rendering: flat (no elevation displacement, like PlateIdentity), smooth normals are unnecessary
(single tone per cell); two saturated tones — land (continental plates) / ocean (rest) — with a
darker frontier tint on cells whose neighbor belongs to a different plate. No typed-arc
polylines, no relief fabric, no hypsometric ramp.

### 3.2 Light refresh path
New `IService.GetGlobeSnapshotAt(long tick)` on `project/contracts/App.World/Services/IService.cs`:
returns only `WorldGlobeSnapshot` at the tick (= `reconstructor.BuildGlobeAt(tick)`), with the
reconstructor/roster **cached per (seed, frequency)** in `Service` (today
`BuildPlanetPresentationRuntime` rebuilds `OnsetRoster` — including lid fracture — on every
document fetch; the probe shows roster + two globe builds ≈ 65 ms total, so a cached
`BuildGlobeAt` fits a per-scrub budget). In `Continents` mode,
`PlanetPresentationBinder.ApplyTimelineTick` calls the light path on every tick change and swaps
only the plate-surface caps; the heavy document refresh keeps its existing crust-snapshot cadence
for the other views.

### 3.3 Play
`TimelineFace`'s existing play machinery sweeps onset→maxTick; target full-window sweep
≈ 15 s (speed knob, presentation-level only). Play + Continents view is the M0 demo.

## 4. Gates (all three; an arc that skips the windowed gate has failed by definition)

1. **Unit (motion regression):** promote `MotionProbeTests` → `MotionGateTests`: assert
   membership change between onset and onset+20M ≥ 30% of cells (floor, not target), and that
   every cell of a continental plate maps to the land tone at any tick.
2. **Windowed (scripted, via the established remote-drive recipe):** Continents view; capture at
   5 evenly spaced ticks across the window; consecutive-frame pixel diff ≥ 10% each; plus one
   short Play recording.
3. **Eye test:** a viewer names, unaided: (a) land vs ocean by tone, (b) that the land masses
   MOVE across the sphere between window start and end. Fail either → the slice fails.

## 5. Non-goals (explicit)

- Any look tuning of World/Hypso views (ramps, belts, relief, lighting).
- Typed-arc alignment (F1), motion-character tuning — 100% reassignment in 200 Ma is chaotic
  rearrangement, not coherent drift; tune rates/axis coherence AFTER motion is visible, with the
  §4.2 diff gate as the instrument (F2), age-anchored time labels replacing raw tick 0 (F3).
- Plate-frame feature accumulation — the doctrine-correct fix that makes World/Hypso views move
  (mountains riding their plates). That is the NEXT spec after M0 proves the motion channel.
- Accretion / SPH / pre-onset regimes (parked by user 2026-07-06).

## 6. Follow-ups

- **F1:** derive typed boundary arcs from the reassigned membership at the tick (today the
  rotated-onset-arcs construction diverges from nearest-seed membership — visible misalignment
  at t=120M).
- **F2:** motion-character tuning (coherent drift; possibly slower rates / correlated poles).
- **F3:** age-anchored timeline labels ("+120 My after onset" / absolute planetary age).
- **F4:** plate-frame crust accumulation spec (the World/Hypso fix).
