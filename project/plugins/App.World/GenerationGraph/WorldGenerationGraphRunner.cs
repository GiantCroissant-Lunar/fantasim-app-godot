using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.NodeGraph;
using FantaSim.App.World.Composition;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>One product-bearing node output captured during a world-generation graph run.</summary>
public sealed record WorldGenerationGraphProduct(
    string NodeId,
    string FunctionId,
    string ProductAddress,
    JsonObject Payload,
    WorldGenerationGraphExecutionScopeKey? ExecutionScopeKey = null);

/// <summary>World-specific execution projection over the generic graph executor.</summary>
public sealed record WorldGenerationGraphRunOutput(
    JsonObject Sink,
    IReadOnlyList<WorldGenerationGraphProduct> Products,
    SphereHandoff? SphereHandoff,
    WorldGenerationGraphExecutionScopeKey? ExecutionScopeKey = null);

/// <summary>
/// Command payload for executing a graph with run-scoped parameters such as the active canonical tick.
/// Legacy callers may still send a raw <see cref="GraphDocument"/>.
/// </summary>
public sealed record WorldGenerationGraphExecutionPayload(
    GraphDocument Graph,
    JsonObject? SharedParams = null,
    WorldGenerationGraphExecutionScopeKey? ExecutionScopeKey = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(
        GraphDocument graph,
        JsonObject? sharedParams = null,
        WorldGenerationGraphExecutionScopeKey? executionScopeKey = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var payload = new JsonObject
        {
            ["graph"] = JsonSerializer.SerializeToNode(graph, JsonOptions),
        };
        if (sharedParams is not null)
            payload["sharedParams"] = sharedParams.DeepClone();
        if (executionScopeKey is not null)
            payload["executionScopeKey"] = JsonSerializer.SerializeToNode(executionScopeKey, JsonOptions);

        return payload.ToJsonString();
    }

    public static WorldGenerationGraphExecutionPayload Deserialize(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new ArgumentException("World generation graph payload is required.", nameof(payloadJson));

        var node = JsonNode.Parse(payloadJson)
            ?? throw new InvalidOperationException("World generation graph payload could not be parsed.");

        if (node is JsonObject wrapper
            && wrapper.TryGetPropertyValue("graph", out var graphNode)
            && graphNode is not null)
        {
            var graph = graphNode.Deserialize<GraphDocument>(JsonOptions)
                ?? throw new InvalidOperationException("World generation graph payload could not be deserialized.");
            var sharedParams = wrapper.TryGetPropertyValue("sharedParams", out var sharedNode)
                && sharedNode is JsonObject sharedObject
                    ? sharedObject.DeepClone().AsObject()
                    : null;
            var executionScopeKey = wrapper.TryGetPropertyValue("executionScopeKey", out var scopeNode)
                && scopeNode is not null
                    ? scopeNode.Deserialize<WorldGenerationGraphExecutionScopeKey>(JsonOptions)
                    : null;
            return new WorldGenerationGraphExecutionPayload(graph, sharedParams, executionScopeKey);
        }

        var legacyGraph = node.Deserialize<GraphDocument>(JsonOptions)
            ?? throw new InvalidOperationException("World generation graph payload could not be deserialized.");
        return new WorldGenerationGraphExecutionPayload(legacyGraph);
    }
}

/// <summary>
/// Runs compiled world-generation graphs and captures world product metadata from node outputs.
/// The generic executor stays domain-free; this runner owns the world interpretation of product
/// addresses and body-to-sphere handoffs.
/// </summary>
public sealed class WorldGenerationGraphRunner
{
    private readonly IReadOnlyList<INodeFunctionProvider> _providers;

    public WorldGenerationGraphRunner(IEnumerable<INodeFunctionProvider> providers)
    {
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));
    }

    public async Task<WorldGenerationGraphRunOutput> RunAsync(
        GraphDocument graph,
        JsonObject? sharedParams = null,
        WorldGenerationGraphExecutionScopeKey? executionScopeKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var products = new List<WorldGenerationGraphProduct>();
        SphereHandoff? sphereHandoff = null;
        var hooks = new RunContext
        {
            AfterNode = (node, _, result, _) =>
            {
                if (TryReadProductAddress(result) is { } productAddress)
                {
                    products.Add(new WorldGenerationGraphProduct(
                        node.Id,
                        node.FunctionId,
                        productAddress,
                        result.DeepClone().AsObject(),
                        executionScopeKey));
                }

                if (result.TryGetPropertyValue("sphereHandoff", out var handoffNode))
                {
                    if (handoffNode is JsonObject handoffJson)
                        sphereHandoff = SphereHandoff.FromJson(handoffJson);
                }

                return Task.CompletedTask;
            },
        };

        var sink = await new GraphExecutor(_providers, hooks)
            .ExecuteAsync(graph, sharedParams, cancellationToken)
            .ConfigureAwait(false);

        if (sphereHandoff is null
            && sink.TryGetPropertyValue("sphereHandoff", out var sinkHandoffNode))
        {
            if (sinkHandoffNode is JsonObject sinkHandoffJson)
                sphereHandoff = SphereHandoff.FromJson(sinkHandoffJson);
        }

        return new WorldGenerationGraphRunOutput(sink, products, sphereHandoff, executionScopeKey);
    }

    public static JsonObject ToCommandResult(WorldGenerationGraphRunOutput run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var result = run.Sink.DeepClone().AsObject();
        if (run.ExecutionScopeKey is not null)
            result["executionScopeKey"] = run.ExecutionScopeKey.ToCacheKey();

        if (run.Products.Count > 0)
        {
            var products = new JsonArray();
            foreach (var product in run.Products)
            {
                var productJson = new JsonObject
                {
                    ["nodeId"] = product.NodeId,
                    ["functionId"] = product.FunctionId,
                    ["productAddress"] = product.ProductAddress,
                };
                if (product.ExecutionScopeKey is not null)
                    productJson["executionScopeKey"] = product.ExecutionScopeKey.ToCacheKey();

                products.Add(productJson);
            }

            result["products"] = products;
        }

        return result;
    }

    private static string? TryReadProductAddress(JsonObject result)
    {
        if (!result.TryGetPropertyValue("productAddress", out var node) || node is null)
            return null;

        var value = node.GetValue<string>();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
