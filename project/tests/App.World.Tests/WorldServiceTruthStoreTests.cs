using Akka.Actor;
using FantaSim.App.World.Dto;
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
    public async Task Service_runs_generation_with_surrealdb_truth_store_when_actor_system_is_supplied()
    {
        var ns = $"app_{Guid.NewGuid():N}";
        var registry = NewRegistry(
            ("world:truthStore:backend", "surrealdb"),
            ("world:truthStore:connectionString", $"Endpoint=mem://;Namespace={ns};Database=world"));

        var actorSystem = ActorSystem.Create($"world-truth-tests-{Guid.NewGuid():N}");
        try
        {
            using var service = new Service(registry, actorSystem);

            Assert.False(service.GetOverviewAsync().IsDirty);

            var result = service.RunGenerationAsync(new WorldGenerationRequest(
                WorldId: "test-world",
                GenerationSpec: "world.generate",
                Parameters: new Dictionary<string, object>(StringComparer.Ordinal)));

            Assert.True(result.Success);
            Assert.True(service.GetOverviewAsync().IsDirty);
        }
        finally
        {
            await actorSystem.Terminate();
        }
    }

    [Fact]
    public async Task Service_allows_multiple_surrealdb_instances_on_same_actor_system()
    {
        var actorSystem = ActorSystem.Create($"world-truth-tests-{Guid.NewGuid():N}");
        try
        {
            using var first = NewSurrealDbService(actorSystem);
            using var second = NewSurrealDbService(actorSystem);

            var firstResult = first.RunGenerationAsync(new WorldGenerationRequest(
                WorldId: "first-world",
                GenerationSpec: "world.generate:first",
                Parameters: new Dictionary<string, object>(StringComparer.Ordinal)));
            var secondResult = second.RunGenerationAsync(new WorldGenerationRequest(
                WorldId: "second-world",
                GenerationSpec: "world.generate:second",
                Parameters: new Dictionary<string, object>(StringComparer.Ordinal)));

            Assert.True(firstResult.Success);
            Assert.True(secondResult.Success);
            Assert.True(first.GetOverviewAsync().IsDirty);
            Assert.True(second.GetOverviewAsync().IsDirty);
        }
        finally
        {
            await actorSystem.Terminate();
        }
    }

    [Fact]
    public async Task Service_dispose_is_safe_after_actor_system_has_terminated()
    {
        var actorSystem = ActorSystem.Create($"world-truth-tests-{Guid.NewGuid():N}");
        var service = NewSurrealDbService(actorSystem);

        await actorSystem.Terminate();

        var ex = Record.Exception(service.Dispose);

        Assert.Null(ex);
        service.Dispose();
    }

    [Fact]
    public void Service_requires_actor_system_when_surrealdb_truth_store_is_enabled()
    {
        var registry = NewRegistry(
            ("world:truthStore:backend", "surrealdb"),
            ("world:truthStore:connectionString", $"Endpoint=mem://;Namespace=app_{Guid.NewGuid():N};Database=world"));

        var ex = Assert.Throws<InvalidOperationException>(() => new Service(registry));

        Assert.Contains("ActorSystem", ex.Message);
    }

    private static Service NewSurrealDbService(ActorSystem actorSystem)
    {
        var ns = $"app_{Guid.NewGuid():N}";
        return new Service(
            NewRegistry(
                ("world:truthStore:backend", "surrealdb"),
                ("world:truthStore:connectionString", $"Endpoint=mem://;Namespace={ns};Database=world")),
            actorSystem);
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
