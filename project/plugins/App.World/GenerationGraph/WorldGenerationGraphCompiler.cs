using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using FantaSim.App.NodeGraph;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// Result of lowering one effective world-generation graph into the generic executable graph model.
/// </summary>
public sealed record CompiledWorldGenerationGraph(
    GraphDocument Document,
    IReadOnlyList<string> OutputNodeIds);

/// <summary>
/// Lowers a typed world-generation authoring graph into the app's generic executable graph.
/// Authoring-only metadata such as comments, group boundaries, and subgraph bindings is ignored here.
/// </summary>
public static class WorldGenerationGraphCompiler
{
    public static CompiledWorldGenerationGraph Compile(
        WorldGenerationGraphView graph,
        string? sinkNodeId = null,
        bool validateRequiredInputs = true)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var byId = ValidateNodes(graph);
        ValidateWires(graph, byId);
        ValidateCycles(graph);
        if (validateRequiredInputs)
            ValidateRequiredInputs(graph);

        var nodes = graph.Nodes
            .Select(node => new GraphNode(node.NodeId, node.TypeId, BuildParams(node)))
            .ToList();

        var wires = graph.Wires
            .Select(wire => new GraphWire(
                wire.FromNodeId,
                wire.FromPortId,
                wire.ToNodeId,
                wire.ToPortId,
                ToWireKind(wire.KindHint)))
            .ToList();

        var outputNodeIds = ResolveOutputNodeIds(graph);
        ValidateOutputNodeIds(graph, outputNodeIds);
        var sink = ResolveSinkNodeId(graph, outputNodeIds, sinkNodeId);

