# Session record — world-gen foundation rebuild + crust geology (2026-06-20)

Detailed record of what was built this session. Companion: `2026-06-20-world-gen-crust-handover.md`
(how to continue). Design docs: `fantasim-world/vault/architecture/canonical-foundation.md`,
`fantasim-world/vault/architecture/crust-geology.md`,
`fantasim-app-godot/vault/architecture/world-generation-cartography-flow.md`.

## Why this session happened
This is roughly the **5th attempt** at the FantaSim world generator. Across prior attempts,
**plate motion over time never looked right** and **mountains/volcanoes never appeared as expected**;
concepts kept accreting while old code didn't evolve. The session began as a direction review and
became a foundation-first rebuild: prove a correct, reproducible motion/reconstruction spine, then a
canonical scale/time language, an extensible field system, and finally evolving crust geology — each
verified green before the next.

## The diagnosis (why it failed before)
- **Time was never a first-class axis.** Three competing "state over time" representations
  (truth-stream events / per-tick field reduction / ECS mutate-a-tick-component-and-rerun) never
  reconciled; the Euler-pole reconstruction solver existed in ref but was wired into nothing.
- Plate *position over time* is a **continuous transform** (integrate Euler rotation from a
  reference) — a trajectory — but everything modeled it as snapshots reduced per tick.
- **THE likely root cause (discovered while building):** a **two-plate world is a closed boundary
  ring, and a single rigid Euler rotation nets to ZERO mean normal rate across any closed ring**
  (antipodal cancellation, verified ≈1e-20). On two plates, convergence/divergence cancels to
  nothing — no mountains can form. **Fix: ≥3 plates** → open boundary arcs between triple junctions.

## What was built (in order, all green)

1. **Node-graph review (start).** Reviewed the in-flight `App.NodeGraph` + iii migration. Found the
   load-bearing issue for a future World provider: the executor threads JSON between nodes, but World
   operators pass rich CLR objects incl. an `ITruthEventDraft[]` **interface array** that cannot
   System.Text.Json round-trip. Decision: **run-scoped handle tokens** for inter-operator object
   graphs, JSON only for params/sink.

2. **`world-stage` — reconstruction kernel** (new repo `yokan-projects/world-stage`). A native C#
   USD-inspired substrate: `Stage`/`Prim`/`Attribute` + `TimeSamples<T>` (Slerp for quats) +
   `EulerPoleOp` + **plate-circuit hierarchy** (`Motion.ReconstructOrientation`, parent-on-left
   composition). = the GPlates reconstruction kernel (finite rotations composed up a plate circuit,
   resolvable at any continuous time). **78 tests.** Self-contained (no Unify/OpenUSD deps).

3. **Truth-stream-backed reconstruction proof** (new repo `yokan-projects/world-stage-proof`).
   Finite rotations committed as **hash-chained truth events** under a 5-axis stream id
   (`demo:main:L0:geosphere:plates`), materialized, reconstructed over 100→0 Ma → `reconstruction.json`.
   **11 tests.** First interactive time-scrubber shown (placeholder orthographic globe).

