using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.NodeGraph;

namespace FantaSim.App.Ui.NodeGraph;

/// <summary>
/// Pure projection of the node-graph view model into the inspector panel's line content.
/// Given the current graph nodes/subgraphs and a selected node id, returns the ordered
/// lines the inspector dock renders. No selection (null or unknown id) yields a single
/// hint line. This is deliberately side-effect free so it is fully unit-testable and so
/// <see cref="NodeGraphViewSource.BuildDocument"/> can bake the result into labels without
/// worrying about mutation or re-entrancy.
/// </summary>
public static class NodeInspectorFormatter
{
    public const string HintText = "Select a node";

    public static IReadOnlyList<InspectorLine> Format(
        IReadOnlyList<NodeItem> nodes,
        IReadOnlyList<SubgraphItem> subgraphs,
        string? selectedNodeId)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(subgraphs);

        if (string.IsNullOrWhiteSpace(selectedNodeId))
            return Hint();

        var node = nodes.FirstOrDefault(n => string.Equals(n.NodeId, selectedNodeId, StringComparison.Ordinal));
        if (node is null)
            return Hint();

        var lines = new List<InspectorLine>();

        lines.Add(new InspectorLine("title", node.TypeId));

        if (!string.IsNullOrWhiteSpace(node.Summary))
            lines.Add(new InspectorLine("summary", node.Summary));

        if (!string.IsNullOrWhiteSpace(node.Detail))
            lines.Add(new InspectorLine("detail", node.Detail));

        lines.Add(new InspectorLine("fact", NodeFacts(node)));

        AddPortLine(lines, "in", node.Inputs);
        AddPortLine(lines, "out", node.Outputs);

        foreach (var providerLine in FunctionProviderDetailFormatter.Format(node.ProviderMetadata, node.ExecutionTraits))
            lines.Add(new InspectorLine("provider", providerLine));

        if (node.ParameterLines is { Count: > 0 })
        {
            foreach (var parameterLine in node.ParameterLines)
                lines.Add(new InspectorLine("param", parameterLine));
        }

        AddOpensLine(lines, subgraphs, node.NodeId);

        return lines;
    }

    private static IReadOnlyList<InspectorLine> Hint()
        => new[] { new InspectorLine("hint", HintText) };

    private static void AddPortLine(List<InspectorLine> lines, string kind, IReadOnlyList<PortItem> ports)
    {
        if (ports.Count == 0)
            return;

        var labels = string.Join(", ", ports.Select(port => port.Label));
        lines.Add(new InspectorLine(kind, $"{kind}: {labels}"));
    }

    private static void AddOpensLine(List<InspectorLine> lines, IReadOnlyList<SubgraphItem> subgraphs, string nodeId)
    {
        var owned = subgraphs
            .Where(subgraph => string.Equals(subgraph.ParentNodeId, nodeId, StringComparison.Ordinal))
            .Select(subgraph => subgraph.Label)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Take(3)
            .ToList();

        if (owned.Count == 0)
            return;

        lines.Add(new InspectorLine("opens", $"opens: {string.Join(", ", owned)}"));
    }

    private static string NodeFacts(NodeItem node)
    {
        var flags = new[]
        {
            node.IsExpensive ? "expensive" : null,
            node.IsSideEffect ? "side-effect" : null,
        }.Where(flag => !string.IsNullOrWhiteSpace(flag));

        var flagText = string.Join(", ", flags);
        return string.IsNullOrWhiteSpace(flagText)
            ? $"{node.Category} | {node.TypeKey}"
            : $"{node.Category} | {node.TypeKey} | {flagText}";
    }
}

/// <summary>
/// A single rendered line in the inspector panel. <see cref="Kind"/> classifies the line
/// (title, summary, detail, fact, in, out, provider, param, opens, hint) so the document
/// builder and future styling can treat each class distinctly; <see cref="Text"/> is the
/// human-readable content.
/// </summary>
public sealed record InspectorLine(string Kind, string Text);
