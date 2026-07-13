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
- Prepare and bind CAS the control stream. Bind references the exact prepared cursor. Both control
  events use the onset tick.
- Playback verifies and reads only through the plate cursor in bound, never through current head.
- Retry states are exactly those in spec section 7.2. A conflicting head fails closed with “new
  branch required”; no fallback reparses raw text for playback.
- The onset-relative convention is
  `R_abs(TickDeltaToMegaAnnum(tick-onsetTick)) * inverse(R_abs(0 Ma))`. Increasing canonical ticks
  sample increasing Ma before present. Samples clamp outside the authored range.
- Valid fixed-parent changes through time are supported by the engine materializer; only invalid or
  cyclic input is rejected.
- `GetFieldValues` returns contract/BCL-owned value types, never an anonymous collectible type.

## Task 1: Preserve the lead-owned engine RED oracle

**Do not modify:**

- `fantasim-world/project/tests/Geosphere.Plate.Reconstruction.Tests/RotationModelParentChangeParityTests.cs`
- `project/tests/App.World.Tests/MaterializedRotationProviderParityTests.cs`

The lead has already run both tests RED. The engine oracle locks absolute-at-authored-keyframes then
SLERP semantics, duplicate-time last-wins, just-before/at/after crossover values, and cycle
rejection. The app oracle locks leading-zero normalization and onset-relative time/sign/order. Run
them unchanged and preserve the same failure causes before production edits.

## Task 2: GREEN the engine materializer without changing event bytes

**Modify:**

- `fantasim-world/project/plugins/Geosphere.Plate.Reconstruction/RotationModelMaterializer.cs`
- `fantasim-world/project/tests/Geosphere.Plate.Reconstruction.Tests/RotationModelMaterializerTests.cs`
- `fantasim-world/project/tests/Geosphere.Plate.Rotation.Stream.Tests/RotationStreamImporterTests.cs`

For plates with a parent change, resolve the time-local parent chain to an absolute quaternion at
each authored keyframe, last row wins for duplicate moving/time, then put those absolute samples on
an anchor-parented circuit node so the existing `PlateCircuit` SLERPs them. Do not interpolate
relative samples at query time. Preserve existing stable-parent wiring and tests. Normalize and
detect time-local cycles. Do not change `PlateRotationPayloadCodec` or truth hashing.

Run focused reconstruction/import tests, then the engine repository compile target through
`dotnet unify-build`. Do not publish yet: the same engine package closure still needs the bounded
reader/cursor/control contracts from Tasks 5–7.

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
by recovery, simultaneous same-source and different-source imports, actor serialization of CAS
imports, and a full production-source scan proving no production code constructs
`ImportedRotationProvider`. Add a hidden-later-head test proving the bound cursor remains the only
materialized prefix. Run project-reference mode now. Run false mode only after the lead publishes
and pins the compatible engine package closure; neither path may reference `StubWorldRuntime`.

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

- `fantasim-world/project/contracts/World.TruthStream/ITruthEventStore.cs`
- `project/plugins/App.World/Services/ITruthEventWriter.cs`
- `project/plugins/App.World/Services/ActorTruthEventWriter.cs`
- `project/plugins/App.World/Services/WorldTruthStoreFactory.cs`

**Add:**

- `fantasim-world/project/contracts/World.TruthStream/ITruthEventReader.cs`

Make `ITruthEventStore` inherit the stable engine `ITruthEventReader` contract. App
`ITruthEventWriter` gains `AppendIfHeadAsync`. Snapshot drafts before sending them to Akka. Add a
dedicated CAS message/handler and keep `ReceiveAsync` serialization. The coordinator receives only
the reader and writer interfaces. It disposes neither. The outer `Service` disposes the writer
before the store handle exactly once.

## Task 6: Implement canonical import control payloads and recovery

**Add in the engine worktree:**

- `fantasim-world/project/contracts/World.TruthStream/TruthEventCursor.cs`
- `fantasim-world/project/contracts/World.TruthStream/WorldEventContextV1.cs`
- `fantasim-world/project/plugins/Geosphere.Plate.Rotation.Stream/Import/RotationImportControlPayloads.cs`
- `fantasim-world/project/plugins/Geosphere.Plate.Rotation.Stream/Import/RotationImportPayloadCodec.cs`

**Add in the app worktree:**

- `project/plugins/App.World/History/RotationImportCoordinator.cs`

**Add tests:**

- `fantasim-world/project/tests/World.TruthStream.Core.Tests/BoundedTruthEventReaderTests.cs`
- `fantasim-world/project/tests/Geosphere.Plate.Rotation.Stream.Tests/RotationImportPayloadCodecTests.cs`
- `project/tests/App.World.Tests/RotationImportRecoveryTests.cs`

