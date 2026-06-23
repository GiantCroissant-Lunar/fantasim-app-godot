using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Xunit;

namespace FantaSim.App.NodeGraph.Tests;

public sealed class GraphDocumentContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void GraphDocument_RoundTrips_ThroughCamelCaseJson()
    {
        var graph = new GraphDocument(
            Nodes: new[]
            {
                new GraphNode(
                    "source",
                    "world.options",
                    new JsonObject
                    {
                        ["seed"] = 42,
                        ["config"] = new JsonObject { ["mode"] = "test" },
                    }),
                new GraphNode("sink", "crust.generate", new JsonObject()),
            },
            Wires: new[] { new GraphWire("source", "options", "sink", "options") },
            SinkNodeId: "sink");

        var json = JsonSerializer.Serialize(graph, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GraphDocument>(json, JsonOptions);

        Assert.Contains("\"sinkNodeId\"", json);
        Assert.Contains("\"functionId\"", json);
        Assert.NotNull(deserialized);
        Assert.Equal(graph.SinkNodeId, deserialized!.SinkNodeId);
        Assert.Equal(graph.Nodes.Select(node => node.Id), deserialized.Nodes.Select(node => node.Id));
        Assert.Equal("world.options", deserialized.Nodes[0].FunctionId);
        Assert.Equal(42, deserialized.Nodes[0].Params["seed"]!.GetValue<int>());
        Assert.Equal("test", deserialized.Nodes[0].Params["config"]!["mode"]!.GetValue<string>());
        Assert.Equal(WireKind.Data, deserialized.Wires[0].Kind);
        Assert.Equal("options", deserialized.Wires[0].ToPort);
    }
}
