---
source: G-010 App migration design dialogue, 2026-07-15 (user + lead), implementing the accepted hub variant-identity design section 6 and end gate
source-status: DESIGN SPEC — architecture approved 2026-07-15; internal adversarial review reconciled; GLM-5.2 cold audit completed with truncated final response; written-spec review pending
distilled: 2026-07-15
plan: prerequisite to the session-scale TDD implementation plan for the complete G-010 end gate
---

# G-010 App world migration and two-world restart design

## Objective

Implement the App-side migration required by the accepted G-010 design:

- two exact-referenced worlds with different declarations and enabled pack sets;
- one process and one open declaration registry;
- real producer resolution through the production extension seam;
- world- and branch-scoped execution, product, truth-stream, cache, timeline, tunnel, and globe state;
- a persisted exact active selection and persisted per-world UI state;
- durable truth, cache, and UI recovery in a second process;
- grandfathered `default` / `app:main` continuity without rewriting old truth;
- collectible world/timeline bundle unload after the final integrated result.

This document is the dependent **App migration plan design** reserved by the completed
world-declaration contract plan. It implements, rather than replaces, the accepted hub design:

- `fantasim-hub/vault/specs/2026-07-15-variant-identity-and-world-declaration-design.md`
  section 6 (`default`, `app`, and `base` dispositions) and section 7 (two-world restart gate);
- `fantasim-hub/vault/ledger/g6-decisions-2026-07-14.md` D2 (new streams use a dotted
  `Domain` and governing model `M0`; the legacy bare-domain list is closed);
- `fantasim-hub/vault/architecture/lrm-axis-model.md` (Variant = world name, Branch = history,
  Domain = truth slice, M = governing model).

## Locked decisions

1. **SurrealDB is the only database used by this migration.** One resident SurrealDB session
   backs truth, exact selection, per-world UI state, and the touched persistent caches. Do not add
   a LiteDB, SQLite, RocksDB-library, or other direct database path. The durable gate runs an
   external SurrealDB server over its RocksDB storage engine; the application still speaks only
   SurrealDB through `SurrealDb.Net` and UnifyStorage.
2. **Registry-backend persistence remains out of scope.** Platform and gate-consumer plugins
   deterministically re-author their immutable declarations on every process start. The persisted
   selection exact-resolves only after all required contributors report ready.
3. **Truth implementation remains collectible.** The resident layer owns the SurrealDB client and
   shared Unify storage adapters. The collectible world runtime root owns `KvTruthEventStore`, one
   actor writer, and every per-world session. `World.TruthStream.Core`, producer instances, and the
   declaration-registry implementation do not become resident.
4. **The UI has one world picker and one branch picker.** Selecting a world atomically replaces
   the timeline/globe/tunnel projection. “Disjoint UI surfaces” means no union or leakage between
   selections, not duplicated simultaneous HUDs.
5. **`fantasim/science` v1 remains frozen.** The complete current platform layer set is authored
   as science v2, and the default world receives declaration v2. Existing v1 exact references and
   golden digests remain resolvable.
6. **No fabricated production data.** Existing atmosphere producers are wired. The generic
   fabricated filmstrip catch-all is removed. `DeclaredEmpty` remains an honest, explicitly
   authored state; a missing required producer is a startup error.
7. **Test consolidation is a later maintenance wave.** G-010 may rewrite or add gates, but it does
   not delete ALC, CAS/hash-chain, digest, exact-resolution, seam, restart, or exported-window
   proofs. Fixture/source-scan consolidation happens after the end gate stands.
8. **A successor stream is durably bound to one truth configuration.** The dotted successor name
   remains world/branch/domain/model shaped, but its first append atomically pins its
   `TruthDigest`. A later declaration with a different truth digest uses a new branch (or a future
   explicitly designed migration); quiescing a process-local session is not enough to reuse the
   existing stream.

## Existing surfaces audited

This design extends existing surfaces rather than creating parallel ones:

