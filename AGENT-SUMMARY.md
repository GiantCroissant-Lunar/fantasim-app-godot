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
`GetActiveRotationProvider` readers and disposal. A reader cannot cross the interval after the
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
  counterfactuals: Mountain removes orogenic pressure and profiles; active VolcanicArc uses a real
  profile-disabled arc baseline; Trench and Ridge use zero profiles. Thresholds remain unchanged
  (mountain/volcanic +750 m, trench -750 m, ridge +300 m), and category fixtures need not co-occur.
- The deterministic frequency-2/tick-200M public document remains the all-four lineage and visual
  fixture. Its Mountain is proved against the no-orogeny dry-crust baseline, not by falsely claiming
  its zero-profile delta is the mountain signal. A frequency-4/tick-200M public cell supplies the
  separate quantitative active-volcanic-arc proof.
- Boundary arcs are local real shared tessellation edges. An incident cell has exact zero
  footprint-to-arc distance; nonincident cells retain centroid-to-arc distance as an interior guard.
  At a multi-edge junction, the one-sample boundary field uses stable input arc order as the exact
  zero-distance tie-break, pinned by a junction test.
- Locked quantitative/mesh fixtures after the correction:
  - lower frequency-2/tick-67M: Mountain 85 is +2000 m default-minus-zero and exceeds +750 m
    versus no-orogeny/no-profile (mesh outward); VolcanicArc 144 is +48.8 m (visual lineage,
    outward); Trench 182 is -2000 m (inward); Ridge 196 is +400 m (outward);
  - public frequency-2/tick-200M all-four document: Mountain 134 has a +40000 m state-derived
    signal versus no-orogeny dry crust (outward), while its default-minus-zero profile delta is
    honestly 0; VolcanicArc 137 is +400 m (outward), Trench 40 -2000 m (inward), Ridge 224 +400 m
    (outward);
  - public frequency-4/tick-200M: active VolcanicArc 803 is +1336.1 m versus zero profiles and
    remains outward in the finalized adaptive mesh.
- Exact shared-edge RED was `-4.021378056e-7` rad at frequency 2 and `-5.437160815e-7` rad at
  frequency 4 under the rejected inradius subtraction. GREEN is exactly `-double.Epsilon` on the
  subducting side at both frequencies, with a same-plate far-interior contribution of exactly zero.
- Current P9b tunnel constants supersede the older prototype values above: independent default
  planet zoom 1.35 (clamp 0.35..3.0), camera `(−0.25, 0.25, 2.9)` at 74 degrees, axis-centred
  instrument anchor `(0, 0, −1)`, inner ring 1.30..1.38 (width 0.08), outer ring 1.52..1.62
  (width 0.10). Tunnel-local reload identifiers use world-bundle wording; the resource service's
  actual `RuntimeChanged` API name is unchanged.
- Verification after the final causal/test changes: modified World focus 63/63; full
  `App.World.Tests` 584/584; `App.Presentation.Tests` 232/232; `App.Timeline.Tests` 339/339;
  `git diff --check` clean. No commit or push was made.
- Core changed areas: world feature mapping/transport, edge-local reconstruction and polarity,
  finite-cell boundary fields and elevation composition, bounded tectonic detail, adaptive-mesh
  integration proofs, independent tunnel zoom/framing/ring hit geometry, and their World,
  Presentation, and Timeline tests. The exported-app screenshot plus collectible-ALC gate remains
  intentionally pending for the lead session; no unit suite is presented as a substitute for it.
