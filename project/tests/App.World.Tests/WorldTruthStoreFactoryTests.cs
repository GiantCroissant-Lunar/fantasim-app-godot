#if USE_PROJECT_REFERENCES
using FantaSim.App.World.Services;
using FantaSim.World.TruthStream;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using TimeDete.Time.Primitives;
using Xunit;
using ConfigService = CrosscutFoundation.Config.IService;

namespace FantaSim.App.World.Tests;

public sealed class WorldTruthStoreFactoryTests
{
    private static readonly TruthStreamIdentity Stream = new("app", "main", 0, "world", "default");

    [Fact]
    public void Options_default_to_in_memory_when_config_is_missing()
    {
        var options = WorldTruthStoreOptions.FromConfig(config: null);

        Assert.Equal(WorldTruthStoreBackend.InMemory, options.Backend);
        Assert.Null(options.ConnectionString);
    }

    [Fact]
    public void Options_require_connection_string_when_surrealdb_is_enabled()
    {
        var config = new TestConfig(("world:truthStore:backend", "surrealdb"));

        var ex = Assert.Throws<InvalidOperationException>(() => WorldTruthStoreOptions.FromConfig(config));

        Assert.Contains("world:truthStore:connectionString", ex.Message);
    }

    [Fact]
    public async Task Create_surrealdb_store_appends_and_reads_through_kv_store()
    {
        var ns = $"fantasim_app_{Guid.NewGuid():N}";
        var options = new WorldTruthStoreOptions(
            WorldTruthStoreBackend.SurrealDb,
            $"Endpoint=mem://;Namespace={ns};Database=world");

        using var store = WorldTruthEventStoreFactory.Create(options);
        var head = await store.EventStore.AppendAsync(Stream, new ITruthEventDraft[]
        {
            new Draft(Stream, "world.generation", new byte[] { 1 }, new CanonicalTick(1)),
        });

        var persistedHead = await store.EventStore.GetHeadAsync(Stream);
        var events = new List<ITruthEvent>();
        await foreach (var evt in store.EventStore.ReadAsync(Stream))
            events.Add(evt);

        Assert.Equal(0, head.Sequence);
        Assert.Equal(head.Sequence, persistedHead!.Value.Sequence);
        Assert.Single(events);
        Assert.Equal("world.generation", events[0].EventType);
    }

    private sealed record Draft(
        TruthStreamIdentity Stream,
        string EventType,
        ReadOnlyMemory<byte> Payload,
        CanonicalTick Tick) : ITruthEventDraft;

    private sealed class TestConfig : ConfigService
    {
        private readonly Dictionary<string, string?> _values;

        public TestConfig(params (string Key, string? Value)[] values)
        {
            _values = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        public IConfigurationRoot Root => throw new NotSupportedException();

        public string? Get(string key)
            => _values.TryGetValue(key, out var value) ? value : null;

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
#endif
