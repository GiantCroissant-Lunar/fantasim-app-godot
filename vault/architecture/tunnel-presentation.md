---
source: project/plugins/App.Presentation/Tunnel/{TunnelPresentationBinder(.Camera,.Corridors,.Rings,.Input),TunnelBoreSpline,TunnelBoreSegments,TunnelBoreSeedPolicy,TunnelCameraFraming,TunnelInputRelay,TunnelCorridorDepthPolicy,TunnelShellDepthPolicy}.cs, project/plugins/App.Timeline.Seam/{TunnelScrubMapper,TunnelGestureCoordinator,TunnelCorridorLayout,TunnelTrackActivity,TunnelRayHitMapper,TunnelFineRequestScheduler,ResidentTunnelModeOwner}.cs, project/contracts/App.Presentation/{ITunnelPresentation,ITunnelModeOwner}.cs; specs: vault/specs/2026-07-11-tunnel-timeline-design.md, 2026-07-12-rotating-tunnel-two-ring-prototype-design.md, 2026-07-12-asymmetric-cockpit-tunnel-design.md, 2026-07-14-spline-tunnel-branch-fork-design.md; plan vault/plans/2026-07-14-spline-tunnel-slice1-plan.md (authored 2026-07-14)
source-status: current-code documentation
distilled: 2026-07-14
divergence: >
  Filmstrip texture production, fine-inspection scheduling internals, corridor
  activity styling, and the tunnel-loss/HUD-safety epoch protocol are named but not
  detailed; camera framing math beyond the shipped constants is omitted. Causal ribs are
  design-only and covered by one line.
---

# Tunnel presentation — two-ring cockpit and the spline bore

The tunnel is the 3D timeline cockpit: the occupant sits at the current-tick plane inside a
bore whose depth axis IS canonical time, with the planet at the center, per-track corridors on
the wall, and a rig-anchored two-ring instrument for scrubbing. It consumes the layer-track
registry and the scrub-origin pipeline documented in [[timeline-core]]. Slice 1 of the
fork-in-place branch design (the bent spline bore) is shipped; junctions are the next arc.

## Doctrine (what must hold — cited, not restated)

- **Truth vs view** — hub `fantasim-hub/vault/architecture/planet-stack-model.md` §8: values
  at a tick come from the fold; Godot is transport/UI only.
- **Binding honesty constraints** — app `vault/specs/2026-07-14-spline-tunnel-branch-fork-design.md`
  §1.1 (concept-lock, user-approved 2026-07-14):
  1. **Flying is scrubbing.** Bore arc-length maps deterministically to canonical tick; no
     momentum, no position that is not a tick.
  2. **Fork placement is truth, not level design.** A junction sits at exactly the
     `world.branch-created.v1` cursor's tick; the shared prefix renders as ONE bore (parent
     events composed, never duplicated); sibling suffixes are capped throat stubs.
  3. **Curvature must not eat legibility.** Parallel-transport frames (no roll), a curvature
     cap, deterministic seeded curvature (never wall-clock/random), instrument layer stays
     rig-anchored.
  User-confirmed verb priority: **audit > compare > switch**.
- **Branch axis** — hub `variant-and-branch.md` owns branch identity doctrine; engine ledger
  mechanics live engine-side (`world.branch-created.v1`, slice 2a shipped 2026-07-14).
- **No smoke** — no demo/fake junctions ever ship (house rule
  `.agent/rules/no-smoke-or-fake-production-code.md`).

## Built (code-verified 2026-07-14)

### Mode, ownership, contract

`ITunnelPresentation` (`contracts/App.Presentation/`) mirrors `IPlanetPresentation`: `Rebind`,
`TrySetEnabled` (enable **fails closed** when any live world/stage/controller/geometry
dependency is unavailable; disable is idempotent), `TrySetZoom`. Driven by the remote command
`timeline.tunnel_view` (`TimelinePlugin.TunnelViewCommandId`; the binder duplicates the const
string because the two live in different collectible ALCs). `ResidentTunnelModeOwner` +
`TunnelHudSafetyState` keep the 2D HUD safe across tunnel loss
(`TimelineFace.TryApplyResidentHudSafety`).

### Geometry frame (`TunnelCameraFraming`)

`TunnelRadius=5`, `MouthZ=0`, `CurrentPlaneZ=-5`, `ThroatZ=-20`, so `TimelineDepth=15`
(current plane → throat). Planet visual radius 2.06 at the current plane. Instrument dial
bands: inner ring 1.30–1.38, outer ring 1.52–1.62 (`InnerRing*/OuterRing*Radius`).

