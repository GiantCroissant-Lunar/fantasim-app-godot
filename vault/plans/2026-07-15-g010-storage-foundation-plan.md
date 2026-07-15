# G-010 Resident Storage Foundation Implementation Plan

> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the `orchestrate-before-implementing` rule + the `external-agent-delegation`
> skill); otherwise execute inline with a review checkpoint per task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Replace every production LiteDB and bundle-owned SurrealDB path with one resident
`FantaSim.App.Storage` runtime, make Activity durably acknowledged and host-owned, and prove the
storage boundary through focused tests, two-process restart, bundle dual-copy audit, and a
committed conclusion deposit.

**Architecture:** `App.Common` opens and owns one Godot-free T3 `App.Storage` runtime before any
resident domain or collectible plugin starts. `App.Storage` uses the pinned
`plate-projects/unify-storage` contracts/runtime and one `SurrealDb.Net` provider with one
singleton client and two process-lifetime scopes/sessions: documents and truth/CAS. Registry and
collectible consumers receive only non-owning `UnifyStorage.Abstractions` facades; generated
adapters, SDK sessions, scopes, provider, and the runtime handle remain resident and private.

**Tech Stack:** .NET 8, C# 12, `UnifyStorage.Abstractions` 0.1.1,
`UnifyStorage.Generators` 0.1.1, `UnifyStorage.Runtime.SurrealDb` 0.1.1,
`SurrealDb.Net` 0.10.2, ServiceArchi/PluginArchi 0.1.x, Akka.NET 1.5.69, xUnit 2.9.2,
Godot 4.7, Taskfile bundle tooling, and UnifyBuild for the exported desktop gate.

## Global Constraints

- Implement the approved design in
  `vault/specs/2026-07-15-g010-app-world-migration-design.md`; this plan covers only its first
  atomic storage-foundation packet.
- Production persistence is SurrealDB only. Do not add LiteDB, SQLite, a direct RocksDB library,
  `SurrealDb.Embedded.InMemory`, or any backend-selection abstraction.
- Use the packages produced by `plate-projects/unify-storage`; do not copy or redefine
  `IDocumentStore`, `IKeyValueStore`, `IConditionalKeyValueStore`, `IWriteBatch`, or their generated
  Surreal adapters.
- `FantaSim.App.Storage` is a Godot-free resident T3 implementation. It is not a T1 contract and
  must not carry `[PluginSharedContract]`.
- `App.Storage` owns one DI provider/singleton client and exactly two scoped
  `ISurrealDbSession` instances. The document and truth adapters wrap different sessions with
  `ownsClient: false`.
- Registry entries are non-owning facades. Disposing the `IConditionalKeyValueStore` facade is a
  no-op and must not close the generated adapter, session, or provider.
- The only production connection key is `storage:surrealDb:connectionString`. Missing, malformed,
  or unreachable configuration aborts startup before Activity or collectible plugin composition.
- Existing `.litedb` files are never opened, imported, renamed, deleted, truncated, or rewritten.
- App.Activity owns its subscription/worker only. It requires `IDocumentStore`, loads before
  registration/subscription, acknowledges a sequence only after successful upsert, drains its
  final sequence before storage shutdown, and never disposes the borrowed store.
- App.World retains only `UnifyStorage.Abstractions`; it does not construct an SDK provider,
  session, or generated adapter. Its public production constructor requires both a non-null
  `ActorSystem` and the registered resident `IConditionalKeyValueStore`; only an explicit internal
  test factory may select `InMemoryTruthEventStore` with the direct writer.
- `DocumentBlob` lives in the existing T1 `FantaSim.App.World.Contracts` assembly under namespace
  `FantaSim.App.World.Persistence`.
- `FantaSim.App.Storage`, `SurrealDb.Net`, `UnifyStorage.Runtime.SurrealDb`, and their managed
  runtime closure are resident exact/common assemblies and are absent from every collectible
  bundle `assemblyNames` list.
- Keep the host's explicit `System.Collections.Immutable` 10.0.1 pin until both dependency modes,
  staging, export, and runtime startup have been reverified.
- Follow RED→GREEN→REFACTOR. Record the failing output before production edits, keep commits
  Conventional, never use `--no-verify`, and do not integrate any intermediate dual-copy state.
- Execute the packet in an isolated native git worktree. The lead session reviews and integrates
  the complete packet only after every gate in Task 7 passes.

---

### Task 1: Create the resident `App.Storage` runtime and non-owning contract facades

**Files:**
- Create: `project/plugins/App.Storage/App.Storage.csproj`
- Create: `project/plugins/App.Storage/Properties/AssemblyInfo.cs`
- Create: `project/plugins/App.Storage/SurrealDbConnectionSettings.cs`
- Create: `project/plugins/App.Storage/NonOwningDocumentStore.cs`
- Create: `project/plugins/App.Storage/NonOwningConditionalKeyValueStore.cs`
- Create: `project/plugins/App.Storage/StorageRuntime.cs`
- Create: `project/tests/App.Storage.Tests/App.Storage.Tests.csproj`
- Create: `project/tests/App.Storage.Tests/SurrealDbConnectionSettingsTests.cs`
- Create: `project/tests/App.Storage.Tests/NonOwningStoreFacadeTests.cs`
- Modify: `project/FantaSim.sln`

**Interfaces:**
- Consumes: `IDocumentStore`, `IConditionalKeyValueStore`, `IWriteBatch`, `IKeyValueIterator`,
  `ISurrealDbSession`, `ServiceCollectionExtensions.AddSurrealDbStorage(string)`.
- Produces: `StorageRuntime.OpenAsync(string, ILoggerFactory, CancellationToken)`,
  `StorageRuntime.DocumentStore`, `StorageRuntime.ConditionalKeyValueStore`, and idempotent
  `StorageRuntime.DisposeAsync()`.

- [ ] **Step 1: Add the test project and write the failing parsing/facade tests**

