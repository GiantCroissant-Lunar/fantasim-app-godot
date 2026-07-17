# Crust-volume generation Slice A evidence

Date: 2026-07-17

## Scope and verdict

Slice A establishes identity and ownership only. It does **not** claim that the current planet
geometry is visually acceptable.

- **Architecture gate: PASS.** `PlateBoundaryArc` is the production boundary authority and
  `CrustVolumeState` is the single canonical materialized crust state.
- **Export/runtime gate: PASS.** The exported app mounted the world bundle, materialized the same
  digest after an A → B → A timeline seek, and remained alive with empty error logs.
- **Visual-quality gate: FAIL / not yet in scope.** The exact-process viewport is functional but
  extremely close and dominated by the existing exploded geometry. This is evidence that later
  slices must replace the presentation geometry; it is not evidence that the desired planet is
  finished.

The viewport evidence is [tick-107m-viewport.png](tick-107m-viewport.png).

## Exact source and executable

- Repository HEAD:
  `55aefc798b82206d285d3350e4b2fd5957708b65`
- Exported application:
  `build/_artifacts/0.1.2/godot/osx/complete-app.app`
- Executable SHA-256:
  `543ff9954fdf6de37dd8b0d1c25616b1afd5ab677cc20b8a9f0b4654003e1e07`
- `common.pck` SHA-256:
  `338e5efd86987f53705a76a31bc81e2f7c8abf0471138cf062de8e1e2e92b64f`
- `world.pck` SHA-256:
  `6487825080914cf9599e94b3b0e9df76234bcae9d155809fee984083507ffdb4`
- Verification PID: `12461`
- Verification remote endpoint: `127.0.0.1:19317`

`lsof` tied PID 12461 to both the exact exported executable and the listening endpoint. The process
was left open for handoff. A separate pre-existing user process was not touched.

## Build and bundle gate

The repository Taskfile/UnifyBuild path completed successfully at the recorded HEAD:

```text
task build:godot:desktop
task bundle:world
task bundle:install
```

`build:godot:desktop` invokes the repository's `BuildGodotDesktop` target. All 96 projects compiled
with zero errors. Per user direction, no new test was added and no test suite was run.

The installed PCK hashes equal the recorded bundle artifact hashes.

## Deterministic A → B → A gate

The dedicated exported process was remotely sought from tick A to tick B and back to tick A:

| Seek | Tick | Cells | Boundary arcs | Digest |
|---|---:|---:|---:|---|
| A | 107,000,000 | 5,120 | 342 | `67031039ada9c499b981994e731b1caa36d1eccaa82fa4cd81b09a99e6f95c7e` |
| B | 112,000,000 | 5,120 | 349 | `f07165708910bab403d0306d8eb4ee2bb48d625624815b8247105d16d6ff1bde` |
| A | 107,000,000 | 5,120 | 342 | `67031039ada9c499b981994e731b1caa36d1eccaa82fa4cd81b09a99e6f95c7e` |

At each seek, the materializer digest and mounted presentation digest matched. The returned A
digest exactly equals the initial A digest.

Relevant process logs:

```text
/tmp/fantasim-crust-volume-55aefc7.stdout.log
/tmp/fantasim-crust-volume-55aefc7.stderr.log
/tmp/fantasim-crust-volume-55aefc7.godot.log
```

The stderr and Godot logs were both zero bytes after the corrected run. The stdout log records:

```text
World slab joints shaped from canonical arcs: segments=342, convergent=56, buriedUnderlap=hidden.
```

That is the intended assembled-view policy: buried underlap affects generation but is not rendered
as a separate exposed slab in the assembled planet.

## Type-ownership audit

Production source search at the recorded HEAD established:

- `CrustVolumeState.Create(...)` has one external caller:
  `WorldCrustMaterializer`.
- `new CrustVolumeState(...)` occurs only inside the type's private factory implementation.
- There are no references to the deleted `SlabJointClassification`, `SlabJointClassifier`,
  `SlabJointKind`, or `SlabJointPolarity` mirror types.
- There are no assignments to the retired parallel presentation products
  `CellElevations`, `CrustalThickness`, `TectonicFeatures`, or `ContinentalFractions`.
- `ShapeSubductionTongues(...)` remains only as a legacy declaration and has no production caller.

This is the enforceable answer to the duplicate-type concern: the canonical owner and its sole
construction seam are named, and future slices must repeat this zero-mirror/caller audit.

## Negative result and corrective action

The first exported run crashed after the switch from merged slab classifications to 342 canonical
boundary segments. The old appended-tongue path repeatedly expanded `PlateSolid`, then inferred the
original top-vertex count from the expanded mesh. That invalid assumption caused an
`IndexOutOfRangeException`.

Commit `55aefc7` removed the fake appended-tongue path from production renderers. This matches the
assembled-view requirement and prevents a second geometry authority from surviving beside
`CrustVolumeState`. Canonical volume extraction in later slices will provide the real cutaway
underlap.
