# RocksDb embedded-SurrealDB spike — 2026-07-11 (evening session)

Verification spike for persistence-spec decision point 1 option (D)
(`vault/specs/2026-07-11-surrealdb-persistence-slice1-design.md` §2 addendum). Fresh-context
sonnet agent; spike test source preserved as `SurrealRocksDbSpikeTests.cs.txt` (removed from
the tree — it reproducibly fails by design of the finding). Tree changes reverted
(Directory.Packages.props pin + test csproj PackageReference).

## Verdict: D-NOT-VIABLE as spiked → slice 1 proceeds on fallback (A), LiteDB behind IDocumentStore

1. **Endpoint plumbing PASSES, zero unify-storage changes.** `AddSurrealDbStorage` is a thin
   `services.AddSurreal(connectionString)` pass-through; `SurrealDb.Net` 0.10.2 itself validates
   `mem://`, `rocksdb://`, `surrealkv://` (IL-verified `SurrealDbOptionsValidation.
   IsValidClientEndpoint`) and dispatches `rocksdb` to `ISurrealDbRocksDbEngine`, satisfied by
   `SurrealDb.Embedded.RocksDb`'s `AddRocksDbProvider(SurrealDbBuilder)`. Only an app-repo
   branch is needed when D is revisited.
2. **Runtime durability FAILS intra-process.** Writes land in a real on-disk RocksDB (WAL,
   MANIFEST, CURRENT, LOCK verified). But dispose + reopen on the same path throws
   `SurrealDbEmbeddedException: IO error: lock hold by current process` — the SDK's embedded
   engine holds the native lock for the LIFE OF THE PROCESS (survives GC.Collect +
   finalizers + 3s). SDK-level limitation, not a harness bug. Consequence for any future D:
   the resident layer must open the engine ONCE per process and never close-reopen (no
   reconnect retries, no reload-triggered reopen). Cross-PROCESS restart was NOT tested and
   likely works (POSIX releases flock at exit) — revisit condition 1.
3. **Export packaging does not exist.** `runtimes/osx-arm64/native/libsurreal_rocksdb.dylib`
   (13 MB) is present in the package, but the export pipeline has NO native-asset handling for
   NuGet packages at all (`stage_bundle.py` globs `*.dll` only; only the gdext bridge ships a
   dylib, via its `.gdextension` manifest). Engineering this from scratch is revisit
   condition 2. Independent finding worth noting: the already-referenced SurrealDb embedded
   path has never shipped a native asset in exports either.

## Revisit conditions for option (D)

1. A real OS-process-restart durability test passes (new process reopens the RocksDB path).
2. Native-dylib export packaging is designed and gated (target dir for the .NET native loader
   in the exported .app is itself unverified: Frameworks/ vs MacOS/ vs DYLD path).
3. (Watch upstream) SurrealDb.Net fixes the embedded engine's intra-process lock retention.