### Two-ring cockpit (`TunnelPresentationBinder.Rings.cs`, `App.Timeline.Seam/TunnelScrubMapper.cs`)

`TunnelInstrumentContract` is the Godot-free node plan (InstrumentRoot, OuterRotationRoot,
InnerRotationRoot, readouts, inspection lens) pinning the non-rotating ownership boundary of
the asymmetric-cockpit design §5.1. Three gesture surfaces (`TunnelGestureKind`):

| Surface | Semantics |
|---|---|
| **Outer ring** | coarse time dial: one clockwise revolution advances exactly one canonical **kb** (`TunnelScrubMapper` resolves the real kb rung from `TimelineModel.GetLadderRungs` and fails loudly if the ladder lacks it); angle deltas map to once-rounded, clamped tick targets applied through the shared scrub coalescer |
| **Inner ring** | presentation-only **fine inspection** preview (fine rail + cursor, latest-wins `TunnelFineRequestScheduler`); it never moves the truth playhead |
| **Wall** | corridor carousel: drag rotates the corridors root about the tunnel axis |

`TunnelGestureCoordinator` wraps the same `TimelineScrubCoalescer` as the 2D face — outer-ring
motion coalesces per frame (`ConsumeFrame`), release commits as `ScrubCommit`, so tunnel
scrubbing rides the identical ScrubPreview/ScrubCommit fold pipeline ([[timeline-core]]).
`ScrubCommit` additionally rebuilds corridor frame requests
(`TunnelPresentationBinder.Input.cs`, `rebuildFrameRequests:` at the commit site).

### Input relay and pick guard (`TunnelInputRelay`, `TunnelPresentationBinder.Input.cs`)

`TunnelInputRelay` (a `Node3D`) funnels `_Input`/`_UnhandledInput`/`_Process` into binder
delegates; an exception in a handler is surfaced, the gesture is cancelled, and the faulted
event is consumed — fail closed, never a half-owned gesture that also falls through to globe
orbit (the dual-drag fix). Focus loss, window close, and exit-tree all cancel.

Hit resolution (`TryResolveHit`): project the pointer onto the instrument plane and test the
two ring bands (`TunnelInstrumentHitPolicy.IsInBand`); intersect the wall as a cylinder of
radius `CorridorSurfaceRadius` bounded to
`[InteractiveThroatZ, MouthZ]` where `TunnelBoreContract.InteractiveThroatZ(currentPlaneZ) =
currentPlaneZ − StraightRadius = −12.5` — the pick guard: only the exactly-straight
near-field is interactive, the bent far-field is scenery. Rings win over wall; a wall hit the
planet occludes is rejected (`IsPlanetOccludingWall`): effective planet radius from
`TunnelPlanetOcclusionPolicy` (original shared-planet scale × tunnel zoom, fail-closed on
non-finite input), sphere intersection via `TunnelRayHitMapper.TryIntersectSphere`, then
nearer-along-ray comparison. Wall drags measure pointer angle on an axis-perpendicular wall
reference plane, rings on the instrument plane (`TunnelPointerAngleSourcePolicy`) — mixing
planes injected parallax snapping near the dial singularity.

### Spline bore slice 1 (`TunnelBoreSpline`, `TunnelBoreSeedPolicy`, `TunnelBoreSegments`)

`TunnelBoreContract`: `StraightRadius=7.5` (the first two corridor depth bands,
2 × TimelineDepth/4 — every shipped input path operates on exactly the straight geometry it
was written against), `CurvatureCapRadPerUnit=0.12`, `RampLength=1.5`,
`MaxSegmentLength=1.25`.

- **Seed**: `TunnelBoreSeedPolicy.SeedFor(branchId)` — FNV-1a 64-bit over UTF-16 code units
  (low byte then high byte per char), deterministic cross-process/platform (unlike
  `string.GetHashCode`); null/blank branch → `"main"`.
- **Curvature**: `TunnelBoreSpline.Create` derives yaw/pitch frequencies (0.03–0.06
  cycles/unit) and phases from the seed via SplitMix64; two sinusoidal turn-rates share the
  cap budget (`amplitude = cap/√2`); curvature ramps in over `RampLength` with smoothstep
  (C1 at the straight/curved boundary) and is integrated at step 0.05 with
  **parallel-transported frames** (the whole frame rotates by the incremental yaw+pitch, so
  zero roll accumulates). Pure function of the seed — no runtime randomness, no wall-clock.
  Math on UnifyMaths `Vector3D`/`Quaternion` (house rule: build on Unify).
