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
    public async Task Dispatch_run_projects_product_count_and_tick_into_status()
    {
        var graph = new GraphDocument(
            Nodes: new[] { new GraphNode("n", "fn", new JsonObject()) },
            Wires: Array.Empty<GraphWire>(),
            SinkNodeId: "n");
        var source = new EditableGraphSource("editable", graph);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new NodeGraphViewSource(
            source,
            runAsync: () => Task.FromResult(new JsonObject
            {
                ["canonicalTick"] = 1_234L,
                ["products"] = new JsonArray
                {
                    new JsonObject { ["productAddress"] = "/base/main/formation/body-set@1234" },
                },
            }));
        view.Changed += () =>
        {
            var json = JsonSerializer.SerializeToNode(view.BuildDocument())?.ToJsonString();
            if (json?.Contains("done: 1 product @ tick 1234", StringComparison.Ordinal) == true)
                done.TrySetResult();
        };

        view.Dispatch("run", "btn-run");
        await done.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var documentJson = JsonSerializer.SerializeToNode(view.BuildDocument())?.ToJsonString();

        Assert.Contains("done: 1 product @ tick 1234", documentJson);
    }

    [Fact]
    public async Task Dispatch_run_drives_real_compile_and_runner_pipeline()
    {
        var graph = WorldGenerationGraphDefaults.BuildBodyFormationGraph();
        var source = new WorldGenerationGraphSource("world-generation", graph);
        var provider = new WorldFunctionProvider();
        var runner = new WorldGenerationGraphRunner(new[] { provider });
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var view = new NodeGraphViewSource(source, runAsync: async () =>
        {
            var compiled = source.CompileForExecution();
            var output = await runner.RunAsync(compiled.Document);
            return WorldGenerationGraphRunner.ToCommandResult(output);
        });
        view.Changed += () =>
        {
            var json = JsonSerializer.SerializeToNode(view.BuildDocument())?.ToJsonString();
            if (json?.Contains("done: 1 product @ tick 0", StringComparison.Ordinal) == true)
                done.TrySetResult();
        };

        view.Dispatch("run", "btn-run");
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var documentJson = JsonSerializer.SerializeToNode(view.BuildDocument())?.ToJsonString();

        Assert.Contains("done: 1 product @ tick 0", documentJson);
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
    public void Dispatch_and_annotation_hooks_forward_comment_boundary_edits()
    {
        var graph = new GraphDocument(
            Nodes: new[]
            {
                new GraphNode("source", "fn.source", new JsonObject()),
                new GraphNode("sink", "fn.sink", new JsonObject()),
            },
            Wires: Array.Empty<GraphWire>(),
            SinkNodeId: "sink");
        var source = new AnnotatedGraphSource(
            "annotated",
            graph,
            Array.Empty<GraphAnnotation>());
        var view = new NodeGraphViewSource(source);
        var json = JsonSerializer.SerializeToNode(view.BuildDocument())?.ToJsonString();

        Assert.Contains("ADD COMMENT", json);

        view.Dispatch("add-comment-boundary", "btn-add-comment-boundary");

        var added = Assert.Single(source.Annotations);
        Assert.IsType<GraphEdit.AddAnnotation>(source.LastEdit);
        Assert.Equal("comment_1", added.AnnotationId);
        Assert.Equal("comment-boundary", added.Kind);
        Assert.Equal(new[] { "source", "sink" }, added.NodeIds);
        Assert.Single(view.Annotations);

        view.SetAnnotationBounds("comment_1", 10, 20, 300, 140);

        var updated = Assert.Single(source.Annotations);
        Assert.IsType<GraphEdit.UpdateAnnotation>(source.LastEdit);
        Assert.Equal(10, updated.Bounds.X);
        Assert.Equal(140, updated.Bounds.Height);

        view.RemoveAnnotation("comment_1");

        Assert.IsType<GraphEdit.RemoveAnnotation>(source.LastEdit);
        Assert.Empty(source.Annotations);
        Assert.Empty(view.Annotations);
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

    [Fact]
    public void BuildDocument_projects_world_node_provider_metadata()
    {
        var source = new WorldGenerationGraphSource(
            "world-generation",
            WorldGenerationGraphDefaults.BuildVplanetEarthGraph());
        var view = new NodeGraphViewSource(source, title: "world generation graph");

        _ = view.BuildDocument();

        var runNode = Assert.Single(view.Nodes, node => node.NodeId == "vplanet_run");
        Assert.Equal("external/science", runNode.Category);
        Assert.Equal(FunctionProviderKinds.Iii, runNode.ProviderMetadata?.ProviderKind);
        Assert.Equal("vplanet-worker", runNode.ProviderMetadata?.ProviderId);
        Assert.True(runNode.ExecutionTraits?.RequiresExternalProcess);
        Assert.True(runNode.ExecutionTraits?.SupportsCancellation);
    }

    [Fact]
    public async Task BuildDocument_projects_runtime_state()
    {
        var graph = new GraphDocument(
            Nodes: new[] { new GraphNode("n", "fn", new JsonObject { ["seed"] = 7 }) },
            Wires: Array.Empty<GraphWire>(),
            SinkNodeId: "n");
        var source = new EditableGraphSource("runtime", graph);
        var tracker = new GraphRuntimeStateTracker();
        var view = new NodeGraphViewSource(source, runtimeState: tracker);
        var provider = new RuntimeProvider();

        await new GraphExecutor(new[] { provider }, tracker.CreateRunContext("run-ui"))
            .ExecuteAsync(graph, sharedParams: new JsonObject { ["job_id"] = "J1" });

        var node = Assert.Single(view.Nodes);
        Assert.Equal(GraphNodeRuntimeStatus.Completed, node.RuntimeState?.Status);
        Assert.Contains("\"seed\":7", node.RuntimeState?.InputsJson);
        Assert.Contains("\"result\":\"ok\"", node.RuntimeState?.OutputsJson);

        var json = JsonSerializer.SerializeToNode(view.BuildDocument())?.ToJsonString();
        Assert.Contains("run-ui", json);
        Assert.Contains("Completed", json);
        Assert.Contains("outputsJson", json);
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
        public IReadOnlyList<GraphAnnotation> Annotations { get; private set; }
        public GraphEdit? LastEdit { get; private set; }
        public event Action? Changed;

        public Task ApplyEditAsync(GraphEdit edit, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastEdit = edit;
            switch (edit)
            {
                case GraphEdit.AddAnnotation add:
                    Annotations = Annotations.Append(add.Annotation).ToList();
                    break;
                case GraphEdit.UpdateAnnotation update:
                    Annotations = Annotations
                        .Select(annotation => string.Equals(annotation.AnnotationId, update.Annotation.AnnotationId, StringComparison.Ordinal)
                            ? update.Annotation
                            : annotation)
                        .ToList();
                    break;
                case GraphEdit.RemoveAnnotation remove:
                    Annotations = Annotations
                        .Where(annotation => !string.Equals(annotation.AnnotationId, remove.AnnotationId, StringComparison.Ordinal))
                        .ToList();
                    break;
            }

            Changed?.Invoke();
            return Task.CompletedTask;
        }
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

    private sealed class RuntimeProvider : INodeFunctionProvider
    {
        public bool Supports(string functionId) => functionId == "fn";

        public Task<JsonObject> InvokeAsync(string functionId, JsonObject payload, CancellationToken cancellationToken = default)
            => Task.FromResult(new JsonObject { ["result"] = "ok", ["path"] = "user://artifact.glb" });
    }
}
