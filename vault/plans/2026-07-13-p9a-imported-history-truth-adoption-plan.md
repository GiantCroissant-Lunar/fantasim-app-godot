# P9a imported-history truth adoption implementation plan

> **For implementing agent:** Follow `test-driven-development`, `source-driven-development`,
> `unify-build`, and the workspace rules. The lead-owned parity oracle is immutable: do not edit,
> weaken, skip, or replace its expected values.

**Goal:** Remove the app's direct `.rot` playback bypass and compile one real
`WorldHistoryCoordinator` in both dependency modes, with recoverable prepared -> CAS plate batch ->
bound imports and materialized onset-relative playback.

**Architecture:** `Service` owns the truth-store handle and actor/direct writer. The coordinator
owns import state transitions and materialized-history queries but does not own injected
infrastructure. Raw import bytes are provenance input; normalized plate events and the bound cursor
are playback authority. Package/project mode selects dependency source only.

**Repositories:**

- App: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot`
- Engine: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-world`

**Authoritative spec:** `vault/specs/2026-07-13-canonical-world-history-and-dry-crust-design.md`

## Locked acceptance behavior

- Stream identities are parameterized by requested world and branch. The plate stream is
  `{world}:{branch}:L0:geosphere:plates`; the control stream is
  `{world}:{branch}:L0:world:imports`.
- Parse issues and an empty import fail before any append.
- Control payloads use versioned MessagePack DTOs with fixed integer keys and a
  `WorldEventContextV1`; existing `PlateRotationPayload` bytes and the top-level truth envelope do
  not change.
- Retry states are exactly those in spec section 7.2. A conflicting head fails closed with “new
  branch required”; no fallback reparses raw text for playback.
- The onset-relative convention is
  `R_abs(TickDeltaToMegaAnnum(tick-onsetTick)) * inverse(R_abs(0 Ma))`. Increasing canonical ticks
  sample increasing Ma before present. Samples clamp outside the authored range.
- Valid fixed-parent changes through time are supported by the engine materializer; only invalid or
  cyclic input is rejected.
- `GetFieldValues` returns contract/BCL-owned value types, never an anonymous collectible type.

## Task 1: RED engine coverage for time-varying fixed parents

**Modify:**

- `fantasim-world/project/tests/Geosphere.Plate.Reconstruction.Tests/RotationModelMaterializerTests.cs`
- `fantasim-world/project/tests/Geosphere.Plate.Rotation.Stream.Tests/RotationStreamImporterTests.cs`

Add fixtures with `001` changing from fixed plate `000` to `002`, including bracketing samples for
both parent circuits. Assert absolute orientations at each keyframe and an interpolated time from
independently composed quaternions. Add a cyclic-parent fixture that still fails closed.

Run the two focused test projects and observe the valid parent-change test fail for the current
“inconsistent FixedPlateId” rejection before production edits.

## Task 2: GREEN the engine materializer without changing event bytes

**Modify:**

- `fantasim-world/project/plugins/Geosphere.Plate.Reconstruction/RotationModelMaterializer.cs`

**Add:**

- `fantasim-world/project/plugins/Geosphere.Plate.Reconstruction/TimeVaryingPlateCircuit.cs`

Represent authored parent segments explicitly in `TimeVaryingPlateCircuit` and let `RotationModel`
(currently declared in `RotationModelMaterializer.cs`) query it while preserving the current
stable-parent `PlateCircuit` property/API. Resolve the parent applicable at query time, interpolate
finite rotations with the existing quaternion interpolator, compose parent on the left, normalize,
and detect time-local cycles. Do not change `PlateRotationPayloadCodec` or truth hashing.

Run focused reconstruction/import tests, then the engine repository test target through
`dotnet unify-build` from the directory containing `build/build.config.json` (consult the
`unify-build` skill for the exact invocation).

## Task 3: RED app coordinator and writer state-machine tests

**Rename:**

- `project/tests/App.World.Tests/WorldRuntimeTests.cs` ->
  `project/tests/App.World.Tests/WorldHistoryCoordinatorTests.cs`

**Add:**

- `project/tests/App.World.Tests/ImportedRotationHistoryTests.cs`
- `project/tests/App.World.Tests/WorldHistoryBuildModeContractTests.cs`

Keep the lead-owned
`project/tests/App.World.Tests/MaterializedRotationProviderParityTests.cs` unchanged.

Add tests for valid prepare/plate/bind ordering, zero append on malformed/empty input, same-source
idempotency, different-source conflict, failure after prepare, failure after plate append followed
by recovery, actor serialization of CAS imports, and a source scan proving
`Service.BuildRotationProvider` does not construct `ImportedRotationProvider`. Test both
`UseProjectReferences=true` and `false` with identical behavior; neither path may reference
`StubWorldRuntime`.

## Task 4: Rename the seam and make it real in both modes

**Rename:**

- `project/plugins/App.World/Services/WorldRuntime.cs` ->
  `project/plugins/App.World/Services/WorldHistoryCoordinator.cs`