Create the test csproj with xUnit packages and a project reference to `App.Storage`. Add tests with
these exact cases:

```csharp
[Fact]
public void Parse_requires_namespace_and_database()
{
    var missingNamespace = Assert.Throws<InvalidOperationException>(() =>
        SurrealDbConnectionSettings.Parse("Endpoint=http://127.0.0.1:8000;Database=app"));
    Assert.Contains("Namespace", missingNamespace.Message);

    var missingDatabase = Assert.Throws<InvalidOperationException>(() =>
        SurrealDbConnectionSettings.Parse("Endpoint=http://127.0.0.1:8000;Namespace=fantasim"));
    Assert.Contains("Database", missingDatabase.Message);
}

[Fact]
public void Parse_rejects_embedded_memory_endpoint()
{
    var ex = Assert.Throws<InvalidOperationException>(() =>
        SurrealDbConnectionSettings.Parse("Endpoint=mem://;Namespace=fantasim;Database=app"));
    Assert.Contains("external SurrealDB endpoint", ex.Message);
}

[Fact]
public void Conditional_facade_dispose_does_not_dispose_or_disable_inner_store()
{
    var inner = new TrackingConditionalKeyValueStore();
    var facade = new NonOwningConditionalKeyValueStore(inner);

    facade.Put(new byte[] { 1 }, new byte[] { 2 });
    facade.Dispose();
    facade.Put(new byte[] { 3 }, new byte[] { 4 });

    Assert.False(inner.Disposed);
    Assert.Equal(2, inner.PutCount);
}

[Fact]
public async Task Document_facade_forwards_without_exposing_disposal()
{
    var inner = new TrackingDocumentStore();
    IDocumentStore facade = new NonOwningDocumentStore(inner);

    await facade.UpsertAsync("probe", "one", new ProbeDocument { Value = 42 });
    var restored = await facade.GetAsync<ProbeDocument>("probe", "one");

    Assert.Equal(42, restored!.Value);
    Assert.False(facade is IDisposable);
}
```

The two tracking stores implement every member of their Unify abstraction. The conditional fake
uses an in-memory dictionary and a private `TrackingWriteBatch`; `TryWrite` first compares all
`KeyValueCondition` values and applies the batch only when every condition matches. The document
fake keys objects by `(collection, id)` and implements query/count/bulk members over that
dictionary. No test project references a storage runtime package.

- [ ] **Step 2: Run the new project and preserve the RED output**

Run:

```bash
dotnet test project/tests/App.Storage.Tests/App.Storage.Tests.csproj
```

Expected: FAIL because `App.Storage.csproj`, `SurrealDbConnectionSettings`, and the two facade types
do not exist.

- [ ] **Step 3: Add the T3 project and generator/package boundary**

Create `App.Storage.csproj` with this project shape:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>FantaSim.App</RootNamespace>
    <AssemblyName>FantaSim.App.Storage</AssemblyName>
    <ServiceArchiTier>T3</ServiceArchiTier>
    <UnifyStorageBackends>SurrealDb</UnifyStorageBackends>
  </PropertyGroup>
  <ItemGroup>
    <CompilerVisibleProperty Include="ServiceArchiTier" />
    <CompilerVisibleProperty Include="UnifyStorageBackends" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="SurrealDb.Net" />
    <PackageReference Include="UnifyStorage.Abstractions" />
    <PackageReference Include="UnifyStorage.Generators" PrivateAssets="all" />
    <PackageReference Include="UnifyStorage.Runtime.SurrealDb" />
  </ItemGroup>
</Project>
```

Add `InternalsVisibleTo("App.Storage.Tests")` in `Properties/AssemblyInfo.cs`. Do not reference
PluginArchi source generators, Godot, Activity, Common, World, or an embedded Surreal provider.

- [ ] **Step 4: Implement strict connection parsing**

`SurrealDbConnectionSettings.Parse` must use `DbConnectionStringBuilder`, preserve the original
connection string, require `Endpoint`, `Namespace`, and `Database` case-insensitively, trim their
values, and reject `mem://`:

```csharp
internal sealed record SurrealDbConnectionSettings(
    string ConnectionString,
    string Endpoint,
    string Namespace,
    string Database)
{
    public static SurrealDbConnectionSettings Parse(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Configuration key 'storage:surrealDb:connectionString' is required.");

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString.Trim() };
        string Require(string key)
        {
            foreach (string candidate in builder.Keys)
                if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(builder[candidate]?.ToString()))
                    return builder[candidate]!.ToString()!.Trim();
            throw new InvalidOperationException($"SurrealDB connection string requires '{key}'.");
        }

        var endpoint = Require("Endpoint");
        if (endpoint.StartsWith("mem://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Production storage requires an external SurrealDB endpoint; mem:// is not allowed.");

        return new(builder.ConnectionString, endpoint, Require("Namespace"), Require("Database"));
    }
}
```

- [ ] **Step 5: Implement both non-owning facades**

`NonOwningDocumentStore` forwards all nine `IDocumentStore` members. It does not implement
`IDisposable`. `NonOwningConditionalKeyValueStore` forwards every KV operation, iterator, batch,
write, and conditional write, while its required `Dispose()` is exactly a no-op:

```csharp
internal sealed class NonOwningConditionalKeyValueStore : IConditionalKeyValueStore
{
    private readonly IConditionalKeyValueStore _inner;
    public NonOwningConditionalKeyValueStore(IConditionalKeyValueStore inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public bool TryGet(ReadOnlySpan<byte> key, Span<byte> buffer, out int written) =>
        _inner.TryGet(key, buffer, out written);
    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) => _inner.Put(key, value);
    public void Delete(ReadOnlySpan<byte> key) => _inner.Delete(key);
    public IKeyValueIterator CreateIterator() => _inner.CreateIterator();
    public IWriteBatch CreateWriteBatch() => _inner.CreateWriteBatch();
    public void Write(IWriteBatch batch) => _inner.Write(batch);
    public bool TryWrite(IWriteBatch batch, IReadOnlyList<KeyValueCondition> conditions) =>
        _inner.TryWrite(batch, conditions);
    public void Dispose() { }
}
```

