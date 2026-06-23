using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.NodeGraph;

/// <summary>
/// In-memory editable graph source for authoring workflows. Domain-specific sources can wrap this
/// class or mirror its validation while persisting the resulting <see cref="GraphDocument"/>.
/// </summary>
public sealed class EditableGraphSource : IGraphSource
{
    private readonly object _gate = new();

    public EditableGraphSource(string sourceId, GraphDocument document)
    {
        SourceId = string.IsNullOrWhiteSpace(sourceId)
            ? throw new ArgumentException("Source id must be non-empty.", nameof(sourceId))
            : sourceId;
        Document = ValidateDocument(document ?? throw new ArgumentNullException(nameof(document)));
    }

    public string SourceId { get; }

    public GraphDocument Document { get; private set; }

    public event Action? Changed;

    public Task ApplyEditAsync(GraphEdit edit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(edit);

        lock (_gate)
        {
            Document = edit switch
            {
                GraphEdit.AddNode add => AddNode(Document, add.Node),
                GraphEdit.RemoveNode remove => RemoveNode(Document, remove.NodeId),
                GraphEdit.AddWire add => AddWire(Document, add.Wire),
                GraphEdit.RemoveWire remove => RemoveWire(Document, remove),
                GraphEdit.SetParam set => SetParam(Document, set.NodeId, set.Key, set.Value),
                _ => throw new NotSupportedException($"Unsupported graph edit '{edit.GetType().Name}'."),
            };
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    private static GraphDocument AddNode(GraphDocument document, GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        RequireNonEmpty(node.Id, nameof(node.Id));
        RequireNonEmpty(node.FunctionId, nameof(node.FunctionId));

        if (document.Nodes.Any(existing => string.Equals(existing.Id, node.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"Graph already contains node '{node.Id}'.");

        var nodes = document.Nodes.Append(CloneNode(node)).ToList();
        return ValidateDocument(document with { Nodes = nodes });
    }

    private static GraphDocument RemoveNode(GraphDocument document, string nodeId)
    {
        RequireNonEmpty(nodeId, nameof(nodeId));
        if (string.Equals(document.SinkNodeId, nodeId, StringComparison.Ordinal))
            throw new ArgumentException($"Cannot remove sink node '{nodeId}'.");

        var nodes = document.Nodes
            .Where(node => !string.Equals(node.Id, nodeId, StringComparison.Ordinal))
            .ToList();
        if (nodes.Count == document.Nodes.Count)
            throw new ArgumentException($"Graph does not contain node '{nodeId}'.");

        var wires = document.Wires
            .Where(wire => !string.Equals(wire.FromNode, nodeId, StringComparison.Ordinal)
                           && !string.Equals(wire.ToNode, nodeId, StringComparison.Ordinal))
            .ToList();

        return ValidateDocument(document with { Nodes = nodes, Wires = wires });
    }

    private static GraphDocument AddWire(GraphDocument document, GraphWire wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        RequireNonEmpty(wire.FromNode, nameof(wire.FromNode));
        RequireNonEmpty(wire.FromPort, nameof(wire.FromPort));
        RequireNonEmpty(wire.ToNode, nameof(wire.ToNode));
        RequireNonEmpty(wire.ToPort, nameof(wire.ToPort));

        if (string.Equals(wire.FromNode, wire.ToNode, StringComparison.Ordinal))
            throw new ArgumentException($"Wire on node '{wire.FromNode}' cannot target the same node.");

        EnsureNodeExists(document, wire.FromNode);
        EnsureNodeExists(document, wire.ToNode);

        if (document.Wires.Any(existing => SameEndpoint(existing, wire)))
            throw new ArgumentException(
                $"Graph already contains wire '{wire.FromNode}.{wire.FromPort} -> {wire.ToNode}.{wire.ToPort}'.");

        if (wire.Kind == WireKind.Data && document.Wires.Any(existing =>
                existing.Kind == WireKind.Data
                && string.Equals(existing.ToNode, wire.ToNode, StringComparison.Ordinal)
                && string.Equals(existing.ToPort, wire.ToPort, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Input port '{wire.ToNode}.{wire.ToPort}' already has a data wire.");
        }

        var wires = document.Wires.Append(wire).ToList();
        EnsureAcyclic(document.Nodes, wires);
        return ValidateDocument(document with { Wires = wires });
    }

    private static GraphDocument RemoveWire(GraphDocument document, GraphEdit.RemoveWire edit)
    {
        var wires = document.Wires
            .Where(wire =>
                !string.Equals(wire.FromNode, edit.FromNode, StringComparison.Ordinal)
                || !string.Equals(wire.FromPort, edit.FromPort, StringComparison.Ordinal)
                || !string.Equals(wire.ToNode, edit.ToNode, StringComparison.Ordinal)
                || !string.Equals(wire.ToPort, edit.ToPort, StringComparison.Ordinal))
            .ToList();

        if (wires.Count == document.Wires.Count)
            throw new ArgumentException(
                $"Graph does not contain wire '{edit.FromNode}.{edit.FromPort} -> {edit.ToNode}.{edit.ToPort}'.");

        return ValidateDocument(document with { Wires = wires });
    }

    private static GraphDocument SetParam(GraphDocument document, string nodeId, string key, JsonNode? value)
    {
        RequireNonEmpty(nodeId, nameof(nodeId));
        RequireNonEmpty(key, nameof(key));

        var index = document.Nodes.ToList().FindIndex(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal));
        if (index < 0)
            throw new ArgumentException($"Graph does not contain node '{nodeId}'.");

        var nodes = document.Nodes.Select(CloneNode).ToList();
        var parameters = CloneJsonObject(nodes[index].Params);
        parameters[key] = value?.DeepClone();
        nodes[index] = nodes[index] with { Params = parameters };

        return ValidateDocument(document with { Nodes = nodes });
    }

    private static GraphDocument ValidateDocument(GraphDocument document)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in document.Nodes)
        {
            RequireNonEmpty(node.Id, nameof(node.Id));
            RequireNonEmpty(node.FunctionId, nameof(node.FunctionId));
            if (!ids.Add(node.Id))
                throw new ArgumentException($"Graph contains duplicate node id '{node.Id}'.");
        }

        if (!ids.Contains(document.SinkNodeId))
            throw new ArgumentException($"Sink node '{document.SinkNodeId}' does not exist.");

        var dataInputs = new HashSet<(string NodeId, string PortId)>();
        foreach (var wire in document.Wires)
        {
            if (!ids.Contains(wire.FromNode) || !ids.Contains(wire.ToNode))
                throw new ArgumentException($"Wire references unknown node ({wire.FromNode} -> {wire.ToNode}).");
            if (string.Equals(wire.FromNode, wire.ToNode, StringComparison.Ordinal))
                throw new ArgumentException($"Wire on node '{wire.FromNode}' cannot target the same node.");
            if (wire.Kind == WireKind.Data && !dataInputs.Add((wire.ToNode, wire.ToPort)))
                throw new ArgumentException($"Input port '{wire.ToNode}.{wire.ToPort}' has more than one data wire.");
        }

        EnsureAcyclic(document.Nodes, document.Wires);
        return CloneDocument(document);
    }

    private static void EnsureAcyclic(
        IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<GraphWire> wires)
    {
        var ids = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var indegree = ids.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var adjacency = ids.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var wire in wires)
        {
            if (!ids.Contains(wire.FromNode) || !ids.Contains(wire.ToNode))
                continue;
            indegree[wire.ToNode]++;
            adjacency[wire.FromNode].Add(wire.ToNode);
        }

        var ready = new Queue<string>(nodes.Select(node => node.Id).Where(id => indegree[id] == 0));
        var visited = 0;
        while (ready.Count > 0)
        {
            var nodeId = ready.Dequeue();
            visited++;
            foreach (var target in adjacency[nodeId])
            {
                indegree[target]--;
                if (indegree[target] == 0)
                    ready.Enqueue(target);
            }
        }

        if (visited != nodes.Count)
            throw new ArgumentException("Graph edit would create a cycle.");
    }

    private static void EnsureNodeExists(GraphDocument document, string nodeId)
    {
        if (!document.Nodes.Any(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal)))
            throw new ArgumentException($"Graph does not contain node '{nodeId}'.");
    }

    private static GraphDocument CloneDocument(GraphDocument document)
        => new(
            document.Nodes.Select(CloneNode).ToList(),
            document.Wires.ToList(),
            document.SinkNodeId);

    private static GraphNode CloneNode(GraphNode node)
        => node with { Params = CloneJsonObject(node.Params) };

    private static JsonObject CloneJsonObject(JsonObject value)
        => value.DeepClone().AsObject();

    private static bool SameEndpoint(GraphWire left, GraphWire right)
        => string.Equals(left.FromNode, right.FromNode, StringComparison.Ordinal)
           && string.Equals(left.FromPort, right.FromPort, StringComparison.Ordinal)
           && string.Equals(left.ToNode, right.ToNode, StringComparison.Ordinal)
           && string.Equals(left.ToPort, right.ToPort, StringComparison.Ordinal);

    private static void RequireNonEmpty(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must be non-empty.", parameterName);
    }
}
