#if USE_PROJECT_REFERENCES
using System.Data.Common;
using FantaSim.App.World.Storage.Generated.SurrealDb;
using FantaSim.World.TruthStream;
using FantaSim.World.TruthStream.Core;
using Microsoft.Extensions.DependencyInjection;
using SurrealDb.Net;
using UnifyStorage.Runtime.SurrealDb;
using ConfigService = CrosscutFoundation.Config.IService;

namespace FantaSim.App.World.Services;

internal enum WorldTruthStoreBackend
{
    InMemory,
    SurrealDb,
}

internal sealed record WorldTruthStoreOptions(
    WorldTruthStoreBackend Backend,
    string? ConnectionString)
{
    private const string BackendKey = "world:truthStore:backend";
    private const string ConnectionStringKey = "world:truthStore:connectionString";

    public static WorldTruthStoreOptions FromConfig(ConfigService? config)
    {
        var backendText = config?.Get(BackendKey);
        var backend = ParseBackend(backendText);
        var connectionString = config?.Get(ConnectionStringKey);

        if (backend == WorldTruthStoreBackend.SurrealDb && string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Configuration key '{ConnectionStringKey}' is required when '{BackendKey}' is 'surrealdb'.");
        }

        return new WorldTruthStoreOptions(backend, string.IsNullOrWhiteSpace(connectionString) ? null : connectionString);
    }

    private static WorldTruthStoreBackend ParseBackend(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return WorldTruthStoreBackend.InMemory;

        return value.Trim().ToLowerInvariant() switch
        {
            "inmemory" or "in-memory" or "memory" => WorldTruthStoreBackend.InMemory,
            "surrealdb" or "surreal" => WorldTruthStoreBackend.SurrealDb,
            _ => throw new InvalidOperationException(
                $"Unsupported world truth store backend '{value}'. Expected 'inmemory' or 'surrealdb'."),
        };
    }
}

internal sealed class WorldTruthEventStoreHandle : IDisposable
{
    private readonly Action? _dispose;
    private int _disposed;

    public WorldTruthEventStoreHandle(ITruthEventStore eventStore, Action? dispose = null)
    {
        EventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _dispose = dispose;
    }

    public ITruthEventStore EventStore { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            if (EventStore is IDisposable disposable)
                disposable.Dispose();
        }
        finally
        {
            _dispose?.Invoke();
        }
    }
}

internal static class WorldTruthEventStoreFactory
{
    public static WorldTruthEventStoreHandle Create(WorldTruthStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Backend switch
        {
            WorldTruthStoreBackend.InMemory => new WorldTruthEventStoreHandle(new InMemoryTruthEventStore()),
            WorldTruthStoreBackend.SurrealDb => CreateSurrealDb(options.ConnectionString),
            _ => throw new InvalidOperationException($"Unsupported world truth store backend '{options.Backend}'."),
        };
    }

    private static WorldTruthEventStoreHandle CreateSurrealDb(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("SurrealDB truth store requires a non-empty connection string.");

        var settings = ParseConnectionString(connectionString);
        var ns = Require(settings, "Namespace");
        var db = Require(settings, "Database");
        var endpoint = Get(settings, "Endpoint");

        var services = new ServiceCollection();
        var builder = services.AddSurrealDbStorage(connectionString);
        if (endpoint is not null && endpoint.StartsWith("mem://", StringComparison.OrdinalIgnoreCase))
            builder.AddInMemoryProvider();

        var provider = services.BuildServiceProvider();
        AsyncServiceScope scope = default;
        var scopeCreated = false;
        try
        {
            scope = provider.CreateAsyncScope();
            scopeCreated = true;

            var session = scope.ServiceProvider.GetRequiredService<ISurrealDbSession>();
            session.Connect().GetAwaiter().GetResult();
            session.Use(ns, db).GetAwaiter().GetResult();

            var store = new KvTruthEventStore(new SurrealDbKeyValueStore(session));
            return new WorldTruthEventStoreHandle(store, () => DisposeSurrealDb(scope, provider));
        }
        catch
        {
            if (scopeCreated)
                scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    private static IReadOnlyDictionary<string, string?> ParseConnectionString(string connectionString)
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString,
        };

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in builder.Keys.Cast<string>())
            values[key] = builder[key]?.ToString();
        return values;
    }

    private static string Require(IReadOnlyDictionary<string, string?> settings, string key)
    {
        var value = Get(settings, key);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"SurrealDB truth store connection string requires '{key}'.");
        return value;
    }

    private static string? Get(IReadOnlyDictionary<string, string?> settings, string key)
        => settings.TryGetValue(key, out var value) ? value : null;

    private static void DisposeSurrealDb(AsyncServiceScope scope, ServiceProvider provider)
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
        provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
#endif
