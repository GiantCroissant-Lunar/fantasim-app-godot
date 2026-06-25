#if USE_PROJECT_REFERENCES
using Akka.Actor;
#endif
using System.Globalization;
using System.Text.Json;
using FantaSim.App.World.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceArchi.Contracts;

namespace FantaSim.App.World.Services;

/// <summary>
/// T3 orchestrator for the App.World service. Implements <see cref="IService"/> by composing
/// the sibling fantasim-world field/catalog/reducer stack at construction and mapping its pure
/// types onto the app-side DTOs defined in <c>FantaSim.App.World.Dto</c>.
/// </summary>
/// <remarks>
/// World-lib types (FantaSim.World.Fields.*, World.TruthStream.*, World.Parameters.*) stay
/// inside this T3 assembly and never leak through the T1 contract surface. The composition is
/// only compiled when <c>UseProjectReferences=true</c> (the local lunar-horse feed does not yet
/// carry FantaSim.World.* packages); the default build path uses a no-op runtime so the solution
/// stays green without the sibling repo wired in.
/// </remarks>
public sealed class Service : IService, IDisposable
{
    private const string GenerationGraphSource = "world-generation.graph";

    private readonly IRegistry _registry;
    private readonly IWorldRuntime _runtime;
    private readonly ILogger _logger;
#if USE_PROJECT_REFERENCES
    private readonly WorldTruthEventStoreHandle _truthStoreHandle;
#endif
    private readonly List<Action<WorldGenerationChangedEvent>> _subscribers = new();
    private readonly object _subscribersGate = new();
    private readonly object _generationProductsGate = new();
    private WorldGenerationProductsView _generationProducts =
        new(0, Array.Empty<string>(), 0L);
    private Exception? _lastSubscriberError;
    private bool _disposed;

#if USE_PROJECT_REFERENCES
    public Service(IRegistry registry, ActorSystem? actorSystem = null)
#else
    public Service(IRegistry registry)
#endif
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
#if USE_PROJECT_REFERENCES
        var config = registry.TryGet<CrosscutFoundation.Config.IService>();
        var truthStoreOptions = WorldTruthStoreOptions.FromConfig(config);
        if (truthStoreOptions.Backend == WorldTruthStoreBackend.SurrealDb && actorSystem is null)
        {
            throw new InvalidOperationException(
                "SurrealDB world truth store requires an ActorSystem so writes go through the single writer actor.");
        }