Use MessagePack fixed integer keys. Compute SHA-256 digests over raw source bytes, the canonical
ordered draft tuple sequence, canonical configuration, and a reproducible normalized producer
manifest (never MVID/timestamp/path). Implement the exact recovery table from the spec by reading
the control stream and verifying draft event type/tick/payload byte-for-byte before treating an
orphan plate batch as recoverable. Add independent known-byte/digest vectors with length framing.
CAS prepare against the observed control head and CAS bind against the exact prepared head; bind
references that prepared cursor. Bound is the only active state. The first slice records a raw
source digest pointer only and must not claim durable raw artifact retention.

## Task 7: Materialize playback and eliminate direct reparsing

**Add:**

- `project/plugins/App.World/Crust/MaterializedRotationProvider.cs`

**Modify:**

- `fantasim-world/project/plugins/Geosphere.Plate.Reconstruction/RotationModelMaterializer.cs`
- `fantasim-world/project/tests/Geosphere.Plate.Reconstruction.Tests/RotationModelMaterializerTests.cs`
- `project/plugins/App.World/Services/WorldHistoryCoordinator.cs`
- `project/plugins/App.World/Services/Service.cs`
- `project/plugins/App.World/Crust/RotationSourceRecipe.cs`

After binding, materialize the committed plate stream with `RotationModelMaterializer`; map integer
app plate IDs to authored IDs by invariant numeric normalization (including leading zeroes). Cache
the active bound cursor/model, not raw `.rot` text. `BuildRotationProvider` asks the coordinator for
the materialized provider and otherwise uses `GeneratedEulerPoleRotationProvider`. Remove
`ImportedRotationProvider.cs` after its accepted parity cases pass and source scans show no runtime
construction.

Add a bounded materializer overload that accepts `ITruthEventReader` plus exact
`TruthEventCursor`, recomputes/verifies the hash-chain prefix through that cursor, stops there, and
fails if the sequence/hash/tick does not match. The app must use only this overload for bound
playback. A later current head or orphan batch is not active.

## Task 8: Close the collectible DTO leak

**Modify:**

- `project/contracts/App.World/Dto/WorldDtos.cs`
- `project/plugins/App.World/Services/WorldHistoryCoordinator.cs`
- `project/plugins/App.World.FieldView/Services/FieldViewService.cs`
- `project/tests/App.World.Tests/WorldHistoryCoordinatorTests.cs`

Add a contract-owned `WorldFieldDescriptorDto(Unit, Kind, Reducer)` and type the dictionary to that
DTO. Add an ALC boundary test that recursively verifies values returned through `IService` are from
the default/shared contract context or BCL and that the world bundle ALC collects after release.
Also cover cancellation and injected failures after prepare, during plate CAS, and before bind;
terminate/drain the actor and ensure serializer caches do not retain collectible DTO/message types.

## Task 9: Build-mode, bundle, and diagnostic gates

Run, in order:

1. Focused engine and app tests.
2. `UseProjectReferences=true` app coordinator/import suite.
3. Immediately after the engine changes in Tasks 2, 5, 6, and 7 are GREEN, pack the complete raised
   truth/rotation/reconstruction closure, inspect nuspec dependency versions, and report the exact
   compatible version. The lead publishes/syncs the reviewed closure and updates central pins.
4. Only then run `UseProjectReferences=false` restore/build/test of the same suite.
5. Full repository build/test through `dotnet unify-build`.
6. Because the contract DTO is resident, rebuild/stage the common bundle/full export and restart
   into it. Then stage/export the collectible world bundle while recording the effective
   `UseProjectReferences` value.
7. Verify staged `FantaSim.App.World.dll` contains `WorldHistoryCoordinator`,
   `ActorTruthEventWriter`, `RotationImportCoordinator`, and `MaterializedRotationProvider`, and
   contains neither `StubWorldRuntime` nor the direct provider.
8. In the exported app configured with a declared durable backend, import a real fixture and record
   diagnostics for control CAS prepare, plate CAS head, control CAS bind, bounded materialization,
   and bound cursor. Reload the world PCK, rediscover bound from the durable streams, rematerialize
   only through its cursor, and prove ALC collection. An in-memory run is explicitly ephemeral and
   cannot satisfy this recovery gate.

## Agent handoff

Do not commit or push. Write `AGENT-SUMMARY.md` in each assigned worktree with changed files, RED and
GREEN commands/results, assumptions, unresolved failures, and the exact package version needed by
the app. Stop on a conflict with the locked acceptance behavior rather than weakening a test.