| Concern | Existing surface | Decision |
|---|---|---|
| Exact declarations | `IWorldDeclarationRegistry`, `WorldDeclarationReference`, two digests | Reuse; add runtime consumption and contributor readiness only |
| Registry implementation | `InMemoryWorldDeclarationRegistry` in the world bundle | Keep collectible; recreate and deterministically re-author at startup |
| Truth events | `ITruthEventStore`, `KvTruthEventStore`, `ActorTruthEventWriter` | Reuse; correct store CAS and move ownership from each `Service` to one bundle runtime root |
| SurrealDB durability | `WorldTruthEventStoreFactory`, `SurrealDbKeyValueStore`, `tools/verify-durable-rotation-restart.sh` | Move client/provider construction resident; extend the existing two-process proof |
| Resident persistence | `App.Common/Bootstrap`, current resident document-store registration | Replace the G-010-touched LiteDB construction with resident SurrealDB adapters; no second persistence kernel |
| Derived cache record | `CrustProductCacheRecord`, `DocumentBlob` | Extend identity with world/branch/digest and store through SurrealDB; old rows are misses |
| Layer projection | `LayerTrackRegistryService`, `LayerTrackRegistryBuilder` | Create one projection per exact world/branch; declaration `EnabledLayers` is authoritative |
| Stale async guards | `FilmstripRevisionGate`, `ScrubApplyScheduler.Generation`, timeline bind generation, tunnel `_generation` / `_modeEpoch` | Compose them with one new selection epoch; do not replace them |
| Durable truth proof | `ExternalSurrealRotationRestartProofTests` and its shell tool | Extend to two exact worlds and all required persisted state |

The external SurrealDB server is an acceptance/deployment dependency, not a new app-managed server
surface. The app does not spawn, supervise, or package a database daemon in this arc.

## Scope and non-goals

### In scope

- shared SurrealDB infrastructure and fail-closed configuration;
- exact active selection and selection epoch;
- bundle-level truth runtime ownership;
- contributor readiness and exact restart resolution;
- successor generation and rotation-selection streams;
- legacy read continuity for `default/main`;
- `WorldName` / `BranchId` execution and product addressing;
- world/branch/digest cache identity and SurrealDB cache persistence;
- per-world layer-track projection, archive/active-layer state, and world/branch selectors;
- selection-safe filmstrip, globe, graph, and tunnel publication;
- science/default v2 completeness and real atmosphere wiring;
- a tool-only consumer pack that exercises the real extension seam;
- two-process, exported-window, and ALC gates.

### Not in scope

- persistent declaration-registry backend;
- registration authentication/authorization;
- G-011 meta-stream taxonomy;
- full branch-overlay revalidation or nested branch composition;
- full G-001 cleanup of every overloaded `WorldId` string;
- app-managed SurrealDB process lifecycle;
- migration of derived cache rows from the existing LiteDB file;
- unrelated `App.Activity` persistence replacement;
- simultaneous side-by-side world HUDs;
- test-count reduction before the functional migration is complete.

## Architecture

### 1. Resident SurrealDB persistence runtime

`App.Common` constructs exactly one SurrealDB service provider, async scope, and
`ISurrealDbSession` for the process lifetime. It connects and selects the configured namespace and
database before collectible bundles initialize. The same session backs:

- a generated `SurrealDbDocumentStore` registered as `IDocumentStore` for selection, UI state,
  crust cache, and filmstrip cache documents;
- a generated `SurrealDbKeyValueStore` registered as `IConditionalKeyValueStore` for truth-event
  atomic batches and compare-and-write.

Both interfaces live in already-shared `UnifyStorage.Abstractions`. Bundle code resolves them at
the point of composition and never receives `ISurrealDbSession`, generated wrapper types, a
service provider, or a disposal handle. Both generated adapters are non-owning wrappers around the
same session; all bundle borrowers are non-owning and must never dispose them. `App.Common` alone
disposes the adapters, async scope/provider, and session, in that order, during process shutdown.

The existing `world:truthStore:connectionString` becomes the one app-wide persistence connection
string for this arc. `world:truthStore:backend` is retired from production composition: the app
does not silently select another implementation. A missing/invalid connection or failed connect
is a boot failure, never an automatic in-memory or other-database fallback. Unit tests may inject
in-memory contract doubles; integration and exported gates use SurrealDB.

