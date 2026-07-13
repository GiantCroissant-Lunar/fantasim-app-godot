# Agent summary

## Tunnel timeline — 2026-07-12

Implemented and runtime-gated across three arcs: the rotating two-ring prototype, an interior-view
+ HUD-ownership amendment, and the asymmetric cockpit refinement. The current geometry/framing
constants below supersede the earlier prototype's oblique-spectator values.

Durable decisions:

- The tunnel is a real hollow cylinder along local Z (`TunnelCameraFraming`): mouth at Z=0, the
  current-time plane at Z=−5 where the real globe is re-anchored (large at frame center), throat at
  Z=−20, `TunnelRadius` 5. Curved corridors carry real filmstrip content from the current plane
  toward the throat; `TunnelCorridorDepthPolicy` keeps time-bearing walls on `[currentPlane, throat]`
  so none crosses in front of the current plane into the near field.
- Exactly two physical controls. The outer ring owns canonical coarse time; one clockwise revolution
  is exactly `+1 kb`. The single inner ring belongs to the focused track and resolves that real
  descriptor's rung. Both are dials on a small camera-relative **instrument plane** (anchor
  `(-2.2, 0, -4)`, radii 0.38–0.82), not annuli on the physical mouth.
- The visible window is five unique tracks; the focus slot sits **left** of center
  (`LeftFocusAngleDegrees = 180`). Wall rotation is cyclic, clockwise-positive, snapping in 30° steps.
- The inner ring is view-only: it moves a magnified axial fine cursor and signed readout but never
  calls `ITimelineController.PushTick` or persists an offset.
- The interior camera sits near-axial inside the mouth (`LocalPosition (-2.0, 0.6, -0.8)`,
  `LocalTarget (-2.0, 0, -7)`, FOV 60°). Enabling the tunnel **hides the 2D HUD**; F9 routes through
  the `timeline.tunnel_view` command so `TimelinePlugin` owns that HUD visibility, and compose
  re-applies it on every world rebind.
- Input is owned in `_Input` and consumed via `SetInputAsHandled()` to preempt globe orbit. Wall
  gesture angle is measured about the **tunnel axis** (via `TunnelRayHitMapper`), while the two ring
  dials are measured on the instrument plane. A wall pick the opaque planet occludes is rejected.
- World/stage teardown severs input/controller/registry/filmstrip references before detaching the
  mount; `TunnelLossSequence` fences HUD-prep-before-geometry teardown in a `finally` so a HUD-prep
  throw can never skip teardown and pin the ALC. A live enabled reload emits
  `Hot-reload: old ALC collected for bundle world` and the next binder remounts.
- Current production descriptors are all rung `ka`; generic rung rebinding is tested without
  inventing production data.

Evidence:
[`.../2026-07-12-rotating-tunnel-two-ring-prototype/`](vault/specs/evidence/2026-07-12-rotating-tunnel-two-ring-prototype/README.md),
[`.../2026-07-12-tunnel-interior-view-gate/`](vault/specs/evidence/2026-07-12-tunnel-interior-view-gate/README.md),
[`.../2026-07-12-tunnel-review-hardening/`](vault/specs/evidence/2026-07-12-tunnel-review-hardening/README.md).

### Review + hardening pass — 2026-07-12 (late)

A three-agent review of the shipped tunnel drove a bugs + perf + hardening pass (all unit-gated;
full suite green). Fixes: `TunnelFineRequestScheduler` latest-wins hole on re-offering the
cancelled-active key; non-finite guards in `TunnelFinePreviewMapper.Map` and
`TunnelCorridorLayout.SnapFocus` (matching `TunnelScrubMapper`); `TunnelLossSequence` try/finally;
`TunnelInputRelay` now surfaces handler exceptions (new `OnError`) and fails closed instead of
leaking a gesture into globe orbit; wall carousel angle measured about the tunnel axis; planet
occlusion in hit-resolution (`TunnelRayHitMapper.TryIntersectSphere`); nested-press consumed;
per-motion logging demoted to Debug; per-tick corridor activity-style rewrite cached behind an
active-flag mask; `UpdateRingLabels` made idempotent. **Owed:** the live windowed gate (real mouse,
screenshot, ALC-collection) on a fresh export, since the seam changes are resident.

Deferred (design decisions, not landed): moving press acceptance out of `_Input` (would risk the
gated globe-preemption); scroll-wheel/keyboard dial control, hover feedback, and inner-ring
accumulator saturation (input-model enhancements).

Open product question for the next visual refinement: after judging the running view, should the
fine ring (1) mutate shared world time, (2) create a layer-local time offset, or (3) remain a
view-only inspection? Do not infer an answer from the current view-only behavior.

## P9a app truth-stream adoption — 2026-07-13