- [ ] **Step 6: Implement the one-provider/two-session runtime**

`StorageRuntime.OpenAsync` must perform this exact ownership sequence:

```csharp
var settings = SurrealDbConnectionSettings.Parse(connectionString);
var services = new ServiceCollection();
services.AddSingleton(loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory)));
services.AddLogging();
services.AddSurrealDbStorage(settings.ConnectionString);
var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

var documentScope = provider.CreateAsyncScope();
var truthScope = provider.CreateAsyncScope();
var documentSession = documentScope.ServiceProvider.GetRequiredService<ISurrealDbSession>();
var truthSession = truthScope.ServiceProvider.GetRequiredService<ISurrealDbSession>();
if (ReferenceEquals(documentSession, truthSession))
    throw new InvalidOperationException("Document and truth storage require distinct scoped sessions.");

ct.ThrowIfCancellationRequested();
await documentSession.Connect().ConfigureAwait(false);
ct.ThrowIfCancellationRequested();
await documentSession.Use(settings.Namespace, settings.Database).ConfigureAwait(false);
ct.ThrowIfCancellationRequested();
await truthSession.Connect().ConfigureAwait(false);
ct.ThrowIfCancellationRequested();
await truthSession.Use(settings.Namespace, settings.Database).ConfigureAwait(false);

var documentAdapter = new SurrealDbDocumentStore(documentSession, ownsClient: false);
var truthAdapter = new SurrealDbKeyValueStore(truthSession, ownsClient: false);
```

Use generated namespace `FantaSim.App.Storage.Generated.SurrealDb`. Construct public interface
properties as `new NonOwningDocumentStore(documentAdapter)` and
`new NonOwningConditionalKeyValueStore(truthAdapter)`. On any open failure, dispose created
adapters, then truth scope, document scope, and provider. `DisposeAsync()` is idempotent via
`Interlocked.Exchange`; it follows that same reverse order and never disposes either facade.

- [ ] **Step 7: Run tests and add both projects to the solution**

Run:

```bash
dotnet sln project/FantaSim.sln add \
  project/plugins/App.Storage/App.Storage.csproj \
  project/tests/App.Storage.Tests/App.Storage.Tests.csproj
dotnet test project/tests/App.Storage.Tests/App.Storage.Tests.csproj
```

Expected: PASS, with no embedded-provider package in the restore graph.

- [ ] **Step 8: Commit Task 1**

```bash
git add project/plugins/App.Storage project/tests/App.Storage.Tests project/FantaSim.sln
git commit -m "feat(storage): add resident SurrealDB runtime"
```

### Task 2: Make App.Common own ordered resident shutdown

**Files:**
- Create: `project/plugins/App.Common/Lifecycle/ShutdownCoordinator.cs`
- Create: `project/tests/App.Common.Tests/ShutdownCoordinatorTests.cs`
- Modify: `project/plugins/App.Common/App.Common.csproj`
- Modify: `project/plugins/App.Common/Bootstrap.cs`
- Modify: `project/plugins/App.Common/AppComposition.cs`
- Modify: `project/hosts/complete-app/Host.cs`
- Delete: `project/plugins/App.Common/Storage/ResidentDocumentStoreFactory.cs`
- Delete: `project/plugins/App.Common/Storage/ResidentPersistenceOptions.cs`
- Delete: `project/tests/App.Common.Tests/ResidentDocumentStoreTests.cs`

**Interfaces:**
- Consumes: `StorageRuntime` from Task 1 and `IAsyncDisposable` resident lifetimes.
- Produces: `AppComposition.RegisterResidentLifetime(IAsyncDisposable)`, plugin-first shutdown, and
  storage-last infrastructure shutdown.

- [ ] **Step 1: Write the RED shutdown-order tests**

Add `InternalsVisibleTo Include="App.Common.Tests"` to `App.Common.csproj`. The tests use recording
delegates/disposables and assert:

```csharp
[Fact]
public async Task Stop_orders_plugins_then_reverse_residents_then_infrastructure()
{
    var events = new List<string>();
    var coordinator = new ShutdownCoordinator();
    coordinator.Add(new RecordingAsyncDisposable("activity", events));
    coordinator.Add(new RecordingAsyncDisposable("other", events));

    await coordinator.StopAsync(
        () => Record("plugins", events),
        () => Record("infrastructure", events));

    Assert.Equal(new[] { "plugins", "other", "activity", "infrastructure" }, events);
}

[Fact]
public async Task Stop_is_idempotent_and_still_runs_infrastructure_after_resident_failure()
{
    var events = new List<string>();
    var coordinator = new ShutdownCoordinator();
    coordinator.Add(new ThrowingAsyncDisposable("activity", events));

    await Assert.ThrowsAsync<AggregateException>(() => coordinator.StopAsync(
        () => Record("plugins", events),
        () => Record("infrastructure", events)).AsTask());
    await coordinator.StopAsync(() => Record("plugins-again", events),
        () => Record("infrastructure-again", events));

    Assert.Equal(new[] { "plugins", "activity", "infrastructure" }, events);
}
```

- [ ] **Step 2: Run the RED test**

Run: `dotnet test project/tests/App.Common.Tests/App.Common.Tests.csproj --filter ShutdownCoordinatorTests`

Expected: FAIL because `ShutdownCoordinator` does not exist.

- [ ] **Step 3: Implement the coordinator**

`ShutdownCoordinator.Add` rejects null or additions after stop. `StopAsync` atomically becomes the
one shutdown caller, runs plugin stop, resident lifetimes in reverse registration order, and
infrastructure stop even when an earlier phase throws. Collect all failures and throw one
`AggregateException` after infrastructure completes; a later call is a no-op.

