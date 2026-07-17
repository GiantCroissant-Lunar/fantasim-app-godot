# Crust-volume generation Slice B evidence

Date: 2026-07-17

## Scope and verdict

Slice B moves the normal assembled World view onto the canonical crust-volume outer envelope.
Buried underlap remains part of generation but is not drawn as exposed under-crust in this view.

- **Canonical-source gate: PASS.** The final runtime reports `source=CrustVolumeState`; assembled
  elevations and feature classifications come from one shared `PlateFrameSampler` projection.
- **Visual gate: PASS for Slice B.** The exact exported-app capture shows one complete,
  unobstructed, faceted globe above the timeline. A coherent dark convergent belt lies beside the
  uplifted pale region, and the silhouette carries bounded large-scale relief.
- **Hidden-underlap gate: PASS.** Every accepted bind reports `buriedUnderlap=hidden`.
- **Deterministic-time gate: PASS.** A seek from 107M to 112M and back to 107M reproduced the first
  digest exactly.
- **Type-ownership gate: PASS.** Slice B added no class, record, struct, interface, or enum and
  changed no test file.

The accepted exact-export visual is
[tick-107m-world-final-export-yaw90.png](tick-107m-world-final-export-yaw90.png). This is an
assembled-envelope result, not a claim that continuous buried plate volume is already available
in cutaway/exploded mode; that is Slice C.

## Exact source, export, and process

- Exported implementation HEAD:
  `4be950886b6b6bfe34f1246d0948fa0c4db6ddb8`
- Exported application:
  `build/_artifacts/0.1.2/godot/osx/complete-app.app`
- Executable SHA-256:
  `543ff9954fdf6de37dd8b0d1c25616b1afd5ab677cc20b8a9f0b4654003e1e07`
- Built and installed `world.pck` SHA-256:
  `b32c277f138cffb486fc8ac54f39fcac1930a29a35e2e60982999b63f38a4b81`
- Built and installed `common.pck` SHA-256:
  `97c656530c7d1172293494de59f81b6be11f1747ac921bd944200a7151d9df78`
- Verification PID: `3556`
- Verification remote endpoint: `127.0.0.1:19321`
- Verification stdout:
  `/tmp/fantasim-crust-volume-slice-b-final-export-2.stdout.log`
- Verification stderr:
  `/tmp/fantasim-crust-volume-slice-b-final-export-2.stderr.log`
- Verification Godot error log:
  `/tmp/fantasim-crust-volume-slice-b-final-export-2.godot.log`

`lsof` ties PID 3556 to the exact exported executable and the listening endpoint. The process is
left open for handoff. The stderr and Godot error logs are both zero bytes.

## Build and bundle gate

The repository build path completed successfully at the recorded HEAD:

```text
task build:godot:desktop
task bundles
task bundle:install
```

`build:godot:desktop` invoked the repository's UnifyBuild `BuildGodotDesktop` target and completed
with zero errors. The solution build compiled existing test projects as dependencies, but no test
runner was invoked, no test was added, and no test file changed.

Before the clean export, repeated live World bundle installs reported:

```text
Hot-reload: old ALC collected for bundle world
```

That proof remains in
`/tmp/fantasim-crust-volume-slice-b-27f1855-clean.stdout.log`.

## Deterministic A → B → A gate

The clean exported process was remotely sought from tick A to tick B and back to tick A:

| Seek | Tick | Triangles | Digest |
|---|---:|---:|---|
| A | 107,000,000 | 12,098 | `bcda1c210caaa745cf9c4f2985465fdb4d36bf8ebd1fad90fd8cc27dab68e59f` |
| B | 112,000,000 | 12,182 | `649746119a0e97fa5df58c434187fe8250a837560a686c6aa13edbc8016ef1c8` |
| A | 107,000,000 | 12,098 | `bcda1c210caaa745cf9c4f2985465fdb4d36bf8ebd1fad90fd8cc27dab68e59f` |

At 107M, the accepted bind records:

```text
source=CrustVolumeState
buriedUnderlap=hidden
elevationMetres=[-2070.8236724391854,3747.237676140628]
features=[None=4103,Mountain=101,VolcanicArc=115,Trench=97,Ridge=125,Fault=579]
boundaryArcs=[Convergent=56,Divergent=24,Transform=262,Inactive=0]
radiusRange=[0.9765938209198253,1.033694838392382]
normalRadialDot=[0.57301020338595,0.9845082103941529,0.9999996514322028]
```

The returned A digest and geometry counts exactly equal the initial A values.

## One surface-data authority

Systematic tracing found a real duplicate path during Slice B: production sampled elevations
through `PlateFrameSampler`, but `Service` independently authored the feature array while the
nominal `WorldCrustMaterialization.BuildSurfaceData` authority had no production caller.

The correction reuses the existing owners:

- `PlateFrameSampler.SampleSurfaceDataAt(...)` now returns elevations and existing
  `CellCrustFeature` values from the same boundary field.
- `SampleElevationsAt(...)` delegates to that shared implementation.
- `WorldCrustMaterialization.BuildSurfaceData(...)` delegates to the same implementation.
- `Service` consumes both outputs and no longer owns a parallel `BuildCellFeatures(...)` path.
- `BoundaryProfileShape` remains the sole boundary-form grammar and maps into the existing
  `TectonicFeatureKind`/`CellCrustFeature` vocabulary.

The shared return is a C# tuple, not another named domain type.

## Type-ownership audit

The Slice B range is the Slice A closure `28db69d` through final HEAD `4be9508`.

- A zero-context diff scan found no added class, record, struct, interface, or enum declaration.
- No path under `project/tests/` changed.
- No second crust state, surface-feature type, boundary kind, plate mesh DTO, or presentation
  document was introduced.
- `CrustVolumeState` remains the single materialized geology owner.
- Existing `BoundaryProfileShape`, `PlateFrameSampler`, `CellCrustFeature`,
  `TectonicFeatureKind`, `CrustAccentMapper`, and presentation policies were evolved in place.

This is the enforceable check for the duplicate-type concern: each slice records the commit range
and scans added declarations, not just the names an agent remembers creating.

## Negative results retained

The directory intentionally keeps intermediate captures:

- `tick-107m-world-uncapped.png` and `tick-107m-world-yaw-minus100.png` show that invoking
  `render.exploded` with factor zero still activates the obsolete solid-crust presentation path;
  factor zero is assembled translation, not a deactivation command. The accepted assembled proof
  never invokes that command.
- `tick-107m-envelope-v1.png` through `v3`, the wireframe/lit-side captures, and
  `tick-107m-world-measured.png` show that a technically bound envelope can still read as a smooth
  ball when the old silhouette clamp and smooth-normal/color policies remain.
- `tick-107m-world-boundary-consequences.png` proves the mechanics existed before the camera was
  framed above the timeline.
- `tick-107m-world-final-export.png` caught the timeline during a partial redraw and was rejected;
  the later yaw-90 frame is the accepted clean export capture.

These failures led to removal of the old world silhouette clamp, reuse of the existing faceted
normal/color modes, stronger existing feature accents, and a labeled bounded height lens. They did
not lead to another geometry authority.