4. **Cartography projection** (new repo `yokan-projects/fantasim-cartography`, curated-port of the
   read-only ref). Minimal projection subset (Shared.Contracts + Projection.Contracts minus the
   World.Export CRS files + Projection.Core Equirectangular/Orthographic/SphericalGlobe minus the
   RegistryArchi catalog). 100% managed, no native, no PlanetBodyModel needed. **11 tests.** Then the
   reconstruction was rendered through the REAL `EquirectangularProjection` → `reconstruction.map.json`
   (**11 tests**). Cartography "completes" reconstruction (it's the projection/output half).

5. **Harden — deterministic event ids.** `EventId = Guid(SHA256(canonical content seed)[0..16])`
   (content = stream id + sequence + tick + eventType + payload + prevHash), replacing
   `Guid.NewGuid()`. Hash chain is now **reproducible** (same head hash + byte-identical reconstruction
   across runs). Contract unchanged. **16 tests.**

6. **Promote into fantasim-world.** New plugins `Geosphere.Plate.Rotation.Stream` (payload+codec+draft,
   mirrors `PlateSeeds.Stream`) and `Geosphere.Plate.Reconstruction` (materializer over the kernel).
   `world-stage` packed to the feed as `GiantCroissant.WorldStage 0.1.0`. (6+6 tests.)

7. **Canonical foundation** (curated-port from ref into `fantasim-world`). `World.Shared/Quantities`
   = the **scaling ladder** (`OdometerLadder`: two-letter base-26 codes, anchor `ka`, **×1000/step**,
   dimension-agnostic; `CanonicalQuantity` + `CanonicalScaleProfile` + `BaselineScaleProfiles`).
   `World.Shared/Time`+`Units` = canonical time (`UnitConverter`, **100,000 ticks = 1 Ma**, via
   `CanonicalTick` from TimeDete). `Mythosphere` + `Mythosphere.Cosmology` = the **root**
   (`CosmologyManifest` embeds CanonicalScale + GenesisTick + Laws). `World.Export` (CanonicalScale).
   **155 tests.** (Express time/scale via the ladder, e.g. `100 ka CTU`, not raw "Ma".)

8. **Full field system** (built on the existing 7 reducers + registry + catalog + validator).
   New plugins `World.Fields.Catalog` (**JSON-schema field descriptors** — register a field from a
   schema string with NO recompile; per-sphere catalog modules Geosphere/Atmosphere/Hydrosphere;
   module registry; `ReduceFieldsOperator` cross-layer reduction driver; `CanonicalQuantityMapper`)
   and `World.Fields.Stream` (`FieldContribution` codec + `FieldStateMaterializer` truth-stream fold).
   **Field VALUES stay scalar** (reduction sound); **DEFINITIONS are JSON-schema** (extensible — add a
   property anytime). Sphere-prefixed field ids. **319-test suite green; truth-stream field round-trip
   deterministic.** (This is the "properties as JSON-schema fields, computed in/cross layer" the user
   wanted, and the coherence the locked design reached for.)

9. **Crust geology** (the crux). `Geosphere.Plate.Topology` (geodesic cells via unify-cell →
   nearest-seed plate assignment → boundaries + junctions → **divergent/convergent/transform**
   classification lifted from ref `RigidBoundaryVelocitySolver`, **15 tests**) and `Geosphere.Crust`
   (crust as accumulating JSON-schema fields `continental-fraction`/`orogenic-pressure`/
   `volcanic-activity`/`crust-age`; `CrustEvolutionOperator` emits per-tick deltas → truth-stream →
   `CrustStateFolder` accumulates; feature derivation Mountain/VolcanicArc/Trench/Ridge/Fault; **22
   tests**). Crust rides plates (Lagrangian); features GROW over time. Proven: orogenic pressure
   1→11→21→31→41; mountain emerges at threshold; 3-plate active (orogenic 528) vs 2-plate cancellation
   (0). Emitted `world-stage-proof/crust.json`; shown as the payoff time-scrubber (mountains form at
   the convergent boundary as plates move). Verified geodesic tessellation works on macOS **without
   the native Geogram kernel** (managed icosphere; Voronoi later via the same `ITessellation` seam).

10. **App wiring — Phase 1 (C#, in `fantasim-app-godot`).** `WorldFunctionProvider`
    (`world.*`/`geosphere.*`/`crust.*`) with `crust.generate` running `PlateTopologyBuilder →
    CrustPipeline`, + `CrustGenerationGraph` recipe, registered in `Host.ComposeWorld` (mirrors
    `ComposeIii`; zero collision with the iii agent's prefixes). Through the real `GraphExecutor`:
    1280 cells, 3 boundaries, 48 mountains, peak orogenic 9.0. **3 App.World tests; the Godot host
    builds headless (0 errors).** (Phase 2 = the Godot visual render — NOT done.)

## Artifacts
- **New repos:** `world-stage`, `world-stage-proof`, `fantasim-cartography` (all under yokan-projects).
- **Feed packages added:** `UnifyMaths*`/`UnifyGeometry.*`/`UnifyCell.*` @1.0.0 (managed geodesic
  chain), `GiantCroissant.WorldStage 0.1.0`, all `GiantCroissant.FantaSim.*` re-packed @**0.1.1**
  (22 packages — 0.1.0 was cache-poisoned, so bumped one patch).
- **Commits:** world-stage `3ca65dd`; world-stage-proof `243f085`,`754a255`; fantasim-cartography
  `6128970`; fantasim-world `1794982` (det ids), `f8fc580` (foundation+fields+reconstruction),
  `4979cd9` (topology+crust); fantasim-app-godot `6627d8b` (design doc). **Phase 1b app changes
  (provider+recipe+Host.cs+props+tests) were uncommitted at pause** (commit at session end).

## Key decisions (with rationale)
- **Foundation-first.** Build the spine/scale/time/fields right before geology (prior attempts
  accreted on a missing spine). User: "the mountains will come."
- **Preserve the truth-stream.** 5-axis stream id (`variant:branch:L{LLevel}:domain:model`) + SHA-256
  hash chain stay authoritative; the new substrate is the reconstruction layer over it. **Resolution
  is NOT a stream-id axis** (it's a reconstruction/cartography LOD concern).
- **Properties = JSON-schema fields, not enums** (continental/oceanic → a `continental-fraction`
  field). Values scalar (reducible); definitions schema-driven (extensible).
- **Crust ≠ plate.** Crust is carried material (a plate has both continental + oceanic); crust is a
  focusable Geosphere layer rendered with exaggerated relief.
- **USD as substrate, GPlates as domain model, OSM (later) for features.** Science-first; tunable for
  fantasy/wuxia.
- **Handle tokens** (not JSON) for rich inter-operator objects in the node graph.
- **ref-projects is read-only** → curated-port (copy out, never edit).