- [ ] **Step 4: Replace LiteDB construction with App.Storage composition**

In `Bootstrap`, replace `_ownedDocumentStore` with these owned fields:

```csharp
private const string StorageConnectionStringKey = "storage:surrealDb:connectionString";
private StorageRuntime? _storageRuntime;
private IDisposable? _documentStoreRegistration;
private IDisposable? _conditionalStoreRegistration;
```

After config and logging registration, before actor/plugin construction, require the key, synchronously
complete `StorageRuntime.OpenAsync` at the process composition boundary, then register only its two
facades using `RegisterOwned` and tags `storage`, `persistence`, `surrealdb`. If the second
registration fails, dispose the first registration and the runtime before rethrowing.

Split current `StopAsync` into:

```csharp
public async ValueTask StopPluginHostAsync()
{
    var host = Interlocked.Exchange(ref _pluginHost, null);
    if (host is not null)
        await host.DisposeAsync().ConfigureAwait(false);
}

public async ValueTask StopInfrastructureAsync()
{
    _conditionalStoreRegistration?.Dispose();
    _conditionalStoreRegistration = null;
    _documentStoreRegistration?.Dispose();
    _documentStoreRegistration = null;

    var storage = Interlocked.Exchange(ref _storageRuntime, null);
    if (storage is not null)
        await storage.DisposeAsync().ConfigureAwait(false);

    var actorSystem = Interlocked.Exchange(ref _actorSystem, null);
    if (actorSystem is not null)
        await actorSystem.Terminate().ConfigureAwait(false);
}
```

Keep `StopAsync` as the compatibility composition of those two phases. Delete the old resident
LiteDB factory/options and their tests.

- [ ] **Step 5: Wire AppComposition and remove the startup race**

`AppComposition` owns one `ShutdownCoordinator`. Add:

```csharp
public void RegisterResidentLifetime(IAsyncDisposable lifetime) => _shutdown.Add(lifetime);
```

Its idempotent stop path calls `Bootstrap.StopPluginHostAsync`, then registered resident lifetimes,
then `Bootstrap.StopInfrastructureAsync`, then disposes `_rootServices`; it surfaces the aggregate
only after cleanup. In `Host.ComposeAndStart`, move `_ = Bootstrap.RunAsync()` from immediately
after `BuildPluginHost` to after resident composition and Activity lifetime registration. In
`_Notification`, dispose `_composition` once and set it to null.

- [ ] **Step 6: GREEN verification and commit**

Run:

```bash
dotnet test project/tests/App.Common.Tests/App.Common.Tests.csproj
dotnet build project/hosts/complete-app/complete-app.csproj
```

Expected: all Common tests pass and host compiles. Then commit:

```bash
git add project/plugins/App.Common project/tests/App.Common.Tests project/hosts/complete-app/Host.cs
git commit -m "refactor(storage): order resident composition shutdown"
```

### Task 3: Make Activity mandatory, acknowledged, and host-owned

**Files:**
- Create: `project/tests/App.Activity.Tests/App.Activity.Tests.csproj`
- Create: `project/tests/App.Activity.Tests/ActivityServicePersistenceTests.cs`
- Create: `project/tests/App.Activity.Tests/ActivityCompositionTests.cs`
- Modify: `project/plugins/App.Activity/Services/Service.cs`
- Modify: `project/plugins/App.Activity/ActivityOptions.cs`
- Modify: `project/plugins/App.Activity/HostComposition/ActivityComposition.cs`
- Modify: `project/plugins/App.Activity/App.Activity.csproj`
- Modify: `project/hosts/complete-app/Host.cs`
- Modify: `project/FantaSim.sln`

**Interfaces:**
- Consumes: required borrowed `IDocumentStore` and `AppComposition.RegisterResidentLifetime`.
- Produces: `ActivityCompositionHandle`, `Service.FlushAsync`, and idempotent async final drain.

- [ ] **Step 1: Write the RED Activity persistence suite**

Create a focused test project referencing App.Activity, App.Common, xUnit, and
`UnifyStorage.Abstractions`, but no storage runtime. A deterministic fake document store queues
read payloads and queued upsert exceptions. A two-method fake `IMessageBus` records subscription
disposal. Cover these exact facts:

```text
Missing_document_starts_empty
Valid_v2_document_loads_and_trims_without_losing_terminal_sequence
Malformed_or_unknown_schema_fails_before_bus_subscription_and_without_upsert
Append_then_flush_writes_activityLedger_activity-ledger.latest_v2
Failed_upsert_does_not_advance_persisted_sequence_and_a_later_flush_retries
Flush_waits_for_a_snapshot_covering_its_captured_sequence
Shutdown_unsubscribes_then_persists_the_exact_final_sequence
Append_after_shutdown_does_not_mutate_or_signal
Shutdown_is_idempotent_and_does_not_dispose_the_borrowed_store
Second_service_over_the_same_fake_store_recovers_the_first_marker
Composition_requires_IDocumentStore_and_returns_a_host_owned_handle
```

The v2 JSON assertion requires fields `schemaVersion`, `terminalSequence`, `updatedUtc`, and
`entries`. For a loaded ring buffer, `terminalSequence` may exceed retained entry count but may
never be negative or smaller than the decoded entry count.

- [ ] **Step 2: Run the RED tests**

Run: `dotnet test project/tests/App.Activity.Tests/App.Activity.Tests.csproj`

Expected: FAIL because the project is not in the solution, the constructor does not accept a
store, schema v2/acknowledgement do not exist, and composition returns void.

- [ ] **Step 3: Remove Activity's database ownership and optional persistence**

Change `ActivityOptions` to:

```csharp
public sealed record ActivityOptions(int MaxEntries = 10_000, int PersistRetryCount = 3);
```

