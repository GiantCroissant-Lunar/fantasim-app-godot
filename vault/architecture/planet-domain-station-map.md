# Planet domain — station map (the mandatory route through the architecture)

**Status:** evergreen constitution for attempt #8 recovery (user-directed, 2026-07-06).
**Why this exists:** eight attempts produced "a ball with strips" because domain features kept
being wired AROUND the architectural stations (render-layer proxies, Host-direct shortcuts) —
see `vault/handover/2026-07-06-project-restart-handover.md` §3-4. The bundle-oriented approach,
4-tier services, ECS, Akka.NET, and node graph are FEATURES of the app (the product). This map
makes the route through them explicit and (via P1) mechanically enforced.

## The stations

Every planet-domain feature (continents, elevation, motion, later: climate, biomes) MUST flow:

```
S1 TRUTH        engine (fantasim-world): event-sourced streams, Akka-hosted writers.
                Plate kinematics, crust fields (ContinentalFraction, OrogenicPressure, …) exist
                as truth events / deterministic folds keyed by CanonicalTick.
                Existing anchors: LidFractureAtOnset drafts, CrustStateFolder.FoldAt, OnsetRoster.

S2 NODE GRAPH   App.NodeGraph world-generation graph: every generation/derivation RECIPE enters
                as a graph node options payload (seed, frequency, crust controls,
                continentalPlates/patches). No recipe hardcoded in services or seams.
                Existing anchors: world.options node, WorldGenerationRenderOptions.Resolve.

S3 ECS          App.Ecs (Arch): per-cell state -> per-cell derived products as systems/pure
                system functions. Existing anchor: CellElevationSystem.Derive (the bimodal
                elevation formula LIVES HERE — reuse it, never duplicate it elsewhere).

S4 SERVICES     App.World IService (T2/T3, Godot-free): tick-addressed products only.
                Presentation consumes products; it never touches engine types.
                Existing anchors: GetPlanetPresentationAsync(tick), GetGlobeSnapshotAt(tick),
                GetGlobeBoundaryCellsAt(tick) on the cached reconstructor.

S5 SEAM+BUNDLE  App.Presentation (T4) + collectible bundles: documents -> Godot meshes/materials.
                NO domain math in the seam (no elevation formulas, no truth-derived palettes
                computed from raw state). Existing anchors: PlanetPresentationBinder,
                PlateCapMeshBuilder (contracts tier), world.pck.
```

## Station contracts (checkable statements)

1. **S5 never references engine assemblies or types** (`FantaSim.Geosphere.*`, engine
   `GlobeReconstructor`/`OnsetRoster`/`WorldCrust*`). It sees `IService` products and
   contracts-tier DTOs only.
2. **S5 never reads config directly** (`CrosscutFoundation.Config`): configuration reaches the
   seam as plain values plumbed by the host or as document fields (per the node-graph station,
   recipes belong in graph payloads, not scattered config reads).
3. **Every product the presentation consumes is tick-addressed.** Parameterless product getters
   (e.g. `GetPlanetPresentationAsync()`) are banned in S5 — that is how the frozen-onset-frame
   defect entered.
4. **Per-cell scalar derivation happens in S3** (App.Ecs `Derive`-style pure systems), not in the
   materializer, not in the binder, not in mesh builders.
5. **One canonical continent representation:** the per-cell `ContinentalFraction` truth field
   (S1), seeded via recipe (S2), derived to elevation (S3), served per tick (S4), painted (S5).
   Render-layer continent proxies (noise provinces, plate-membership coloring, ad-hoc palettes
   from plate ids) are BANNED — see the circle map
   (`vault/handover/2026-07-06-project-restart-handover.md` §3.2).
6. **Domain state is evaluated in the MOVING frame** — features ride plates ("Lagrangian motion",
   already the engine's own documented intent in `Geosphere.Crust/CrustInit.cs`). Building
   features on frozen topology violates the locked 2026-06-21 doctrine.

## The two gates (every arc must pass BOTH)

- **Conformance gate (mechanical):** the P1 architecture test suite fails the build when a
  contract above is violated (assembly-reference scans + source scans). See
  `vault/plans/2026-07-06-p1-conformance-gates.md`.
- **Eye gate (scripted):** windowed capture via the remote drive (health → seek →
  select layer → render.screenshot), pixel-diff thresholds + the north-star legibility test.
  An arc without gate evidence in its handover is failed by definition.

## Non-goals of this map

- It does not freeze implementations — stations may be refactored freely; the CONTRACTS hold.
- It does not (yet) route iii/gdext, boom-hud, or timeline UI — planet domain only; extend
  per-domain when those are next touched.