SurrealDB assemblies move from the collectible `world` manifest into the resident common floor.
The Surreal backend generator activation and package reference move with them: `App.Common`
generates/constructs its own resident document and conditional-KV adapters, while the collectible
App.World project removes its generated Surreal adapter namespace and direct `SurrealDb.Net`
construction. There is one generated implementation set, not a resident wrapper around a second
bundle-local generated set. Concretely, the Surreal runtime assemblies are removed from the
`world` entry in `collectible-bundles.json` before their required production closure is added to
`shared-assembly-policy.json` / common staging; an assembly may not remain on both sides of that
policy. The unused `SurrealDb.Embedded.InMemory` package is not production-staged merely because it
was in the old world closure.
`World.TruthStream.Contracts` and `World.TruthStream.Core` do not move merely because the backend
does. Common-bundle staging, dual-copy detection, a clean fresh boot, and final ALC collection are
required whenever this assembly placement changes.

The durable acceptance profile starts an external SurrealDB server with a RocksDB path, exactly as
the existing restart tool does. SurrealDB documents RocksDB as a persistent single-node storage
engine and `surreal start ... rocksdb:<path>` as the supported server shape:

- https://surrealdb.com/docs/reference/cli/surrealdb-cli/commands/start
- https://surrealdb.com/docs/build/deployment

### 2. Resident selection authority

Add shared records to `FantaSim.App.World.Contracts`:

```text
PersistedWorldSelection
  ExactReference: exact/digest-pinned WorldDeclarationReference
  BranchId

ResolvedWorldSelectionIdentity
  ExactReference: exact/digest-pinned WorldDeclarationReference
  WorldName
  BranchId
  TruthDigest
  PresentationDigest

WorldSelectionStamp
  Identity: ResolvedWorldSelectionIdentity
  Epoch: long
```

`PersistedWorldSelection` does not duplicate declaration-owned identity. Recovery exact-resolves
its pinned reference after the contributor barrier, then a validating factory constructs
`ResolvedWorldSelectionIdentity` from that resolved declaration. The factory rejects a bare/latest
reference, a reference whose name/version/digest does not match the resolved declaration, an
invalid branch, or any attempted caller-supplied digest. The runtime convenience fields are
therefore derived facts, never a second identity authority.

The stamp contains only shared immutable/value data. It never contains a declaration registry,
producer, runtime service, cache, delegate, async enumerator, or bundle implementation instance.

A resident selection service owns:

- the current stamp;
- a checked, monotonic, process-local epoch incremented for every accepted transition, including
  A → B → A;
- a persisted exact selection document;
- persisted per-`(WorldName, BranchId)` UI preferences (archived tracks and active-layer set);
- a disposable subscription surface whose collectible subscribers must release on unload.

The epoch is never persisted and is not truth identity. Restart exact-resolves the persisted
reference and creates a fresh epoch. If no selection record exists, `default/main` is authored and
selected. If a record exists but its exact declaration or enabled pack is unavailable after the
readiness barrier, startup fails closed with a visible diagnostic; it never falls back to latest or
default.

Document ids percent-encode each identity field as a path/key segment. The unencoded values remain
in the document payload and truth provenance. `/` therefore remains legal in qualified world names,
while `:` remains forbidden by stream vocabulary validation.

### 3. Collectible world runtime root and sessions

`WorldPlugin` owns one `WorldRuntimeRoot` for one collectible bundle activation. It receives the
resident `IConditionalKeyValueStore`, constructs a collectible `KvTruthEventStore`, starts one
`ActorTruthEventWriter`, and creates a `WorldRuntimeCatalog`.

The runtime root, not individual `Service` sessions, owns the truth-store reference and the actor
writer lifecycle. `KvTruthEventStore` is not disposable and owns no resident backend resource; the
root stops the writer, then drops the store/interface references. Every world session borrows them.
Shutdown order is fixed:

1. detach resident commands, selection subscribers, and facades;
2. cancel and drain all in-flight session work;
3. dispose every world/presentation session and producer instance;
4. stop the actor writer and verify termination;
5. release bundle references to the resident storage interfaces;
6. dispose the registry and plugin root;
7. allow the collectible ALC probe to collect.

The resident SurrealDB session remains open until app shutdown. The bundle-owned
`KvTruthEventStore` does not own or dispose that session.

The runtime catalog separates truth identity from presentation identity:

- truth session key: `(WorldName, BranchId, TruthDigest)`;
- presentation projection key: `(WorldName, BranchId, PresentationDigest)`;
- active exact selection: both keys plus the exact reference and current epoch.

