using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.NodeGraph;
using FantaSim.App.Ui.NodeGraph;
using FantaSim.App.World;
using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.Ui.Tests;

public sealed class NodeGraphViewSourceTests
{
    [Fact]
    public async Task Dispose_unsubscribes_from_graph_source_changes()
    {
        var graph = new GraphDocument(
            Nodes: new[] { new GraphNode("n", "fn", new JsonObject()) },
            Wires: Array.Empty<GraphWire>(),
            SinkNodeId: "n");
        var source = new EditableGraphSource("editable", graph);
        var view = new NodeGraphViewSource(source);
        var changes = 0;
        view.Changed += () => changes++;

        await source.ApplyEditAsync(new GraphEdit.SetParam("n", "before", JsonValue.Create(1)));
        view.Dispose();
        await source.ApplyEditAsync(new GraphEdit.SetParam("n", "after", JsonValue.Create(2)));

        Assert.Equal(1, changes);
    }

    [Fact]
    public void BuildDocument_projects_graph_annotations()
    {
        var graph = new GraphDocument(
            Nodes: new[] { new GraphNode("n", "fn", new JsonObject()) },
            Wires: Array.Empty<GraphWire>(),
            SinkNodeId: "n");
        var source = new AnnotatedGraphSource(
            "annotated",
            graph,
            new[]
            {
                new GraphAnnotation(
                    "comment_1",
                    "comment-boundary",
                    "Comment",
                    new GraphAnnotationBounds(1, 2, 300, 120),
                    new[] { "n" },
                    "Node group",
                    "#6ea8fe"),
            });
        var view = new NodeGraphViewSource(source);

        var document = view.BuildDocument();
        var json = JsonSerializer.SerializeToNode(document)?.ToJsonString();

        var annotation = Assert.Single(view.Annotations);
        Assert.Equal("comment_1", annotation.AnnotationId);
        Assert.Equal("comment-boundary", annotation.Kind);
        Assert.Contains("annotations", json);
    }

    [Fact]
    public void BuildDocument_projects_subgraphs_and_dispatch_navigates_open_and_back()
    {
        var root = new GraphDocument(
            Nodes: new[] { new GraphNode("parent", "fn.parent", new JsonObject()) },
            Wires: Array.Empty<GraphWire>(),
            SinkNodeId: "parent");
        var child = new GraphDocument(
            Nodes: new[] { new GraphNode("child", "fn.child", new JsonObject()) },
            Wires: Array.Empty<GraphWire>(),
            SinkNodeId: "child");
        var source = new NavigableGraphSource("navigable", root, child);
        var view = new NodeGraphViewSource(source);
        var changes = 0;
        view.Changed += () => changes++;

        var document = view.BuildDocument();
        var json = JsonSerializer.SerializeToNode(document)?.ToJsonString();

        var subgraph = Assert.Single(view.Subgraphs);
        Assert.Equal("parent", subgraph.ParentNodeId);
        Assert.Equal("child.graph", subgraph.SubgraphId);
        Assert.Contains("subgraphs", json);
        Assert.Contains("OPEN Child Graph", json);

        view.Dispatch("open-subgraph:child.graph", "btn-subgraph-child-graph");

        Assert.Equal("child.graph", source.ActiveGraphId);
        Assert.Equal("child", Assert.Single(view.Nodes).NodeId);
        Assert.True(changes >= 1);

        source.SelectGraph("root.graph");
        document = view.BuildDocument();
        json = JsonSerializer.SerializeToNode(document)?.ToJsonString();

        Assert.Equal("root.graph", source.ActiveGraphId);
        Assert.DoesNotContain("btn-graph-back", json);

        view.Dispatch("open-subgraph:child.graph", "btn-subgraph-parent-child-graph");
        Assert.Equal("child.graph", source.ActiveGraphId);

        view.Dispatch("graph-back", "btn-graph-back");

        Assert.Equal("root.graph", source.ActiveGraphId);
        Assert.Equal("parent", Assert.Single(view.Nodes).NodeId);
    }

    [Fact]
    public void BuildDocument_projects_world_family_subgraphs()
    {
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            WorldGenerationGraphDefaults.BuildFamily(),
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 0,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);
        var view = new NodeGraphViewSource(source, title: "world generation graph");

        var document = view.BuildDocument();
        var json = JsonSerializer.SerializeToNode(document)?.ToJsonString();

        var subgraph = Assert.Single(view.Subgraphs);
        Assert.Equal("crust", subgraph.ParentNodeId);
        Assert.Equal(WorldGenerationGraphDefaults.MobilePlateLayerGraphId, subgraph.SubgraphId);
        Assert.Equal("Mobile Plate Layers", subgraph.Label);
        Assert.Contains("OPEN Mobile Plate Layers", json);
        Assert.Contains("world generation graph - Mobile Plate Geosphere", json);
    }

    private sealed class AnnotatedGraphSource : IGraphSource, IGraphAnnotationSource
    {
        public AnnotatedGraphSource(
            string sourceId,
            GraphDocument document,
            IReadOnlyList<GraphAnnotation> annotations)
        {
            SourceId = sourceId;
            Document = document;
            Annotations = annotations;
        }

        public string SourceId { get; }
        public GraphDocument Document { get; }
        public IReadOnlyList<GraphAnnotation> Annotations { get; }
        public event Action? Changed { add { } remove { } }

        public Task ApplyEditAsync(GraphEdit edit, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NavigableGraphSource : IGraphSource, IGraphSubgraphSource
    {
        private readonly GraphDocument _root;
        private readonly GraphDocument _child;

        public NavigableGraphSource(string sourceId, GraphDocument root, GraphDocument child)
        {
            SourceId = sourceId;
            _root = root;
            _child = child;
            Document = root;
        }

        public string SourceId { get; }
        public GraphDocument Document { get; private set; }
        public string ActiveGraphId { get; private set; } = "root.graph";
        public string ActiveGraphLabel { get; private set; } = "Root Graph";
        public IReadOnlyList<GraphSubgraph> Subgraphs { get; private set; } = new[]
        {
            new GraphSubgraph(
                ParentGraphId: "root.graph",
                ParentNodeId: "parent",
                SubgraphId: "child.graph",
                Label: "Child Graph",
                Description: "Child graph"),
        };

        public event Action? Changed;

        public Task ApplyEditAsync(GraphEdit edit, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void SelectGraph(string graphId)
        {
            if (graphId == "child.graph")
            {
                ActiveGraphId = "child.graph";
                ActiveGraphLabel = "Child Graph";
                Document = _child;
                Subgraphs = Array.Empty<GraphSubgraph>();
                Changed?.Invoke();
                return;
            }

            if (graphId == "root.graph")
            {
                ActiveGraphId = "root.graph";
                ActiveGraphLabel = "Root Graph";
                Document = _root;
                Subgraphs = new[]
                {
                    new GraphSubgraph(
                        ParentGraphId: "root.graph",
                        ParentNodeId: "parent",
                        SubgraphId: "child.graph",
                        Label: "Child Graph",
                        Description: "Child graph"),
                };
                Changed?.Invoke();
                return;
            }

            throw new ArgumentException($"Unknown graph '{graphId}'.", nameof(graphId));
        }
    }
}
