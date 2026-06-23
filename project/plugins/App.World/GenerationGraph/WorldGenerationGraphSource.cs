using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.NodeGraph;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// Editable world-generation graph source backed by the typed world authoring model and projected
/// into the generic node-graph document expected by the shared UI/executor.
/// </summary>
public sealed class WorldGenerationGraphSource : IGraphSource
{
    private readonly object _gate = new();

    public WorldGenerationGraphSource(string sourceId, WorldGenerationGraphView graph)
    {
        SourceId = string.IsNullOrWhiteSpace(sourceId)
            ? throw new ArgumentException("Source id must be non-empty.", nameof(sourceId))
            : sourceId;
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        Document = CompileForAuthoring(Graph);
    }

    public string SourceId { get; }

    public WorldGenerationGraphView Graph { get; private set; }

    public GraphDocument Document { get; private set; }

    public event Action? Changed;

    public Task ApplyEditAsync(GraphEdit edit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(edit);

        lock (_gate)
        {
            var next = ApplyEdit(Graph, edit);

            Graph = next;
            Document = CompileForAuthoring(next);
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>Compile with full execution validation. Use this before running the graph.</summary>
    public CompiledWorldGenerationGraph CompileForExecution(string? sinkNodeId = null)
        => WorldGenerationGraphCompiler.Compile(Graph, sinkNodeId);

    internal static WorldGenerationGraphView ApplyEdit(WorldGenerationGraphView graph, GraphEdit edit)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(edit);

        return edit switch
        {
            GraphEdit.AddNode add => AddNode(graph, add.Node),
            GraphEdit.RemoveNode remove => RemoveNode(graph, remove.NodeId),
            GraphEdit.AddWire add => AddWire(graph, add.Wire),
            GraphEdit.RemoveWire remove => RemoveWire(graph, remove),
            GraphEdit.SetParam set => SetParam(graph, set.NodeId, set.Key, set.Value),
            _ => throw new NotSupportedException($"Unsupported graph edit '{edit.GetType().Name}'."),
        };
    }

    private static GraphDocument CompileForAuthoring(WorldGenerationGraphView graph)
        => WorldGenerationGraphCompiler.Compile(graph, validateRequiredInputs: false).Document;

    private static WorldGenerationGraphView AddNode(WorldGenerationGraphView graph, GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (graph.Nodes.Any(existing => string.Equals(existing.NodeId, node.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"Graph '{graph.GraphId}' already contains node '{node.Id}'.");

        var overrides = node.Params.ToDictionary(
            kv => kv.Key,
            kv => FormatParameterValue(kv.Value),
            StringComparer.Ordinal);
        var typedNode = WorldGenerationGraphDefaults.NodeFromSchema(node.Id, node.FunctionId, overrides);
        var nodes = graph.Nodes.Append(typedNode).ToList();
        return ValidateAuthoringGraph(graph with { Nodes = nodes });
    }

    private static WorldGenerationGraphView RemoveNode(WorldGenerationGraphView graph, string nodeId)
    {
        RequireNonEmpty(nodeId, nameof(nodeId));
        if (graph.OutputNodeIds?.Any(id => string.Equals(id, nodeId, StringComparison.Ordinal)) == true)
            throw new ArgumentException($"Cannot remove output node '{nodeId}'.");

        var nodes = graph.Nodes
            .Where(node => !string.Equals(node.NodeId, nodeId, StringComparison.Ordinal))
            .ToList();
        if (nodes.Count == graph.Nodes.Count)
            throw new ArgumentException($"Graph '{graph.GraphId}' does not contain node '{nodeId}'.");

        var wires = graph.Wires
            .Where(wire => !string.Equals(wire.FromNodeId, nodeId, StringComparison.Ordinal)
                           && !string.Equals(wire.ToNodeId, nodeId, StringComparison.Ordinal))
            .ToList();
        var annotations = graph.Annotations?
            .Select(annotation => annotation with
            {
                NodeIds = annotation.NodeIds
                    .Where(id => !string.Equals(id, nodeId, StringComparison.Ordinal))
                    .ToList(),
            })
            .ToList();

        return ValidateAuthoringGraph(graph with { Nodes = nodes, Wires = wires, Annotations = annotations });
    }

    private static WorldGenerationGraphView AddWire(WorldGenerationGraphView graph, GraphWire wire)
    {
        ArgumentNullException.ThrowIfNull(wire);

        var fromNode = graph.Nodes.FirstOrDefault(node => string.Equals(node.NodeId, wire.FromNode, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Graph '{graph.GraphId}' does not contain node '{wire.FromNode}'.");
        var toNode = graph.Nodes.FirstOrDefault(node => string.Equals(node.NodeId, wire.ToNode, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Graph '{graph.GraphId}' does not contain node '{wire.ToNode}'.");

        var fromPort = fromNode.Outputs.FirstOrDefault(port => string.Equals(port.PortId, wire.FromPort, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Node '{wire.FromNode}' has no output port '{wire.FromPort}'.");
        var toPort = toNode.Inputs.FirstOrDefault(port => string.Equals(port.PortId, wire.ToPort, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Node '{wire.ToNode}' has no input port '{wire.ToPort}'.");

        var kindHint = string.IsNullOrWhiteSpace(fromPort.KindHint) ? toPort.KindHint : fromPort.KindHint;
        var typedWire = new WorldGenerationGraphWire(wire.FromNode, wire.FromPort, wire.ToNode, wire.ToPort, kindHint);
        if (graph.Wires.Any(existing => SameEndpoint(existing, typedWire)))
            throw new ArgumentException($"Graph already contains wire '{wire.FromNode}.{wire.FromPort} -> {wire.ToNode}.{wire.ToPort}'.");

        var wires = graph.Wires.Append(typedWire).ToList();
        return ValidateAuthoringGraph(graph with { Wires = wires });
    }

    private static WorldGenerationGraphView RemoveWire(WorldGenerationGraphView graph, GraphEdit.RemoveWire edit)
    {
        var wires = graph.Wires
            .Where(wire =>
                !string.Equals(wire.FromNodeId, edit.FromNode, StringComparison.Ordinal)
                || !string.Equals(wire.FromPortId, edit.FromPort, StringComparison.Ordinal)
                || !string.Equals(wire.ToNodeId, edit.ToNode, StringComparison.Ordinal)
                || !string.Equals(wire.ToPortId, edit.ToPort, StringComparison.Ordinal))
            .ToList();

        if (wires.Count == graph.Wires.Count)
            throw new ArgumentException(
                $"Graph does not contain wire '{edit.FromNode}.{edit.FromPort} -> {edit.ToNode}.{edit.ToPort}'.");

        return ValidateAuthoringGraph(graph with { Wires = wires });
    }

    private static WorldGenerationGraphView SetParam(
        WorldGenerationGraphView graph,
        string nodeId,
        string key,
        JsonNode? value)
    {
        RequireNonEmpty(nodeId, nameof(nodeId));
        RequireNonEmpty(key, nameof(key));

        var index = graph.Nodes.ToList().FindIndex(node => string.Equals(node.NodeId, nodeId, StringComparison.Ordinal));
        if (index < 0)
            throw new ArgumentException($"Graph '{graph.GraphId}' does not contain node '{nodeId}'.");

        var node = graph.Nodes[index];
        if (node.Parameters is null)
            throw new ArgumentException($"Node '{nodeId}' has no parameters.");

        var parameters = node.Parameters.ToList();
        var paramIndex = parameters.FindIndex(parameter => string.Equals(parameter.Key, key, StringComparison.Ordinal));
        if (paramIndex < 0)
            throw new ArgumentException($"Node '{nodeId}' has no parameter '{key}'.");

        parameters[paramIndex] = parameters[paramIndex] with { Value = FormatParameterValue(value) };
        var nodes = graph.Nodes.ToList();
        nodes[index] = node with { Parameters = parameters };
        return ValidateAuthoringGraph(graph with { Nodes = nodes });
    }

    private static WorldGenerationGraphView ValidateAuthoringGraph(WorldGenerationGraphView graph)
    {
        _ = WorldGenerationGraphCompiler.Compile(graph, validateRequiredInputs: false);
        return graph;
    }

    private static string FormatParameterValue(JsonNode? value)
    {
        if (value is null)
            return string.Empty;
        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out var intValue))
                return intValue.ToString(CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<long>(out var longValue))
                return longValue.ToString(CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<double>(out var doubleValue))
                return doubleValue.ToString(CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<bool>(out var boolValue))
                return boolValue.ToString();
            if (jsonValue.TryGetValue<string>(out var stringValue))
                return stringValue;
        }

        return value.ToJsonString();
    }

    private static bool SameEndpoint(WorldGenerationGraphWire left, WorldGenerationGraphWire right)
        => string.Equals(left.FromNodeId, right.FromNodeId, StringComparison.Ordinal)
           && string.Equals(left.FromPortId, right.FromPortId, StringComparison.Ordinal)
           && string.Equals(left.ToNodeId, right.ToNodeId, StringComparison.Ordinal)
           && string.Equals(left.ToPortId, right.ToPortId, StringComparison.Ordinal);

    private static void RequireNonEmpty(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must be non-empty.", parameterName);
    }
}
