# P9b signed dry-crust and tunnel framing implementation plan

> **For implementing agent:** Follow `test-driven-development`, `source-driven-development`,
> `unify-build`, and the workspace rules. Preserve the real plate/crust data path; do not replace it
> with a showcase mesh, hard-coded screenshot fixture, fake feature field, or production smoke path.

**Goal:** Make mountain, volcanic arc, trench, ridge, and fault semantics visibly and testably shape
the dry crust, then tune the tunnel so the independently zoomable planet is larger and the two rings
are thinner without acting as a planet aperture.

**Architecture:** `CellElevations` is the authoritative signed crust envelope, already composed from
state-derived elevation and boundary profiles. `CellFeatures` selects only bounded residual fabric
and adaptive emphasis; presentation must not add the same tectonic uplift/depression twice. The same
adaptive `GlobePlateSurfaces` path produces the mesh. Presentation tuning changes only
scale/framing and does not alter canonical history.

**Repository:** `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot`

**Authoritative spec:** `vault/specs/2026-07-13-canonical-world-history-and-dry-crust-design.md`

## Locked acceptance behavior

- In zero-noise real-pipeline, category-specific fixtures: mountain cells are at least 750 m above
  the same real state's no-orogeny/no-profile counterfactual; cells classified `VolcanicArc` from
  transported volcanic state are at least 750 m above the same real state's profile-disabled
  counterfactual; trench cells are at least 750 m
  below zero profiles; and ridge flanks are at least 300 m above zero profiles. These gates need not
  co-occur in one snapshot or tessellation frequency. Fault gets no feature-enum uplift beyond the
  configured transform profile. `BoundaryProfileParameters.Zero` is not a valid mountain baseline
  by itself because it deliberately preserves `OrogenicPressure * OrogenicGain`.
- Trench never selects positive ridged noise. Residual noise is at most 250 m peak and at most one
  third of the smallest mandatory 800 m tectonic boundary signal.
- Geometry remains deterministic and watertight. Hydrology and biome color remain out of this gate.
- Tunnel planet scale is independent, restored on tunnel exit, and starts visibly larger. It may
  project beyond the instrument rings. Both ring bands are thinner and remain pickable using the
  same declared geometry/hit radii.

## Task 1: RED authoritative signed-elevation and finalized-mesh tests

**Modify:**

- `project/tests/App.World.Tests/GlobePlateSurfacesTests.cs`
- `project/tests/App.World.Tests/BoundaryProfileIntegrationTests.cs`
- `project/tests/App.World.Tests/WorldCrustMaterializerTests.cs`
- `project/tests/App.Presentation.Tests/PlateSurfaceReliefFabricTests.cs`

Use formation-specific causal counterfactuals: mountain removes orogenic pressure and profiles;
a transported `VolcanicArc` feature disables its profile (and volcanic state when that state term
is the subject); the feature label alone is not evidence of current active overriding-arc adjacency;
trench and ridge disable profiles. With noise disabled, assert mountain/volcanic target cells gain
at least 750 m, trench cells lose at least 750 m, and ridge flanks gain at least 300 m. Category
fixtures may use different real ticks/frequencies when the deterministic seed does not make all
categories co-occur. Trace those exact source cells through `PlanetPresentationDocument` and
source-triangle provenance to finalized cap/mesh vertex radii after the height lens and displacement
cap; assert the same directions there. Add a
trench residual-profile assertion that `Ridged == false`, deterministic-repeat assertions, and the
250 m / one-third noise bounds. Observe the intended current failures before production edits.

## Task 2: Fix non-snapshot feature transport through the production plate frame

**Modify:**

- `project/plugins/App.World/Crust/PlateFrameSampler.cs`
- `project/plugins/App.World/Services/Service.cs`
- `project/tests/App.World.Tests/PlateFrameSamplerSmoothnessTests.cs`
- `project/tests/App.World.Tests/MotionGateTests.cs`

At arbitrary playhead ticks, read the governing `products.SnapshotTick` material state and transport
it through the exact source-cell plate-frame mapping. Re-derive features from that transported state
plus the current Eulerian assignment/typed boundaries; do not advect topology-bound trench/ridge/fault
markers as material labels and do not query `FeaturesByTick` with the arbitrary playhead tick. Add an
onset+2,500,000 non-snapshot test that matches every public fraction/feature to an independently
sampled source cell and keeps topology-bound features incident to the current matching frontier.

## Task 3: GREEN bounded residual fabric without duplicate uplift

**Modify:**