Change `Service` to implement `IAsyncDisposable` as well as `IDisposable`, require
`IDocumentStore documentStore` between bus and logger factory, and delete `_ownedDocumentStore`,
`CreateDocumentStore`, `ResolveStorePath`, all `System.IO` path creation, and the LiteDB using.
Load synchronously to completion before worker creation and bus subscription. A null document is
empty; empty bytes, JSON failure, schema other than 2, negative sequence, or sequence smaller than
entry count throws `InvalidDataException` out of construction.

- [ ] **Step 4: Implement success-only acknowledgement and bounded retries**

Use one `_persistGate` to serialize background, explicit flush, and shutdown writes. A snapshot is
`(List<ActivityEntry> Entries, long TerminalSequence)`. The minimal write loop is:

```csharp
private async Task PersistThroughAsync(long target, CancellationToken ct)
{
    await _persistGate.WaitAsync(ct).ConfigureAwait(false);
    try
    {
        while (Interlocked.Read(ref _persistedSequence) < target)
        {
            List<ActivityEntry> entries;
            long sequence;
            lock (_lock)
            {
                entries = _entries.ToList();
                sequence = _appendSequence;
            }

            Exception? last = null;
            var written = false;
            for (var attempt = 1; attempt <= _persistRetryCount; attempt++)
            {
                try
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(
                        new ActivityLedgerDocument(2, sequence, DateTimeOffset.UtcNow, entries),
                        s_jsonOptions);
                    await _documentStore.UpsertAsync(
                        DocumentCollection, DocumentId, new DocumentPayload(bytes), ct)
                        .ConfigureAwait(false);
                    written = true;
                    break;
                }
                catch (Exception ex) when (attempt < _persistRetryCount)
                {
                    last = ex;
                    await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }

            if (!written)
            {
                Interlocked.Exchange(ref _dirty, 1);
                throw new ActivityPersistenceException(sequence, last!);
            }

            Interlocked.Exchange(ref _persistedSequence, sequence);
            lock (_lock)
                Interlocked.Exchange(ref _dirty, _appendSequence > sequence ? 1 : 0);
        }
    }
    finally
    {
        _persistGate.Release();
    }
}
```

The background worker logs a bounded-cycle failure, leaves `_dirty == 1`, waits `RetryDelay`, and
re-signals itself unless shutdown cancellation was requested. `FlushAsync` captures
`_appendSequence` at call entry, signals the worker, and calls `PersistThroughAsync(target, ct)`;
no polling loop may claim success from a failed write.

- [ ] **Step 5: Implement idempotent final drain and the composition handle**

Gate `Append` with an `_accepting` flag checked both before and inside `_lock`. `ShutdownAsync`
sets accepting to false, disposes the bus subscription, captures the final sequence, persists
through it, then cancels and joins the worker and disposes worker primitives in `finally`. Cache
one shutdown `Task` behind a lock so concurrent `DisposeAsync` calls await the same result.
`Dispose()` is the synchronous host bridge and never touches the store.

`ActivityComposition.ComposeActivity` resolves required `IDocumentStore`, constructs the service,
registers `IService`, and returns an `ActivityCompositionHandle`. The handle first disposes the
registration and then awaits the service shutdown; it is idempotent. In `Host`, register that
handle with `_composition.RegisterResidentLifetime(...)` before starting the plugin host.

- [ ] **Step 6: Run focused tests and commit**

```bash
dotnet sln project/FantaSim.sln add project/tests/App.Activity.Tests/App.Activity.Tests.csproj
dotnet test project/tests/App.Activity.Tests/App.Activity.Tests.csproj
dotnet build project/hosts/complete-app/complete-app.csproj
git add project/plugins/App.Activity project/tests/App.Activity.Tests project/hosts/complete-app/Host.cs project/FantaSim.sln
git commit -m "feat(activity): persist through resident document store"
```

Expected: all Activity tests pass; App.Activity has no LiteDB reference; host compiles.

### Task 4: Make collectible App.World borrow resident Unify stores

**Files:**
- Create: `project/contracts/App.World/Persistence/DocumentBlob.cs`
- Create: `project/tests/App.World.Tests/WorldServiceTestFactory.cs`
- Modify: `project/plugins/App.World/Services/Service.cs`
- Modify: `project/plugins/App.World/App.World.csproj`
- Modify: `project/tests/App.World.Tests/BoundaryProfileIntegrationTests.cs`
- Modify: `project/tests/App.World.Tests/ContinentalFractionAtTickTests.cs`
- Modify: `project/tests/App.World.Tests/WorldServiceTruthStoreTests.cs`
- Modify: `project/tests/App.World.Tests/CrustProductPersistenceTests.cs`
- Modify: `project/tests/App.World.Tests/DeterminismFixesTests.cs`
- Modify: `project/tests/App.World.Tests/MotionGateTests.cs`
- Modify: `project/tests/App.World.Tests/RotationAuthorityConsistencyTests.cs`
- Modify: `project/tests/App.World.Tests/RotationSourceSeamTests.cs`
- Modify: `project/tests/App.World.Tests/WorldCrustMaterializerTests.cs`
- Modify: `project/tests/App.World.Tests/WorldServiceGenerationProductsTests.cs`
- Delete: `project/plugins/App.World/Services/WorldTruthStoreFactory.cs`
- Delete: `project/tests/App.World.Tests/WorldTruthStoreFactoryTests.cs`
- Delete: `project/plugins/App.Common/Storage/DocumentBlob.cs`

**Interfaces:**
- Consumes: resident `IDocumentStore` and `IConditionalKeyValueStore` facades.
- Produces: a collectible `KvTruthEventStore`/writer over the borrowed conditional KV contract and
  a T1 `FantaSim.App.World.Persistence.DocumentBlob`.

- [ ] **Step 1: Rewrite the World tests to state the resident-borrowing contract**

Replace backend/config tests with these facts using a deterministic in-memory
`IConditionalKeyValueStore` fake:

```text
Production_constructor_requires_non_null_actor_system
Production_constructor_requires_registered_conditional_store
Production_constructor_runs_generation_through_registered_conditional_store
Two_production_services_share_the_registered_store_without_disposing_it
Production_service_dispose_after_actor_system_termination_does_not_dispose_the_store
Explicit_test_factory_uses_in_memory_truth_without_a_registered_store
```

Replace the LiteDB instance in `CrustProductPersistenceTests` with one shared dictionary-backed
`IDocumentStore` fake used by two sequential Service instances. Retain the existing build-count,
warm-restore equality, missing-store cache miss, and throwing-read fail-soft assertions. Route
every no-actor `new Service(...)` call in the files listed above through
`WorldServiceTestFactory.Create(...)`; do not preserve the public optional-actor constructor.

- [ ] **Step 2: Run the focused RED tests**

```bash
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj \
  --filter "WorldServiceTruthStoreTests|CrustProductPersistenceTests"
```

Expected: FAIL because Service still reads backend config/constructs its own provider and the crust
test still depends on LiteDB.

- [ ] **Step 3: Move the byte DTO to T1 and update consumers**

Create the exact contract type:

```csharp
namespace FantaSim.App.World.Persistence;

public sealed class DocumentBlob
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public DocumentBlob() { }
    public DocumentBlob(byte[] data) => Data = data ?? throw new ArgumentNullException(nameof(data));
}
```

Delete the App.Common definition. `Service.cs` already imports
`FantaSim.App.World.Persistence`; remove `using FantaSim.App.Common.Storage` and keep all document
serialization before the `IDocumentStore` boundary.

- [ ] **Step 4: Replace bundle-owned Surreal construction**

Delete `WorldTruthStoreFactory.cs`, its backend enum/options/handle, and its test. In the Service
constructors use this production/test split:

```csharp
public Service(IRegistry registry, ActorSystem actorSystem)
    : this(
        registry,
        CreateProductionTruthStore(registry),
        actorSystem ?? throw new ArgumentNullException(nameof(actorSystem)),
        WorldHistoryCoordinatorFactory.Create)
{
}

internal Service(
    IRegistry registry,
    ITruthEventStore truthStore,
    ActorSystem? actorSystem,
    Func<IRegistry, ITruthEventReader, ITruthEventWriter, IWorldHistoryCoordinator> historyFactory)
{
    ArgumentNullException.ThrowIfNull(registry);
    ArgumentNullException.ThrowIfNull(truthStore);
    ArgumentNullException.ThrowIfNull(historyFactory);

    ITruthEventWriter? writer = actorSystem is null
        ? new DirectTruthEventWriter(truthStore)
        : ActorTruthEventWriter.Start(actorSystem, truthStore, NewTruthWriterActorName());
    try
    {
        _history = historyFactory(registry, truthStore, writer);
        _truthWriter = writer;
        writer = null;
    }
    finally
    {
        writer?.Dispose();
    }
    // Preserve the remaining existing initialization after assigning _registry.
}

private static ITruthEventStore CreateProductionTruthStore(IRegistry registry)
{
    ArgumentNullException.ThrowIfNull(registry);
    var store = registry.TryGet<IConditionalKeyValueStore>()
        ?? throw new InvalidOperationException(
            "The production world runtime requires resident IConditionalKeyValueStore storage.");
    return new KvTruthEventStore(store);
}
```

Remove `_truthStoreHandle`; Service disposal stops `_truthWriter` but does not dispose the
conditional store or any resident storage object. Keep the actor-null branch internal so only a
test can choose the direct writer deliberately. Add this test-only factory rather than letting
production code infer an in-memory backend from a missing dependency:

```csharp
internal static class WorldServiceTestFactory
{
    public static Service Create(IRegistry? registry = null) =>
        Create(registry, WorldHistoryCoordinatorFactory.Create);

    public static Service Create(
        IRegistry? registry,
        Func<IRegistry, ITruthEventReader, ITruthEventWriter, IWorldHistoryCoordinator> historyFactory) =>
        new(
            registry ?? new ServiceRegistry(),
            new InMemoryTruthEventStore(),
            actorSystem: null,
            historyFactory);
}
```

`WorldComposition` continues to call the public constructor with its resident `ActorSystem`; it
therefore fails closed if the resident conditional store registration is absent.

- [ ] **Step 5: Remove Surreal generator/runtime ownership from App.World and go GREEN**

Remove `UnifyStorageBackends`, its compiler-visible property, `SurrealDb.Embedded.InMemory`,
`SurrealDb.Net`, `UnifyStorage.Generators`, and `UnifyStorage.Runtime.SurrealDb` from
`App.World.csproj`; retain `UnifyStorage.Abstractions`.

Run:

```bash
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj \
  --filter "WorldServiceTruthStoreTests|CrustProductPersistenceTests"
```

Expected: PASS without generated `FantaSim.App.World.Storage.Generated.SurrealDb` types.

- [ ] **Step 6: Commit Task 4**

```bash
git add project/contracts/App.World/Persistence/DocumentBlob.cs \
  project/plugins/App.World project/tests/App.World.Tests \
  project/plugins/App.Common/Storage/DocumentBlob.cs
git commit -m "refactor(world): borrow resident Unify storage"
```

### Task 5: Relocate packages and enforce resident plugin/ALC policy

**Files:**
- Create: `project/tests/App.Architecture.Tests/Gates/StorageAssemblyPlacementTests.cs`
- Modify: `project/tests/App.Architecture.Tests/Gates/WorldDeclarationAssemblyPlacementTests.cs`
- Modify: `project/tests/App.Architecture.Tests/App.Architecture.Tests.csproj`
- Modify: `project/Directory.Packages.props`
- Modify: `project/plugins/App.Common/App.Common.csproj`
- Modify: `project/plugins/App.Activity/App.Activity.csproj`
- Modify: `project/hosts/complete-app/complete-app.csproj`
- Modify: `project/hosts/complete-app/config/app.json`
- Modify: `project/hosts/complete-app/config/shared-assembly-policy.json`
- Modify: `project/hosts/complete-app/config/collectible-bundles.json`
- Regenerate: `project/bundles/common/manifest.json`
- Regenerate: `project/bundles/world/manifest.json`
- Regenerate: `project/bundles/world/*.deps.json`