        var truthStoreHandle = WorldTruthEventStoreFactory.Create(truthStoreOptions);
        ITruthEventWriter? truthWriter = null;
        try
        {
            truthWriter = actorSystem is null
                ? new DirectTruthEventWriter(truthStoreHandle.EventStore)
                : ActorTruthEventWriter.Start(
                    actorSystem,
                    truthStoreHandle.EventStore,
                    actorName: NewTruthWriterActorName());
            _runtime = WorldRuntimeFactory.Create(registry, truthWriter);
            _truthStoreHandle = truthStoreHandle;
            truthWriter = null;
        }
        catch
        {
            truthWriter?.Dispose();
            truthStoreHandle.Dispose();
            throw;
        }
#else
        _runtime = WorldRuntimeFactory.Create(registry);
#endif
        var loggerFactory = registry.TryGet<ILoggerFactory>();
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<Service>();
    }

    /// <summary>
    /// Last exception thrown by a subscriber during <see cref="EmitGenerationChanged"/>, or null
    /// when no subscriber has faulted since the last successful emit. Populated only when no
    /// ILoggerFactory is registered in the registry (the registry-resolved logger path logs the
    /// fault directly, making this field unnecessary in that case).
    /// </summary>
    public Exception? LastSubscriberError => _lastSubscriberError;

    public WorldOverview GetOverviewAsync()
        => _runtime.GetOverview();

    public WorldFieldValues GetFieldValuesAsync(WorldFieldValuesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _runtime.GetFieldValues(request);
    }

    public WorldScalarFieldValues GetScalarFieldValuesAsync(WorldScalarFieldValuesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _runtime.GetScalarFieldValues(request);
    }

    public WorldRenderSnapshot GetRenderSnapshotAsync()
        => _runtime.GetRenderSnapshot();

    public WorldGenerationProductsView GetGenerationProductsAsync()
    {
        lock (_generationProductsGate)
            return _generationProducts;
    }

    public PlanetPresentationDocument GetPlanetPresentationAsync()
    {
        var overview = _runtime.GetOverview();
        var renderSnapshot = _runtime.GetRenderSnapshot();
        WorldGenerationProductsView products;
        lock (_generationProductsGate)
            products = _generationProducts;

        var layers = products.Products
            .Select(ToPlanetLayer)
            .Where(layer => layer is not null)
            .Cast<PlanetPresentationLayer>()
            .ToArray();

        return new PlanetPresentationDocument(
            PlanetId: overview.WorldId,
            SourceWorldId: overview.WorldId,
            ReferenceTick: products.ReferenceTick,
            Revision: products.GraphRevision,
            Layers: layers,
            RenderEntities: renderSnapshot.Entities ?? Array.Empty<RenderEntityDto>());
    }

    public WorldGenerationResult RunGenerationAsync(WorldGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = _runtime.RunGeneration(request);
        if (result.Success && IsGenerationGraphRequest(request))
            UpdateGenerationProducts(request);
        EmitGenerationChanged(new WorldGenerationChangedEvent(result.ResultWorldId, "generation", request.GenerationSpec));
        return result;
    }

    public void SubscribeGenerationChanged(Action<WorldGenerationChangedEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_subscribersGate) _subscribers.Add(callback);
    }

    private void EmitGenerationChanged(WorldGenerationChangedEvent evt)
    {
        Action<WorldGenerationChangedEvent>[] snapshot;
        lock (_subscribersGate) snapshot = _subscribers.ToArray();
        foreach (var cb in snapshot)
        {
            try
            {
                cb(evt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GenerationChanged subscriber faulted.");
                _lastSubscriberError = ex;
            }
        }
    }

    private void UpdateGenerationProducts(WorldGenerationRequest request)
    {
        lock (_generationProductsGate)
            _generationProducts = ToProductsView(request, _generationProducts);
    }

    private static WorldGenerationProductsView ToProductsView(
        WorldGenerationRequest request,
        WorldGenerationProductsView previous)
    {
        IReadOnlyDictionary<string, object> parameters =
            request.Parameters ?? new Dictionary<string, object>(StringComparer.Ordinal);
        var products = ReadStringArray(parameters, "productAddresses");
        var graphRevision = ReadInt(parameters, "graphRevision", previous.GraphRevision);
        var referenceTick = ReadLong(
            parameters,
            "canonicalTick",
            ReadLong(parameters, "tick", 0L));
        // TODO(cache): repopulate when a cache-tick source exists
        // var cachedTicks = ReadLongArray(parameters, "cachedTicks");

        return new WorldGenerationProductsView(graphRevision, products, referenceTick);
    }

    private static bool IsGenerationGraphRequest(WorldGenerationRequest request)
        => request.Parameters is not null
           && request.Parameters.TryGetValue("source", out var source)
           && string.Equals(source?.ToString(), GenerationGraphSource, StringComparison.Ordinal);

    private static PlanetPresentationLayer? ToPlanetLayer(string productAddress)
    {
        if (!WorldGenerationProductAddress.TryParse(productAddress, out var address) || address is null)
            return null;

        var (regimeId, layerId) = SplitRegimeLayer(address);
        return new PlanetPresentationLayer(
            LayerId: layerId,
            RegimeId: regimeId,
            Variant: address.Variant,
            Branch: address.Branch,
            ProductDomain: address.Domain,
            ProductName: address.Product,
            ProductTick: address.Tick,
            ProductAddress: address.ToPath());
    }

    private static (string RegimeId, string LayerId) SplitRegimeLayer(WorldGenerationProductAddress address)
    {
        var separator = address.Product.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || separator == address.Product.Length - 1)
            return (string.Empty, $"{address.Domain}.{address.Product}");

        return (
            RegimeId: address.Product[..separator],
            LayerId: $"{address.Domain}.{address.Product[(separator + 1)..]}");
    }

