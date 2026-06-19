using System.Collections;
using System.Reflection;
using Godot;
using Microsoft.Extensions.Logging;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Miscellaneous;
using MsaglNode = Microsoft.Msagl.Core.Layout.Node;

namespace FantaSim.App.Ui.Seam;

internal static class MsaglGraphLayoutApplicator
{
    private const double HorizontalPadding = 56.0;
    private const double VerticalPadding = 48.0;
    private const double ColumnGap = 96.0;
    private const double NodeGapX = 54.0;
    private const double NodeGapY = 42.0;

    public static bool TryApply(GraphEdit graphEdit, object viewModel, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(graphEdit);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            var nodes = ReadNodes(viewModel).ToList();
            if (nodes.Count == 0)
                return false;

            var nodeIds = nodes.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);
            var wires = ReadWires(viewModel)
                .Where(wire => nodeIds.Contains(wire.FromNodeId) && nodeIds.Contains(wire.ToNodeId))
                .Distinct()
                .ToList();

            var positions = Layout(nodes, wires);
            var applied = 0;

            foreach (var child in graphEdit.GetChildren())
            {
                if (child is not GraphNode graphNode)
                    continue;

                var nodeId = graphNode.Name.ToString();
                if (!positions.TryGetValue(nodeId, out var position))
                    continue;

                graphNode.PositionOffset = position;
                applied++;
            }

            graphEdit.ScrollOffset = Vector2.Zero;

            logger.LogInformation(
                "ViewRenderer: MSAGL layout applied to {Applied}/{Nodes} graph nodes ({Wires} wires).",
                applied,
                nodes.Count,
                wires.Count);

