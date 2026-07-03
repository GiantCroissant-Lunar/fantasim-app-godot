using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.NodeGraph;
using FantaSim.App.Ui.NodeGraph;
using Xunit;

namespace FantaSim.App.Ui.Tests;

public sealed class NodeInspectorFormatterTests
{
    [Fact]
    public void Format_with_no_selection_returns_single_hint_line()
    {
        var nodes = new[] { MkNode("n") };

        var lines = NodeInspectorFormatter.Format(nodes, Array.Empty<SubgraphItem>(), selectedNodeId: null);

        var line = Assert.Single(lines);
        Assert.Equal("hint", line.Kind);
        Assert.Contains("select a node", line.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Format_with_unknown_selection_returns_hint_line()
    {
        var nodes = new[] { MkNode("n") };

        var lines = NodeInspectorFormatter.Format(nodes, Array.Empty<SubgraphItem>(), selectedNodeId: "missing");

        var line = Assert.Single(lines);
        Assert.Equal("hint", line.Kind);
    }

    [Fact]
    public void Format_for_selected_node_emits_title_summary_facts_ports_provider_and_params()
    {
        var node = MkNode(
            "options",
            typeId: "World Options",
            summary: "Configures world generation.",
            category: "source",
            typeKey: "world.options",
            isExpensive: true,
            inputs: new[] { new PortItem("seed", "Seed", "data", true) },
            outputs: new[] { new PortItem("result", "Result", "data", false) },
            parameterLines: new[] { "Seed: 42", "Frequency: 0.7" },
            provider: new FunctionProviderMetadata("iii", "vplanet-worker", "process"),
            traits: new FunctionExecutionTraits(RequiresExternalProcess: true, SupportsCancellation: true));

        var lines = NodeInspectorFormatter.Format(new[] { node }, Array.Empty<SubgraphItem>(), "options").ToList();

        Assert.Equal("title", lines[0].Kind);
        Assert.Equal("World Options", lines[0].Text);
        Assert.Contains(lines, l => l.Kind == "summary" && l.Text == "Configures world generation.");
        Assert.Contains(lines, l => l.Kind == "fact" && l.Text.Contains("source", StringComparison.Ordinal) && l.Text.Contains("world.options", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Kind == "fact" && l.Text.Contains("expensive", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Kind == "in" && l.Text.Contains("Seed", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Kind == "out" && l.Text.Contains("Result", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Kind == "provider" && l.Text.Contains("vplanet-worker", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Kind == "param" && l.Text == "Seed: 42");
        Assert.Contains(lines, l => l.Kind == "param" && l.Text == "Frequency: 0.7");
    }

    [Fact]
    public void Format_includes_opens_line_when_selected_node_owns_a_subgraph()
    {
        var node = MkNode("crust");
        var subgraphs = new[]
        {
            new SubgraphItem(
                ParentGraphId: "base",
                ParentNodeId: "crust",
                SubgraphId: "layers.graph",
                Label: "Mobile Plate Layers"),
        };

        var lines = NodeInspectorFormatter.Format(new[] { node }, subgraphs, "crust").ToList();

        Assert.Contains(lines, l => l.Kind == "opens" && l.Text.Contains("Mobile Plate Layers", StringComparison.Ordinal));
    }

    [Fact]
    public void Format_omits_param_lines_when_parameter_lines_empty_but_keeps_rest()
    {
        var node = MkNode("n", summary: "A node.", parameterLines: Array.Empty<string>());

        var lines = NodeInspectorFormatter.Format(new[] { node }, Array.Empty<SubgraphItem>(), "n").ToList();

        Assert.DoesNotContain(lines, l => l.Kind == "param");
        Assert.Contains(lines, l => l.Kind == "title");
        Assert.Contains(lines, l => l.Kind == "summary" && l.Text == "A node.");
    }

    private static NodeItem MkNode(
        string nodeId,
        string typeId = "Node",
        string summary = "summary",
        string category = "graph",
        string typeKey = "fn",
        bool isSideEffect = false,
        bool isExpensive = false,
        IReadOnlyList<PortItem>? inputs = null,
        IReadOnlyList<PortItem>? outputs = null,
        IReadOnlyList<string>? parameterLines = null,
        FunctionProviderMetadata? provider = null,
        FunctionExecutionTraits? traits = null)
    {
        return new NodeItem(
            NodeId: nodeId,
            TypeId: typeId,
            InputCount: inputs?.Count ?? 0,
            OutputCount: outputs?.Count ?? 0,
            Category: category,
            TypeKey: typeKey,
            Summary: summary,
            Detail: string.Empty,
            IsSideEffect: isSideEffect,
            IsExpensive: isExpensive,
            Inputs: inputs ?? Array.Empty<PortItem>(),
            Outputs: outputs ?? Array.Empty<PortItem>(),
            ParameterLines: parameterLines,
            ProviderMetadata: provider,
            ExecutionTraits: traits);
    }
}
