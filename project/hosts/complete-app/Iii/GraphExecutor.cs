using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.Iii;

/// <summary>
/// Walks an executable iii <see cref="GraphDocument"/> in dependency order and runs each node by
/// firing its iii function through an <see cref="IIiiInvoker"/>, threading each node's result fields
/// into downstream payloads per the wires. This is the app-side replacement for the Python
/// pipeline-worker: the DAG is now data, not code.
/// </summary>
public sealed class GraphExecutor
{
    private readonly IIiiInvoker _iii;

    public GraphExecutor(IIiiInvoker iii) => _iii = iii ?? throw new ArgumentNullException(nameof(iii));

    /// <summary>Run the graph. <paramref name="sharedParams"/> (e.g. job_id) are merged into every
    /// node's payload. Returns the sink node's result object.</summary>
    public async Task<JsonObject> ExecuteAsync(
        GraphDocument graph, JsonObject? sharedParams = null, CancellationToken cancellationToken = default)
    {
        var outputs = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        foreach (var node in TopologicalOrder(graph))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var payload = new JsonObject();
            if (sharedParams is not null)
                foreach (var kv in sharedParams) payload[kv.Key] = kv.Value?.DeepClone();
            foreach (var kv in node.Params) payload[kv.Key] = kv.Value?.DeepClone();

            // Pull wired inputs from upstream results.
            foreach (var wire in graph.Wires.Where(w => w.ToNode == node.Id))
            {
                if (!outputs.TryGetValue(wire.FromNode, out var upstream))
                    throw new InvalidOperationException($"Wire source '{wire.FromNode}' has no result for node '{node.Id}'.");
                payload[wire.ToPort] = upstream[wire.FromPort]?.DeepClone();
            }

            outputs[node.Id] = await _iii.RequestAsync(node.FunctionId, payload, cancellationToken).ConfigureAwait(false);
        }

        if (!outputs.TryGetValue(graph.SinkNodeId, out var sink))
            throw new InvalidOperationException($"Sink node '{graph.SinkNodeId}' not found in graph.");
        return sink;
    }

    /// <summary>Kahn's algorithm — deterministic order (ties broken by node declaration order).</summary>
    private static IReadOnlyList<GraphNode> TopologicalOrder(GraphDocument graph)
    {
        var byId = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var indegree = graph.Nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        foreach (var w in graph.Wires)
        {
            if (!byId.ContainsKey(w.FromNode) || !byId.ContainsKey(w.ToNode))
                throw new InvalidOperationException($"Wire references unknown node ({w.FromNode} -> {w.ToNode}).");
            indegree[w.ToNode]++;
        }

        var ready = new Queue<GraphNode>(graph.Nodes.Where(n => indegree[n.Id] == 0));
        var ordered = new List<GraphNode>(graph.Nodes.Count);
        while (ready.Count > 0)
        {
            var n = ready.Dequeue();
            ordered.Add(n);
            foreach (var w in graph.Wires.Where(w => w.FromNode == n.Id))
                if (--indegree[w.ToNode] == 0)
                    ready.Enqueue(byId[w.ToNode]);
        }

        if (ordered.Count != graph.Nodes.Count)
            throw new InvalidOperationException("Graph has a cycle; cannot execute.");
        return ordered;
    }
}
