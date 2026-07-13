# Canonical world history, artifact checkpoints, and dry-crust proof

**Status:** accepted for implementation in conversation on 2026-07-13 after the architecture,
adversarial-review corrections, and revised written specification were approved.

**Scope:** the architecture shared by imported real-world histories and generated
fantasy/alien planets, plus the first app-side vertical slice that proves the architecture
instead of merely describing it.

**Related authority:**

- `vault/plans/2026-07-07-gplates-truth-playback-and-viewport-systems.md`
- `vault/architecture/runtime-geodata-import-boundary.md`
- `vault/architecture/world-generation-consolidation-refactor.md`
- `../fantasim-world/vault/architecture/event-sourced-plate-topology-port.md`
- `../fantasim-world/project/contracts/World.TruthStream/**`

## 1. Decision

Use a **hybrid canonical history**:

1. Small semantic changes are canonical truth events.
2. Raw imported inputs are immutable provenance artifacts.
3. Accepted dense mantle/crust states are immutable, content-addressed canonical artifacts
   only when a committed event references them.
4. Cross-domain world steps are visible only through a committed manifest that references
   exact mantle, plate, and crust event cursors.
5. Godot meshes, textures, thumbnails, filmstrips, LODs, and unaccepted solver intermediates
   are disposable projections.

This is deliberately neither “events only” nor “snapshots are the whole truth.” Replaying a
large numerical simulation from semantic intent alone is expensive and may not remain
bit-identical across algorithm/runtime changes. Treating every snapshot as opaque truth loses
causality and editability. The hybrid keeps both exact accepted numerical state and the semantic
lineage that produced it.

### 1.1 Classification and authority

| Data | Classification | Replay authority |
|---|---|---|
| Normalized plate/topology/crust semantic event | Canonical | Yes |
| Raw `.rot`, GPML, or normalized shapefile bundle | Canonical provenance artifact | No: replay consumes committed semantic events, not reparsed source bytes |
| Accepted mantle/crust dense checkpoint | Canonical state artifact | Yes, after digest verification |
| World-step manifest | Canonical visibility record | Yes |
| Existing `CrustProductCacheRecord` | Disposable cache | Never |
| Godot mesh/texture/filmstrip/thumbnail/LOD | Disposable projection | Never |
| Uncommitted solver substep | Disposable intermediate | Never |

The existing crust-product persistence remains fail-soft and rebuildable. It must not be renamed,
migrated, or implicitly promoted into canonical checkpoint storage. Canonical checkpoints require
distinct contracts, exact input cursors, fail-closed reads, and a different retention policy.

### 1.2 Source artifact versus semantic truth

Raw import bytes are retained for provenance and audit. They are not reparsed during ordinary
world replay. The parser version and raw artifact digest are recorded by the import-acceptance
event; the normalized semantic events are execution truth. Reprocessing the same bytes with a
new parser creates a new branch. This removes the ambiguity of two competing truths.

## 2. Current evidence and gaps

The engine repo already contains the real `.rot` path:

```text
RotParser
  -> RotationStreamImporter.ToDrafts / ImportAsync
  -> geosphere.plate-rotation.v1 truth events
  -> RotationModelMaterializer
  -> RotationModel / PlateCircuit
```

The app does not use it. `App.World.Services.Service.BuildRotationProvider` reparses raw text and
constructs `ImportedRotationProvider` directly. `WorldRuntime.RunGeneration` only appends a generic
`world.generation` event and explicitly defers materialization. The application therefore has a
working engine path and a separate app bypass.

Other established facts:

- `TruthStreamIdentity` already provides variant, branch, L-level, domain, and model axes.
- `ITruthEvent`/`ITruthEventDraft` currently hash tick, sequence, stream identity, previous hash,
  event type, and payload. They have no standardized world-history lineage envelope.
- SurrealDB writes must go through `ActorTruthEventWriter`; giving an importer the underlying
  store would violate that boundary.
