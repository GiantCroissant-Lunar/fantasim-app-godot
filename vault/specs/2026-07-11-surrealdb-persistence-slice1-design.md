# SurrealDB Persistence — Slice 1 (Crust-Product + Filmstrip Caches)

**STATUS: DRAFT for user adjustment round (2026-07-11).**

Goal restated: persist the crust-product cache and the filmstrip preview cache across app
sessions via the house `unify-storage` layer, so scrubbing is warm on a fresh boot. Fix the
known crust-cache-key defect (missing `Seed`) in the same slice. Extend the existing SurrealDB
wiring (already present for the world truth store) rather than inventing a parallel mechanism.

**Headline finding that reshapes the slice (read §2 before anything else):** the .NET SurrealDB
client stack available in this workspace today has **no on-disk embedded provider**. Only
`mem://` (non-persistent, in-process) and remote `ws://`/`http://` (an external server this repo
does not run) exist. The existing truth-store "surrealdb backend" is *not durable across restarts
either* — it is in-memory-only exactly like the default. This is not a slice-1 implementation
detail; it changes what "SurrealDB persistence" can mean for slice 1 and is DECISION POINT 1.

---

## 1. Current-state map

### 1.1 World truth store — what's actually wired

`project/plugins/App.World/Services/WorldTruthStoreFactory.cs` is a single file, and it is
**entirely gated behind `#if USE_PROJECT_REFERENCES`** (line 1 open, line 169 `#endif`). Per
`project/plugins/App.World/App.World.csproj:21-30`, this define is only set when
`UseProjectReferences=true` (the dev/project-reference path against `fantasim-world` `main`); the
published-package release path compiles a no-op runtime instead (`App.World.csproj:24-26`
comment). So the truth-store backend selection machinery does not exist in the package-build
configuration at all today — it is dev-path-only code.

Backend selection (`WorldTruthStoreFactory.cs:13-54`):
- `WorldTruthStoreBackend` enum: `InMemory | SurrealDb` (:13-17).
- `WorldTruthStoreOptions.FromConfig` reads config keys `world:truthStore:backend` and
  `world:truthStore:connectionString` (:23-24, :26-39). SurrealDb backend requires a non-empty
  connection string or throws (:32-36).
- Shipped config, `project/hosts/complete-app/config/app.json:16-19`:
  ```json
  "truthStore": { "backend": "inmemory", "connectionString": null }
  ```
  The SurrealDb path is **not the active default** — it exists but is unused in the shipped app.

Store construction (`WorldTruthStoreFactory.cs:86-168`):
- `InMemory` → `new InMemoryTruthEventStore()` (:94), pure C#, process-lifetime, no disk.
- `SurrealDb` → `CreateSurrealDb(connectionString)` (:100-137): parses the ADO-style connection
  string for `Namespace`/`Database`/`Endpoint` (:105-108, :139-150), registers
  `services.AddSurrealDbStorage(connectionString)`, and — **only when the endpoint starts with
  `mem://`** — chains `.AddInMemoryProvider()` (:111-113). Any other endpoint scheme is left to
  `SurrealDb.Net`'s own connector (which per §2 means a remote `ws://`/`http://` server — there is
  no on-disk embedded option to select here). The store returned is
  `new KvTruthEventStore(new SurrealDbKeyValueStore(session))` (:127).

Actor-writer requirement (git history, hardened June/July):
- `1b2d2a9` "feat(world): support configurable truth store backend" — added the factory.
- `b6a866c` "refactor(world): route truth commits through actor writer" — added
  `ITruthEventWriter`/`ActorTruthEventWriter`/`DirectTruthEventWriter`
  (`project/plugins/App.World/Services/ITruthEventWriter.cs`).
- `6f9b60e` "fix(world): require actor writer for surrealdb truth store" — `Service.cs:74-78` now
  throws if `Backend == SurrealDb && actorSystem is null`; SurrealDb writes are forced through
  `ActorTruthEventWriter.Start(...)` (`Service.cs:84-88`) instead of `DirectTruthEventWriter`
  (`Service.cs:89`). Reason (confirmed by reading the generated store, §1.4): the embedded
  SurrealDB session issues **synchronous blocking calls** (`.GetAwaiter().GetResult()` throughout
  `SurrealDbEmitter.cs`, e.g. :941, :956, :980, :992) and is not safe for uncoordinated concurrent
  access — the actor mailbox serializes writes onto one thread. LiteDB does not need this (§1.4).
- `fcd6cdf` "test(world): cover surrealdb service writer path" — test coverage for the above.
- `Service.cs:41-100` (the `Service` constructor, itself `#if USE_PROJECT_REFERENCES`-gated at
  :48 and :64) is where truth-store construction happens per-`Service`-instance, i.e. per world
  session.

Encoding: `KvTruthEventStore` is **not part of this repo** — it lives in the sibling
`fantasim-world` engine repo,
`yokan-projects/fantasim-world/project/plugins/World.TruthStream.Core/KvTruthEventStore.cs`. It
builds an append-only, hash-chained event log on top of the generic `IKeyValueStore` byte
substrate (key layout `S:{streamKey}:E:{seq:X16}` / `S:{streamKey}:Head`, :238-245), and
serializes with **MessagePack** via `MessagePackTruthEventSerializer` (:249-255) — matching
unify-storage's own doctrine ("Canonical event encoding: MessagePack (DB-first; JSON is
export/import only)," `plate-projects/unify-storage/.agent/rules/04-persistence.md`).

**The concrete `SurrealDbKeyValueStore` type is source-generated, not hand-written.** It is
emitted by `UnifyStorage.Generators` (`plate-projects/unify-storage/dotnet/src/
UnifyStorage.Generators/Backends/SurrealDbEmitter.cs:902-981`, class `SurrealDbKeyValueStore :
IConditionalKeyValueStore`) into the *consuming project's own compiled output*, triggered by
`App.World.csproj:8` (`<UnifyStorageBackends>SurrealDb</UnifyStorageBackends>`) +
`App.World.csproj:87` (`<PackageReference Include="UnifyStorage.Generators" PrivateAssets="all"
/>`). `WorldTruthStoreFactory.cs:3` imports it from `FantaSim.App.World.Storage.Generated.
SurrealDb` — i.e. **the generated wrapper class lives inside the `FantaSim.App.World` assembly
itself**, which is the *collectible* world-bundle plugin assembly
(`project/hosts/complete-app/config/collectible-bundles.json`, `bundleId: "world"`,
`pluginAssembly: "FantaSim.App.World.dll"`). The generator emits the SAME KV store on a single
SurrealDB table `kv_pairs`, keyed by `StringRecordId("kv_pairs:{hexKey}")`, value Base64-encoded
(`SurrealDbEmitter.cs:897-901, 936-937, 970-976`).

The generator also emits a **`SurrealDbDocumentStore : IDocumentStore`**
(`SurrealDbEmitter.cs:16-20, 55-70`) in the same pass — `EmitRoleImplementations` emits document
store, graph store, key-value store, time-series store, change feed, and geo-feature store
together (:13-51). This means the document-store surface is *already mechanically available* in
`App.World` today (same `UnifyStorageBackends=SurrealDb` switch), just unused — nobody constructs
a `SurrealDbDocumentStore` instance yet. This is directly relevant to §3: slice 1 does not need
new source-gen work, only new callers and a different residency.