            return applied > 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ViewRenderer: MSAGL graph layout failed; keeping binder fallback positions.");
            return false;
        }
    }

    private static IReadOnlyDictionary<string, Vector2> Layout(
        IReadOnlyList<LayoutNode> nodes,
        IReadOnlyList<LayoutWire> wires)
    {
        var graph = new GeometryGraph();
        var msaglNodes = new Dictionary<string, MsaglNode>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            var geometryNode = new MsaglNode(
                CurveFactory.CreateRectangle(NodeWidth(node), NodeHeight(node), new Point()),
                node.NodeId);

            graph.Nodes.Add(geometryNode);
            msaglNodes[node.NodeId] = geometryNode;
        }

        foreach (var wire in wires)
        {
            if (msaglNodes.TryGetValue(wire.FromNodeId, out var from)
                && msaglNodes.TryGetValue(wire.ToNodeId, out var to)
                && !ReferenceEquals(from, to))
            {
                graph.Edges.Add(new Edge(from, to));
            }
        }

        var settings = new SugiyamaLayoutSettings
        {
            NodeSeparation = 68,
            LayerSeparation = 190,
            AspectRatio = 2.4,
            RandomSeedForOrdering = 1,
        };

        LayoutHelpers.CalculateLayout(graph, settings, null);

        var ranks = BuildRanks(nodes, wires);
        var rawNodes = msaglNodes
            .Select(pair => new
            {
                pair.Key,
                Rank = ranks.TryGetValue(pair.Key, out var rank) ? rank : 0,
                Order = pair.Value.Center.X,
                Width = NodeWidth(nodes.First(node => node.NodeId == pair.Key)),
                Height = NodeHeight(nodes.First(node => node.NodeId == pair.Key)),
            })
            .ToList();

        var columnWidth = rawNodes.Max(node => node.Width) + ColumnGap;
        var positioned = new List<PositionedNode>(rawNodes.Count);

        foreach (var column in rawNodes.GroupBy(node => node.Rank).OrderBy(group => group.Key))
        {
            var y = VerticalPadding;
            foreach (var node in column.OrderBy(node => node.Order))
            {
                positioned.Add(new PositionedNode(
                    node.Key,
                    HorizontalPadding + column.Key * columnWidth,
                    y,
                    node.Width,
                    node.Height));
                y += node.Height + NodeGapY;
            }
        }

        ResolveOverlaps(positioned);

        return positioned.ToDictionary(
            node => node.NodeId,
            node => new Vector2((float)node.X, (float)node.Y),
            StringComparer.Ordinal);
    }

    private static IEnumerable<LayoutNode> ReadNodes(object viewModel)
    {
        var property = viewModel.GetType().GetProperty("Nodes", BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetValue(viewModel) is not IEnumerable items)
            yield break;

        foreach (var item in items)
        {
            var nodeId = ReadString(item, "NodeId");
            if (string.IsNullOrWhiteSpace(nodeId))
                continue;

            yield return new LayoutNode(
                nodeId,
                ReadString(item, "TypeId") ?? nodeId,
                ReadInt(item, "InputCount", 1),
                ReadInt(item, "OutputCount", 1),
                ReadString(item, "Summary") ?? string.Empty,
                ReadString(item, "Detail") ?? string.Empty);
        }
    }

    private static IEnumerable<LayoutWire> ReadWires(object viewModel)
    {
        var property = viewModel.GetType().GetProperty("Wires", BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetValue(viewModel) is not IEnumerable items)
            yield break;

        foreach (var item in items)
        {
            var fromNodeId = ReadString(item, "FromNodeId");
            var toNodeId = ReadString(item, "ToNodeId");
            if (string.IsNullOrWhiteSpace(fromNodeId) || string.IsNullOrWhiteSpace(toNodeId))
                continue;

            yield return new LayoutWire(fromNodeId, toNodeId);
        }
    }

    private static double NodeWidth(LayoutNode node)
        => Math.Clamp(150 + node.Title.Length * 8, 300, 380);

    private static double NodeHeight(LayoutNode node)
        => 142 + Math.Max(node.InputCount, node.OutputCount) * 30
           + (string.IsNullOrWhiteSpace(node.Summary) ? 0 : 44)
           + (string.IsNullOrWhiteSpace(node.Detail) ? 0 : 18);

    private static IReadOnlyDictionary<string, int> BuildRanks(
        IReadOnlyList<LayoutNode> nodes,
        IReadOnlyList<LayoutWire> wires)
    {
        var ranks = nodes.ToDictionary(node => node.NodeId, _ => 0, StringComparer.Ordinal);

        for (var pass = 0; pass < nodes.Count; pass++)
        {
            var changed = false;
            foreach (var wire in wires)
            {
                if (!ranks.TryGetValue(wire.FromNodeId, out var fromRank)
                    || !ranks.TryGetValue(wire.ToNodeId, out var toRank)
                    || toRank >= fromRank + 1)
                    continue;

                ranks[wire.ToNodeId] = fromRank + 1;
                changed = true;
            }

            if (!changed)
                break;
        }

        return ranks;
    }

    private static void ResolveOverlaps(List<PositionedNode> nodes)
    {
        for (var pass = 0; pass < nodes.Count; pass++)
        {
            var changed = false;
            for (var i = 0; i < nodes.Count; i++)
            {
                for (var j = 0; j < i; j++)
                {
                    if (!Overlaps(nodes[i], nodes[j]))
                        continue;

                    nodes[i] = nodes[i] with { Y = nodes[j].Y + nodes[j].Height + NodeGapY };
                    changed = true;
                }
            }

            if (!changed)
                return;
        }
    }

    private static bool Overlaps(PositionedNode a, PositionedNode b)
        => a.X < b.X + b.Width + NodeGapX
           && a.X + a.Width + NodeGapX > b.X
           && a.Y < b.Y + b.Height + NodeGapY
           && a.Y + a.Height + NodeGapY > b.Y;

    private static string? ReadString(object? item, string propertyName)
        => item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item)?.ToString();

    private static int ReadInt(object? item, string propertyName, int fallback)
        => item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item) is int value
            ? value
            : fallback;

    private sealed record LayoutNode(
        string NodeId,
        string Title,
        int InputCount,
        int OutputCount,
        string Summary,
        string Detail);

    private sealed record LayoutWire(string FromNodeId, string ToNodeId);

    private sealed record PositionedNode(
        string NodeId,
        double X,
        double Y,
        double Width,
        double Height);
}