- `project/plugins/App.World/Services/IWorldRuntime.cs` ->
  `project/plugins/App.World/Services/IWorldHistoryCoordinator.cs`

**Delete:**

- `project/plugins/App.World/Services/StubWorldRuntime.cs`

**Modify:**

- `project/plugins/App.World/Services/Service.cs`
- `project/plugins/App.World/App.World.csproj`
- `project/tests/App.World.Tests/App.World.Tests.csproj`
- `project/Directory.Packages.props`

Rename `WorldRuntime`, `IWorldRuntime`, `WorldRuntimeFactory`, tests, and `_runtime` to their accepted
history names. Remove `USE_PROJECT_REFERENCES` behavioral branches from the coordinator, writer,
reader, and store factory. Add the same fields/truth/rotation/reconstruction package closure used by
the project-reference mode, including
`GiantCroissant.FantaSim.Geosphere.Plate.Reconstruction`.

## Task 5: Add separated read/write capabilities and CAS actor messages

**Modify:**

- `project/plugins/App.World/Services/ITruthEventWriter.cs`
- `project/plugins/App.World/Services/ActorTruthEventWriter.cs`
- `project/plugins/App.World/Services/WorldTruthStoreFactory.cs`

**Add:**

- `project/plugins/App.World/Services/ITruthEventReader.cs`

`ITruthEventWriter` gains `AppendIfHeadAsync`. Snapshot drafts before sending them to Akka. Add a
dedicated CAS message/handler and keep `ReceiveAsync` serialization. `ITruthEventReader` exposes
only `ReadAsync` and `GetHeadAsync`; its direct adapter may wrap the same store that `Service` owns.
The coordinator disposes neither injected capability. The outer `Service` disposes the writer and
store handle exactly once.

## Task 6: Implement canonical import control payloads and recovery

**Add:**

- `project/plugins/App.World/History/WorldEventContextV1.cs`
- `project/plugins/App.World/History/RotationImportControlPayloads.cs`
- `project/plugins/App.World/History/RotationImportPayloadCodec.cs`
- `project/plugins/App.World/History/RotationImportCoordinator.cs`

Use MessagePack fixed integer keys. Compute SHA-256 digests over raw source bytes, the canonical
ordered draft tuple sequence, canonical configuration, and a reproducible normalized producer
manifest (never MVID/timestamp/path). Implement the exact recovery table from the spec by reading
the control stream and verifying draft event type/tick/payload byte-for-byte before treating an
orphan plate batch as recoverable. Bound is the only active state.

## Task 7: Materialize playback and eliminate direct reparsing

**Add:**

- `project/plugins/App.World/Crust/MaterializedRotationProvider.cs`

**Modify:**

- `project/plugins/App.World/Services/WorldHistoryCoordinator.cs`
- `project/plugins/App.World/Services/Service.cs`
- `project/plugins/App.World/Crust/RotationSourceRecipe.cs`

After binding, materialize the committed plate stream with `RotationModelMaterializer`; map integer
app plate IDs to authored IDs by invariant numeric normalization (including leading zeroes). Cache
the active bound cursor/model, not raw `.rot` text. `BuildRotationProvider` asks the coordinator for
the materialized provider and otherwise uses `GeneratedEulerPoleRotationProvider`. Remove
`ImportedRotationProvider.cs` after its accepted parity cases pass and source scans show no runtime
construction.

## Task 8: Close the collectible DTO leak

**Modify:**

- `project/contracts/App.World/Dto/WorldDtos.cs`
- `project/plugins/App.World/Services/WorldHistoryCoordinator.cs`
- affected app/test consumers of `WorldFieldValues`

Add a contract-owned `WorldFieldDescriptorDto(Unit, Kind, Reducer)` and type the dictionary to that
DTO. Add an ALC boundary test that recursively verifies values returned through `IService` are from
the default/shared contract context or BCL and that the world bundle ALC collects after release.

## Task 9: Build-mode, bundle, and diagnostic gates

Run, in order:

1. Focused engine and app tests.
2. `UseProjectReferences=true` app coordinator/import suite.
3. Pack/publish the changed engine packages through `dotnet unify-build` using a new compatible
   package version; update central pins.
4. `UseProjectReferences=false` restore/build/test of the same suite.
5. Full repository build/test through `dotnet unify-build`.
6. Stage/export the world bundle while recording the effective `UseProjectReferences` value.
7. Verify staged `FantaSim.App.World.dll` contains `WorldHistoryCoordinator`,
   `ActorTruthEventWriter`, `RotationImportCoordinator`, and `MaterializedRotationProvider`, and
   contains neither `StubWorldRuntime` nor the direct provider.
8. In the exported app, import a real fixture and record diagnostics for prepare, CAS plate head,
   bound cursor, and materialized query. Reload the PCK and prove ALC collection.

## Agent handoff

Do not commit or push. Write `AGENT-SUMMARY.md` in each assigned worktree with changed files, RED and
GREEN commands/results, assumptions, unresolved failures, and the exact package version needed by
the app. Stop on a conflict with the locked acceptance behavior rather than weakening a test.