#if USE_PROJECT_REFERENCES
    private static string NewTruthWriterActorName()
        => $"world-truth-writer-{Guid.NewGuid():N}";
#endif

    private static int ReadInt(IReadOnlyDictionary<string, object> parameters, string key, int fallback)
    {
        if (!parameters.TryGetValue(key, out var value) || !TryReadLong(value, out var parsed))
            return fallback;
        return parsed is >= int.MinValue and <= int.MaxValue ? (int)parsed : fallback;
    }

    private static long ReadLong(IReadOnlyDictionary<string, object> parameters, string key, long fallback)
        => parameters.TryGetValue(key, out var value) && TryReadLong(value, out var parsed)
            ? parsed
            : fallback;

    private static bool TryReadLong(object? value, out long result)
    {
        if (value is long longValue)
        {
            result = longValue;
            return true;
        }

        if (value is int intValue)
        {
            result = intValue;
            return true;
        }

        if (value is string text)
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

        if (value is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Number && json.TryGetInt64(out result))
                return true;
            if (json.ValueKind == JsonValueKind.String)
                return long.TryParse(json.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        result = 0L;
        return false;
    }

    private static string[] ReadStringArray(IReadOnlyDictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return Array.Empty<string>();

        return value switch
        {
            string[] values => NormalizeStrings(values),
            IEnumerable<string> values => NormalizeStrings(values),
            JsonElement json => ReadStringArray(json),
            string text => ReadStringArray(text),
            _ => Array.Empty<string>(),
        };
    }

    private static string[] ReadStringArray(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        if (TryDeserializeArray<string>(text, out var values))
            return NormalizeStrings(values);

        return new[] { text };
    }

    private static string[] ReadStringArray(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            return NormalizeStrings(value.EnumerateArray().Select(item =>
                item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString()));
        }

        return value.ValueKind == JsonValueKind.String
            ? ReadStringArray(value.GetString() ?? string.Empty)
            : Array.Empty<string>();
    }

    private static long[] ReadLongArray(IReadOnlyDictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return Array.Empty<long>();

        return value switch
        {
            long[] values => values,
            int[] values => values.Select(item => (long)item).ToArray(),
            IEnumerable<long> values => values.ToArray(),
            IEnumerable<int> values => values.Select(item => (long)item).ToArray(),
            JsonElement json => ReadLongArray(json),
            string text => ReadLongArray(text),
            _ => Array.Empty<long>(),
        };
    }

    private static long[] ReadLongArray(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<long>();

        if (TryDeserializeArray<long>(text, out var values))
            return values;

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? new[] { parsed }
            : Array.Empty<long>();
    }

    private static long[] ReadLongArray(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Select(item => TryReadLong(item, out var parsed) ? parsed : (long?)null)
                .Where(item => item.HasValue)
                .Select(item => item!.Value)
                .ToArray();
        }

        if (TryReadLong(value, out var single))
            return new[] { single };

        return Array.Empty<long>();
    }

    private static bool TryReadLong(JsonElement value, out long result)
        => TryReadLong((object)value, out result);

    private static bool TryDeserializeArray<T>(string text, out T[] values)
    {
        values = Array.Empty<T>();
        if (!text.TrimStart().StartsWith("[", StringComparison.Ordinal))
            return false;

        try
        {
            values = JsonSerializer.Deserialize<T[]>(text) ?? Array.Empty<T>();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string[] NormalizeStrings(IEnumerable<string?> values)
    {
        var result = new List<string>();
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                result.Add(value);
        }

        return result.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _runtime.Dispose();
        }
        finally
        {
#if USE_PROJECT_REFERENCES
            _truthStoreHandle.Dispose();
#endif
        }
    }
}
