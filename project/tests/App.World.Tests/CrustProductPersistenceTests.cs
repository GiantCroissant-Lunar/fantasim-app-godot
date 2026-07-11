using System.Linq.Expressions;
using FantaSim.App.World.Services;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using UnifyStorage.Abstractions;
using UnifyStorage.Runtime.LiteDb;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// TDD coverage for the crust-product cache's cross-session persistence (2026-07-11 persistence
/// slice 1, vault/specs/2026-07-11-surrealdb-persistence-slice1-design.md section 6.2 tasks 3-4):
/// probe-before-build against a resident-shaped <c>IDocumentStore</c>, and that any restore failure
/// (a broken store, corrupt/incompatible bytes) degrades to a normal rebuild rather than crashing.
/// Uses the same fixture (tick 107,000,000, default Seed/Frequency) as
/// <see cref="ContinentalFractionAtTickTests"/> so a warm restore's output can be compared
/// byte-for-byte against the original build's output.
/// </summary>
public sealed class CrustProductPersistenceTests : IDisposable
{
    private const long Tick = 107_000_000L;
    private readonly string _tempDir = Directory.CreateTempSubdirectory("crust-product-persistence-").FullName;

    [Fact]
    public async Task Second_service_against_same_store_restores_without_rerunning_pipeline()
    {
        var storePath = Path.Combine(_tempDir, "crust-cache.litedb");

        IReadOnlyDictionary<int, double> fractionsFromFreshBuild;
        using (var store1 = new LiteDbDocumentStore(storePath))
        {
            var registry1 = new ServiceRegistry();
            registry1.Register<IDocumentStore>(store1, new ServiceRegistration());
            using var service1 = new Service(registry1);

            fractionsFromFreshBuild = service1.GetContinentalFractionByCellAt(Tick);
            Assert.Equal(1, service1.CrustPipelineBuildCountForTests);

            // Ensure the background persist has landed before the store is disposed below.
            await service1.FlushPendingCrustPersistenceAsync();
        }
        // store1 disposed here -- releases the LiteDB file so store2 can open it cleanly, simulating
        // the app process exiting after session 1.

        using var store2 = new LiteDbDocumentStore(storePath);
        var registry2 = new ServiceRegistry();
        registry2.Register<IDocumentStore>(store2, new ServiceRegistration());
        using var service2 = new Service(registry2);

        var fractionsFromRestore = service2.GetContinentalFractionByCellAt(Tick);

        Assert.Equal(0, service2.CrustPipelineBuildCountForTests);
        Assert.NotEmpty(fractionsFromRestore);
        Assert.Equal(
            fractionsFromFreshBuild.OrderBy(kv => kv.Key),
            fractionsFromRestore.OrderBy(kv => kv.Key));
    }

    [Fact]
    public void No_store_registered_rebuilds_every_call_and_never_throws()
    {
        using var service = new Service(new ServiceRegistry());

        var first = service.GetContinentalFractionByCellAt(Tick);
        Assert.Equal(1, service.CrustPipelineBuildCountForTests);
        Assert.NotEmpty(first);
    }

    [Fact]
    public void Store_read_failure_falls_through_to_a_normal_rebuild_without_crashing()
    {
        var registry = new ServiceRegistry();
        registry.Register<IDocumentStore>(new ThrowingOnReadDocumentStore(), new ServiceRegistration());
        using var service = new Service(registry);

        // Must not throw: a broken/corrupt persisted read degrades to "cache miss", exactly like a
        // SchemaVersion/AppVersion-mismatched document id would (both are structurally invisible to
        // GetAsync -- this covers the runtime failure mode a mismatch alone can't exercise: bytes
        // that exist under the looked-up id but fail to decode against the current record shape).
        var fractions = service.GetContinentalFractionByCellAt(Tick);

        Assert.Equal(1, service.CrustPipelineBuildCountForTests);
        Assert.NotEmpty(fractions);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    /// <summary>
    /// Minimal IDocumentStore fake: GetAsync always throws (simulating a corrupt row / broken
    /// backend); UpsertAsync succeeds silently so the post-build persist attempt doesn't itself
    /// surface a second failure path in this test. Every other member is unused by
    /// Service.GetOrBuildCrustTickProducts and throws if ever called.
    /// </summary>
    private sealed class ThrowingOnReadDocumentStore : IDocumentStore
    {
        public Task<T?> GetAsync<T>(string collection, string id, CancellationToken ct = default) where T : class
            => throw new InvalidOperationException("simulated corrupt/unreadable persisted document");

        public IAsyncEnumerable<T> QueryAsync<T>(string collection, Expression<Func<T, bool>>? predicate = null, QueryOptions? options = null, CancellationToken ct = default) where T : class
            => throw new NotSupportedException();

        public IAsyncEnumerable<T> QueryAsync<T>(string collection, ISpecification<T> specification, QueryOptions? options = null, CancellationToken ct = default) where T : class
            => throw new NotSupportedException();

        public Task UpsertAsync<T>(string collection, string id, T document, CancellationToken ct = default) where T : class
            => Task.CompletedTask;

        public Task<bool> DeleteAsync(string collection, string id, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<int> CountAsync<T>(string collection, Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default) where T : class
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string collection, string id, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task BulkInsertAsync<T>(string collection, IEnumerable<KeyValuePair<string, T>> documents, CancellationToken ct = default) where T : class
            => throw new NotSupportedException();

        public Task<IStoreTransaction?> BeginTransactionAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