Packaging today (confirmed against two independent sources):
- `collectible-bundles.json`, `"world"` bundle `assemblyNames` lists `SurrealDb.Embedded.
  InMemory`, `SurrealDb.Net`, `UnifyStorage.Runtime.SurrealDb`, plus their transitive closure
  (`Dahomey.Cbor`, `Microsoft.Extensions.Http`, `Microsoft.IO.RecyclableMemoryStream`,
  `Microsoft.Spatial`, `Semver`, `System.Collections.Immutable`, `System.IO.Pipelines`,
  `System.Linq.AsyncEnumerable`, `System.Reactive`, `SystemTextJsonPatch`, `Websocket.Client`).
- `project/hosts/complete-app/config/shared-assembly-policy.json` `exactMatches` (:3-46) and
  `common.exactMatches` (:67-94) both list `UnifyStorage.Abstractions`, `UnifyStorage.Runtime.
  LiteDb`, `LiteDB` — but **no SurrealDb-named assembly appears in either list**.

Net effect: today, both the SurrealDB client packages *and* the generated wrapper types compiled
against them load into the **world bundle's own collectible ALC**. This is precisely the
dual-copy/ALC-pin risk class the user's standing directive addresses (§3).

### 1.2 Crust-product cache — the Seed defect, confirmed and scoped

`Service.cs:56-58`:
```csharp
private readonly object _crustProductCacheGate = new();
private readonly Dictionary<CrustProductCacheKey, CrustTickProducts> _crustProductCache = new();
```
In-memory only, `Dictionary`, process-lifetime, guarded by a plain `lock`.

Key type, `Service.cs:1089`:
```csharp
private readonly record struct CrustProductCacheKey(int Frequency, long SnapshotTick);
```

Build/cache path, `GetOrBuildCrustTickProducts` (`Service.cs:869-905`):
```csharp
var key = new CrustProductCacheKey(renderOptions.TessellationFrequency, snapshotTick);  // :885
```
**`renderOptions.Seed` is not part of the key** — confirmed exactly as the user described. Two
worlds with different seeds but the same tessellation frequency and the same selected snapshot
tick collide on the same cache entry.

The defect is worse under scrutiny than "just Seed": `WorldGenerationRenderOptions` (`project/
plugins/App.World/GenerationGraph/WorldGenerationRenderOptions.cs:16`) is `record
WorldGenerationRenderOptions(int Seed, int TessellationFrequency)` with an init-only
`SpinRateRadiansPerMegaAnnum` (:42, default `OnsetRoster.DefaultAngularDriftPerMegaAnnum`). The
**sibling** cache in the same class, the globe-reconstructor cache, was extended on 2026-07-11 to
key on all three:
```csharp
// Service.cs:1094-1104
private (int Seed, int Frequency, double SpinRateRadiansPerMegaAnnum) _globeReconstructorKey;
...
var key = (renderOptions.Seed, renderOptions.TessellationFrequency, renderOptions.SpinRateRadiansPerMegaAnnum);
```
So `_globeReconstructorKey` already includes Seed + SpinRate; `CrustProductCacheKey` includes
neither Seed nor SpinRate — an inconsistency between two caches on the same options object, not
just a missing field.

A third dimension is missing from **both**: `GraphRevision`. The world-generation-graph family
carries its own revision (`project/contracts/App.World/GenerationGraph/
WorldGenerationGraph.cs:404-436`, family key format `{LifecycleKind}:{RegimeId}:{GraphId}:
G{GraphRevision}:S{ScheduleRevision}:{Variant}:{Branch}`), and the crust *generation trigger*
(deciding whether to regenerate at all) already keys on it:
`CrustGenerationTriggerKey(int GraphRevision, long WindowIndex, long SnapshotTick)`
(`project/plugins/App.World/GenerationGraph/CrustGenerationTriggerPolicy.cs:13`). The in-memory
`CrustProductCacheKey` omits `GraphRevision` too — latent today because a graph edit mid-process
is rare and the in-memory cache resets on restart anyway. **A cross-session persisted cache does
not get that free reset**, so `GraphRevision` becomes load-bearing (§5).

`SnapshotTick` is a *window* index: `CrustSnapshotTickSeries.DefaultSpacingTicks = 5_000_000L`
canonical ticks (`WorldGenerationGraph.cs:178`, comment: "50 ka = 50 Ma at 100k ticks/Ma");
`CrustSnapshotTickSeries.ForRegime` (:186-208) buckets a playhead tick down to the nearest
5M-tick window start. This is the "window" vocabulary the schema should use (§4.1).

Payload shape, `Service.cs:1083`:
```csharp
private sealed record CrustTickProducts(
    long SnapshotTick, CrustEvolutionResult Result, WorldGlobeSnapshot GlobeAtSnapshot, /* arcs */ );
```
`CrustEvolutionResult` and `WorldGlobeSnapshot` are engine/app DTOs — not yet proven
MessagePack- or JSON-serializable; that is new surface slice 1 must add deliberately, not assume.

### 1.3 Filmstrip cache — two objects, one ledger

`FilmstripCacheLedger` (`project/plugins/App.Timeline.Seam/FilmstripCacheLedger.cs`, 55 lines,
whole file read) is **pure bookkeeping** — a `LinkedList<string>` + `HashSet<string>` FIFO over
string keys, cap-based eviction (`Record` returns the evicted key, :30-46). It does **not** hold
textures. Docstring (:6-8): "Split from TimelineFace 2026-07-11
(vault/plans/2026-07-11-timelineface-split-plan.md)." No test coverage exists for it per the
codegraph blast-radius scan (flagged ⚠️ during exploration) beyond
`project/tests/App.Timeline.Tests/FilmstripCacheLedgerTests.cs`.

The actual texture cache lives in `FilmstripPreviewController`
(`project/plugins/App.Timeline.Seam/FilmstripPreviewController.cs`):
```csharp
private readonly Dictionary<TimelineFilmstripCacheKey, ImageTexture> _filmstripTextureCache = new();  // :34
private const int MaxFilmstripTextureCacheEntries = 512;  // :47
```
Key shape (`project/contracts/App.Timeline/TimelineFilmstrip.cs:18-24`):
```csharp
public readonly record struct TimelineFilmstripCacheKey(
    string SphereId, string LayerId, long SnapshotTick, string ViewRung, int Width, int Height);
```
Same class of gap as the crust key: **no Seed, no SpinRate, no GraphRevision.** The task's ask
scoped the "known defect to fix" to the crust key; this finding is surfaced for the invalidation
design (§5) and DECISION POINT 4, not silently folded into the fix.