Only one `TruthDigest` may be active for one `(WorldName, BranchId)` at a time. More strongly, each
non-empty successor stream has a durable binding record containing its identity and one
`TruthDigest`. The first append CAS-writes that binding with the first event/head; every later open,
read, and append requires an exact match. Every versioned domain payload carries
`WorldEventContextV1` with the same digest in `CanonicalConfigurationDigest`, and recovery verifies
that context on every event. A presentation-only version may replace the presentation projection
without forking truth. A different truth digest for the same world uses a new branch unless a
future migration contract explicitly rebinds history; process-local quiescence never authorizes
reuse.

### 4. Contributor readiness and deterministic re-authoring

The process registry remains an in-memory, bundle-owned implementation. Contribution is a distinct
host composition phase, not work nested inside `BundleHost.InitializeAsync`:

1. the world bundle constructs the empty registry and ephemeral registrar, registers their shared
   T1 capability surfaces, and returns from initialization without waiting for another bundle;
2. each already-loaded platform/consumer bundle registers an `IWorldRuntimeContributor` shared
   capability and returns from its own initialization without loading another bundle;
3. after all `AddGroupAsync` mutations have returned and the BundleHost gate is free, the resident
   composition coordinator invokes the contributors in a deterministic order;
4. contributors author immutable lawsets, layers, producer registrations, and declarations, then
   the coordinator seals the registrar;
5. only after a successful seal may persisted selection exact-resolve and runtime sessions start.

Contributor failures, duplicate claims, digest mismatches, or missing exact versions abort the
seal. No timeout path silently starts a partial/default world. A contributor is invoked once per
fresh registry activation; the existing first-write-wins claim semantics stay unchanged. Reload
first quiesces sessions and disposes the old registration set, performs BundleHost mutations, then
runs a fresh seal outside the mutation gate.

Pack loading remains process-wide; enablement is per declaration. A loaded pack is inert in a
world that does not list it in `EnabledPacks` / `EnabledLayers`.

### 5. Producer resolution and science/default v2

The producer catalog implementation remains collectible/internal, but the extension on-ramp is an
explicit shared T1 contract in `FantaSim.App.World.Contracts`:

```text
IWorldRuntimeContributor.Contribute(IWorldContributionRegistrar registrar)
IWorldContributionRegistrar.RegisterProducer(ProducerRegistration registration,
                                              IWorldProducerFactory factory)
IWorldProducerFactory.Create(WorldProducerCreationContext context) -> IFieldProducer
```

The registrar is valid only during the host contribution phase and rejects calls after sealing.
Registration and creation-context DTOs contain only shared immutable values. `IFieldProducer`
already lives in the shared App.World contract assembly; `IWorldProducerFactory` is a new shared
method-based capability implemented by the consumer bundle. No delegate, `object`, `Type`, async
enumerable, or bundle implementation type appears in a shared DTO. The collectible catalog may
retain the capability until quiescence, owns every created producer, disposes disposable
producers/factories and releases all cross-ALC edges before either the consumer or world bundle
unloads. The resident coordinator retains no contributor, registrar, or factory after the seal
call returns. While a sealed registration set is live, BundleHost treats the contributing consumer
bundles as dependencies of the world activation: an independent consumer unload is rejected until
world sessions quiesce and the catalog releases that contributor's factories.

Registration and lookup keys include producer origin kind, origin id, origin content digest, layer
id, and producer content digest. The resolved declaration supplies the allowed origins: its exact
lawset-chain contributors plus the exact `(PackId, PackContentDigest)` entries in `EnabledPacks`.
A registration from a merely loaded but disabled pack is ineligible even if its layer id and
producer digest collide with an enabled registration.

For each enabled layer, runtime construction filters by those allowed origins and must yield
exactly one of:

- a real generation/field producer and its real preview source;
- a real presentation-only source such as the existing mantle preview path;
- an explicit `DeclaredEmpty` binding authored by the declaration.

Zero bindings or duplicate bindings fail startup. The generic presenter remains available for real
content without a rich presenter, but it never invents values. The `Service` switch arms only known
real preview sources; its current `atmosphere-placeholder` and generic procedural catch-all are
removed.

`fantasim/science` v2 adds immutable descriptors for the existing real mantle source and the
existing `AtmosphereBulkLayer` / `AtmosphereCoupledLayer` producers. `default` declaration v2 pins
science v2 and enables the seven intended visible layers in lane order. Science v1 and default v1
remain byte/digest frozen and exact-resolvable.