- **Evaluate** returns the exact straight frame for depth ≤ 7.5 and lerp/nlerp-interpolated
  sampled frames beyond; depth is clamped, non-finite input coerced to 0.
- **Segments**: `TunnelBoreSegments.Plan` chops a depth band into rigid chords on the spline —
  the straight portion as one exact segment, the curved remainder subdivided to
  ≤ `MaxSegmentLength` so the polyline reads smooth at the capped curvature.

The binder caches one spline per seed (`EnsureBoreSpline`), keyed by
`ResolveActiveBranchId()` = the first source track's `StreamId.Branch`
(`TunnelPresentationBinder.Corridors.cs`) with `maxDepth = TimelineDepth`.

### Bore consumers — corridors, filmstrips, dark shell

- **Corridor walls** (`RebuildCorridors`/`BuildCorridorSlot`): per visible track
  (`TunnelCorridorLayout.BuildFocusedWindow`), per depth band
  (`TunnelCorridorDepthPolicy.Plan`), wall sector meshes are planned by `TunnelBoreSegments`
  and mounted at `BoreWorldPosition`/`BoreBasis` of each segment frame. Corridor headers are
  billboarded Label3D pairs mirroring the 2D track header (name + state + canonical rung
  sub-line); activity color via `TunnelTrackActivity`.
- **Filmstrip frames** (`BuildFilmstripFrames`): each frame evaluates the bore at its depth
  and offsets laterally in the frame's Right/Up basis, so strips bend with the bore.
- **Dark shell** (`BuildDarkShell` + `TunnelShellDepthPolicy`, 4 bands): bands fully inside
  the straight field keep the legacy single-mesh path (band 0's node name `"Shell"` is a
  validity-gate contract for `EnsureMounted`); deeper bands are segmented along the spline.
  Depth-graded near-dark values carry the falloff.

Tests: `TunnelBoreSplineTests`, `TunnelBoreSegmentsTests`, `TunnelBoreSeedPolicyTests`,
`TunnelInstrumentContractTests`, `TunnelPlanetOcclusionPolicyTests`,
`TunnelPointerAngleSourcePolicyTests`, `TunnelRayHitMapperTests`, `TunnelScrubMapperTests`,
`TunnelCorridorLayoutTests`, `TunnelTrackActivityTests`. Windowed gate for slice 1 passed
2026-07-14 on a fresh export (handover
`vault/handover/2026-07-14-branch-arc-l2-doctrine-session-handover.md`; evidence folder
`/tmp/fantasim-spline-tunnel-gate/` — session-local, not vendored).

## Not built / open

- **Slice 3 — junctions (the NEXT arc):** fork seams + capped throat stubs at real
  `world.branch-created.v1` ticks, steer-to-switch (camera swings into a throat; the branch
  rematerializes through its composed history), audit panel at the seam, transient compare
  ghost. Requires the engine branch ledger (slice 2a shipped engine-side 2026-07-14) plus the
  app-side slice 2b branch coordinator. Junction visual language gets its own concept-lock
  first; no demo junctions (no-smoke).
- **Flight mode deferred:** camera riding the playhead on the same spline substrate is pure
  navigation-feel risk with no new information; held until the bent bore passes the user's
  eye.
- **User curvature eye-sitting OWED.** The feel knobs are exactly
  `TunnelBoreContract.StraightRadius` (7.5), `CurvatureCapRadPerUnit` (0.12), and
  `RampLength` (1.5) — judged windowed, by the user's eye, before flight mode or junction
  work tunes anything on top.
- **Branch id plumbing is a placeholder:** `ResolveActiveBranchId` reads the first track's
  `StreamId.Branch`, and every track today mints `branch="main"` ([[timeline-core]]); real
  branch selection arrives with slice 2b/junctions.
- **Causal ribs** (fork design §1.2) are an independent, design-only slice: rib direction
  must derive from stored lineage/event cursors, never hardcoded labels.

Lineage: specs 2026-07-11 (Concept A tunnel), 2026-07-12 (two-ring prototype + asymmetric
cockpit), 2026-07-14 (spline tunnel / fork-in-place branch design + slice-1 plan).