Build/cache flow (`FilmstripPreviewController.cs:247-281`):
```csharp
var key = new TimelineFilmstripCacheKey(...);                                    // :247
var isNewKey = !_filmstripTextureCache.ContainsKey(key);
if (!_filmstripTextureCache.TryGetValue(key, out var texture) ...)
{
    var image = Image.CreateFromData(map.Width, map.Height, false, Image.Format.Rgba8, map.Rgba32); // :258
    texture = ImageTexture.CreateFromImage(image);                                // :259
    _filmstripTextureCache[key] = texture;
}
...
var evictedKey = _cacheLedger.Record(ledgerKey);   // :266, ledger drives eviction of the dictionary
```
The **source data** is `LayerFilmstripPreviewMap` (`project/contracts/App.World/
LayerFilmstripPreview.cs:15-25`), which already carries a plain `byte[] Rgba32` at `Width x
Height` — today `ThumbnailWidth=96, ThumbnailHeight=48` (`TimelineFilmstrip.cs:29-30`), i.e.
96×48×4 = 18,432 bytes uncompressed per frame. **This byte array — not the `ImageTexture`, which
is a Godot GPU-resident object with no serialization contract — is what a persisted cache stores
and later feeds back into `Image.CreateFromData`/`ImageTexture.CreateFromImage` to rebuild the
texture on load.** At the 512-entry cap, raw RGBA32 is ≈9.4 MB uncompressed; real fan-out (per
sphere × per layer × per view-rung × width/height variants) could multiply that — a budget
decision, not a rounding error (DECISION POINT 5).

### 1.4 unify-storage abstractions (plate-projects/unify-storage) — read from source

Three relevant interfaces in `plate-projects/unify-storage/dotnet/src/UnifyStorage.Abstractions/`:

- **`IKeyValueStore`** (`IKeyValueStore.cs`, whole file read) — byte-span `TryGet`/`Put`/
  `Delete`/`CreateIterator`/`CreateWriteBatch`/`Write`, plus `IConditionalKeyValueStore` for
  compare-and-write. This is what the truth store uses as its byte substrate (§1.1). Not a fit
  for the two caches — they are keyed blobs/records, not an append-only byte-range log.
- **`IEventStore`** (`IEventStore.cs`, whole file read) — a *different*, typed, generic
  append-only interface (`AppendAsync<TEvent>`/`ReadAsync<TEvent>`/`GetStreamVersionAsync`). **The
  truth store does not use this interface at all** — `KvTruthEventStore` implements the
  domain-specific `ITruthEventStore` (from `FantaSim.World.TruthStream`) directly on top of
  `IKeyValueStore`, bypassing `IEventStore` entirely. Worth noting so slice 1 doesn't assume
  `IEventStore` is "the" unify-storage event abstraction in use here — it isn't, today.
- **`IDocumentStore`** (`IDocumentStore.cs`, whole file read) — provider-agnostic
  collection/id CRUD + expression queries + transactions (`GetAsync<T>`/`QueryAsync<T>`/
  `UpsertAsync<T>`/`DeleteAsync`/`CountAsync`/`ExistsAsync`/`BulkInsertAsync`/
  `BeginTransactionAsync`). **This is the interface already proven in this app**:
  `project/plugins/App.Activity/Services/Service.cs:11-12` imports `UnifyStorage.Abstractions` +
  `UnifyStorage.Runtime.LiteDb`, and `CreateDocumentStore`/`ResolveStorePath`
  (`Service.cs:181-229`) construct a `LiteDbDocumentStore` for the activity ledger — the *only*
  other place in this repo that persists app-owned state via unify-storage today. Its disk-path
  convention:
  ```csharp
  // Service.cs:206-229
  "user://activity-ledger.litedb"  // default
  // user:// resolves to Environment.SpecialFolder.LocalApplicationData/FantaSim/complete-app/<relative>
  // (falls back to AppContext.BaseDirectory if LocalApplicationData is empty)
  // exe-adjacent relative paths resolve against AppContext.BaseDirectory
  ```
  This is the concrete precedent for "how does persistence location get decided today" — **the
  truth store has no such decision to make, because it only ever runs `mem://` (§2)**. `IDocumentStore`
  is the abstraction slice 1 should target for both new caches — it matches the "keyed blob" shape
  of both the crust products and the filmstrip entries.