The second-world acceptance content is a tool-only consumer pack, excluded from production
manifests. It registers through the same contributor and producer-catalog contracts as a real
consumer, supplies a deterministic real producer, and authors an exact world declaration with a
pack/layer set different from `default`. Test/tool-only content satisfies the no-fake rule without
shipping named fantasy production content from the platform.

### 6. Truth-stream migration and legacy continuity

`WorldStreamVocabulary` gains the only two successor factories:

```text
RotationSelection(worldName, branchId)
  {worldName}:{branchId}:L2:world.rotation-bindings:M0

Generation(worldName, branchId)
  {worldName}:{branchId}:L2:world.generation:M0
```

The stream guard expands to every production directory that may mint truth or track identities. It
asserts dotted domain + `M0` for every new factory and carries an explicit closed allowlist for the
grandfathered bare-domain factories.

One frozen `LegacyDefaultCompatibilityBinding` makes the otherwise unversioned grandfathered data
explicit. It pins the exact `default` v2 declaration reference and truth digest accepted by this
migration, the two legacy identities, and the compatibility-adapter version. The adapter validates
that every legacy rotation binding refers to an enabled, semantically compatible layer; legacy
generation remains only a dirty marker. No other `default` declaration version inherits this
binding automatically. A future v3 needs its own reviewed binding or starts from empty successors.
The binding's exact digest is a golden acceptance constant after default v2 is authored.

Continuity rules are per stream, not for an imaginary combined `app:main` stream:

1. all new writes target the world/branch successor only;
2. every successor read verifies identity, contiguous sequence, previous hashes, recomputed event
   hashes, and stored head;
3. a non-empty successor is authoritative; corruption is an error and never triggers fallback;
4. only the exact declaration named by `LegacyDefaultCompatibilityBinding`, on `default/main`, and
   only while the corresponding successor and durable binding are genuinely absent, may read its
   grandfathered stream;
5. non-default worlds and non-main branches never consult grandfathered app streams;
6. old and new hash chains are never concatenated, re-sequenced, rehashed, dual-written, or joined
   with a bridge/meta event.

For rotation selection, legacy terminal generated/imported state supplies the effective initial
state. The first post-migration change appends a normal full replacement-state v2 event to the empty
successor with CAS against the successor head. V2 payloads include `WorldEventContextV1`; imported
state continues to reference the exact bound control cursor.

For generation, the current consumer uses the stream head as a dirty marker. `default/main` is
dirty when either verified legacy or successor has a head. New generation v2 events contain the
generation request plus `WorldEventContextV1` and append only to the successor. The legacy archive
remains independently readable; this arc does not pretend the two streams are one replay chain.

### 7. Truth-store concurrency corrections

The multiworld migration depends on storage-level atomicity, not only the in-process actor:

- `KvTruthEventStore.AppendIfHeadAsync` requires `IConditionalKeyValueStore`; it fails closed if
  genuine compare-and-write is unavailable instead of falling back to unconditional write.
- `AppendAsync` becomes an observed-head CAS retry loop so a second process cannot overwrite the
  same next sequence/head.
- the first append writes the stream's exact truth-digest binding, events, and head in one
  SurrealDB transaction through `TryWrite`; later appends condition both head and binding.
- bounded retries surface a concurrency diagnostic; they never silently drop an append.

Recovery verifies the binding and full event chain in the application coordinator. Deterministic
fault-injection tests at the store seam prove failure before commit and after commit; the external
SurrealDB proof kills the client process around a controlled append while leaving the server
storage recoverable. The acceptable post-restart states are the complete binding/batch/head or no
new batch; a partial binding/event/head combination fails the gate. Server-crash durability beyond
SurrealDB's documented transaction/storage contract is not inferred from a timing-sensitive shell
kill.

### 8. Execution, products, and caches

Every runtime request carries `ResolvedWorldSelectionIdentity`. The literal `base` retires from
`WorldGenerationGraphExecutionScopeKey` and `WorldGenerationProductAddress`; their Variant slot is
the exact `WorldName`, and their Branch slot is the selected `BranchId`.

Qualified world names are percent-encoded only when embedded in product paths or document ids.
Parsing decodes the segment and validates that it exactly matches the provenance payload.

Cache identity rules:

- simulation/crust caches include `WorldName`, `BranchId`, and `TruthDigest` in addition to their
  existing seed/frequency/revision/rotation/tick fields;
- presentation/filmstrip caches include `WorldName`, `BranchId`, `TruthDigest`, and
  `PresentationDigest` plus graph revision/rung/dimensions;
- selection epoch is an in-memory stale-publication guard, not a persisted cache key;
- old unscoped rows receive no migration and become misses.

Crust and filmstrip documents use the resident SurrealDB `IDocumentStore`. Cache failures remain
fail-soft misses because caches are derived; selection/UI persistence and truth connection failures
are fail-closed because they are required for exact restart recovery.

### 9. Per-world UI projection and atomic switching

Each exact world/branch session owns one declaration-derived `LayerTrackRegistryService` projection.
Only enabled, visible declaration layers appear. Lane order comes from presentation bindings.
Every track stream identity uses the selected world and branch. The global declared-layers union is
not an authority after this migration. A prepared `WorldProjectionLease` groups the resolved
selection identity, runtime session, layer snapshot, timeline context, and globe/tunnel bind inputs;
callers never pair a separately read current identity with an unrelated projection.

The existing timeline header gains one world `OptionButton` and one branch `OptionButton`. A switch
uses prepare/commit:

1. exact-resolve and build the candidate runtime/projection off-screen while A remains the current
   visible lease;
2. if preparation fails, dispose the candidate and leave A untouched;
3. durably upsert the candidate `PersistedWorldSelection`; if that write fails, dispose the
   candidate and leave A untouched;
4. on the Godot main thread, enter one non-reentrant commit section, mint the next epoch, cancel and
   detach A, install the complete B lease into all binders, and publish the new current
   `WorldSelectionStamp` as part of that same lease swap;
5. notify observers only after the complete B lease is current; then dispose A's remaining
   resources after its work has drained.

There is no state in which the authority reports B while A remains visible. Every asynchronous
operation captures the full lease stamp at start and compares it immediately before publish:

```text
publish iff
  captured selection stamp == current selection stamp
  AND captured subsystem generation == current subsystem generation
  AND the subsystem-specific revision/epoch remains valid
```

The new check composes with existing guards:

- filmstrip: selection stamp + `FilmstripRevisionGate` + cancellation;
- globe: captured selection stamp + captured mount generation before document fetch;
- scrub: selection stamp + `ScrubApplyScheduler.Generation`;
- timeline face: selection stamp + resident bind generation; tunnel mode epoch stays orthogonal;
- tunnel: selection change increments its existing generation, cancels gestures/F9/filmstrip work,
  unsubscribes the old controller/registry, and rejects stale completions;
- graph and layer-registry callbacks carry/capture the selection stamp and cannot rebuild a later
  selection.

The A → B → A race is a required test: a callback from the first A is stale even though its world,
branch, and declaration digests match the current A. The epoch distinguishes the two lifetimes.

Persisted UI state is keyed by `(WorldName, BranchId)` and contains only layer ids. On restore, ids
are intersected with the exact declaration's enabled layers; removed/disabled ids never reappear.

## Error handling

- SurrealDB connection/configuration failure: fail app boot; no alternate database fallback.
- Missing exact declaration after contributor readiness: fail closed; no latest/default fallback.
- Missing/duplicate producer for a non-empty enabled layer: fail world-session construction.
- Different truth digest requested for an already bound world/branch successor: reject and require
  a new branch (or a future explicit migration), even after the prior session quiesces.
- Successor truth corruption: fail recovery; never conceal with legacy fallback.
- Derived cache read/decode/version failure: log, delete/ignore only that derived row, recompute.
- Persisted UI id not enabled by the exact declaration: ignore the id and persist the normalized
  state on the next mutation.
- Selection preparation failure: keep the old complete projection active; do not expose a
  half-rebound UI.
- An unexpected exception after the durable selection write or inside the non-reentrant
  main-thread commit is fatal to that UI host and leaves a diagnostic unavailable surface carrying
  the exact requested reference; restart exact-resolves that durable requested selection. It never
  silently returns to default or claims the old lease under the new identity.

## Dependency and parallelism model

The work is not one implementation session. It is a dependency-ordered program of bounded goals.

### Wave 1 — serial foundation