- The existing API reads from a starting sequence but cannot read through a historical cursor,
  verify a cursor hash, or compose branch ancestry.
- Current crust state already distinguishes mountain, volcanic arc, trench, ridge, and fault.
  Presentation code currently permits procedural relief to dominate or give a trench positive
  ridged detail, so a screenshot alone is not proof of tectonic relief.
- `WorldRuntime` was introduced by commit `0345466` on 2026-06-19 as an internal world-library
  composition seam. No linked agent-memory record explains a prior rename request. The present
  rename is justified by its target responsibility, not invented historical intent.

## 3. Canonical references without breaking the hash envelope

Do not add provenance or causation fields directly to `ITruthEvent` in the first slice. Changing
the MessagePack preimage would change deterministic event IDs and hashes and requires a separate,
versioned event-envelope migration.

Instead, new versioned domain payloads carry a common hash-covered context:

```text
WorldEventContextV1
  schema version
  producer component + semantic version + reproducible release digest
  model id/version + canonical configuration digest
  input event cursors[]
  artifact references[]
  units + coordinate/reference-frame id
```

An event cursor is immutable and exact:

```text
TruthEventCursor
  TruthStreamIdentity
  Sequence
  EventHash
  Tick
```

Cross-stream causes reference cursors, not only `EventId` or a command `CausationId`. Command and
activity causation remain a separate UI/operations concept. World-history causal edges must be
acyclic within one committed world step; feedback is expressed across later ticks.

The release digest is a reproducible hash of a normalized producer/package manifest (component id,
semantic version, source/package content digests, and canonical build configuration). It is never an
assembly MVID, timestamp, machine path, or per-build binary hash. Ephemeral build-instance metadata
belongs in logs/activity records, not the canonical event payload.

## 4. Canonical artifact protocol

A canonical artifact reference records at least:

```text
algorithm (sha256)
digest
byte length
media type
schema id/version
compression
dimensional shape/resolution
units
coordinate/reference frame
```

Publication is ordered:

1. Encode the artifact deterministically using an explicit schema: fixed integer field keys, sorted
   map keys where maps cannot be avoided, stable array ordering, invariant culture, and binary
   IEEE-754 floats (never culture-sensitive float text or anonymous-object JSON).
2. Compute its digest and write it to temporary storage.
3. Durably publish it at the digest address.
4. Read back and verify the digest.
5. Append the referencing acceptance/checkpoint event through the single writer.
6. Only then may a world-step manifest expose it.

A missing or corrupt referenced canonical artifact is a hard replay failure. It must never fall
back silently to a cache or regenerate under the current algorithm. Garbage collection may delete
only unreferenced artifacts after a reachability scan from retained branch/manifests.

The default in-memory truth backend is suitable for tests and ephemeral sessions, not durable
offline history. Persistent offline replay requires a local durable truth/artifact backend. That
backend is a later architecture slice; the first slice must not claim cross-process durability
when configured in-memory.

## 5. Replay, branches, and complete world steps

### 5.1 Bounded replay

Exact historical materialization requires a read-through-cursor operation. A materializer must:

1. resolve the requested manifest/cursor;
2. verify the stream prefix and hash chain through that cursor;
3. select a compatible checkpoint whose input cursor is an ancestor of the request;
4. verify and load its artifact;
5. replay only events after the checkpoint and through the requested cursor.

Unbounded “read the current head” materialization is not valid for historical replay.

### 5.2 Branch ancestry

`BranchId` names a branch but does not encode ancestry. A branch begins with a canonical
`world.branch-created.v1` record referencing an exact parent cursor. Parent events are not copied or
rehashed into the new stream. A branch materializer composes the immutable parent prefix with the
child stream. Re-running an algorithm or parser with changed parameters creates a child branch.

### 5.3 World-step manifest

Per-stream append atomicity cannot provide a multi-stream transaction by itself. Use a manifest as
the visibility gate:

```text
world.step-committed.v1
  canonical tick
  domain cursor map (mantle/plate/crust entries when that regime has them)
  shared cause/input cursors
  accepted artifact refs
```

Producers capture exact input cursors before computation. Output events reference those cursors.
The manifest validator verifies tick, branch, ancestry, declared causes, and referenced hashes
before committing the manifest. Newer stream heads do not invalidate an older exact manifest.
A crash before the manifest leaves hidden, unaccepted output; recovery may resume or collect it.

## 6. One architecture, two authoring directions

### 6.1 Imported/reconstructed history

```text
raw source artifact
  -> parser + validation
  -> normalized plate/topology events
  -> plate materialization
  -> conditioned mantle state/events
  -> reconstructed crust state/events
  -> world-step manifest
```

For imported Earth histories, plate motion is observed input. Mantle is a reconstruction
conditioned by plate/boundary history; crust is reconstructed from plate transport and boundary
interaction. The tunnel must show that direction truthfully.

### 6.2 Emergent generated history

```text
world recipe/seed event
  -> accepted mantle state
  -> plate kinematics/topology events
  -> crust state/events
  -> world-step manifest
```

For fantasy/alien planets, an offline or runtime simulator may produce mantle states first. Planets
without mobile-lid tectonics may emit stagnant-lid and crust histories with no plate stream. The
presentation consumes manifests/materialized states and does not special-case the author.

Feedback stays causal by crossing ticks, for example:

```text
mantle(T) -> plates(T+1) -> crust(T+1) -> mantle inputs(T+2)
```

## 7. First vertical slice

This slice proves the architecture at the existing imported-rotation seam and at the visible crust.
It does not pretend the canonical artifact store, branch compositor, or mantle-driven solver is
already implemented.

Execution is split into two independently reviewable packets under the same session goal:

- **P9a:** responsibility rename, both-mode production composition, imported-rotation truth
  adoption, recovery/idempotency, and independent parity proof.
- **P9b:** signed dry-crust geometry, planet/ring presentation adjustments, and exported-app visual
  proof.

P9a and P9b may run in separate worktrees and pass their own gates. Neither packet's success is
substituted for the other's.

### 7.1 Responsibility-accurate rename

Rename:

- `WorldRuntime` -> `WorldHistoryCoordinator`
- `IWorldRuntime` -> `IWorldHistoryCoordinator`
- `WorldRuntimeFactory` -> `WorldHistoryCoordinatorFactory`
- `WorldRuntimeTests` -> `WorldHistoryCoordinatorTests`
- `_runtime` fields referring to this seam -> `_history`

Delete `StubWorldRuntime`; do not rename or preserve a meaningful no-op production path.

The rename is not allowed to be purely mechanical. The class earns “history coordinator” by owning
the app-side import/materialize/query workflow while the outer `Service` continues to enforce
backend and actor composition. It receives a read capability and a writer capability without
exposing the raw writable store to importers. `Service` owns/disposes the store handle and writer;
the coordinator does not dispose injected infrastructure it does not own.

Current DTO queries, including render snapshots, are materialized-history queries and may remain on
the coordinator for this slice. If the implementation leaves the type as only a catalog façade plus
placeholder append, the rename fails its acceptance gate.

`UseProjectReferences` may select dependency source only; it must no longer select real versus stub
behavior. The coordinator, truth reader/writer, store factory, and import/materialization workflow
compile in both modes:

- `UseProjectReferences=true`: sibling `fantasim-world` project references;
- `UseProjectReferences=false`: published package references for the same fields, truth-stream,
  rotation-stream, and reconstruction closure.

P9a adds/publishes any missing engine package (notably plate reconstruction) and adds the missing
package references. Both modes run the same behavior tests. The current bundle/export tasks default
to project references, but the exported gate must record the actual MSBuild property and prove the
active coordinator/writer/materializer in the staged DLL and running app.

