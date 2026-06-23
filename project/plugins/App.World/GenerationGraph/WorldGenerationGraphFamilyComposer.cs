using System;
using System.Collections.Generic;
using System.Linq;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>Result of composing one effective graph from a family document at a tick.</summary>
public sealed record WorldGenerationGraphCompositionResult(
    WorldGenerationGraphView Graph,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Selects regime graphs and composes sparse graph-scoped overrides for a playhead tick.
/// </summary>
public static class WorldGenerationGraphFamilyComposer
{
    public static WorldGenerationGraphView ResolveRegimeGraph(
        WorldGenerationGraphFamilyDocument family,
        string scheduleKind,
        string regimeId,
        string? sphereId = null)
    {
        ArgumentNullException.ThrowIfNull(family);
        RequireNonEmpty(scheduleKind, nameof(scheduleKind));
        RequireNonEmpty(regimeId, nameof(regimeId));

        var binding = family.RegimeGraphBindings.FirstOrDefault(candidate =>
            string.Equals(candidate.ScheduleKind, scheduleKind, StringComparison.Ordinal)
            && string.Equals(candidate.RegimeId, regimeId, StringComparison.Ordinal)
            && (sphereId is null
                || candidate.SphereId is null
                || string.Equals(candidate.SphereId, sphereId, StringComparison.Ordinal)));

        if (binding is null)
            throw new ArgumentException($"No graph binding exists for regime '{scheduleKind}:{regimeId}'.");

        return ResolveGraph(family, binding.GraphId);
    }

    public static WorldGenerationGraphCompositionResult ComposeEffectiveGraph(
        WorldGenerationGraphFamilyDocument family,
        string graphId,
        long tick)
    {
        ArgumentNullException.ThrowIfNull(family);
        RequireNonEmpty(graphId, nameof(graphId));

        var graph = ResolveGraph(family, graphId);
        var nodes = graph.Nodes.ToList();
        var wires = graph.Wires.ToList();
        var warnings = new List<string>();

        foreach (var layer in family.GraphOverrides
                     .Where(layer => string.Equals(layer.GraphId, graphId, StringComparison.Ordinal)
                                     && layer.Range.Contains(tick))
                     .OrderBy(layer => layer.StrengthOrder)
                     .ThenBy(layer => layer.OverrideId, StringComparer.Ordinal))
        {
            foreach (var edit in layer.Edits)
                ApplyEdit(layer.OverrideId, edit, nodes, wires, warnings);
        }

        return new WorldGenerationGraphCompositionResult(
            graph with { Nodes = nodes, Wires = wires },
            warnings);
    }

    public static IReadOnlyList<WorldGenerationSubgraphBinding> ListSubgraphs(
        WorldGenerationGraphFamilyDocument family,
        string parentGraphId)
    {
        ArgumentNullException.ThrowIfNull(family);
        RequireNonEmpty(parentGraphId, nameof(parentGraphId));

        return (family.SubgraphBindings ?? Array.Empty<WorldGenerationSubgraphBinding>())
            .Where(binding => string.Equals(binding.ParentGraphId, parentGraphId, StringComparison.Ordinal))
            .ToList();
    }

    private static WorldGenerationGraphView ResolveGraph(
        WorldGenerationGraphFamilyDocument family,
        string graphId)
    {
        if (string.Equals(family.BaseGraph.GraphId, graphId, StringComparison.Ordinal))
            return family.BaseGraph;

        var graph = family.Graphs.FirstOrDefault(candidate =>
            string.Equals(candidate.GraphId, graphId, StringComparison.Ordinal));
        if (graph is not null)
            return graph;

        throw new ArgumentException($"Graph '{graphId}' does not exist in family '{family.DocumentId}'.");
    }

    private static void ApplyEdit(
        string overrideId,
        WorldGenerationGraphEdit edit,
        List<WorldGenerationGraphNode> nodes,
        List<WorldGenerationGraphWire> wires,
        List<string> warnings)
    {
        switch (edit.Kind)
        {
            case "remove-node":
                ApplyRemoveNode(edit, nodes, wires, warnings);
                return;
            case "add-wire":
                ApplyAddWire(edit, nodes, wires, warnings);
                return;
            case "remove-wire":
                ApplyRemoveWire(edit, wires);
                return;
            case "set-param":
                ApplySetParam(edit, nodes, warnings);
                return;
            case "add-node":
                warnings.Add($"Override '{overrideId}' skipped add-node edit because no node catalog was provided.");
                return;
            default:
                warnings.Add($"Override '{overrideId}' skipped unsupported edit kind '{edit.Kind}'.");
                return;
        }
    }

    private static void ApplyRemoveNode(
        WorldGenerationGraphEdit edit,
        List<WorldGenerationGraphNode> nodes,
        List<WorldGenerationGraphWire> wires,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(edit.NodeId))
        {
            warnings.Add("Skipped remove-node edit with no node id.");
            return;
        }

        var removed = nodes.RemoveAll(node => string.Equals(node.NodeId, edit.NodeId, StringComparison.Ordinal));
        if (removed == 0)
        {
            warnings.Add($"Skipped remove-node edit for unknown node '{edit.NodeId}'.");
            return;
        }

        wires.RemoveAll(wire =>
            string.Equals(wire.FromNodeId, edit.NodeId, StringComparison.Ordinal)
            || string.Equals(wire.ToNodeId, edit.NodeId, StringComparison.Ordinal));
    }

    private static void ApplyAddWire(
        WorldGenerationGraphEdit edit,
        List<WorldGenerationGraphNode> nodes,
        List<WorldGenerationGraphWire> wires,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(edit.FromNodeId)
            || string.IsNullOrWhiteSpace(edit.FromPortId)
            || string.IsNullOrWhiteSpace(edit.ToNodeId)
            || string.IsNullOrWhiteSpace(edit.ToPortId))
        {
            warnings.Add("Skipped add-wire edit with incomplete endpoint fields.");
            return;
        }

        var fromNode = nodes.FirstOrDefault(node => string.Equals(node.NodeId, edit.FromNodeId, StringComparison.Ordinal));
        var toNode = nodes.FirstOrDefault(node => string.Equals(node.NodeId, edit.ToNodeId, StringComparison.Ordinal));
        var fromPort = fromNode?.Outputs.FirstOrDefault(port => string.Equals(port.PortId, edit.FromPortId, StringComparison.Ordinal));
        var toPort = toNode?.Inputs.FirstOrDefault(port => string.Equals(port.PortId, edit.ToPortId, StringComparison.Ordinal));

        if (fromNode is null || toNode is null || fromPort is null || toPort is null)
        {
            warnings.Add($"Skipped add-wire edit for invalid endpoint '{edit.FromNodeId}.{edit.FromPortId} -> {edit.ToNodeId}.{edit.ToPortId}'.");
            return;
        }

        if (wires.Any(wire => string.Equals(wire.ToNodeId, edit.ToNodeId, StringComparison.Ordinal)
                              && string.Equals(wire.ToPortId, edit.ToPortId, StringComparison.Ordinal)))
        {
            warnings.Add($"Skipped add-wire edit because input '{edit.ToNodeId}.{edit.ToPortId}' is already wired.");
            return;
        }

        var kindHint = !string.IsNullOrWhiteSpace(fromPort.KindHint) ? fromPort.KindHint : toPort.KindHint;
        wires.Add(new WorldGenerationGraphWire(
            edit.FromNodeId,
            edit.FromPortId,
            edit.ToNodeId,
            edit.ToPortId,
            kindHint));
    }

    private static void ApplyRemoveWire(
        WorldGenerationGraphEdit edit,
        List<WorldGenerationGraphWire> wires)
    {
        wires.RemoveAll(wire =>
            string.Equals(wire.FromNodeId, edit.FromNodeId, StringComparison.Ordinal)
            && string.Equals(wire.FromPortId, edit.FromPortId, StringComparison.Ordinal)
            && string.Equals(wire.ToNodeId, edit.ToNodeId, StringComparison.Ordinal)
            && string.Equals(wire.ToPortId, edit.ToPortId, StringComparison.Ordinal));
    }

    private static void ApplySetParam(
        WorldGenerationGraphEdit edit,
        List<WorldGenerationGraphNode> nodes,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(edit.NodeId)
            || string.IsNullOrWhiteSpace(edit.ParamKey)
            || edit.ParamValue is null)
        {
            warnings.Add("Skipped set-param edit with incomplete fields.");
            return;
        }

        var index = nodes.FindIndex(node => string.Equals(node.NodeId, edit.NodeId, StringComparison.Ordinal));
        if (index < 0)
        {
            warnings.Add($"Skipped set-param edit for unknown node '{edit.NodeId}'.");
            return;
        }

        var node = nodes[index];
        if (node.Parameters is null)
        {
            warnings.Add($"Skipped set-param edit for node '{edit.NodeId}' because it has no parameters.");
            return;
        }

        var parameterIndex = node.Parameters.ToList().FindIndex(parameter =>
            string.Equals(parameter.Key, edit.ParamKey, StringComparison.Ordinal));
        if (parameterIndex < 0)
        {
            warnings.Add($"Skipped set-param edit for unknown parameter '{edit.NodeId}.{edit.ParamKey}'.");
            return;
        }

        var parameters = node.Parameters.ToList();
        parameters[parameterIndex] = parameters[parameterIndex] with { Value = edit.ParamValue };
        nodes[index] = node with { Parameters = parameters };
    }

    private static void RequireNonEmpty(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must be non-empty.", parameterName);
    }
}
