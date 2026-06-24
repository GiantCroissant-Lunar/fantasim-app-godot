#if USE_PROJECT_REFERENCES
using FantaSim.App.World.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using Xunit;
using ConfigService = CrosscutFoundation.Config.IService;

namespace FantaSim.App.World.Tests;

public sealed class WorldServiceTruthStoreTests
{
    [Fact]
    public void Service_requires_actor_system_when_surrealdb_truth_store_is_enabled()
    {
        var registry = NewRegistry(
            ("world:truthStore:backend", "surrealdb"),
            ("world:truthStore:connectionString", $"Endpoint=mem://;Namespace=app_{Guid.NewGuid():N};Database=world"));

        var ex = Assert.Throws<InvalidOperationException>(() => new Service(registry));

        Assert.Contains("ActorSystem", ex.Message);
    }

    private static IRegistry NewRegistry(params (string Key, string? Value)[] values)
    {
        var registry = new ServiceRegistry();
        registry.Register<ConfigService>(
            new TestConfig(values),
            new ServiceRegistration { Tags = new[] { "config" }, Description = "Test config" });
        return registry;
    }

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