While opening this seam, remove the anonymous collectible value that `GetFieldValues` currently
returns through `IReadOnlyDictionary<string, object>`. Use a contract-owned `WorldFieldDescriptorDto`
(or another contract/BCL-only shape) and add an ALC test proving no bundle-defined anonymous value
escapes through `IService`.

### 7.2 Imported rotation commit/materialization path

The app workflow is:

1. Accept source name, `.rot` text/bytes, world/branch identity, and plate-onset binding.
2. Parse once with `RotParser`.
3. Preserve current fail-closed behavior: any parse issue rejects the import; append nothing.
4. Append `world.rotation-source-prepared.v1` on the separate
   `{world}:{branch}:L0:world:imports` control stream. It records source digest, parser/release
   digest, onset tick, target plate stream, deterministic ordered-draft digest/count, and the
   expected prior plate head. Prepared does not activate the source.
5. Use `RotationStreamImporter.ToDrafts`, then append those drafts through a new
   `ITruthEventWriter.AppendIfHeadAsync`/`ActorTruthEventWriter` CAS path using the prepared expected
   head. Do not call `ImportAsync` with the underlying store.
6. After the atomic plate-draft batch returns its exact head, append a versioned
   `world.rotation-source-bound.v1` event on a separate `{world}:{branch}:L0:world:imports`
   control stream. Its payload contains source digest, parser version, onset tick, and the
   resulting plate-stream cursor. Only that binding activates the imported source; a crash after
   the plate append but before bind leaves plate events recoverable but invisible to the app.
7. Read the committed stream through a read-only capability and materialize `RotationModel`.
8. Adapt the materialized total rotations to the app's onset-relative `IPlateRotationProvider`:
   `R_abs(timeMa(tick)) * inverse(R_abs(onsetMa))`. The existing playback convention pins
   `onsetMa = 0 Ma` and maps `timeMa(tick) = TickDeltaToMegaAnnum(tick - onsetTick)`; interpolation
   is SLERP and out-of-range samples preserve the current clamping behavior.
9. Store the committed stream/cursor binding in app state; raw `_rotationSourceRecipe.RotText` is no
   longer the authority after commit.

Do not blindly replace `ImportedRotationProvider`. Its behavior must be matched or deliberately
rejected before removal. Parity tests cover:

- numeric plate-id normalization, including leading zeroes;
- exact keyframes and interpolated times;
- missing 0 Ma keyframe;
- non-identity 0 Ma keyframe;
- fixed-plate circuit composition;
- a source whose fixed parent changes over time.
- explicit time direction: the onset tick is identity, increasing canonical ticks sample increasing
  Ma before present, and the expected orientation sign/order matches the current contract.

If `RotationModelMaterializer` cannot represent a semantically valid GPlates case the current
provider accepts, including a valid fixed-parent change through time, enhance the engine
materializer with tests. Rejection is reserved for invalid/cyclic input, not used as an easier
substitute for real GPlates coverage. No hidden fallback to direct reparsing is allowed.

The plate stream uses the established five-axis convention, parameterized from the requested world
instead of hardcoded app constants. For the existing plate stream the domain/model convention is
`{world}:main:L0:geosphere:plates`; exact branch and world ids come from the request/world record.
The first slice permits one active source binding per immutable world/branch: importing the same
source digest/onset is idempotent, while a different source is rejected with “new branch required”
until parent-cursor branch composition lands. Rotation ticks must never be appended a second time
after a later head on the same immutable plate stream.

Retry/recovery follows the control-stream state machine:

- prepared + plate head still equals the expected prior head: execute the CAS append;
- prepared + the appended event range exactly matches the deterministic ordered drafts: verify the
  actual head and append the missing bound event;
- bound already exists for the same source/onset/head: return idempotent success;
- any other plate/control head: fail closed as an import conflict.

This closes the crash window without re-appending ticks or treating an orphan batch as active.