`UnifyStorage.Runtime.SurrealDb` (`plate-projects/unify-storage/dotnet/src/
UnifyStorage.Runtime.SurrealDb/`) **already exists and is already published** —
`UnifyStorage.Runtime.SurrealDb.csproj:8-9` (`PackageId`, `Version 0.1.0` in-repo, pinned to
`0.1.1` by this app's `Directory.Packages.props:45`). **No new plate-projects package needs to be
created for slice 1** — this resolves what would otherwise be the biggest decision point. The
project itself is thin: `ServiceCollectionExtensions.cs` (whole file read) only wraps
`services.AddSurreal(...)` / `AddSurrealDbStorage(...)`; all the real store implementations
(`SurrealDbDocumentStore`, `SurrealDbKeyValueStore`, etc.) come from the source generator
(§1.1), not from hand-written classes in this project. `LiteDbDocumentStore`
(`plate-projects/unify-storage/dotnet/src/UnifyStorage.Runtime.LiteDb/
LiteDbDocumentStore.cs`, whole file read) is, by contrast, hand-written and wraps `ILiteDatabase`
directly — no generator, no `UnifyStorageBackends` switch needed on the consuming project. This
explains why LiteDb needs no actor-writer funnel (§1.1): `LiteDatabase` is a real embedded-file
database engine with its own internal concurrency handling; `App.Activity/Services/Service.cs`
gets away with a plain `_lock` + one background flush `Task` (:33-34, :75).

Per `plate-projects/unify-storage/.agent/rules/04-persistence.md` (project rule, read via
CLAUDE.md import): *"Persistence backend: SurrealDB via SurrealDb.Net and
UnifyStorage.Runtime.SurrealDb. RocksDB was the previous experimental backend... Canonical event
encoding: MessagePack."* This is the house's stated direction for `unify-storage` generally — it
does not, on its own, resolve the on-disk gap in §2 (that gap is in the SurrealDB .NET SDK's
distribution, not in unify-storage's design intent).

### 1.5 ALC / resident-layer constraints (vault/architecture, vault/specs)

`vault/architecture/cross-alc-rules.md` (whole file read): the resident (shared/Default) ALC vs.
collectible per-bundle ALCs; `SharedAssemblyPolicy` routes named assemblies to the resident ALC so
type identity survives the boundary (§1-2 of that doc). Today `UnifyStorage.Abstractions` +
`UnifyStorage.Runtime.LiteDb` + `LiteDB` are resident-shared (`shared-assembly-policy.json`
`exactMatches`, confirmed above); no SurrealDb-named assembly is. §4 of the doc: contract
interfaces MAY cross; "the bundle implementation assemblies themselves" MUST NOT.

`vault/specs/2026-07-08-common-resident-layer-bundle.md` (whole file read, status DECIDED
2026-07-08, "implementation NOT started"): defines `common.pck` — resident-layer assemblies
extracted once into the Default ALC at boot, never unloaded, never collectible. Its "Goes to
common" packing list (line 44-50) explicitly includes **"UnifyStorage.Abstractions + Runtime.
LiteDb"** — SurrealDb runtime is not on that list. `shared-assembly-policy.json`'s `common.
exactMatches` (:67-94, dated 2026-07-11) mirrors this exactly. Two gating experiments already ran
(§ "Adversarial review outcome"): **E1 — Godot-facing script assemblies FAIL in the resident
loader (SIGSEGV); pure-support DLLs only.** SurrealDb.Net / UnifyStorage.Runtime.SurrealDb carry
no Godot dependency, so they fit the "pure support DLL" class E1 validated, same as LiteDB
already does. **E2 — assembly demand is lazy (first-executed-use), not type-load time** — a
`Resolving` hook installed early in `Host._Ready` is sufficient; no signature-free micro-bootstrap
needed for this addition.

Today's `shared-assembly-policy.json` (polarity-flip commit `9bda14f`, 2026-07-11, "feat(bundles):
flip SharedAssemblyPolicy polarity — shared is contracts + floor, collectible is the default") is
the live mechanism: `exactMatches` is now an **enumerated allowlist**, not a prefix — adding a
resident SurrealDb closure is an explicit, auditable edit to that one JSON file plus the mirrored
`common.exactMatches` block, exactly the shape of the LiteDB entries already there. The commit
message itself documents the gate this repo already uses for this class of change ("staging diff
= +world/DynamicData.dll only, `--check-dual` clean, fresh boot 0 errors, all 5 bundles reload
with old ALC collected, 0 pinned") — §6.3 reuses this gate.

---

## 2. The load-bearing gap: no on-disk embedded SurrealDB provider

Read directly from the installed NuGet packages (`~/.nuget/packages`, versions pinned by this
app's `Directory.Packages.props:45-47`: `UnifyStorage.Runtime.SurrealDb 0.1.1`, `SurrealDb.Net
0.10.2`, `SurrealDb.Embedded.InMemory 0.10.2`):

- `SurrealDb.Net`'s own README (`~/.nuget/packages/surrealdb.net/0.10.2/README.md`): *"This
  library supports connecting to SurrealDB over the remote HTTP and WebSocket connection
  protocols `http`, `https`, `ws`, and `wss`... require SurrealDB to be installed and running."*
  I.e. `SurrealDb.Net` alone talks to an **external server process** this repo does not run,
  package, or supervise anywhere (confirmed by grep across the app and `unify-storage/docs` for
  `surreal start`/server-process wiring — zero matches).
- `SurrealDb.Embedded.InMemory`'s README (`~/.nuget/packages/surrealdb.embedded.inmemory/
  0.10.2/README.md`): *"In-memory provider... `services.AddSurreal("Endpoint=mem://") .
  AddInMemoryProvider()"`.* The package's `.nuspec` shows a **pure managed dependency
  closure** (Dahomey.Cbor, Microsoft.Extensions.Http, etc. — no native library dependency), and
  its `runtimes/{rid}/` folders contain **no files** (checked: `osx-x64`, `osx-arm64`, `linux-x64`,
  `win-x64`, etc. are all empty scaffolding). This confirms it is not a wrapped on-disk storage
  engine with pluggable backends — it is exactly what its name says: memory-only.
- No other `SurrealDb.Embedded.*` package (e.g. a RocksDB- or file-backed embedded provider) is
  referenced anywhere in `Directory.Packages.props`, cached in `~/.nuget/packages`, or mentioned
  in `unify-storage`'s docs/RFCs.

**LEAD VERIFICATION ADDENDUM (2026-07-11, same day):** the gap above is "not installed", NOT
"does not exist". nuget.org (a configured feed in this repo's `nuget.config`) carries
**`SurrealDb.Embedded.RocksDb` 0.10.2** and `SurrealDb.Embedded.SurrealKv` 0.10.2 — the same
version family as the pinned `SurrealDb.Net`/`SurrealDb.Embedded.InMemory` 0.10.2. On-disk
embedded SurrealDB is therefore one `PackageReference` away, no external server process needed.
Caveat that aligns with (rather than fights) the resident-client directive: an embedded storage
engine ships a NATIVE library per RID, and native libraries never unload — so the RocksDb
provider must load on the RESIDENT side (common layer / floor), never inside a collectible
bundle, and the Godot export packaging must carry the dylib the way the gdext bridge already is.
Open verification items for this option: does `UnifyStorage.Runtime.SurrealDb` 0.1.1's endpoint
plumbing accept a `rocksdb://<path>` connection string, and does the export pipeline pick up the
package's `runtimes/osx-arm64` native asset. See DECISION POINT 1 option (D).

Consequence: **`WorldTruthStoreFactory.CreateSurrealDb` today can only ever produce a
non-persistent store**, whether the endpoint is `mem://` (in-process, gone at process exit) or
points at a remote server (which doesn't exist in this deployment). The truth store's "SurrealDB
backend" toggle is not actually a durability toggle today — it changes engine, not persistence.
This is worth surfacing to the user directly: the premise "SurrealDB is already wired for TIMED
data" is true for *code path*, not for *cross-session durability* — nothing in this repo
persists to disk via SurrealDB today. LiteDB is the only unify-storage backend in this repo with
a proven on-disk story (`App.Activity`, §1.4).

Three ways slice 1 can still honor "SurrealDB via unify-storage" while getting a warm second
boot, laid out as DECISION POINT 1 in §7 — this document does not choose one unilaterally.

---

## 3. Design

### 3.1 Resident storage contract shape

Per the user's standing directive: the DB client (and any generated wrapper type that holds a
live client handle) must live in the resident/common layer, never inside a collectible bundle
assembly, and bundles must reach it only through a shared-resident **interface**, never the
concrete class.

`UnifyStorage.Abstractions.IDocumentStore` is already resident-shared today (`shared-assembly-
policy.json` `exactMatches` line 31, `common.exactMatches` line 79) — it is a T1-shaped contract
interface with zero engine/Godot types in its signatures (`IDocumentStore.cs`, §1.4). **No new
contracts project is required to expose it** — any bundle that already resolves `UnifyStorage.
Abstractions` (App.World already does, via its `UnifyStorage.Abstractions` `PackageReference`,
`App.World.csproj:86`) can hold an `IDocumentStore` reference safely, *provided the concrete
instance behind it was constructed in resident code*.

What's missing is the **construction site and lifetime owner**. Today `SurrealDbDocumentStore`
would be generated inside whichever project sets `UnifyStorageBackends=SurrealDb` (currently only
`App.World`, a collectible bundle assembly — §1.1). Slice 1 needs that generation (and the live
`ISurrealDbSession`/`ILiteDatabase` construction) to happen in a **resident** project instead, so
neither the connection nor the generated wrapper type is bundle-private.

Two placement options, both compatible with the existing floor:

1. **Extend `App.Common`** (`project/plugins/App.Common/App.Common.csproj` — already permanent-
   resident per `shared-assembly-policy.json` `exactMatches` line 4, already hosts
   `Bootstrap.cs`/`AppComposition.cs`, the natural home for "infrastructure every resident and
   collectible consumer needs"). Add `UnifyStorageBackends` (LiteDb and/or SurrealDb),
   `UnifyStorage.Generators`, and the relevant runtime `PackageReference`s directly to
   `App.Common.csproj`; expose a small new resident service (e.g. `IWorldCacheStore` or reuse
   `IDocumentStore` directly) via the registry the same way other resident services are exposed.
2. **A new small resident project** (e.g. `App.Storage`, T3/T4-less "floor" project like
   `App.Common`) dedicated to this concern, referenced by `App.Common` or added alongside it to
   the resident exactMatches list. Cleaner separation, but it is a **new project** — the house
   rule ("ask before creating structure") applies even though it's inside this repo, not
   plate-projects, because it changes the resident-floor surface area. **Flagged as DECISION
   POINT 2.**

This document does not pick between them; §6 assumes option 1 (extend `App.Common`) as the
lower-ceremony default and calls out where option 2 would differ.

### 3.2 Where the client lives

The live `ISurrealDbSession` (or `ILiteDatabase`) is constructed once, in the resident project's
composition (mirroring `WorldTruthStoreFactory.CreateSurrealDb`'s connect/`Use` sequence,
`WorldTruthStoreFactory.cs:118-125`, but resident instead of per-`Service`-instance and
per-bundle). It is held for process lifetime, disposed only at app shutdown — the same lifetime
class as the `ActorSystem` (`cross-alc-rules.md` §3, "ActorSystem is resident"). Collectible
bundles (App.World for crust products, App.Timeline.Seam for filmstrips) receive only an
`IDocumentStore` handle resolved through `IRegistry`/DI — the same pattern `App.Activity` already
uses locally (§1.4), except the store is now constructed by resident code and handed down instead
of constructed inside the bundle.

### 3.3 Cross-ALC consumption without pinning

Applying the seven known pin classes (`.agent/memory` — MEMORY.md
`fantasim-alc-shared-type-identity`) to this addition:

1. **Type/Assembly-keyed static cache in a shared assembly** — `IDocumentStore` itself carries no
   static caches; the resident `SurrealDbDocumentStore`/`LiteDbDocumentStore` instance is a
   per-process singleton owned by resident code, not cached by bundle-keyed identity. No new risk
   introduced if construction happens once, resident-side, as in §3.2.
2. **Fossil bundle DLL's dead static** — not applicable; this addition doesn't touch the stager's
   fossil-staging path.
3. **`JsonSerializer.Serialize(new {...})` anonymous types pinning via resident STJ cache** —
   directly relevant: whatever payload type the crust/filmstrip caches serialize through
   `IDocumentStore.UpsertAsync<T>`/`GetAsync<T>` must be a **named, resident-or-contract type**
   (never an anonymous type, never a bundle-private closure type captured by a resident
   delegate) — see §3.4 on serialization choice.
4. **STJ pooling `CachingContext`s across value-equal options, pinned by a bundle-local static
   `JsonSerializerOptions`** — avoided by *not* using `System.Text.Json` for the payload at all
   if the chosen encoding is MessagePack (§3.4); if JSON is used instead, the `JsonSerializerOptions`
   instance must be constructed and owned resident-side, never as a `static readonly` field inside
   `App.World` or `App.Timeline.Seam`.
5. **Host rebind race re-capturing the outgoing binder during multi-pck installs** — the resident
   `IDocumentStore` instance is not part of any bundle's install/reload cycle, so this class does
   not apply directly; but the crust/filmstrip *cache-population* code paths inside the bundles
   must not hold a stale `IDocumentStore` reference captured before a hot-reload — resolve it via
   `IRegistry` at point of use, not cached in a bundle-static field.
6. **TimelinePlugin recompose racing its own shutdown** — the filmstrip persistence writes
   (§4.2) must be flushed/cancelled on `App.Timeline.Seam` unload exactly like the existing
   `FilmstripPreviewController.DisposeCache`/`Clear` path (`FilmstripPreviewController.cs:316-325`)
   already tears down the in-memory dictionary — extend that teardown to await any in-flight
   persistence write, not fire-and-forget past unload.
7. **Resident Task/CallDeferred closures + in-flight render stacks holding bundle delegates past
   the probe** — any resident background flush (mirroring `App.Activity`'s `FlushWorkerAsync`,
   `Service.cs:57-75`) must not close over bundle-supplied delegates or bundle DTOs; it should
   only see the resident `IDocumentStore` and resident-or-contract payload types.

R1-R7 of `cross-alc-rules.md` §5 apply unchanged; nothing about this addition changes bundle
unload mechanics, since the resident store is never part of a bundle's own ALC.

### 3.4 Serialization choice

The existing truth store's answer is unambiguous and house-documented: **MessagePack**
(`KvTruthEventStore` via `MessagePackTruthEventSerializer`, §1.1; `unify-storage`'s own
`04-persistence.md` rule: "Canonical event encoding: MessagePack (DB-first; JSON is export/import
only)"). `MessagePack` + `MessagePack.Annotations` + `UnifySerialization.MessagePack.Runtime` are
**already resident-shared** (`shared-assembly-policy.json` `exactMatches` :39-41 and `common.
exactMatches` :73-75) — this is the lowest-friction choice: no new resident-floor addition needed
for the encoding itself, only for the SurrealDb/LiteDb runtime pieces (§3.1-3.2).

Recommendation: MessagePack-encode the payload records (`CrustProductCacheRecord`,
`FilmstripCacheRecord` — new, resident-or-contract types per pin-class 3/4 above) and store the
resulting bytes via `IDocumentStore.UpsertAsync<T>`/`GetAsync<T>` where `T` wraps the byte
payload plus the key fields needed for querying (collection = one per cache; id = the composite
key string, §4). This mirrors `App.Activity`'s pattern of a `DocumentPayload` wrapper
(`Service.cs` `DocumentCollection`/`DocumentId` constants, :27-28) but swaps its
`System.Text.Json` encoding for MessagePack, consistent with the truth-store precedent and
avoiding pin class 3/4 outright.

---

## 4. Schema sketch

### 4.1 Crust products

Persisted key — a superset of every dimension in scope, not just today's in-memory key (§1.2
established the in-memory key is itself incomplete; the on-disk key cannot inherit that gap,
because it survives process restarts where the in-memory key's implicit reset no longer helps):

```
CrustProductCacheRecord key = {
    Seed:                        int      // THE FIX — WorldGenerationRenderOptions.Seed
    TessellationFrequency:       int      // existing key field
    SpinRateRadiansPerMegaAnnum: double   // present in the sibling reconstructor-cache key; absent
                                           // here today — needed once persisted (§1.2)
    GraphRevision:               int      // world-generation-graph family revision (§1.2); absent
                                           // from both in-memory keys today — needed once persisted
    SnapshotTick:                long     // existing key field — a 5,000,000-canonical-tick window
                                           // index (CrustSnapshotTickSeries.DefaultSpacingTicks)
    SchemaVersion:                int     // invalidation stamp, §5
}
```
Collection: `"crustProducts"`. Document id: a deterministic string composition of the key fields
(e.g. `"{Seed}:{Frequency}:{SpinRate:R}:{GraphRevision}:{SnapshotTick}:{SchemaVersion}"`),
mirroring the family-key string-composition pattern already used elsewhere
(`WorldGenerationGraph.cs:436`).

Payload: the `CrustTickProducts` fields (`CrustEvolutionResult`, `WorldGlobeSnapshot`, boundary
arcs) MessagePack-encoded. **Open item, not yet verified**: whether `CrustEvolutionResult` /
`WorldGlobeSnapshot` (both defined in the `fantasim-world`/App.World composition layer) already
carry `[MessagePackObject]`/source-gen resolver support, or whether slice 1 must add serialization
surface for them. Flagged as a task-list item (§6.2), not assumed.

### 4.2 Filmstrip entries

```
FilmstripCacheRecord key = {
    SphereId:      string   // existing key field (TimelineFilmstripCacheKey)
    LayerId:       string   // existing key field
    SnapshotTick:  long     // existing key field
    ViewRung:      string   // existing key field
    Width:         int      // existing key field
    Height:        int      // existing key field
    Seed:          int      // NOT in today's in-memory key — needed once persisted (§1.3)
    GraphRevision: int      // NOT in today's in-memory key — needed once persisted (§1.3)
    SchemaVersion: int      // invalidation stamp, §5
}
```
Collection: `"filmstripPreviews"`. Document id: composite string of the key fields above.

Payload: `Rgba32` bytes (`LayerFilmstripPreviewMap.Rgba32`, §1.3) — **re-encoded, not raw**. At
96×48 the raw cost is small (18 KB/frame) but the cap (512) times realistic sphere×layer×rung
fan-out is not small once persisted indefinitely across sessions (§1.3, DECISION POINT 5).
Recommendation: PNG-encode before persisting (Godot's `Image.SavePngToBuffer()` on the same
`Image` object already constructed at `FilmstripPreviewController.cs:258`, before or instead of
`ImageTexture.CreateFromImage`), decode back to RGBA32 on load via `Image.LoadPngFromBuffer()`
before rebuilding the `ImageTexture`. This keeps the on-disk cost close to the "flat color +
relief gradient" reality of these thumbnails (`ColorCrustCell`, `Service.cs:983-989`) — a strong
PNG-compression candidate — without inventing a new image codec dependency (Godot's `Image` API
already has this built in; no new package).

### 4.3 Disk location

Reuse `App.Activity`'s exact convention (`Service.cs:206-229`, §1.4) rather than inventing a
second one: `user://<name>` → `Environment.SpecialFolder.LocalApplicationData/FantaSim/
complete-app/<relative>`, falling back to `AppContext.BaseDirectory` when that's empty. Suggested
paths: `user://crust-cache.<backend-ext>` and `user://filmstrip-cache.<backend-ext>` (two stores,
so a corrupt/oversized filmstrip cache can't take crust products down with it — see DECISION
POINT 3 on whether they should instead share one store/file). This directly answers "check how
the truth store decides today" — **it doesn't**; the truth store has never had a disk-path
decision to make (§2). `App.Activity` is the only real precedent in this repo, and it is a
resident-adjacent bundle (`activity`, collectible per `collectible-bundles.json`) constructing its
own `LiteDbDocumentStore` directly — i.e. the *inverse* of the residency slice 1 is trying to
establish. Slice 1 should adopt its path convention but not its bundle-local construction pattern.

---

## 5. Invalidation story

What makes a persisted entry stale, ranked by how surely it changes content:

1. **Seed** — different world, always different content. Part of the key (the fix), not a
   separate invalidation check.
2. **GraphRevision** — a generation-graph edit between sessions must not serve a stale product.
   Part of the key (§4), not a separate check — a revision bump naturally produces a new document
   id, and old ids simply stop being requested (eligible for eviction, §6, not actively purged).
3. **SpinRateRadiansPerMegaAnnum** — changes crust motion; part of the key.
4. **Tick-scale/constant changes across app versions — the real, already-observed case.** G35
   (`vault/handover/2026-07-11-session-close-handover.md:84-85`,
   `vault/handover/2026-07-11-parallel-packets-handover.md:48-50`): the D4.2 sweep rescaled
   `MaxTick` from ~700M to 200M canonical ticks on 2026-07-11 — "pre-07-11 fixtures used ticks up
   to 700M... they clamp now." A persisted crust/filmstrip entry keyed by `SnapshotTick=650_000_000`
   from a pre-rescale session is not just stale, it references a tick value **outside the current
   `MaxTick`entirely** — silently loading it back would put the timeline in a state the current
   build cannot reach by scrubbing. This is exactly why `SchemaVersion` is in both keys (§4): bump
   it whenever a tick-scale constant, canonical-tick spacing, or DTO shape changes, and treat a
   `SchemaVersion` mismatch as "does not exist" rather than attempting to read/migrate old rows.
   Precedent: `vault/specs/2026-07-08-common-resident-layer-bundle.md` "Version discipline" (point
   4 of "Mechanics") already establishes this exact pattern for `common.pck` — "the catalog stamps
   the compatible common version; mismatch = fail-hard at boot." Slice 1's `SchemaVersion` is the
   same idea applied to cache rows instead of bundle catalogs — fail-soft (miss and rebuild), not
   fail-hard, since this is a cache, not a truth stream.
5. **App version** (`GitVersion`-derived) — a coarser, optional invalidation lever if `SchemaVersion`
   proves too fine-grained in practice; not required for slice 1 if `SchemaVersion` is bumped
   disciplined at every DTO/constant change. Flagged as DECISION POINT 6 (whether to also stamp
   app version, or rely on `SchemaVersion` alone).
6. **Capacity/eviction, not staleness** — the in-memory filmstrip cache is FIFO-capped at 512
   (§1.3); a persisted store needs its own budget policy independent of the in-memory ledger
   (DECISION POINT 5). This is an eviction question, not an invalidation question — an entry can
   be perfectly valid and still evicted for space.

---

## 6. Slice-1 scope cut

### 6.1 Recommendation: crust products only; filmstrips are slice 2

Argument for cutting filmstrips from slice 1:
- **Payload complexity is lower for crust.** The crust cache's payload
  (`CrustEvolutionResult`/`WorldGlobeSnapshot`) needs new MessagePack serialization surface
  verified end-to-end (§4.1, open item) — that work has to happen regardless of scope, and is the
  same work whether or not filmstrips are included, so it doesn't shrink by deferring filmstrips.
- **Filmstrips add a second, genuinely new sub-problem**: image re-encoding (PNG round-trip
  through Godot's `Image` API, §4.2) that crust products don't need at all. Bundling it into slice
  1 doubles the payload-format surface being proven for the first time.
  boot budget: crust products directly gate "is scrubbing warm" — the user's own framing
  ("scrubbing is warm on a fresh boot") — the filmstrip thumbnails are a secondary visual polish
  layer on top of an already-warm crust cache (§1.3, `FilmstripPreviewController` calls into the
  same `GetLayerFilmstripPreview` chain that eventually reaches `GetOrBuildCrustTickProducts` for
  crust-sourced layers, `Service.cs:798`).
- **The Seed defect fix (the other half of this slice's mandate) lives entirely in the crust
  path.** Fixing it and shipping crust persistence together is one coherent, gateable unit; adding
  filmstrips does not make the fix any more correct, only the deliverable larger.
- Both caches share the resident-storage-contract plumbing (§3) — building that plumbing once,
  proving it end-to-end on the simpler payload (crust), then re-using it for filmstrips in slice 2
  is lower-risk than proving two payload formats against a not-yet-proven resident contract
  simultaneously.

This is a recommendation, not a unilateral cut — DECISION POINT 7 asks the user to confirm or
override it.

### 6.2 TDD-shaped task list (crust-only slice 1)

This is a design spec, not an implementation plan — exact code belongs in a `writing-plans`-skill
follow-up once this design round settles. Paths below are real, verified against the current
tree; steps are RED→GREEN-shaped per house convention (`test-driven-development` skill), not
fully expanded.

1. **RED: crust cache key must include Seed.**
   `project/tests/App.World.Tests/` — new test (or extend `WorldServiceTruthStoreTests.cs`
   neighbor) asserting two `Service` builds with identical `TessellationFrequency`/`SnapshotTick`
   but different `Seed` produce **distinct** cached products (currently they alias — reproduce the
   defect first). Requires making `CrustProductCacheKey` (`Service.cs:1089`) visible enough to
   assert on, or asserting indirectly through observable product identity/counts.
   **GREEN:** extend `CrustProductCacheKey` to `(int Seed, int Frequency, double SpinRate, int
   GraphRevision, long SnapshotTick)` and the construction site (`Service.cs:885`) to pass all
   five. This step alone (no persistence) already satisfies "fix the known crust-cache-key
   defect" independent of the rest of the slice — it can ship first.
2. **RED: resident storage contract resolves and survives a bundle reload.**
   New test in `project/tests/App.Common.Tests/` (create if absent — verify path first) proving
   `IDocumentStore` resolves via `IRegistry` from resident composition, and that a second
   resolution after a simulated bundle reload returns a store that still sees data written before
   the reload (proves residency, not bundle-scoping).
   **GREEN:** implement per §3.1/§3.2 (extend `App.Common` or new resident project per DECISION
   POINT 2).
3. **RED: crust product round-trips through the resident store.**
   `project/tests/App.World.Tests/` — new test constructing a `CrustProductCacheRecord`
   (§4.1), writing via `IDocumentStore.UpsertAsync`, reading back via `GetAsync`, asserting
   byte-identical MessagePack round-trip. Exercises the "open item" from §4.1 — will surface
   whether `CrustEvolutionResult`/`WorldGlobeSnapshot` need new `[MessagePackObject]` annotations.
   **GREEN:** wire `GetOrBuildCrustTickProducts` (`Service.cs:869-905`) to check the persisted
   store before rebuilding, and to write through after a fresh build.
4. **RED: SchemaVersion mismatch is treated as a miss, not a crash.**
   Test writing a record with `SchemaVersion=N-1`, then requesting with the current
   `SchemaVersion=N`, asserting a cache miss (rebuild) rather than a deserialization exception or
   stale content.
   **GREEN:** implement the version-gated read path per §5.
5. **Windowed gate (§6.3).**

### 6.3 Windowed gate design

Following `bundle-hot-reload-verify`/`verify-windowed` house convention (`.agent/rules/bundle-
hot-reload-verify.md`) and the exact evidence shape the 2026-07-11 polarity flip already used
(`9bda14f` commit message: "staging diff = ..., `--check-dual` clean, fresh boot 0 errors, all 5
bundles reload with old ALC collected, 0 pinned"):

1. Fresh boot of the exported windowed app (no prior `user://` cache directory — clean-state
   control run).
2. Seek to a specific tick in a specific world (fixed Seed); log the crust-build duration for that
   `(Seed, Frequency, SpinRate, GraphRevision, SnapshotTick)` — expect the full generation cost
   (this run populates the persisted cache for the first time).
3. Restart the app (same `user://` directory retained this time — no clean).
4. Seek to the **same** tick in the **same** world. Log the duration again — expect it to be
   near-instant (cache hit, no regeneration), measurably smaller than step 2's timing. This is the
   literal "cold boot → seek → restart → same seek must be warm" gate from the task brief, made
   concrete with a timing log line to diff.
5. Negative control: seek to the same tick with a **different Seed** (same session). Expect a full
   rebuild, not a stale hit — this is the regression test for the defect this slice fixes,
   verified in the real running app, not just in xunit.
6. ALC check unchanged from every other bundle-touching change in this repo: `--check-dual`
   clean, all affected bundles (`world` at minimum) still report `"Hot-reload: old ALC collected"`
   on their next reload — proving the new resident storage plumbing didn't reintroduce a pin.

---

## 7. Decision points for the user

1. **What "SurrealDB persistence" means given §2 (+ the lead addendum).** No on-disk embedded
   .NET SurrealDB provider is INSTALLED today, but one exists upstream. Options: **(A)** ship
   slice 1 against `IDocumentStore` with the **LiteDB** runtime concretely (proven on-disk
   today, zero new packaging surface), keeping the abstraction backend-swappable; **(B)** stand
   up and connect to a local `surreal` server process at app boot — a genuinely new operational
   responsibility (binary distribution, process supervision, port/lifecycle) not present
   anywhere in this codebase today; **(C)** wait — defer this slice; **(D)** add
   **`SurrealDb.Embedded.RocksDb` 0.10.2** (nuget.org, same version family as the pinned SDK) —
   real on-disk embedded SurrealDB, no server process; costs: the native engine must live
   RESIDENT (never in a collectible bundle — native never unloads), export packaging must carry
   the per-RID dylib, and `UnifyStorage.Runtime.SurrealDb`'s `rocksdb://` endpoint support needs
   a verification spike. **Lead lean: (D) if its two verification items pass a short spike —
   it is the literal reading of the standing "storage=SurrealDB via unify-storage" direction and
   its resident-native constraint coincides with the already-locked resident-client rule; (A) is
   the fallback if the spike sours.** Not chosen unilaterally — the user decides.
2. **Resident storage contract home**: extend `App.Common` directly (§3.1 option 1, lower
   ceremony) vs. a new dedicated resident project (§3.1 option 2, cleaner separation but a new
   project — house rule requires explicit approval before creating it).
3. **One persisted store or two** for crust products vs. filmstrips (§4.3) — separate files
   isolate a corrupt/oversized filmstrip cache from crust products, but double the resident
   construction/lifetime surface.
4. **Filmstrip cache key gap** (§1.3: no Seed/GraphRevision in `TimelineFilmstripCacheKey` even
   in memory today) — fix now as a drive-by alongside the crust fix (same defect class, small
   diff), or leave for slice 2 alongside the rest of the filmstrip persistence work.
5. **Filmstrip disk budget/eviction policy** (§1.3, §4.2): PNG-encode (recommended) vs. raw
   RGBA32; a persisted entry cap independent of the in-memory 512-entry FIFO; whether eviction is
   LRU-by-last-access or FIFO-by-write-order once entries outlive a single process.
6. **Invalidation granularity** (§5 point 5): rely on `SchemaVersion` alone, or also stamp
   app/`GitVersion` so a build swap invalidates caches even when a developer forgets to bump
   `SchemaVersion` by hand.
7. **Scope cut confirmation** (§6.1): crust-only slice 1, filmstrips as slice 2 — confirm or
   override.

---

## 8. Sources

App-repo (`fantasim-app-godot`), verbatim reads:
- `project/plugins/App.World/Services/WorldTruthStoreFactory.cs` (whole file) — truth-store
  backend selection, config keys, SurrealDb connection parsing, actor-writer requirement.
- `project/plugins/App.World/Services/Service.cs` (targeted reads: 1-100, 860-990, 1083-1130) —
  `Service` ctor truth-store wiring; crust-product cache fields/key/build path; globe-reconstructor
  cache key (Seed+Frequency+SpinRate) as the sibling-cache contrast.
- `project/plugins/App.World/Services/ITruthEventWriter.cs`, `ActorTruthEventWriter.cs` (partial)
  — writer abstraction and why SurrealDb writes are actor-funneled.
  `project/plugins/App.World/Services/CollectibleBundles.cs` not read (not relevant); collectible
  bundle registry lives in host config, see below.
- `project/plugins/App.World/GenerationGraph/WorldGenerationRenderOptions.cs` (partial, 1-46) —
  `Seed`/`TessellationFrequency`/`SpinRateRadiansPerMegaAnnum` shape and defaults.
- `project/plugins/App.World/GenerationGraph/CrustGenerationTriggerPolicy.cs` (partial) —
  `CrustGenerationTriggerKey(GraphRevision, WindowIndex, SnapshotTick)`.
- `project/contracts/App.World/GenerationGraph/WorldGenerationGraph.cs` (partial, 135-436) —
  `CrustSnapshotTickSeries.DefaultSpacingTicks`/`ForRegime`; `GraphRevision`/family key format.
- `project/contracts/App.World/LayerFilmstripPreview.cs` (whole file) — `LayerFilmstripPreviewMap`
  raw `Rgba32` payload shape.
- `project/contracts/App.Timeline/TimelineFilmstrip.cs` (whole file) — `TimelineFilmstripCacheKey`,
  thumbnail dimensions.
- `project/plugins/App.Timeline.Seam/FilmstripCacheLedger.cs` (whole file) — FIFO bookkeeping only.
- `project/plugins/App.Timeline.Seam/FilmstripPreviewController.cs` (partial, 1-140, 240-330) —
  texture cache dictionary, cap, build/evict flow, RGBA8→`ImageTexture` construction.
- `project/plugins/App.Activity/Services/Service.cs` (partial, 1-260) — the only existing
  unify-storage consumer in this app; `IDocumentStore`/`LiteDbDocumentStore` usage, `user://`
  disk-path resolution precedent.
- `project/plugins/App.World/App.World.csproj` (whole file) — `UnifyStorageBackends=SurrealDb`,
  `UseProjectReferences`/`USE_PROJECT_REFERENCES` gating, SurrealDb/UnifyStorage
  `PackageReference`s.
- `project/hosts/complete-app/config/app.json` (whole file) — shipped truth-store config
  (`backend: "inmemory"`).
- `project/hosts/complete-app/config/collectible-bundles.json` (whole file) — `world` bundle's
  `assemblyNames`, confirming SurrealDb/UnifyStorage.Runtime.SurrealDb ship bundle-private today.
- `project/hosts/complete-app/config/shared-assembly-policy.json` (whole file) — `exactMatches`,
  `common.exactMatches`, `common.prefixes`, confirming LiteDb residency and SurrealDb's absence.
- `project/Directory.Packages.props` (grep) — pinned versions for `UnifyStorage.Runtime.SurrealDb`
  (0.1.1), `SurrealDb.Net` (0.10.2), `SurrealDb.Embedded.InMemory` (0.10.2).
- `project/tests/App.World.Tests/WorldTruthStoreFactoryTests.cs` (whole file) — existing test
  pattern for the SurrealDb truth-store path (`mem://` harness), reused as TDD precedent.
- `vault/architecture/cross-alc-rules.md` (whole file) — resident vs. collectible ALC rules,
  `SharedAssemblyPolicy` mechanics, MAY/must-not-cross lists.
- `vault/specs/2026-07-08-common-resident-layer-bundle.md` (whole file) — `common.pck` packing
  list (LiteDb in, SurrealDb not), E1/E2 experiment results, version-discipline precedent for
  `SchemaVersion`.
- `vault/specs/2026-07-08-track-filmstrip-design.md`, `vault/specs/2026-07-10-layer-track-
  registry-design.md` (partial) — filmstrip cache-key precedent, and explicit note that "SurrealDB
  persistence (separate slice)" was already flagged as a companion arc on 2026-07-10.
- `vault/handover/2026-07-11-session-close-handover.md`,
  `vault/handover/2026-07-11-parallel-packets-handover.md` (grep + partial reads) — G35 tick-scale
  rescale, used as the concrete invalidation precedent in §5.
- `.claude/... MEMORY.md` entry `fantasim-alc-shared-type-identity` — the seven known pin classes,
  applied in §3.3.
- git log (`git log --oneline --all -i --grep=...`) and `git show --stat`/full diffs for:
  `1b2d2a9`, `b6a866c`, `6f9b60e`, `fcd6cdf`, `a101232` (truth-store backend history) and `9bda14f`
  (2026-07-11 polarity flip, today's shared-assembly-policy state and its verification gate).

`plate-projects/unify-storage`, verbatim reads:
- `dotnet/src/UnifyStorage.Abstractions/IKeyValueStore.cs` (whole file).
- `dotnet/src/UnifyStorage.Abstractions/IEventStore.cs` (whole file) — confirmed NOT what the
  truth store uses.
- `dotnet/src/UnifyStorage.Abstractions/IDocumentStore.cs` (whole file) — the recommended
  abstraction for both new caches.
- `dotnet/src/UnifyStorage.Runtime.SurrealDb/ServiceCollectionExtensions.cs` (whole file),
  `UnifyStorage.Runtime.SurrealDb.csproj` (whole file) — confirms the package already exists/is
  published; no new plate-projects package needed.
- `dotnet/src/UnifyStorage.Generators/Backends/SurrealDbEmitter.cs` (partial, 1-90, 890-1020) —
  source-generated `SurrealDbDocumentStore`/`SurrealDbKeyValueStore`, generated into the consuming
  project's own assembly, table/record-id/encoding scheme.
- `dotnet/src/UnifyStorage.Runtime.LiteDb/LiteDbDocumentStore.cs` (partial, 1-60) — hand-written
  contrast to the generated SurrealDb store; no actor-writer needed.
- `dotnet/tests/UnifyStorage.Runtime.SurrealDb.Tests/SurrealDbTestFixture.cs` (whole file) — only
  `mem://` is exercised in the runtime's own test suite too, reinforcing §2.
- `.agent/rules/04-persistence.md`, `.agent/rules/09-plate-stack.md` (CLAUDE.md imports, read as
  system-reminder context) — house direction ("SurrealDB... MessagePack canonical encoding") and
  the "reference projects/packages via relative paths, no NuGet for local Plate deps" convention
  (noted, not directly load-bearing for this app-repo-only design).

`fantasim-world` (sibling engine repo, read-only, referenced via `ProjectReference` from
`App.World.csproj`):
- `project/plugins/World.TruthStream.Core/KvTruthEventStore.cs` (whole file) — confirms MessagePack
  encoding and the `IKeyValueStore`-based event-log design the truth store actually uses.

NuGet package contents (`~/.nuget/packages`, read directly — not web-fetched, since network docs
tools were not needed once the installed packages answered the question):
- `surrealdb.embedded.inmemory/0.10.2/README.md`, `.nuspec`, and `runtimes/` folder listing — basis
  for §2's "no on-disk embedded provider" finding.
- `surrealdb.net/0.10.2/README.md` — basis for "remote server only" outside `mem://`.

**Verification flags (not confirmed, called out explicitly, not assumed away):**
- Whether `CrustEvolutionResult`/`WorldGlobeSnapshot` (fantasim-world/App.World composition DTOs)
  already have MessagePack resolver support — unverified, listed as an open item in §4.1 and a
  first RED step in §6.2.
- Whether a `surreal` server binary could be vendored/spawned cross-platform (macOS/Windows/Linux)
  within this repo's existing packaging pipeline (DECISION POINT 1 option B) — not investigated;
  flagged as needing its own spike if the user picks that option.