App.World now uses one history architecture in project-reference and package modes. The old
`WorldRuntime`/stub split and raw-text playback path are gone; `WorldHistoryCoordinator` owns the
app-side field/history projections while the service owns/disposes the injected truth reader,
writer, and store in coordinator → writer → store order.

Rotation imports use a recoverable prepare → plate CAS → bind protocol. Imported playback is
materialized only from committed truth through the exact bound cursor. A stable app-owned
`app:main:L0:world:rotation-bindings` stream records active authority: imported markers contain
only the exact bound-control cursor, and generated selections have their own canonical marker.
Dispose releases the projection without changing that durable selection. A reconstructed
coordinator verifies the index chain, rereads the referenced prepared/bound control events, proves
the plate prefix and batch, and rebuilds the materialized provider without raw-source reimport.

The shared pre-bind/replay/reload verifier requires:

- canonical prepared/bound metadata with exact bound context inputs `[preparedCursor, plateCursor]`;
- `C.Sequence = (H0?.Sequence ?? -1) + N`, a complete hash-valid genesis→H0 prefix, and the exact
  contiguous H0+1…C batch;
- recomputed event hashes, canonical plate payload bytes, candidate byte equality when raw source
  is available, the prepared ordered-draft digest, and exact terminal hash/tick C.

This rejects hash-valid stale-prefix and different-batch bindings, malformed cursor contexts,
missing index references, and earlier-prefix corruption before an immutable bound event is
written. A bind→active-index interruption does not report success; retrying the same raw source
converges and publishes the index. Prepared and bound control-event envelope ticks must exactly
equal their payload onset tick.

Durable selection and its in-memory provider projection share one authority gate, including
atomic `GetActiveRotationProjection` readers and disposal. A reader cannot cross the interval after the
selection CAS but before its cache update, and an earlier switch rereads terminal selection before
applying its projection so a newer re-entrant selection cannot be overwritten.

The T1 boundary is enforced by a real collectible-ALC test. It recursively walks returned object
graphs (including `DictionaryEntry` and `KeyValuePair<,>` values), rejects a negative-control
`Dictionary<string, object>` containing a collectible service, disposes the service, and requires
the weak ALC reference to collect. The returned world field DTO graph is resident-contract owned.

Evidence: `RotationImportRecoveryTests` + `WorldHistoryBuildModeContractTests`; 26/26 focused,
598/598 full App.World tests with `UseProjectReferences=true`, and 598/598 with
`UseProjectReferences=false`. `dotnet tool restore` and the configured `dotnet unify-build Compile`
gate also succeed (the current build config has no compile project groups, so the two full test
modes are the meaningful compile/runtime proof).

## P9b signed dry-crust review correction — 2026-07-13

- The original universal default-minus-`BoundaryProfileParameters.Zero` quantitative comparator
  was causally wrong for Mountain: zero profiles intentionally retain the authoritative
  `OrogenicPressure * OrogenicGain` elevation. The accepted gate now uses formation-specific
  counterfactuals: Mountain removes orogenic pressure and profiles; a transported `VolcanicArc`
  feature uses a real profile-disabled baseline; Trench and Ridge use zero profiles. The field-driven
  `VolcanicArc` label alone does not prove current active overriding-boundary adjacency. Thresholds remain unchanged
  (mountain/volcanic +750 m, trench -750 m, ridge +300 m), and category fixtures need not co-occur.
- The deterministic frequency-2/tick-200M public document remains the all-four lineage and visual
  fixture. Its Mountain label is transported/state-derived rather than proof of a current collision,
  and its zero-profile delta is not misreported as the mountain signal. A frequency-4/tick-200M
  public cell supplies a separate quantitative active-arc proof with current overriding topology.
- Boundary arcs are local real shared tessellation edges. An incident cell has exact zero
  footprint-to-arc distance; nonincident cells retain centroid-to-arc distance as an interior guard.
  At a multi-edge junction, exact-distance ties resolve by semantic priority
  Convergent &gt; Divergent &gt; Transform &gt; Inactive, then normalized plate pair and canonical
  endpoint geometry; input order cannot change the selected sample.
- Locked quantitative/mesh fixtures after the correction:
  - lower frequency-2/tick-67M: Mountain 85 is a current convergent collision, is +2000 m
    default-minus-zero, and exceeds +750 m versus no-orogeny/no-profile (mesh outward); Ridge 196
    is current divergent and +400 m (outward). The old lower “Trench 182” fixture is retired:
    corrected current semantics classify cell 182 as Ridge, so it cannot truthfully prove trench;
  - public frequency-2/tick-200M all-four document: Mountain 134 has a +40000 m state-derived
    signal versus no-orogeny dry crust (outward), while its default-minus-zero profile delta is
    honestly 0; transported VolcanicArc 137 is +400 m (outward); corrected Eulerian fixtures replace
    old Trench 40 / Ridge 224 with current subducting Trench 4 at -2000 m and current divergent
    Ridge 40 at +400 m;
  - public frequency-4/tick-200M: VolcanicArc 803 is also current Convergent/non-collision on
    overriding plate 1 (subducting plate 0), is +1336.1 m versus zero profiles, and remains outward
    in the finalized adaptive mesh.
