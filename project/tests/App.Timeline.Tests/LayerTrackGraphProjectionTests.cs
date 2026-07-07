using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.Timeline;
using FantaSim.App.World;
using Xunit;

namespace FantaSim.App.Timeline.Tests;

public sealed class LayerTrackGraphProjectionTests
{
    [Fact]
    public void Resolve_UsesRegimeSpecificLayerBindingAndOrdersPipeline()
    {
        var family = Family(
            Graph(
                "geosphere.crust.layer",
                "Crust Layer",
                Nodes("normalize", "source", "scope"),
                new[]
                {
                    Wire("scope", "layer", "source", "layer"),
                    Wire("source", "source", "normalize", "primarySource"),
                }),
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "geosphere.crust.layer", "mobile-plate"),
            new WorldLayerGraphBinding("geosphere", "geosphere.crust", "fallback.layer"));

        var view = LayerTrackGraphProjection.Resolve(
            family,
            "geosphere",
            "geosphere.crust",
            "mobile-plate");

        Assert.Equal("geosphere.crust.layer", view.GraphId);
        Assert.Equal(new[] { "scope", "source", "normalize" }, view.PipelineNodeIds);
        Assert.Equal(3, view.Nodes.Count);
        Assert.Equal(2, view.Wires.Count);
        Assert.Equal(0, view.Wires[0].FromSlot);
        Assert.Equal(0, view.Wires[0].ToSlot);
    }

    [Fact]
    public void Resolve_FallsBackToEmptyWhenLayerGraphIsUnavailable()
    {
        var view = LayerTrackGraphProjection.Resolve(
            family: null,
            "geosphere",
            "geosphere.mantle",
            "mobile-plate");

        Assert.Empty(view.Nodes);
        Assert.Empty(view.Wires);
        Assert.Equal("graph unavailable", view.Label);
    }

    private static WorldGenerationGraphFamilyDocument Family(
        WorldGenerationGraphView graph,
        params WorldLayerGraphBinding[] layerBindings)
    {
        var baseGraph = Graph("base", "Base", Nodes("base"), Array.Empty<WorldGenerationGraphWire>());
        return new WorldGenerationGraphFamilyDocument(
            DocumentId: "test.family",
            SchemaVersion: 1,
            Revision: 1,
            BaseGraph: baseGraph,
            Graphs: new[] { graph },
            RegimeGraphBindings: Array.Empty<WorldRegimeGraphBinding>(),
            GraphOverrides: Array.Empty<WorldGenerationGraphScopedOverride>(),
            LegacyOverrides: Array.Empty<WorldGenerationGraphOverride>(),
            RunHistory: Array.Empty<WorldGenerationRunHistoryEntry>(),
            UpdatedUtc: DateTimeOffset.UnixEpoch,
            LayerGraphBindings: layerBindings);
    }

    private static WorldGenerationGraphView Graph(
        string graphId,
        string label,
        IReadOnlyList<WorldGenerationGraphNode> nodes,
        IReadOnlyList<WorldGenerationGraphWire> wires)
        => new(
            graphId,
            label,
            "test graph",
            nodes,
            wires,
            OutputNodeIds: nodes.Select(node => node.NodeId).ToArray());

    private static IReadOnlyList<WorldGenerationGraphNode> Nodes(params string[] ids)
        => ids.Select(id => new WorldGenerationGraphNode(
            NodeId: id,
            TypeId: $"test.{id}",
            Label: id,
            Category: "test",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: new[] { new WorldGenerationGraphPort("layer", "Layer", "world/layer", false), new WorldGenerationGraphPort("primarySource", "Primary", "world/source", false) },
            Outputs: new[] { new WorldGenerationGraphPort("layer", "Layer", "world/layer", false), new WorldGenerationGraphPort("source", "Source", "world/source", false) },
            Parameters: new[] { new WorldGenerationGraphParameter("layerId", "Layer", id, "string") },
            Summary: $"summary {id}"))
            .ToArray();

    private static WorldGenerationGraphWire Wire(string from, string fromPort, string to, string toPort)
        => new(from, fromPort, to, toPort, "test");
}
