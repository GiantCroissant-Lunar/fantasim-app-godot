using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.NodeGraph;
using FantaSim.App.Ui.NodeGraph;
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
}
