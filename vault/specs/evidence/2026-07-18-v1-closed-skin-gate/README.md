# V1 "closed skin" gate evidence — 2026-07-18

Authority: `vault/specs/2026-07-18-visual-fidelity-slices-decision.md` (V1). The user's eye is
the gate; this file records identity, markers, and OBSERVABLE facts only — no closeness verdict.

## Identity

- Repo: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot`
- Code HEAD at capture: `0c51f25` + working-tree color-mode fix (committed immediately after as
  the commit that carries this evidence — see git log)
- App: `build/_artifacts/0.1.2/godot/osx/complete-app.app` (rebuilt this session; bundles
  re-exported + installed; world bundle HOT-RELOADED with `old ALC collected for bundle world`)
- PID: `83088`, `lsof -d txt` bound to the exact executable above
- Launch env: `remote__enabled=true FANTASIM_NEUTRAL_CRUST_GEOMETRY=1`
- State: `crust-volume.v2`, tick=100,000,000, cells=5120, arcs=349,
  digest=`58afd482f2289f41a8903645180ba79e9af415e3442bacad1ea217a54a50f7e1`
  (identical before and after hot-reload — determinism held)

## V1 log markers (verbatim in `markers.log`)

- Packet B: `World envelope closed: contacts=80 openContacts=0.`
- §4.3 preserved: `Crust underlap proof: … arc=8, overriding=7, downGoing=2, downGoingCell=300 …`
- Envelope bind: `source=CrustVolumeState, plates=10, triangles=11974`,
  features Mountain=71 VolcanicArc=83 Trench=72 Fault=640, arcs Convergent=40 Transform=309.
- Packet A's slab-assembly marker (`World envelope: smooth-shaded …`) did NOT fire: the
  assembled World binds through the adaptive outer-envelope path, where packet A's smoothing
  applies via `PlateSurfaceNormalModePolicy` (no marker on that path). The
  `BoundaryConcentratedSubdivider` is wired into the slab-assembly/exploded top caps only —
  it does not affect this envelope path. Recorded as a wiring fact, not a pass.

## Captures

- `before-color-fix-facets.png` — same running app, same digest, BEFORE the color-mode fix:
  flat-shaded triangular cell facets dominate the surface.
- `after-assembled-tunnel-default.png` — AFTER smooth normals (58f2c05) + closed contacts
  (0c51f25) + World color mode `SourceCellFacet` → `VertexEnvelope` (this commit): the cell
  grid is not visible; the surface reads as one continuous mottled body; no open boundary
  channels visible at this framing.
- `after-assembled-os.png` — OS-level screencapture of the same state (windowed proof).

## Observable limitations (facts, no grades)

1. Timeline is in `Scrubbing` state on the 1-kb preview rung (`tick 100000000 | kb` label);
   `origin=scrubCommit` seek did not climb the rung — the KNOWN pre-existing "rung never
   climbs" defect (2026-07-17 handover §4). Some surface softness may be preview-rung
   resolution rather than final-rung geometry.
2. The globe renders small inside the tunnel radial view (default since 2026-07-16);
   `camera.orbit` does not reframe this layout, so the reference framing (globe filling the
   frame) was not achieved. `camera.frame_joint`-style framing remains owed.
3. No boundary belts, trench, or ridge geometry readable at globe distance — V2 scope.
4. No medium/fine formed detail — V3 scope.
5. Orange patch at the pole persists (unchanged from A0/B0 captures).
6. `FANTASIM_NEUTRAL_CRUST_GEOMETRY=1` did not produce a neutral-gray material (same as the
   A0/B0 run's captures) — the brown palette is unchanged by that gate.
