using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.NodeGraph;
using Xunit;

namespace FantaSim.App.NodeGraph.Tests;

public class GraphExecutorTests
{
    private sealed class FakeProvider : INodeFunctionProvider
    {
        private readonly Dictionary<string, JsonObject> _outputs;
        public List<(string FunctionId, JsonObject Payload)> Invocations { get; } = new();

        public FakeProvider(Dictionary<string, JsonObject> outputs) => _outputs = outputs;

        public bool Supports(string functionId) => _outputs.ContainsKey(functionId);

        public Task<JsonObject> InvokeAsync(string functionId, JsonObject payload, CancellationToken cancellationToken = default)
        {
            Invocations.Add((functionId, payload));
            return Task.FromResult(_outputs[functionId].DeepClone().AsObject());
        }
    }

    private static GraphNode Node(string id, string fn, params (string Key, JsonNode? Value)[] p)
    {
        var obj = new JsonObject();
        foreach (var (k, v) in p)
            obj[k] = v?.DeepClone();
        return new GraphNode(id, fn, obj);
    }

    [Fact]
    public async Task Execute_runs_in_topological_order_and_threads_wires()
    {
        var provider = new FakeProvider(new()
        {
            ["a"] = new() { ["out"] = "A_OUT" },
            ["b"] = new() { ["result"] = "B_DONE" },
        });
        var graph = new GraphDocument(
            Nodes: new[] { Node("n1", "a"), Node("n2", "b") },
            Wires: new[] { new GraphWire("n1", "out", "n2", "src") },
            SinkNodeId: "n2");

        var sink = await new GraphExecutor(new[] { provider }).ExecuteAsync(graph);

        Assert.Equal("B_DONE", sink["result"]?.ToString());
        var n2Payload = provider.Invocations.Single(i => i.FunctionId == "b").Payload;
        Assert.Equal("A_OUT", n2Payload["src"]?.ToString());
    }

    [Fact]
    public async Task Execute_merges_shared_and_static_params()
    {
        var provider = new FakeProvider(new() { ["fn"] = new() { ["doubled"] = "x" } });
        var graph = new GraphDocument(
            Nodes: new[] { Node("n", "fn", ("seed", 42)) },
            Wires: Array.Empty<GraphWire>(),
            SinkNodeId: "n");

        await new GraphExecutor(new[] { provider }).ExecuteAsync(graph, sharedParams: new JsonObject { ["job_id"] = "J1" });

        var payload = provider.Invocations.Single().Payload;
        Assert.Equal("J1", payload["job_id"]?.ToString());
        Assert.Equal(42, payload["seed"]?.GetValue<int>());
    }

    [Fact]
    public async Task Execute_returns_sink_result()
    {
        var provider = new FakeProvider(new() { ["fn"] = new() { ["final"] = "SINK" } });
        var graph = new GraphDocument(
            Nodes: new[] { Node("sink", "fn") },
            Wires: Array.Empty<GraphWire>(),
            SinkNodeId: "sink");

        var sink = await new GraphExecutor(new[] { provider }).ExecuteAsync(graph);
        Assert.Equal("SINK", sink["final"]?.ToString());
    }

    [Fact]
    public async Task Execute_throws_on_cycle()
    {
        var provider = new FakeProvider(new() { ["fn"] = new JsonObject() });
        var graph = new GraphDocument(
            Nodes: new[] { Node("a", "fn"), Node("b", "fn") },
            Wires: new[]
            {
                new GraphWire("a", "o", "b", "i"),
                new GraphWire("b", "o", "a", "i"),
            },
            SinkNodeId: "a");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new GraphExecutor(new[] { provider }).ExecuteAsync(graph));
    }

    [Fact]
    public async Task Execute_throws_when_no_provider_supports_function()
    {
        var provider = new FakeProvider(new() { ["other"] = new JsonObject() });
        var graph = new GraphDocument(
            Nodes: new[] { Node("n", "unsupported") },
            Wires: Array.Empty<GraphWire>(),
            SinkNodeId: "n");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new GraphExecutor(new[] { provider }).ExecuteAsync(graph));
    }

    [Fact]
    public async Task Execute_throws_on_unknown_sink()
    {
        var provider = new FakeProvider(new() { ["fn"] = new() { ["o"] = "x" } });
        var graph = new GraphDocument(
            Nodes: new[] { Node("n", "fn") },
            Wires: Array.Empty<GraphWire>(),
            SinkNodeId: "missing");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new GraphExecutor(new[] { provider }).ExecuteAsync(graph));
    }

    [Fact]
    public async Task Execute_throws_on_wire_referencing_unknown_node()
    {
        var provider = new FakeProvider(new() { ["fn"] = new JsonObject() });
        var graph = new GraphDocument(
            Nodes: new[] { Node("n", "fn") },
            Wires: new[] { new GraphWire("n", "o", "ghost", "i") },
            SinkNodeId: "n");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new GraphExecutor(new[] { provider }).ExecuteAsync(graph));
    }

    [Fact]
    public async Task Execute_invokes_run_context_hooks_in_order()
    {
        var provider = new FakeProvider(new()
        {
            ["a"] = new() { ["o"] = "A" },
            ["b"] = new() { ["o"] = "B" },
        });
        var graph = new GraphDocument(
            Nodes: new[] { Node("n1", "a"), Node("n2", "b") },
            Wires: new[] { new GraphWire("n1", "o", "n2", "src") },
            SinkNodeId: "n2");

        var order = new List<string>();
        var hooks = new RunContext
        {
            BeforeRun = (g, ct) => { order.Add("before-run"); return Task.CompletedTask; },
            AfterNode = (n, input, output, ct) => { order.Add($"after-node:{n.Id}"); return Task.CompletedTask; },
            AfterRun = (g, ct) => { order.Add("after-run"); return Task.CompletedTask; },
        };

        await new GraphExecutor(new[] { provider }, hooks).ExecuteAsync(graph);

        Assert.Equal(new[] { "before-run", "after-node:n1", "after-node:n2", "after-run" }, order);
    }
}

public class ReadOnlyGraphSourceTests
{
    [Fact]
    public void Constructor_sets_source_and_document()
    {
        var doc = new GraphDocument(
            Nodes: new[] { new GraphNode("n", "fn", new JsonObject()) },
            Wires: Array.Empty<GraphWire>(),
            SinkNodeId: "n");
        var source = new ReadOnlyGraphSource("iii", doc);

        Assert.Equal("iii", source.SourceId);
        Assert.Same(doc, source.Document);
    }

    [Fact]
    public async Task ApplyEditAsync_rejects_edits()
    {
        var doc = new GraphDocument(
            Nodes: new[] { new GraphNode("n", "fn", new JsonObject()) },
            Wires: Array.Empty<GraphWire>(),
            SinkNodeId: "n");
        var source = new ReadOnlyGraphSource("iii", doc);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => source.ApplyEditAsync(new GraphEdit.RemoveNode("n")));
    }
}