Existing `PlateRotationPayload` bytes remain unchanged in this slice so current codecs and event
hashes stay stable. New import-control payloads carry `WorldEventContextV1`; a later versioned
payload migration can add the context directly to rotation events if required.

The lead session—not the implementation agent—authors the parity oracle from independently derived
quaternion fixtures and expected values. The GLM implementation packet may consume those RED tests
but may not define or weaken their expected results.

### 7.3 Data-driven dry crust

Dry-crust geometry starts from `CellElevations`. `CellFeatures` contributes signed, feature-specific
detail:

| Feature | Required geometric direction |
|---|---|
| Mountain | positive relief, broadened orogenic mass |
| Volcanic arc | positive localized/conical relief along active arc cells |
| Trench | negative narrow depression; never positive ridge detail |
| Ridge | positive flanks/crest with optional shallow axial notch |
| Fault | no default vertical displacement unless a later model provides one |

Procedural noise is secondary fabric. With a fixed base state, changing/removing feature data must
change geometry more at tagged cells than changing/removing the noise fabric. Noise-disabled tests
must prove signs before any screenshot is accepted.

Visual target: the gray, faceted, rocky reference supplied on 2026-07-13—mountains, trenches, and
volcanic structures visible without hydrology or biome color. Hydrology and biome presentation stay
disabled for this gate. The planet has independent zoom, may grow beyond the instrument-ring aperture,
and tunnel rings remain thin enough not to frame it as a bounded token.

## 8. Acceptance gates

### 8.1 Architecture and import tests

- A source-level/architecture test proves production `Service.BuildRotationProvider` no longer
  constructs or reparses `ImportedRotationProvider`.
- Valid `.rot` input appends `geosphere.plate-rotation.v1` events through the writer and the app
  consumes the committed materialization.
- Malformed/empty input appends zero events and returns a failure.
- Repeating the same import has an explicit idempotency result; it does not silently duplicate the
  active binding.
- Onset-relative parity tests listed in section 7.2 pass.
- Concurrent SurrealDB-path imports remain serialized through `ActorTruthEventWriter`; in-memory
  tests alone are insufficient evidence.
- A forced failure between prepare, plate CAS append, and bind proves retry completes or fails
  closed without duplicating the immutable plate events.
- `UseProjectReferences=true` and `UseProjectReferences=false` both compile and pass the same
  coordinator/import behavior suite; neither constructs a stub.
- The staged/exported world bundle records its build mode and contains the active coordinator,
  actor writer, truth reader, and rotation materializer. Runtime logs prove a real imported append
  and materialization reached that path before the exported visual gate is accepted.
- Existing hash-chain, serializer, field-catalog, and ALC collection tests remain green.
- An ALC test proves `GetFieldValues` returns only shared contract/BCL value types.
- No new top-level event-envelope field changes existing event hashes in this slice.

### 8.2 Crust tests

- With noise disabled, mountain and volcanic feature vertices are higher than their no-feature
  baseline; trench vertices are lower; ridge flanks are higher.
- A trench never receives the positive ridged-detail branch.
- Geometry changes when `CellElevations`/`CellFeatures` change and stays deterministic for fixed
  inputs.
- Noise contribution is bounded below the tectonic feature signal at tagged cells.
- Existing watertight plate-surface and adaptive-subdivision tests remain green.

### 8.3 Exported-app visual gate

Keep the exported Godot app open, reload/install the changed PCKs, and capture fresh screenshots at
real generated ticks/orientations containing actual mountain, trench, and volcanic feature cells.
Evidence must include:

- dry gray/faceted crust with hydrology and biomes off;
- visible positive mountains/volcanic relief and negative trenches;
- a larger independently zoomable planet not bounded by the rings;
- thinner tunnel rings;
- runtime diagnostics identifying selected tick, feature counts/kinds, and displacement extrema;
- successful collectible-ALC unload/collection after bundle reload.

A screenshot complements but does not replace the directional geometry tests.

## 9. Remaining adoption roadmap

