using System;
using System.Collections.Generic;
using System.Linq;
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
    JsonObject Payload);

/// <summary>World-specific execution projection over the generic graph executor.</summary>
public sealed record WorldGenerationGraphRunOutput(
    JsonObject Sink,
    IReadOnlyList<WorldGenerationGraphProduct> Products,
    SphereHandoff? SphereHandoff);

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
                        result.DeepClone().AsObject()));
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

        return new WorldGenerationGraphRunOutput(sink, products, sphereHandoff);
    }

    public static JsonObject ToCommandResult(WorldGenerationGraphRunOutput run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var result = run.Sink.DeepClone().AsObject();
        if (run.Products.Count > 0)
        {
            var products = new JsonArray();
            foreach (var product in run.Products)
            {
                products.Add(new JsonObject
                {
                    ["nodeId"] = product.NodeId,
                    ["functionId"] = product.FunctionId,
                    ["productAddress"] = product.ProductAddress,
                });
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