- extract truth, cache/product, filmstrip, and session-lifecycle collaborators from `Service`;
- establish resident SurrealDB interfaces and assembly placement;
- add selection records/service plus all shared persisted selection/UI/cache DTO schemas and pure
  normalization contracts;
- add the collectible runtime root and non-owning session contract;
- plumb world/branch through vocabulary and expand guard scope;
- land contributor/readiness and producer-catalog contracts.

The collaborator extraction is what makes later packets file-isolated. Splitting `Service.cs` into
partials without separating constructor fields/lifetimes is not sufficient.

### Wave 2 — parallel packets from the same foundation commit

1. **Engine truth atomicity:** fail-closed CAS, append CAS retry, adversarial store tests.
2. **Execution/product identity:** retire `base`, percent-encoded addresses, disjoint execution keys.
3. **Truth migration:** successors, v2 payloads/context, legacy fallback readers.
4. **Persistence/cache identity:** Surreal repositories for the foundation's
   selection/UI/crust/filmstrip records and disjoint keys.
5. **Godot-free UI projection:** per-world registry snapshots and pure persisted-state
   normalization against the foundation DTO contracts.
6. **Producer completeness:** atmosphere wiring, science/default v2, no fabricated catch-all.
7. **Consumer gate pack:** tool-only contributor, real producer, second exact world.

Each packet receives exclusive file ownership and its own RED tests. Packets must not independently
edit `WorldPlugin`, `TimelinePlugin`, planet/tunnel binders, or shared constructors.

### Wave 3 — serial integration

- compose the runtime root/catalog in `WorldPlugin`;
- connect readiness to persisted exact resolution;
- add pickers and selection commits in `TimelinePlugin` / timeline face;
- wire the epoch through planet, tunnel, graph, scrub, and filmstrip paths;
- integrate the consumer gate pack and both runtime sessions;
- reconcile package versions and generated bundle manifests.

### Wave 4 — serial acceptance and deposit

- full engine and app suites in both dependency modes;
- two-process SurrealDB restart proof;
- exported-window selection proof for A, B, and A again;
- disjoint truth head/key, cache id, track, archive, active selection, globe, and tunnel assertions;
- `default/main` successor precedence and grandfathered fallback proof;
- world/timeline bundle reload with old ALC collected;
- hub/App evidence and plan-index updates.

### Wave 5 — separate test-maintenance pass

Consolidate duplicated fixtures and mixed-purpose/source-scanning tests only after the new behavior is
stable. Preserve all safety-critical gates named in the locked decisions. Test deletion requires an
explicit replacement mapping, not a lower target count.

## Acceptance gates

### Contract/unit gates

- exact selection round-trip and epoch A → B → A behavior;
- persisted selection rejects bare references, duplicate-field identity, and resolved-reference
  mismatches;
- no resident selection DTO contains `object`, `Type`, delegate, producer, registry, async reader, or
  bundle implementation fields;
- contributor seal runs only after BundleHost mutations return, blocks until all loaded required
  contributors finish, and fails on missing exact versions without nested bundle loading;
- producer resolution rejects a matching registration from a disabled/wrong-digest pack and
  releases all contributor/factory capabilities before bundle unload;
- one-active-truth-digest guard for each world/branch;
- first append atomically pins the stream truth digest; reopen/read/append reject a different
  digest even in another process;
- successor factories exactly match the two locked dotted-domain identities;
- closed legacy factory allowlist cannot grow accidentally;
- full successor hash verification and corruption failure;
- exact legacy-compatibility-binding empty-successor fallback; no fallback for other declaration
  versions, worlds, or branches;
- CAS loser/retry and atomic batch/head behavior;
- declaration-to-layer projection contains only enabled visible layers;
- prepare/commit keeps A current when B preparation fails and never exposes identity B with
  projection A;
- cache/document ids are disjoint for the two exact worlds;
- atmosphere resolves the real producers; unknown non-empty content fails instead of fabricating.

### Two-process restart gate

Process/server A:

1. start SurrealDB over a fresh RocksDB directory;
2. run a frozen pre-migration compatibility seeder that writes valid grandfathered rotation and
   generation chains through the legacy factories, verifies them, and leaves both default
   successors absent;
3. start the app/harness and wait for platform + consumer contributors to seal outside the
   BundleHost mutation gate;
4. exact-resolve the pinned compatibility-bound `default/main`, prove it reads legacy fallback,
   and exact-resolve the gate consumer world/main;
