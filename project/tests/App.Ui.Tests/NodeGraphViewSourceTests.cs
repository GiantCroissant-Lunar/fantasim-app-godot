using System;
using System.Text.Json.Nodes;
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
}
