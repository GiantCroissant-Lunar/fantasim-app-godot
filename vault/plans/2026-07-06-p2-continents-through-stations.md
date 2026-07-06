# P2 — continents through the stations (detailed plan)

**Parent:** `2026-07-06-attempt8-recovery-roadmap.md` · **Prereq:** P1 conformance suite green.
**Goal:** the ONE canonical continent field (`ContinentalFraction`) flows S1→S5 — organically
seeded, evaluated in the moving plate frame, painted via the existing ECS bimodal formula — and
the render proxies are retired. After P2, the Continents view shows SEVERAL land masses with
stable shapes drifting/colliding, colored from truth-derived elevations, honoring the waterless
lock (`e3b84ef`) via `CellElevationHydrosphereMode.Absent`.

Fixes (from the circle map): defect 1 seeding + defect 2 frozen frame. Defect 3 (calibration) = P3.

## Packet E (engine repo `fantasim-world` — dispatchable, pure .NET, TDD)

**E1. Patch-based crust seeding** — `project/plugins/Geosphere.Crust/CrustInit.cs`:
- Add `CrustPatchRecipe(int Seed, int PatchCount, double MeanAngularRadiusRad, double RadiusJitter, double EdgeNoiseAmplitude)`
  alongside `CrustInitRecipe` (do NOT remove the plate-set recipe — existing tests/streams use it).
- `CrustInitializer` gains an overload: per-cell initial `ContinentalFraction` from patches —
  deterministic from Seed: place PatchCount centers (SplitMix64 like ConvectionCenters — same
  PRNG idiom), fraction = smoothstep over (angular distance to nearest center vs its radius) with
  edge noise so coastlines are organic, clamped [0,1]. **Use UnifyMaths/UnifyGeometry primitives**
  (house rule — no hand-rolled spherical math).
- Tests (`Geosphere.Crust.Tests`, mirror existing style): determinism (same seed ⇒ identical
  field); patch count respected (connected components of fraction≥0.5 == PatchCount for a
  non-degenerate seed); fraction bounded; patches are sub-plate scale (largest component < the
  largest plate's cell count for default params).

**E2. Nothing else in the engine.** Evolution/folding already carries fraction as state.
Publish flow: pack via `dotnet unify-build` per the unify-build skill; app consumes per the
hybrid projectref/package policy — for P2, the app repo builds against the LOCAL feed pin
(bump per `nuget-feed-sync` conventions).

## Packet A (app repo `fantasim-app-godot` — dispatchable after E lands, TDD)

**A1. Recipe enters via the node graph (S2).**
`WorldCrustRunSpec.ReadRecipe` (`project/plugins/App.World/Crust/WorldCrustRunSpec.cs:204`):
accept `continentalPatches: { seed, count, meanRadiusDeg, radiusJitter, edgeNoise }` in the
`world.options` payload → `CrustPatchRecipe`; keep `continentalPlates` (legacy) and the
`Continental(0,1)` fallback for compat. Default when absent: **patches** (count 5, seed = world
seed) — the new default IS the fix. Update `WorldGenerationNodeCatalog` option docs.

**A2. Moving-frame evaluation (S3/S4) — the frozen-frame fix.**
New pure type `project/plugins/App.World/Crust/PlateFrameSampler.cs`:
`SampleAt(tick)`: for each cell at `tick` (reassigned membership from the cached reconstructor),
inverse-rotate the cell center by its plate's Euler rotation delta (tick − onset), map to the
nearest onset cell (`GeodesicSphereTessellation` lookup), and read THAT cell's accumulated crust
state. Continents therefore ride plates with stable shapes ("Lagrangian motion" —
the engine's own documented intent).
- `Service.BuildPlanetPresentationRuntime` (`Service.cs:401`): replace the
  `globeAtOnset`-anchored `BuildSurfaceData`/features/sections inputs with plate-frame-sampled
  state at `arcTick`; elevations still computed by `CellElevationSystem.Derive` (S3 — do NOT
  duplicate the formula) with `HydrosphereMode.Absent`; boundary contributions built from
  `BuildBoundaryArcsAt(arcTick)` (arcs and membership at the SAME tick).
- The light path (`GetGlobeSnapshotAt`) is unchanged; add `GetCellProductsAt(tick)` (or extend
  the existing document flow) so the Continents view can fetch per-tick elevations WITHOUT crust
  re-materialization per scrub — reuse the crust result cache keyed by snapshot tick (spacing
  already exists via `CrustSnapshotTickSeries`), advection applied per tick on top.
- Tests: shape preservation (a patch's cell-set at onset, rigidly rotated by its plate,
  ≈ the sampled patch at onset+20M — Jaccard ≥ 0.8 modulo cell quantization); frontier
  consistency (coastline derives from the SAME sampled field); determinism.

**A3. Fraction-driven Continents view + proxy retirement (S5).**
- `PlanetPresentationBinder.BindPlateSurface`: Continents branch colors from per-cell products —
  land/ocean by sampled fraction (threshold 0.5) with `ContinentsPalette` tones; coastline tint =
  fraction contour cells (NOT plate frontier). Plate-membership coloring (lap 4) is REMOVED.
  `GetGlobeBoundaryCellsAt` stays for diagnostics/PlateIdentity only.
- `MotionGateTests` update: membership-floor test stays; ADD patch-motion gate — patch centroid
  angular displacement across the window matches its plate rotation (>0, and shape stable per A2).
- Conformance suite (P1) must stay green — this packet must not add seam-side domain math.

## Gates (both, evidence into the roadmap progress log)

1. Unit: E tests + A tests + MotionGateTests green; conformance green.
2. Eye gate (scripted, reuse `m0-windowed-gate.sh` pattern): Continents view at 5 ticks across
   the window — expect: ≥3 distinct land masses; per-step pixel diff ≥ 10%; and NEW: a chosen
   patch is trackable by eye across frames (stable shape, moved position). Screenshots attached
   to the progress log; user judges the feel.

## Explicitly out of scope (P3/P4)

Ramp calibration/bands (P3, needs measured histogram of P2's elevations); tessellation/jaggies
(P3); World/Hypso view rewiring onto moving data (P3 — do not touch the locked World-view look);
rates/coherence tuning + Play sweep (P4); ProvinceTint removal decision (P3, whitelisted in C4).