        return new CompiledWorldGenerationGraph(
            new GraphDocument(nodes, wires, sink),
            outputNodeIds);
    }

    private static Dictionary<string, WorldGenerationGraphNode> ValidateNodes(WorldGenerationGraphView graph)
    {
        var byId = new Dictionary<string, WorldGenerationGraphNode>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            RequireNonEmpty(node.NodeId, nameof(node.NodeId));
            RequireNonEmpty(node.TypeId, nameof(node.TypeId));
            if (!byId.TryAdd(node.NodeId, node))
                throw new ArgumentException($"Graph '{graph.GraphId}' contains duplicate node id '{node.NodeId}'.");

            ValidateUniquePorts(node.NodeId, "input", node.Inputs);
            ValidateUniquePorts(node.NodeId, "output", node.Outputs);
            ValidateUniqueParameters(node);
        }

        return byId;
    }

    private static void ValidateUniquePorts(
        string nodeId,
        string side,
        IReadOnlyList<WorldGenerationGraphPort> ports)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var port in ports)
        {
            RequireNonEmpty(port.PortId, nameof(port.PortId));
            if (!seen.Add(port.PortId))
                throw new ArgumentException($"Node '{nodeId}' contains duplicate {side} port '{port.PortId}'.");
        }
    }

    private static void ValidateUniqueParameters(WorldGenerationGraphNode node)
    {
        if (node.Parameters is null)
            return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in node.Parameters)
        {
            RequireNonEmpty(parameter.Key, nameof(parameter.Key));
            if (!seen.Add(parameter.Key))
                throw new ArgumentException($"Node '{node.NodeId}' contains duplicate parameter '{parameter.Key}'.");
        }
    }

    private static void ValidateWires(
        WorldGenerationGraphView graph,
        IReadOnlyDictionary<string, WorldGenerationGraphNode> byId)
    {
        var occupiedInputs = new HashSet<(string NodeId, string PortId)>();

        foreach (var wire in graph.Wires)
        {
            if (!byId.TryGetValue(wire.FromNodeId, out var fromNode))
                throw new ArgumentException($"Wire source node '{wire.FromNodeId}' does not exist.");
            if (!byId.TryGetValue(wire.ToNodeId, out var toNode))
                throw new ArgumentException($"Wire target node '{wire.ToNodeId}' does not exist.");
            if (string.Equals(wire.FromNodeId, wire.ToNodeId, StringComparison.Ordinal))
                throw new ArgumentException($"Wire on node '{wire.FromNodeId}' cannot target the same node.");

            var fromPort = fromNode.Outputs.FirstOrDefault(p => string.Equals(p.PortId, wire.FromPortId, StringComparison.Ordinal))
                ?? throw new ArgumentException($"Node '{wire.FromNodeId}' has no output port '{wire.FromPortId}'.");
            var toPort = toNode.Inputs.FirstOrDefault(p => string.Equals(p.PortId, wire.ToPortId, StringComparison.Ordinal))
                ?? throw new ArgumentException($"Node '{wire.ToNodeId}' has no input port '{wire.ToPortId}'.");

            ValidateKindHint(wire, fromPort, toPort);

            if (ToWireKind(wire.KindHint) == WireKind.Data && !occupiedInputs.Add((wire.ToNodeId, wire.ToPortId)))
                throw new ArgumentException($"Input port '{wire.ToNodeId}.{wire.ToPortId}' has more than one data wire.");
        }
    }

    private static void ValidateKindHint(
        WorldGenerationGraphWire wire,
        WorldGenerationGraphPort fromPort,
        WorldGenerationGraphPort toPort)
    {
        if (ToWireKind(wire.KindHint) == WireKind.Control)
            return;

        if (!string.IsNullOrWhiteSpace(fromPort.KindHint)
            && !string.IsNullOrWhiteSpace(toPort.KindHint)
            && !string.Equals(fromPort.KindHint, toPort.KindHint, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Wire '{wire.FromNodeId}.{wire.FromPortId} -> {wire.ToNodeId}.{wire.ToPortId}' connects incompatible port kinds '{fromPort.KindHint}' and '{toPort.KindHint}'.");
        }

        if (!string.IsNullOrWhiteSpace(wire.KindHint)
            && !string.IsNullOrWhiteSpace(fromPort.KindHint)
            && !string.Equals(wire.KindHint, fromPort.KindHint, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Wire '{wire.FromNodeId}.{wire.FromPortId}' kind '{wire.KindHint}' does not match source port kind '{fromPort.KindHint}'.");
        }

        if (!string.IsNullOrWhiteSpace(wire.KindHint)
            && !string.IsNullOrWhiteSpace(toPort.KindHint)
            && !string.Equals(wire.KindHint, toPort.KindHint, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Wire '{wire.ToNodeId}.{wire.ToPortId}' kind '{wire.KindHint}' does not match target port kind '{toPort.KindHint}'.");
        }
    }

    private static void ValidateRequiredInputs(WorldGenerationGraphView graph)
    {
        var wiredInputs = graph.Wires
            .Where(wire => ToWireKind(wire.KindHint) == WireKind.Data)
            .Select(wire => (wire.ToNodeId, wire.ToPortId))
            .ToHashSet();

        foreach (var node in graph.Nodes)
        {
            foreach (var input in node.Inputs.Where(port => port.Required))
            {
                if (!wiredInputs.Contains((node.NodeId, input.PortId)))
                    throw new ArgumentException($"Node '{node.NodeId}' is missing required input '{input.PortId}'.");
            }
        }
    }

    private static JsonObject BuildParams(WorldGenerationGraphNode node)
    {
        var payload = new JsonObject();
        if (node.Parameters is null)
            return payload;

        foreach (var parameter in node.Parameters)
            payload[parameter.Key] = ParseParameterValue(parameter);
        return payload;
    }

    private static JsonNode? ParseParameterValue(WorldGenerationGraphParameter parameter)
    {
        var kind = parameter.KindHint.Trim().ToLowerInvariant();
        var value = parameter.Value;

        return kind switch
        {
            "int" or "integer" => JsonValue.Create(int.Parse(value, CultureInfo.InvariantCulture)),
            "long" or "tick" or "ticks" => JsonValue.Create(long.Parse(value, CultureInfo.InvariantCulture)),
            "float" or "double" or "number" => JsonValue.Create(double.Parse(value, CultureInfo.InvariantCulture)),
            "bool" or "boolean" => JsonValue.Create(bool.Parse(value)),
            "json" => JsonNode.Parse(value),
            _ => JsonValue.Create(value),
        };
    }

    private static IReadOnlyList<string> ResolveOutputNodeIds(WorldGenerationGraphView graph)
    {
        if (graph.OutputNodeIds is { Count: > 0 })
            return graph.OutputNodeIds;

        var withOutgoing = graph.Wires
            .Where(wire => ToWireKind(wire.KindHint) == WireKind.Data)
            .Select(wire => wire.FromNodeId)
            .ToHashSet(StringComparer.Ordinal);

        return graph.Nodes
            .Where(node => !withOutgoing.Contains(node.NodeId))
            .Select(node => node.NodeId)
            .ToList();
    }

    private static void ValidateOutputNodeIds(
        WorldGenerationGraphView graph,
        IReadOnlyList<string> outputNodeIds)
    {
        var nodeIds = graph.Nodes
            .Select(node => node.NodeId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var outputNodeId in outputNodeIds)
        {
            if (!nodeIds.Contains(outputNodeId))
                throw new ArgumentException($"Output node '{outputNodeId}' does not exist in graph '{graph.GraphId}'.");
        }
    }

    private static string ResolveSinkNodeId(
        WorldGenerationGraphView graph,
        IReadOnlyList<string> outputNodeIds,
        string? requestedSinkNodeId)
    {
        if (!string.IsNullOrWhiteSpace(requestedSinkNodeId))
        {
            if (!graph.Nodes.Any(node => string.Equals(node.NodeId, requestedSinkNodeId, StringComparison.Ordinal)))
                throw new ArgumentException($"Requested sink node '{requestedSinkNodeId}' does not exist.");
            return requestedSinkNodeId;
        }

        if (outputNodeIds.Count == 0)
            throw new ArgumentException($"Graph '{graph.GraphId}' has no output node to use as a sink.");

        return outputNodeIds[0];
    }

    private static WireKind ToWireKind(string kindHint)
        => string.Equals(kindHint, "control", StringComparison.OrdinalIgnoreCase)
           || string.Equals(kindHint, "control-flow", StringComparison.OrdinalIgnoreCase)
            ? WireKind.Control
            : WireKind.Data;

    private static void ValidateCycles(WorldGenerationGraphView graph)
    {
        var adj = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            adj[node.NodeId] = new List<string>();
        }

        foreach (var wire in graph.Wires)
        {
            if (adj.ContainsKey(wire.FromNodeId) && adj.ContainsKey(wire.ToNodeId))
            {
                adj[wire.FromNodeId].Add(wire.ToNodeId);
            }
        }

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            state[node.NodeId] = 0; // unvisited
        }

        bool HasCycle(string u, List<string> cyclePath)
        {
            state[u] = 1; // visiting
            cyclePath.Add(u);

            var targets = adj[u].OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (var v in targets)
            {
                if (state[v] == 1)
                {
                    cyclePath.Add(v);
                    return true;
                }
                else if (state[v] == 0)
                {
                    if (HasCycle(v, cyclePath))
                        return true;
                }
            }

            cyclePath.RemoveAt(cyclePath.Count - 1);
            state[u] = 2; // visited
            return false;
        }

        var orderedNodes = graph.Nodes.Select(n => n.NodeId).OrderBy(id => id, StringComparer.Ordinal).ToList();
        foreach (var nodeId in orderedNodes)
        {
            if (state[nodeId] == 0)
            {
                var cyclePath = new List<string>();
                if (HasCycle(nodeId, cyclePath))
                {
                    var cycleStartIdx = cyclePath.IndexOf(cyclePath[^1]);
                    var cycleSubPath = cyclePath.Skip(cycleStartIdx).ToList();
                    var cycleStr = string.Join(" -> ", cycleSubPath);
                    throw new ArgumentException($"Graph contains a cycle: {cycleStr}.");
                }
            }
        }
    }

    private static void RequireNonEmpty(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must be non-empty.", parameterName);
    }
}