**Interfaces:**
- Consumes: completed App.Storage/App.Common/Activity/World code.
- Produces: one resident storage closure, no production LiteDB, and no resident/collectible dual
  copies.

- [ ] **Step 1: Write the RED architecture gates**

`StorageAssemblyPlacementTests` must parse csproj/XML and JSON manifests, not scan C# text. It
asserts:

```text
Storage_assembly_is_Godot_free_T3_and_not_a_PluginSharedContract
Storage_and_Surreal_runtime_are_in_shared_and_common_exact_matches
Storage_and_Surreal_runtime_are_absent_from_every_collectible_bundle
AppWorld_has_only_the_Unify_abstraction_and_no_backend_generator_or_SDK_package
AppStorage_owns_the_generator_runtime_and_SDK_packages
No_production_csproj_or_central_package_pin_references_LiteDB
App_json_contains_only_storage_surrealDb_connectionString_for_persistence_backend_selection
Embedded_in_memory_provider_is_absent_from_projects_policy_and_manifests
System_Collections_Immutable_remains_pinned_at_10_0_1
```

Use reflection on `typeof(StorageRuntime).Assembly` for the Godot reference and assembly-attribute
checks. Parse every `project/**/*.csproj` and `Directory.Packages.props` with `XDocument`. Parse
policy/bundle/app JSON with `JsonDocument`.

- [ ] **Step 2: Run the RED architecture test**

Run: `dotnet test project/tests/App.Architecture.Tests/App.Architecture.Tests.csproj --filter StorageAssemblyPlacementTests`

Expected: FAIL on LiteDB pins, absent resident policy entries, and current collectible Surreal names.

- [ ] **Step 3: Reconcile package/project ownership**

- App.Common adds a project reference to `../App.Storage/App.Storage.csproj`, keeps
  `UnifyStorage.Abstractions`, and removes `UnifyStorage.Runtime.LiteDb`.
- App.Activity removes `UnifyStorage.Runtime.LiteDb`.
- Central packages remove `UnifyStorage.Runtime.LiteDb` and `SurrealDb.Embedded.InMemory`; keep the
  exact four approved Unify/Surreal versions and add `Microsoft.Extensions.Logging` 10.0.1 beside
  the already-pinned DependencyInjection and Logging.Abstractions packages required by
  `ServiceCollection.AddLogging()`.
- App.Common tests remove their LiteDB runtime package.
- Host retains `System.Collections.Immutable` 10.0.1 and updates its comment to the now-resident
  Surreal closure.

- [ ] **Step 4: Reconcile configuration and assembly placement**

Replace `world.truthStore` and `persistence.crustCache` in `app.json` with:

```json
"storage": {
  "surrealDb": {
    "connectionString": "Endpoint=http://127.0.0.1:8000;Namespace=fantasim;Database=app"
  }
}
```

Add these exact names to both top-level `exactMatches` and `common.exactMatches`:

```text
FantaSim.App.Storage
UnifyStorage.Runtime.SurrealDb
SurrealDb.Net
ConcurrentCollections
Dahomey.Cbor
Microsoft.Extensions.Http
Microsoft.IO.RecyclableMemoryStream
Microsoft.Spatial
Semver
System.IO.Pipelines
System.Linq.AsyncEnumerable
System.Reactive
SystemTextJsonPatch
Websocket.Client
```

Remove `UnifyStorage.Runtime.LiteDb` and `LiteDB` from both lists. Remove every name above plus
`SurrealDb.Embedded.InMemory` from the world bundle `assemblyNames`. Do not add
`FantaSim.App.Storage` to any collectible bundle project or assembly list.

- [ ] **Step 5: Regenerate staging artifacts and run the dual-copy gate**

Run the existing generators/staging tasks; do not hand-edit generated manifests or `.deps.json`
files.

Run in this order:

```bash
dotnet build project/hosts/complete-app/complete-app.csproj
task bundle:world:build
task bundle:common:build
python3 -m unittest discover -s tools/bundles -p "test_*.py"
python3 tools/bundles/stage_bundle.py --check-dual
```

Expected: world manifest/deps contain no Storage/Surreal runtime implementation or LiteDB;
common manifest contains Storage/Surreal runtime exactly once; dual-copy audit reports
`no dual copies; bundle/resident split is clean`.

- [ ] **Step 6: Run architecture tests and commit**

```bash
dotnet test project/tests/App.Architecture.Tests/App.Architecture.Tests.csproj
git add project/Directory.Packages.props project/plugins/App.Common project/plugins/App.Activity \
  project/hosts/complete-app project/tests/App.Architecture.Tests \
  project/bundles/common project/bundles/world
git commit -m "build(storage): make SurrealDB closure resident"
```

### Task 6: Extend the external two-process proof through App.Storage and Activity

**Files:**
- Modify: `project/tests/App.World.Tests/App.World.Tests.csproj`
- Modify: `project/tests/App.World.Tests/ExternalSurrealRotationRestartProofTests.cs`
- Modify: `tools/verify-durable-rotation-restart.sh`

**Interfaces:**
- Consumes: `StorageRuntime`, `KvTruthEventStore`, and the Activity service from prior tasks.
- Produces: one external-server restart receipt proving truth and Activity survive while legacy
  `.litedb` sentinels remain byte-identical.

- [ ] **Step 1: Write the RED external proof changes**

Add test-only project references to App.Storage and App.Activity. Replace
`WorldTruthEventStoreFactory` construction with:

```csharp
await using var storage = await StorageRuntime.OpenAsync(
    connectionString!, NullLoggerFactory.Instance, CancellationToken.None);
var truthStore = new KvTruthEventStore(storage.ConditionalKeyValueStore);
```

Start an Activity service over `storage.DocumentStore` and a minimal test bus. In write phase append
one exact marker with correlation id `g010-storage-foundation`, flush by awaiting Activity shutdown,
and retain existing rotation truth assertions. In read phase create a fresh Activity service,
assert exactly one recovered marker with that correlation id and the original name/outcome, then
shut it down. Keep the external connection assertion that rejects `mem://`.

- [ ] **Step 2: Run the proof and capture the expected RED result**

Run: `tools/verify-durable-rotation-restart.sh`

Expected before completing references/lifecycle wiring: FAIL at compile or missing Activity marker.

- [ ] **Step 3: Add legacy-file byte preservation to the harness**

Before server A starts, create two sentinel files under `$evidence_dir/legacy/` named
`activity-ledger.litedb` and `crust-cache.litedb`, record `shasum -a 256` and file sizes in
`legacy.before`, and never pass those paths to a database API. After process B and server B stop,
record `legacy.after`, compare with `cmp -s`, and fail on any difference. Add
`legacy_files_unchanged=1` to `sequence.status`. The architecture gate from Task 5 proves no
production code retains a LiteDB opener or path setting; this runtime sentinel proves the gate
does not mutate unrelated legacy files.

- [ ] **Step 4: Run the GREEN two-process proof and commit**

```bash
FANTASIM_ROTATION_RESTART_KEEP_EVIDENCE=1 tools/verify-durable-rotation-restart.sh
git add project/tests/App.World.Tests tools/verify-durable-rotation-restart.sh
git commit -m "test(storage): prove Surreal Activity restart"
```

Expected: distinct Surreal server PIDs, write/read receipts, recovered rotation and Activity
marker, unchanged legacy hashes, and `proof_complete=1`.

### Task 7: Run integration gates and deposit the foundation conclusion

**Files:**
- Create: `vault/handover/2026-07-15-g010-storage-foundation-handover.md`
- Modify: `vault/plans/2026-07-15-g010-storage-foundation-plan.md` checkboxes only after evidence exists

**Interfaces:**
- Consumes: the complete atomic packet.
- Produces: reviewable evidence and a session conclusion; no later G-010 identity/UI work is
  implemented in this packet.

- [ ] **Step 1: Run focused suites**

```bash
dotnet test project/tests/App.Storage.Tests/App.Storage.Tests.csproj
dotnet test project/tests/App.Activity.Tests/App.Activity.Tests.csproj
dotnet test project/tests/App.Common.Tests/App.Common.Tests.csproj
dotnet test project/tests/App.World.Tests/App.World.Tests.csproj \
  --filter "WorldServiceTruthStoreTests|CrustProductPersistenceTests|ExternalSurrealRotationRestartProofTests"
dotnet test project/tests/App.Architecture.Tests/App.Architecture.Tests.csproj
dotnet test project/tests/App.Resource.Tests/App.Resource.Tests.csproj \
  --filter "ReloadCollectionTests|ReloadPolicyGateTests|SharedStjCachePurgeTests"
```

Expected: every focused suite passes; the external fact explicitly reports its no-op only when its
phase environment variable is absent, while Task 6 supplies the real non-no-op proof.

- [ ] **Step 2: Run the full suite in both dependency modes**

```bash
dotnet test project/FantaSim.sln -p:UseProjectReferences=true
dotnet test project/FantaSim.sln -p:UseProjectReferences=false
```

Expected: both full runs pass. A known nondeterministic filmstrip failure is not waived; diagnose
and disclose it separately without rerunning until green.

- [ ] **Step 3: Re-run packaging and exported desktop build through UnifyBuild**

```bash
dotnet tool restore
task bundle:world:build
task bundle:common:build
task bundle:stagetool:test
task build:godot:desktop
```

Expected: common/world staging succeeds, dual-copy audit is clean, and the exported desktop app
contains one resident Storage/Surreal closure with no LiteDB or embedded-provider asset.

- [ ] **Step 4: Run static dependency absence checks**

```bash
if rg -n "LiteDB|UnifyStorage.Runtime.LiteDb|SurrealDb.Embedded.InMemory" project \
  --glob '!**/bin/**' --glob '!**/obj/**'; then exit 1; fi
if rg -n "SurrealDb.Net|UnifyStorage.Runtime.SurrealDb|UnifyStorageBackends" \
  project/plugins/App.World --glob '!**/bin/**' --glob '!**/obj/**'; then exit 1; fi
```

Expected: both commands produce no matches and exit successfully through the enclosing `if` logic.

- [ ] **Step 5: Write the conclusion deposit**

The handover records:

- the approved T1/T3 ownership boundary and exact package versions;
- every commit in this packet;
- RED and GREEN command results with test counts;
- external restart evidence directory, two server PIDs, Activity marker, and legacy hashes;
- common/world manifest assembly lists and dual-copy result;
- exported artifact path and the result of the resident-closure inspection;
- any negative result or nondeterministic failure without converting a retry into evidence;
- explicit remaining G-010 work: exact selection, truth identity/digest binding, two-world cache/UI
  separation, successor/legacy streams, UI epoch switching, changed-PCK window gate, and final ALC
  collection in the integrated end gate.

- [ ] **Step 6: Commit the evidence deposit**

```bash
git add vault/handover/2026-07-15-g010-storage-foundation-handover.md \
  vault/plans/2026-07-15-g010-storage-foundation-plan.md
git commit -m "docs(storage): deposit G-010 foundation evidence"
```

- [ ] **Step 7: Hand the complete packet to the lead reviewer**

Provide the worktree path, ordered commit list, `git diff <base>..HEAD --stat`, all verifier
receipts, and any unresolved failure. Do not merge, cherry-pick, push, or start the later parallel
G-010 packets; the lead session owns review and integration.