### Phase B — canonical checkpoint foundation

- canonical artifact store and publication protocol;
- bounded read-through-cursor and hash verification;
- branch ancestry/composition;
- world-step manifest validator;
- durable local offline backend;
- canonical mantle/crust checkpoint payloads distinct from caches.

### Phase C — event-source the emergent producer chain

- recipe/seed events;
- offline/runtime mantle producer emits accepted state/checkpoints;
- plate solver consumes mantle cursors and emits kinematics/topology;
- crust solver consumes plate cursors and emits state/checkpoints;
- feedback only through later-tick events/manifests.

### Phase D — causal tunnel

- materialize domain causal edges from exact event cursors;
- derive corridor direction from stored lineage rather than hardcoded labels;
- distinguish observed/imported forcing from simulated/emergent forcing;
- select a tunnel time plane by committed world-step manifest.

### Phase E — import coverage and other planets

- GPML/shapefile normalized source artifacts and semantic topology events;
- Mars/Venus/stagnant-lid histories without forced plate streams;
- real exoplanet/fantasy recipes using the same event/artifact contracts.

## 10. Adversarial review reconciliation

Two fresh reviews were run before this design stood: an isolated reviewer and OpenCode Z.AI
GLM-5.2. Their high-impact findings were reconciled as follows.

| Finding | Classification | Design response |
|---|---|---|
| Multi-stream append is not atomic | Valid/actionable | Manifest is the visibility gate; exact cursors and validation are mandatory |
| Historical reads are unbounded | Valid/actionable | Add read-through-cursor and hash verification before checkpoint replay |
| `BranchId` has no ancestry | Valid/actionable | Add parent-cursor branch creation; never copy/rehash parent events |
| Importer could bypass SurrealDB actor | Valid/actionable | Use `ToDrafts` + `ITruthEventWriter`, never raw store append from app workflow |
| Current provider and materializer differ | Valid/actionable | Require onset/id/circuit parity; enhance or explicitly reject before removal |
| Coordinator has writer but no reader | Valid/actionable | Inject separated read capability and writer; keep ownership in outer service |
| Existing crust cache could become truth | Valid/actionable | Explicitly prohibit promotion; use separate canonical types/store |
| Raw source and semantic events conflict | Valid/actionable | Raw bytes are provenance; committed normalized events are replay authority |
| Canonical blob may be missing | Valid/actionable | Publish/verify blob before reference event; missing reference fails closed |
| Screenshot cannot prove relief direction | Valid/actionable | Add noise-disabled signed geometry tests and runtime diagnostics |
| Event-envelope metadata change breaks hashes | Valid/actionable | Put V1 context in versioned payloads; defer envelope migration |
| Importer/materializer do not exist | Contract misread | They exist in sibling `fantasim-world`; the real gap is app adoption |
| `.rot` must be one huge event payload | Noise | Importer emits normalized per-rotation drafts; raw bytes use artifact protocol later |
| Export necessarily composes the stub | Contract misread + valid risk | Current bundle defaults project refs and contains the actor path; P9a nevertheless removes the stub and proves both modes |
| Import/bind crash can wedge immutable stream | Valid/actionable | Add prepared -> CAS plate batch -> bound state machine and recovery tests |
| Parity oracle may share implementation bug | Valid/actionable | Lead owns independent expected values and explicit time-direction cases |
| Anonymous field descriptor can pin ALC | Valid/actionable | Replace with contract/BCL shape and add collection/type-boundary test |

## 11. Implementation sequencing gate

After the user reviews this written specification:

1. write a TDD implementation plan with exact paths and RED/GREEN gates;
2. dispatch P9a and P9b as bounded packets through OpenCode
   (`zai-coding-plan/glm-5.2` for implementation);
3. lead session reviews every diff and commits by meaningful step;
4. run focused tests, the repository build/test workflow, and the exported-app visual/ALC gate;
5. deposit established/disproven conclusions in the active plan and Supermemory.