- `project/contracts/App.World/Dto/WorldDtos.cs`
- `project/contracts/App.World.Rendering/Globe/TectonicDetailSampler.cs`
- `project/plugins/App.Presentation/PlateSurfaceMeshFactory.cs`
- `project/plugins/App.Presentation/PlateSurfaceReliefFabric.cs`
- `project/plugins/App.World/Services/Service.cs`

Introduce a contract-owned feature-kind enum, but map `CrustFeatureKind` to it with an explicit
exhaustive switch plus unknown-value behavior; never ordinal-cast across packages. Do not add signed
feature displacement in presentation. Keep sign/broad morphology in `CellElevations`. Give trenches
non-ridged, zero-mean residual fabric and reduce every residual profile to at most 250 m peak and no
more than one third of the smallest mandatory boundary-profile signal. Other feature kinds may vary
frequency/roughness only within that bound.

Thread the bounded sampler through the existing adaptive surface builder. Do not bypass
`CellElevations`, `BuildAdaptiveSurfaces`, the displacement cap, or vertex provenance. Add no
presentation constants for tectonic widths/amplitudes; those remain data in
`BoundaryProfileParameters`.

## Task 4: Lock real crust-pipeline causality and dry-mode fixture

**Modify:**

- `project/tests/App.World.Tests/WorldCrustMaterializerTests.cs`
- `project/tests/App.World.Tests/MotionGateTests.cs`

Run the real generated crust pipeline at selected ticks and locate actual cells of each available
feature kind. Independently compute the formation-specific counterfactual delta for those cells,
assert its sign/range, then trace the same cell IDs through `CellElevations`, the presentation
document, and finalized mesh. `CellFeatures` is a lineage tag and residual-profile selector, not the
source of another renderer offset. Use the default patch recipe (seed 0, five patches) and lock real
category-specific frequency/tick fixtures; quantitative mountain, trench, volcanic-arc, and ridge
gates do not have to coexist in one snapshot. Keep an all-four public document as lineage/visual
proof when available, without misreporting a zero-profile mountain delta as total mountain relief.
Name one plate pair whose boundary classification changes at the
later tick and assert the expected downstream feature and elevation direction for its cells. Assert
effective hydrosphere mode is `Absent`.

## Task 5: RED/GREEN larger independent planet and thinner rings

**Modify:**

- `project/plugins/App.Timeline.Seam/TunnelPlanetZoom.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelCameraFraming.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Rings.cs`
- `project/tests/App.Timeline.Tests/TunnelPlanetZoomTests.cs`
- `project/tests/App.Presentation.Tests/TunnelCameraFramingSafeBoundsTests.cs`
- `project/tests/App.Presentation.Tests/TunnelInstrumentContractTests.cs`
- `project/tests/App.Presentation.Tests/TunnelInputPolicyTests.cs`

Lock `TunnelPlanetZoom.DefaultScale = 1.35f`, retain the current 0.35..3.0 clamp, set exact inner and
outer ring widths to 0.08 and 0.10 world units, and keep hit bands at those same radii (minimum width
0.08). At 4:3, 16:9, and 21:9, require the default projected planet radius to exceed the projected
inner aperture by at least 5% while camera/readouts stay inside their current safe rectangle. Apply
the default zoom immediately after capture and restore the exact original scale on ordinary disable,
repeated enable/disable, disable-before-capture, rebind, cancellation/exception, bundle teardown,
and a second enable (which must not recapture the already-zoomed scale). Tune radii/readout offsets
together; do not couple maximum planet zoom to either ring radius.

## Task 6: Focused and full verification

Run focused `App.World.Tests`, `App.Presentation.Tests`, and `App.Timeline.Tests`, then the repository
build/test workflow through `dotnet unify-build`. Preserve watertight seam, adaptive subdivision,
camera framing, hit arbitration, teardown, and collectible-ALC tests.

## Task 7: Exported-app visual gate

The feature enum and `TectonicDetailSampler` live in resident contract/rendering assemblies, so first
build/stage a full common bundle/export and restart into that build; merely reloading `world.pck` is
not evidence. Keep the restarted export open, then exercise collectible PCK reloads. Capture fresh
same-camera screenshots at actual generated ticks/orientations with real mountain, trench, and
volcanic cells. Record feature cell IDs and projected screen locations plus finalized mesh vertex
min/max radii, noise-off versus feature-on values, planet zoom scale, ring widths, effective
hydrosphere mode `Absent`, and confirmation that no biome/hydrology material is active. Finish with
successful collectible-ALC unload/collection evidence.

## Agent handoff

Do not commit or push. Write `AGENT-SUMMARY.md` in the assigned worktree with changed files, RED and
GREEN commands/results, selected visual tick/feature cells, screenshot/log paths, assumptions, and
remaining failures. Stop if the only way to pass would be fake production geometry or a weakened
directional assertion.
