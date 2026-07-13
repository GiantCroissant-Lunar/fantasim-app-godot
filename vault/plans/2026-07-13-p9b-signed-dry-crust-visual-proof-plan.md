# P9b signed dry-crust and tunnel framing implementation plan

> **For implementing agent:** Follow `test-driven-development`, `source-driven-development`,
> `unify-build`, and the workspace rules. Preserve the real plate/crust data path; do not replace it
> with a showcase mesh, hard-coded screenshot fixture, fake feature field, or production smoke path.

**Goal:** Make mountain, volcanic arc, trench, ridge, and fault semantics visibly and testably shape
the dry crust, then tune the tunnel so the independently zoomable planet is larger and the two rings
are thinner without acting as a planet aperture.

**Architecture:** `CellElevations` remains the broad crust envelope. `CellFeatures` adds a signed,
feature-specific deterministic signal before bounded procedural fabric. The same adaptive
`GlobePlateSurfaces` path produces the mesh. Presentation tuning changes only scale/framing and does
not alter canonical history.

**Repository:** `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot`

**Authoritative spec:** `vault/specs/2026-07-13-canonical-world-history-and-dry-crust-design.md`

## Locked acceptance behavior

- Mountain and volcanic arc are positive; trench is negative; ridge has positive crest/flanks;
  fault defaults to zero vertical signal.
- A zero-noise configuration still produces those signs. Trench never selects positive ridged
  noise. Noise at a tagged cell is strictly bounded below that feature's tectonic signal.
- Geometry remains deterministic and watertight. Hydrology and biome color remain out of this gate.
- Tunnel planet scale is independent, restored on tunnel exit, and starts visibly larger. It may
  project beyond the instrument rings. Both ring bands are thinner and remain pickable using the
  same declared geometry/hit radii.

## Task 1: RED signed feature-profile tests

**Modify:**

- `project/tests/App.World.Tests/GlobePlateSurfacesTests.cs`
- `project/tests/App.Presentation.Tests/PlateSurfaceReliefFabricTests.cs`

Add one-cell-center and final-vertex tests with noise amplitude zero. Compare each feature against
the identical no-feature baseline: mountain > 0, volcanic > 0, trench < 0, ridge > 0, fault == 0.
Add a trench profile assertion that `Ridged == false`, a deterministic-repeat assertion, and a
bound proving `abs(noise contribution) < abs(feature signal)` for all non-fault tagged kinds.
Observe current failures: zero noise disables the sampler and active trenches take the ridged path.

## Task 2: GREEN signed tectonic detail before fabric

**Modify:**

- `project/contracts/App.World/Dto/WorldDtos.cs`
- `project/contracts/App.World.Rendering/Globe/TectonicDetailSampler.cs`
- `project/plugins/App.Presentation/PlateSurfaceMeshFactory.cs`
- `project/plugins/App.Presentation/PlateSurfaceReliefFabric.cs`

Introduce a contract-owned feature-kind enum matching the engine semantic values and expose a
signed feature displacement in `TectonicDetailProfile`. Evaluate the feature signal even when noise
amplitude is zero. Use deterministic magnitude shaping with finite clamps: broad positive mountain,
localized stronger volcanic peak, narrow negative trench, positive ridge (an axial notch may be
added only if the flanks stay positive), and zero fault. Feature sign must not be derived from random
noise. Give trenches non-ridged bounded fabric; reduce/bound world and diagnostic noise so it cannot
dominate tagged feature signal.

Thread the combined signed sampler through the existing adaptive surface builder. Do not bypass
`CellElevations`, `BuildAdaptiveSurfaces`, the displacement cap, or vertex provenance.

## Task 3: RED real crust-pipeline causality tests

**Modify:**

- `project/tests/App.World.Tests/WorldCrustMaterializerTests.cs`
- `project/tests/App.World.Tests/MotionGateTests.cs`

Run the real generated crust pipeline at selected ticks and locate actual cells of each available
feature kind. Prove the returned `CellFeatures` changes signed final displacement at those cells
relative to the same `CellElevations` with features removed. Prove changing the plate-motion tick
changes at least one boundary classification/feature/elevation for the accepted fixture. Tests may
skip only a feature kind the deterministic fixture demonstrably cannot produce; the visual fixture
must be selected so mountain, trench, and volcanic arc are all present.

## Task 4: RED/ GREEN larger independent planet and thinner rings

**Modify:**

- `project/plugins/App.Timeline.Seam/TunnelPlanetZoom.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelCameraFraming.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.cs`
- `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Rings.cs`
- `project/tests/App.Timeline.Tests/TunnelPlanetZoomTests.cs`
- `project/tests/App.Presentation.Tests/TunnelCameraFramingSafeBoundsTests.cs`
- `project/tests/App.Presentation.Tests/TunnelInstrumentContractTests.cs`
- `project/tests/App.Presentation.Tests/TunnelInputPolicyTests.cs`

First add tests requiring a default tunnel scale greater than 1, exact restoration of the captured
original scale, ring band widths materially below the current 0.20/0.25, consistent visual/hit
radii, and projected planet bounds that extend beyond at least the inner-ring aperture while the
camera/readouts remain safe at supported aspect ratios. Then apply the default zoom immediately
after capture, retain wheel zoom/clamps, and restore on every disable/teardown path. Tune radii and
readout offsets together; do not couple maximum planet zoom to either ring radius.

## Task 5: Focused and full verification

Run focused `App.World.Tests`, `App.Presentation.Tests`, and `App.Timeline.Tests`, then the repository
build/test workflow through `dotnet unify-build`. Preserve watertight seam, adaptive subdivision,
camera framing, hit arbitration, teardown, and collectible-ALC tests.

## Task 6: Exported-app visual gate

Keep the exported Godot app open per the workspace bundle rule. Stage/reload changed PCKs into the
live process and capture fresh screenshots at actual generated ticks/orientations with real
mountain, trench, and volcanic cells. Record logs containing tick, counts by feature kind, minimum
and maximum base elevation, signed feature displacement extrema, noise extrema, final displacement
extrema, planet zoom scale, and ring widths. The accepted frame must show gray/faceted dry crust,
positive mountain/volcanic relief, negative trenches, a larger planet extending beyond the ring
aperture, and thinner rings. Hydrology and biomes stay off. Finish with successful ALC unload and
collection evidence.

## Agent handoff

Do not commit or push. Write `AGENT-SUMMARY.md` in the assigned worktree with changed files, RED and
GREEN commands/results, selected visual tick/feature cells, screenshot/log paths, assumptions, and
remaining failures. Stop if the only way to pass would be fake production geometry or a weakened
directional assertion.
