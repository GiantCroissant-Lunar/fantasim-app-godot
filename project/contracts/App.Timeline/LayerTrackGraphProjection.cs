using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World;

namespace FantaSim.App.Timeline;

public sealed record LayerTrackGraphNode(
    string NodeId,
    string TypeId,
    string Label,
    string Category,
    string Summary,
    int InputCount,
    int OutputCount,
    IReadOnlyList<LayerTrackGraphPort> Inputs,
    IReadOnlyList<LayerTrackGraphPort> Outputs,
    IReadOnlyList<string> ParameterLines);

public sealed record LayerTrackGraphPort(string PortId, string Label, string KindHint, bool Required);

public sealed record LayerTrackGraphWire(
    string FromNodeId,
    int FromSlot,
    string ToNodeId,
    int ToSlot,
    string FromPortId,
    string ToPortId,
    string KindHint);

public sealed record LayerTrackGraphView(
    string SphereId,
    string LayerId,
    string? RegimeId,
    string GraphId,
    string Label,
    IReadOnlyList<LayerTrackGraphNode> Nodes,
    IReadOnlyList<LayerTrackGraphWire> Wires,
    IReadOnlyList<string> PipelineNodeIds)
{
    public static LayerTrackGraphView Empty(string sphereId, string layerId, string? regimeId, string reason)
        => new(
            sphereId,
            layerId,
            regimeId,
            GraphId: string.Empty,
            Label: reason,
            Nodes: Array.Empty<LayerTrackGraphNode>(),
            Wires: Array.Empty<LayerTrackGraphWire>(),
            PipelineNodeIds: Array.Empty<string>());
}

public static class LayerTrackGraphProjection
{
    public static LayerTrackGraphView Resolve(
        WorldGenerationGraphFamilyDocument? family,
        string sphereId,
        string layerId,
        string? regimeId)
    {
        if (family is null)
            return LayerTrackGraphView.Empty(sphereId, layerId, regimeId, "graph unavailable");

        var binding = FindLayerBinding(family, sphereId, layerId, regimeId);
        if (binding is null)
            return LayerTrackGraphView.Empty(sphereId, layerId, regimeId, "no layer graph");

        var graph = ResolveGraph(family, binding.GraphId);
        if (graph is null)
            return LayerTrackGraphView.Empty(sphereId, layerId, regimeId, $"missing graph {binding.GraphId}");

        var nodes = graph.Nodes.Select(MapNode).ToList();
        var nodeIds = nodes.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);
        var wires = graph.Wires
            .Where(wire => nodeIds.Contains(wire.FromNodeId) && nodeIds.Contains(wire.ToNodeId))
            .Select(wire => MapWire(wire, graph.Nodes))
            .ToList();

        return new LayerTrackGraphView(
            sphereId,
            layerId,
            regimeId,
            graph.GraphId,
            graph.Label,
            nodes,
            wires,
            TopologicalOrder(nodes.Select(node => node.NodeId), wires));
    }

    private static WorldLayerGraphBinding? FindLayerBinding(
        WorldGenerationGraphFamilyDocument family,
        string sphereId,
        string layerId,
        string? regimeId)
    {
        var bindings = family.LayerGraphBindings ?? Array.Empty<WorldLayerGraphBinding>();

        if (!string.IsNullOrWhiteSpace(regimeId))
        {
            var specific = bindings.FirstOrDefault(candidate =>
                string.Equals(candidate.SphereId, sphereId, StringComparison.Ordinal)
                && string.Equals(candidate.LayerId, layerId, StringComparison.Ordinal)
                && string.Equals(candidate.RegimeId, regimeId, StringComparison.Ordinal));
            if (specific is not null)
                return specific;
        }

        return bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.SphereId, sphereId, StringComparison.Ordinal)
            && string.Equals(candidate.LayerId, layerId, StringComparison.Ordinal)
            && candidate.RegimeId is null);
    }

    private static WorldGenerationGraphView? ResolveGraph(WorldGenerationGraphFamilyDocument family, string graphId)
    {
        if (string.Equals(family.BaseGraph.GraphId, graphId, StringComparison.Ordinal))
            return family.BaseGraph;

        return family.Graphs.FirstOrDefault(candidate =>
            string.Equals(candidate.GraphId, graphId, StringComparison.Ordinal));
    }

    private static LayerTrackGraphNode MapNode(WorldGenerationGraphNode node)
        => new(
            node.NodeId,
            node.TypeId,
            node.Label,
            node.Category,
            node.Summary ?? string.Empty,
            node.Inputs.Count,
            node.Outputs.Count,
            node.Inputs.Select(port => new LayerTrackGraphPort(port.PortId, port.Label, port.KindHint, port.Required)).ToArray(),
            node.Outputs.Select(port => new LayerTrackGraphPort(port.PortId, port.Label, port.KindHint, port.Required)).ToArray(),
            (node.Parameters ?? Array.Empty<WorldGenerationGraphParameter>())
            .Select(parameter => $"{parameter.Label}: {parameter.Value}")
            .ToArray());

    private static LayerTrackGraphWire MapWire(
        WorldGenerationGraphWire wire,
        IReadOnlyList<WorldGenerationGraphNode> graphNodes)
    {
        var from = graphNodes.FirstOrDefault(node => string.Equals(node.NodeId, wire.FromNodeId, StringComparison.Ordinal));
        var to = graphNodes.FirstOrDefault(node => string.Equals(node.NodeId, wire.ToNodeId, StringComparison.Ordinal));

        return new LayerTrackGraphWire(
            wire.FromNodeId,
            PortIndex(from?.Outputs, wire.FromPortId),
            wire.ToNodeId,
            PortIndex(to?.Inputs, wire.ToPortId),
            wire.FromPortId,
            wire.ToPortId,
            wire.KindHint);
    }

    private static int PortIndex(IReadOnlyList<WorldGenerationGraphPort>? ports, string portId)
    {
        if (ports is null)
            return 0;

        for (var i = 0; i < ports.Count; i++)
        {
            if (string.Equals(ports[i].PortId, portId, StringComparison.Ordinal))
                return i;
        }

        return 0;
    }

    private static IReadOnlyList<string> TopologicalOrder(
        IEnumerable<string> nodeIds,
        IReadOnlyList<LayerTrackGraphWire> wires)
    {
        var orderedIds = nodeIds.ToList();
        var originalIndex = orderedIds
            .Select((id, index) => (id, index))
            .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
        var incoming = orderedIds.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var outgoing = orderedIds.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var wire in wires)
        {
            if (!incoming.ContainsKey(wire.FromNodeId) || !incoming.ContainsKey(wire.ToNodeId))
                continue;

            incoming[wire.ToNodeId]++;
            outgoing[wire.FromNodeId].Add(wire.ToNodeId);
        }

        var ready = incoming
            .Where(pair => pair.Value == 0)
            .Select(pair => pair.Key)
            .OrderBy(id => originalIndex[id])
            .ToList();
        var result = new List<string>(orderedIds.Count);

        while (ready.Count > 0)
        {
            var id = ready[0];
            ready.RemoveAt(0);
            result.Add(id);

            foreach (var to in outgoing[id].OrderBy(candidate => originalIndex[candidate]))
            {
                incoming[to]--;
                if (incoming[to] == 0)
                {
                    ready.Add(to);
                    ready.Sort((left, right) => originalIndex[left].CompareTo(originalIndex[right]));
                }
            }
        }

        return result.Count == orderedIds.Count ? result : orderedIds;
    }
}
