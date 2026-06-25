using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.NodeGraph;

/// <summary>
/// General, domain-agnostic graph executor. Walks an executable <see cref="GraphDocument"/> in
/// topological order and runs each node by resolving its <see cref="GraphNode.FunctionId"/> to a
/// registered <see cref="INodeFunctionProvider"/>, threading each node's output fields into
/// downstream payloads per the data wires. Optional <see cref="RunContext"/> hooks let domains
/// preserve cross-node invariants (ordered commits, cache invalidation, completion events).
/// </summary>
/// <remarks>
/// Pure C#: no Godot, no network, no domain handlers. Fully testable with a fake
/// <see cref="INodeFunctionProvider"/>. This is the shared engine every axis (iii pipelines, World
/// generation recipes, a future VisualScript) drives; each axis contributes providers, not its own
/// executor.
/// </remarks>
public sealed class GraphExecutor
{
    private readonly IReadOnlyList<INodeFunctionProvider> _providers;
    private readonly RunContext? _hooks;

    public GraphExecutor(IEnumerable<INodeFunctionProvider> providers, RunContext? hooks = null)
    {
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));
        _hooks = hooks;
    }

    /// <summary>Run the graph. <paramref name="sharedParams"/> (e.g. job_id) are merged into every
    /// node's payload. Returns the sink node's result object.</summary>
    public async Task<JsonObject> ExecuteAsync(
        GraphDocument graph,
        JsonObject? sharedParams = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        try
        {
            if (_hooks?.BeforeRun is not null)
                await _hooks.BeforeRun(graph, cancellationToken).ConfigureAwait(false);

            var order = TopologicalOrder(graph);
            var incomingByNode = graph.Wires
                .Where(w => w.Kind == WireKind.Data)
                .GroupBy(w => w.ToNode, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<GraphWire>)g.ToList(), StringComparer.Ordinal);

            var outputs = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

            foreach (var node in order)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var payload = new JsonObject();
                if (sharedParams is not null)
                    foreach (var kv in sharedParams)
                        payload[kv.Key] = kv.Value?.DeepClone();
                foreach (var kv in node.Params)
                    payload[kv.Key] = kv.Value?.DeepClone();

                if (incomingByNode.TryGetValue(node.Id, out var incoming))
                {
                    foreach (var wire in incoming)
                    {
                        if (!outputs.TryGetValue(wire.FromNode, out var upstream))
                            throw new InvalidOperationException(
                                $"Wire source '{wire.FromNode}' has no result for node '{node.Id}'.");
                        payload[wire.ToPort] = upstream[wire.FromPort]?.DeepClone();
                    }
                }

                if (_hooks?.BeforeNode is not null)
                    await _hooks.BeforeNode(node, payload, cancellationToken).ConfigureAwait(false);

                JsonObject result;
                try
                {
                    var provider = ResolveProvider(node.FunctionId);
                    result = await provider
                        .InvokeAsync(node.FunctionId, payload, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (_hooks?.OnNodeFailed is not null)
                        await _hooks.OnNodeFailed(node, ex, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                if (_hooks?.AfterNode is not null)
                    await _hooks.AfterNode(node, payload, result, cancellationToken).ConfigureAwait(false);

                outputs[node.Id] = result;
            }

            if (!outputs.TryGetValue(graph.SinkNodeId, out var sink))
                throw new InvalidOperationException(
                    $"Sink node '{graph.SinkNodeId}' not found in graph.");

            if (_hooks?.AfterRun is not null)
                await _hooks.AfterRun(graph, cancellationToken).ConfigureAwait(false);

            return sink;
        }
        catch (Exception ex)
        {
            if (_hooks?.OnRunFailed is not null)
                await _hooks.OnRunFailed(graph, ex, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private INodeFunctionProvider ResolveProvider(string functionId)
    {
        foreach (var p in _providers)
            if (p.Supports(functionId))
                return p;

        throw new InvalidOperationException(
            $"No registered INodeFunctionProvider supports function '{functionId}'.");
    }

    /// <summary>Kahn's algorithm. Ties broken by node declaration order so execution is deterministic
    /// for a given graph. Both data and control wires count toward indegree so future VisualScript
    /// ordering is preserved. A cycle throws.</summary>
    private static IReadOnlyList<GraphNode> TopologicalOrder(GraphDocument graph)
    {
        var byId = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var indegree = graph.Nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        var adjacency = graph.Nodes.ToDictionary(n => n.Id, _ => new List<GraphWire>(), StringComparer.Ordinal);
        foreach (var w in graph.Wires)
        {
            if (!byId.ContainsKey(w.FromNode) || !byId.ContainsKey(w.ToNode))
                throw new InvalidOperationException(
                    $"Wire references unknown node ({w.FromNode} -> {w.ToNode}).");
            indegree[w.ToNode]++;
            adjacency[w.FromNode].Add(w);
        }

        var ready = new Queue<GraphNode>(graph.Nodes.Where(n => indegree[n.Id] == 0));
        var ordered = new List<GraphNode>(graph.Nodes.Count);
        while (ready.Count > 0)
        {
            var n = ready.Dequeue();
            ordered.Add(n);
            foreach (var w in adjacency[n.Id])
                if (--indegree[w.ToNode] == 0)
                    ready.Enqueue(byId[w.ToNode]);
        }

        if (ordered.Count != graph.Nodes.Count)
            throw new InvalidOperationException("Graph has a cycle; cannot execute.");
        return ordered;
    }
}