5. append consumer successor truth while intentionally leaving the default successors empty;
6. materialize and persist disjoint default/consumer caches and UI preferences;
7. persist the gate consumer as active selection;
8. stop the app and SurrealDB cleanly, recording PIDs and receipts.

Process/server B:

1. start a distinct SurrealDB process over the same RocksDB directory;
2. start a distinct app/harness process;
3. deterministically re-author declarations and exact-resolve the persisted consumer selection;
4. verify consumer successor truth and both worlds' caches/UI state independently;
5. switch to the exact compatibility-bound default, prove legacy fallback still works after the
   process restart while its successors are empty, then append normal full-state default successor
   events, reopen the session, and prove successor precedence plus durable truth-digest binding;
6. emit a non-no-op receipt proving both worlds and both continuity states recovered.

### Exported-window gate

- a gate-only export profile explicitly stages the tool consumer bundle/PCK and its gate manifest;
  the normal production manifest has an assertion proving that bundle is absent;
- the exported app displays separate world and branch pickers;
- default shows exactly its seven declaration-enabled tracks and real atmosphere previews;
- the consumer world shows only its enabled layers/pack content;
- B has no default-only archive, selection, filmstrip, graph, globe, or tunnel state;
- switching back to A restores only A state;
- an intentionally delayed first-A completion cannot publish after A → B → A;
- the running app reloads deliberately changed world and timeline PCKs from fresh extraction
  directories using `CacheMode.ReplaceDeep`, records the staged-byte/build digest, and logs each old
  ALC collected;
- the changed PCK exposes a gate-only visible marker and changed exact consumer-pack content digest;
  the post-reload window must display that marker/content, proving newly mounted resources—not a
  cached pre-reload scene—are active;
- screenshots/logs capture both picker labels, both track sets, exact declaration and staged-build
  digests, the visible post-reload marker, and the ALC result.

## Adversarial review reconciliation

A fresh-context internal issues-only review of the first written artifact found eleven material
gaps. They are resolved here as follows:

- successor streams now durably bind one truth digest, and legacy fallback is limited by one exact
  default-v2 compatibility binding;
- the persisted selection stores only the pinned reference/branch and runtime identity is derived
  through a validating exact-resolution factory;
- the consumer on-ramp is a shared T1 capability with pack/lawset origin-digest filtering and an
  explicit cross-ALC release/unload dependency;
- contributor sealing runs after BundleHost mutations, avoiding an initialization-gate cycle;
- UI switching prepares a complete lease, durably records the intent, and swaps it in one
  main-thread commit rather than publishing identity before projection;
- persisted DTO contracts move to the serial foundation so repository and normalization packets
  are genuinely parallel;
- the restart proof now seeds real grandfathered shapes while default successors remain empty
  across the process boundary; and
- the gate-only pack has an explicit export profile, while reload must activate deliberately
  changed PCK content as well as collect the old ALC.

The authorized OpenCode/GLM-5.2 cold audit read the artifact and all nominated contract/source
files. Its provider session reached its output limit before emitting the requested final findings
message. The persisted audit reasoning completed one actionable low-severity finding: the original
text incorrectly said the root “disposes” non-disposable `KvTruthEventStore`; the design now says
the root stops the writer and drops the store/backend-interface references. The audit also examined
and rejected as non-findings the deliberate new conditional-KV registration, the explicit
fail-soft-to-fail-closed factory change, and the successor Domain/M names. Its assembly-policy
warning is made explicit above by requiring removal from the collectible exclusion list before
resident staging. This is not recorded as a clean external “no findings” verdict, and no
unauthorized second external invocation was made.

## Source-driven constraints

- .NET collectible unloading is cooperative: any surviving strong reference, running thread,
  task, or stack frame can delay collection. The design therefore keeps producer/registry/Core
  instances collectible and makes all resident subscriptions explicitly disposable:
  https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability
- SurrealDB supplies transaction isolation and persistent single-node RocksDB operation through
  the server CLI. The implementation must use the version-pinned SDK and generated UnifyStorage
  adapters already present in the repository rather than reconstructing an SDK API from memory:
  https://surrealdb.com/docs/architecture
- canonical truth payloads remain MessagePack; JSON is configuration/export only, per
  `plate-projects/unify-storage/.agent/rules/04-persistence.md`.