- Canonical selection now drives visible and materialized behavior end to end. The coordinator
  exposes an atomic `(RotationAuthorityIdentity, provider)` projection; imported identity hashes the
  canonical bound cursor, and generated identity is `generated:v1`. Globe and crust cache keys plus
  persisted crust-cache schema v2 include that identity, preventing generated/imported aliasing.
  Imported rotation changes globe ownership/arcs, boundary deposits/features, signed elevations,
  and exact replay after coordinator reconstruction. Every public rotation-dependent query captures
  the projection once and threads it through reconstruction, materialization, cache identity, and
  sampling, so concurrent selection changes cannot mix authorities. Imported authority without a
  provider (including an onset mismatch after recovery), and generated authority with an imported
  provider, fail closed.
- `.rot` scope remains intentionally narrow: it supplies finite rotations for matching ids on the
  procedurally generated onset roster and geometry. Authored `.rot` ids absent from that roster have
  no geometry to drive; generated roster ids absent from the materialized provider retain identity
  rotation and a zero-rate pole. It does not import Earth's authored plate polygons;
  GPML/shapefile topology normalization remains future Phase E.
- Imported finite-rotation kinematics use world-frame `later * inverse(earlier)` deltas in radians
  per canonical tick, central differences in-range, one-sided derivatives at exact keyframe
  endpoints, and an intentional stationary pole strictly outside the authored finite range.
- Frame doctrine is explicit: the Eulerian globe classifies reassigned ownership on fixed world
  centers plus current poles (a RED parity test caught the prior double rotation); Lagrangian crust
  classifies onset-owned original centers after rotation. The non-snapshot gate now checks every
  target cell's exact source material, public fraction/feature, current globe ownership, and current
  topology-bound marker incidence.
- Transform scarp phase uses a stable world-space coordinate instead of resetting per edge-local arc.
  Mantle forcing groups normalized pair/kind records, coalesces exact endpoint-connected degree-two
  chains, terminates at junctions, never bridges disconnected components, and reduces a real
  frequency-4 frontier to at most one third as many forcing segments as visual forcing arcs. A
  shuffled/reversed three-branch Y-junction regression proves all branches terminate at the junction
  and no leaf-to-leaf shortcut is introduced.
- Exact shared-edge RED was `-4.021378056e-7` rad at frequency 2 and `-5.437160815e-7` rad at
  frequency 4 under the rejected inradius subtraction. GREEN is exactly `-double.Epsilon` on the
  subducting side at both frequencies, with a same-plate far-interior contribution of exactly zero.
- Integrated closure: World passed 629/629 in project-reference and package modes; Composition
  passed 110/110 in both; Presentation 232/232; Timeline 339/339; engine 602/602; bundle staging
  23/23. The exported macOS app hot-reloaded world and timeline with both old ALCs collected. At
  tick 200,000,000 it bound a 10-plate, 15,008-triangle adaptive HypsometricTerrain crust. Fresh
  3840x1914 captures prove the planet exceeds the thin rings and independently zooms. The current
  dry rock ramp is brown rather than literal reference gray; per-category sign is established by
  quantitative/final-mesh tests, while more unmistakable category art remains presentation polish.
- Current P9b tunnel constants supersede the older prototype values above: independent default
  planet zoom 1.35 (clamp 0.35..3.0), camera `(−0.25, 0.25, 2.9)` at 74 degrees, axis-centred
  instrument anchor `(0, 0, −1)`, inner ring 1.30..1.38 (width 0.08), outer ring 1.52..1.62
  (width 0.10). Tunnel-local reload identifiers use world-bundle wording; the resource service's
  actual `RuntimeChanged` API name is unchanged.
- Verification after the atomic-authority/kinematics follow-up: focused World authority/rotation/
  cache 40/40; full `App.World.Composition.Tests` 110/110 and full `App.World.Tests` 627/627 with
  project references; package-mode restore plus those same full suites at 110/110 and 627/627. The prior
  checkpoint's `App.Presentation.Tests` 232/232 and `App.Timeline.Tests` 339/339 were not rerun for
  this World-only increment. `git diff --check` is clean; commit hashes are reported in the handoff,
  and nothing was pushed.
- Core changed areas: world feature mapping/transport, edge-local reconstruction and polarity,
  finite-cell boundary fields and elevation composition, bounded tectonic detail, adaptive-mesh
  integration proofs, independent tunnel zoom/framing/ring hit geometry, and their World,
  Presentation, and Timeline tests. The exported-app screenshot plus collectible-ALC gate remains
  intentionally pending for the lead session; no unit suite is presented as a substitute for it.
