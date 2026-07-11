using FantaSim.App.Common.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using UnifyStorage.Abstractions;
using UnifyStorage.Runtime.LiteDb;
using Xunit;
using ConfigService = CrosscutFoundation.Config.IService;

namespace FantaSim.App.Common.Tests;

/// <summary>
/// TDD coverage for the resident crust-product cache store (2026-07-11 persistence slice 1,
/// vault/specs/2026-07-11-surrealdb-persistence-slice1-design.md section 3.1/3.2/6.2 task 2):
/// <see cref="ResidentPersistenceOptions"/> config parsing, <see cref="ResidentDocumentStoreFactory"/>
/// construction (never throws; degrades to null on failure/disable), and the "resolves via IRegistry
/// AND survives a simulated bundle reload" property the spec's task list calls out explicitly — proven
/// here at the IRegistry + on-disk level directly (not through the full <c>Bootstrap</c>, which would
/// otherwise write to the real user profile directory under its "enabled by default" resolved path).
/// </summary>
public sealed class ResidentDocumentStoreTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("resident-document-store-").FullName;

    [Fact]
    public void FromConfig_defaults_to_enabled_when_config_is_null()
    {
        var options = ResidentPersistenceOptions.FromConfig(config: null);

        Assert.True(options.CrustCacheEnabled);
        Assert.Equal("user://crust-cache.litedb", options.CrustCacheStorePath);
    }

    [Fact]
    public void FromConfig_respects_explicit_disable()
    {
        var config = new TestConfig(("persistence:crustCache:enabled", "false"));

        var options = ResidentPersistenceOptions.FromConfig(config);

        Assert.False(options.CrustCacheEnabled);
    }

    [Fact]
    public void FromConfig_respects_custom_path()
    {
        var config = new TestConfig(("persistence:crustCache:path", "user://custom-crust.litedb"));

        var options = ResidentPersistenceOptions.FromConfig(config);

        Assert.Equal("user://custom-crust.litedb", options.CrustCacheStorePath);
    }

    [Fact]
    public void ResolveStorePath_resolves_rooted_path_as_is()
    {
        var rooted = Path.Combine(_tempDir, "explicit.litedb");

        Assert.Equal(Path.GetFullPath(rooted), ResidentPersistenceOptions.ResolveStorePath(rooted));
    }

    [Fact]
    public void Factory_disabled_options_returns_no_store()
    {
        var options = new ResidentPersistenceOptions(CrustCacheEnabled: false, CrustCacheStorePath: "user://crust-cache.litedb");

        var (store, owned) = ResidentDocumentStoreFactory.Create(options, NullLogger.Instance);

        Assert.Null(store);
        Assert.Null(owned);
    }

    [Fact]
    public void Factory_enabled_options_constructs_a_working_store_at_the_configured_path()
    {
        var storePath = Path.Combine(_tempDir, "crust-cache.litedb");
        var options = new ResidentPersistenceOptions(CrustCacheEnabled: true, CrustCacheStorePath: storePath);

        var (store, owned) = ResidentDocumentStoreFactory.Create(options, NullLogger.Instance);
        try
        {
            Assert.NotNull(store);
            Assert.NotNull(owned);
            Assert.True(File.Exists(storePath));
        }
        finally
        {
            owned?.Dispose();
        }
    }

    [Fact]
    public async Task Store_registered_in_IRegistry_resolves_as_IDocumentStore()
    {
        var storePath = Path.Combine(_tempDir, "registry-resolve.litedb");
        var registry = new ServiceRegistry();
        using var store = new LiteDbDocumentStore(storePath);
        registry.Register<IDocumentStore>(store, new ServiceRegistration { Tags = new[] { "storage", "persistence" } });

        var resolved = registry.TryGet<IDocumentStore>();
        Assert.NotNull(resolved);

        await resolved!.UpsertAsync("probe", "id-1", new DocumentBlob(new byte[] { 1, 2, 3 }));
        var readBack = await resolved.GetAsync<DocumentBlob>("probe", "id-1");
        Assert.Equal(new byte[] { 1, 2, 3 }, readBack!.Data);
    }

    /// <summary>
    /// Proves residency (not bundle-scoping): a document written by ONE store instance is visible to
    /// a SECOND, independently-constructed store instance opened against the same on-disk path —
    /// exactly what "a bundle reload re-resolves IDocumentStore from the registry and still sees data
    /// written before the reload" reduces to once the store itself is resident and disk-backed.
    /// </summary>
    [Fact]
    public async Task Document_written_before_a_simulated_reload_is_visible_after_it()
    {
        var storePath = Path.Combine(_tempDir, "reload-survives.litedb");
        var registryBeforeReload = new ServiceRegistry();
        using (var storeBeforeReload = new LiteDbDocumentStore(storePath))
        {
            registryBeforeReload.Register<IDocumentStore>(storeBeforeReload, new ServiceRegistration());
            var resolvedBeforeReload = registryBeforeReload.TryGet<IDocumentStore>();
            await resolvedBeforeReload!.UpsertAsync("probe", "id-1", new DocumentBlob(new byte[] { 9, 8, 7 }));
        }
        // storeBeforeReload disposed here — simulates the bundle (and its captured registry reference)
        // being torn down.

        var registryAfterReload = new ServiceRegistry();
        using var storeAfterReload = new LiteDbDocumentStore(storePath);
        registryAfterReload.Register<IDocumentStore>(storeAfterReload, new ServiceRegistration());
        var resolvedAfterReload = registryAfterReload.TryGet<IDocumentStore>();

        var readBack = await resolvedAfterReload!.GetAsync<DocumentBlob>("probe", "id-1");
        Assert.NotNull(readBack);
        Assert.Equal(new byte[] { 9, 8, 7 }, readBack!.Data);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private sealed class TestConfig : ConfigService
    {
        private readonly Dictionary<string, string?> _values;

        public TestConfig(params (string Key, string? Value)[] values)
        {
            _values = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        public IConfigurationRoot Root => throw new NotSupportedException();

        public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();

        public IEnumerable<IConfigurationSection> GetChildren() => Array.Empty<IConfigurationSection>();

        public T GetValue<T>(string key, T defaultValue)
        {
            var value = Get(key);
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }

        public IChangeToken GetReloadToken() => throw new NotSupportedException();

        public void Reload()
        {
        }
    }
}
