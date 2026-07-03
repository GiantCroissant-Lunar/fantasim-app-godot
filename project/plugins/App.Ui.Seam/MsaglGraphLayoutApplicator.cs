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
    // Card geometry must match GraphNodeVisualEnhancer so MSAGL node boxes reflect the real
    // rendered card. The enhancer fixes content width at 318px with 10px left/right panel
    // content margins and 1px borders, giving a 340px rendered card width.
    private const double CardContentWidth = 318.0;
    private const double CardContentMarginX = 10.0;
    private const double CardBorderX = 1.0;
    private const double CardWidth = CardContentWidth + 2 * CardContentMarginX + 2 * CardBorderX;

    private const double TitleBarHeight = 26.0;   // titlebar content 5+5 + font line ~16
    private const double StripeHeight = 5.0;       // category color stripe under titlebar
    private const double SlotRowHeight = 26.0;    // matches enhancer SlotRowHeight
    private const double DetailLineHeight = 15.0; // font 11 + line spacing
    private const double ParamLineHeight = 15.0;
    private const double RuntimeLineHeight = 14.0;
    private const double PreviewHeight = 72.0;
    private const double CardPadTop = 8.0;
    private const double CardPadBottom = 8.0;

    // Left graph panel is 760px wide (ViewHost.graphPanelWidth). Two columns of 340px cards
    // plus a 64px column gap fit inside that panel while staying clear of the activity panel.
    private const double HorizontalPadding = 24.0;
    private const double ColumnGap = 64.0;
    private const double NodeGapX = 24.0;
    private const double NodeGapY = 28.0;

    public static bool TryApply(GraphEdit graphEdit, object viewModel, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(graphEdit);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            var compact = ReadBool(viewModel, "CompactCards");
            var nodes = ReadNodes(viewModel, compact).ToList();
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
            NodeSeparation = NodeGapX,
            LayerSeparation = NodeGapY + 40,
            AspectRatio = 1.6,
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
            var y = 16.0;
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

    private static IEnumerable<LayoutNode> ReadNodes(object viewModel, bool compact)
    {
        var property = viewModel.GetType().GetProperty("Nodes", BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetValue(viewModel) is not IEnumerable items)
            yield break;

        foreach (var item in items)
        {
            var nodeId = ReadString(item, "NodeId");
            if (string.IsNullOrWhiteSpace(nodeId))
                continue;

            var runtime = item.GetType()
                .GetProperty("RuntimeState", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(item);

            var extraDetailLines = compact
                ? 0
                : CountExtraDetailLines(
                    item.GetType()
                        .GetProperty("ProviderMetadata", BindingFlags.Public | BindingFlags.Instance)
                        ?.GetValue(item),
                    item.GetType()
                        .GetProperty("ExecutionTraits", BindingFlags.Public | BindingFlags.Instance)
                        ?.GetValue(item));

            var runtimeLines = CountRuntimeLines(runtime);
            var hasPreview = !compact && ReadHasPreview(item);

            yield return new LayoutNode(
                nodeId,
                ReadString(item, "TypeId") ?? nodeId,
                ReadInt(item, "InputCount", 1),
                ReadInt(item, "OutputCount", 1),
                ReadString(item, "Summary") ?? string.Empty,
                ReadString(item, "Detail") ?? string.Empty,
                compact ? 0 : ReadListCount(item, "ParameterLines"),
                extraDetailLines,
                runtimeLines,
                hasPreview,
                compact);
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

    // Cards are a fixed width in the enhancer; width never varies by content. Returning a
    // constant keeps the MSAGL geometry boxes aligned with what Godot actually draws.
    private static double NodeWidth(LayoutNode node) => CardWidth;

    // Height mirrors the enhancer's AddNodeBody layout:
    //   titlebar + stripe + max(input,output) slot rows + detail block + provider/trait lines
    //   + runtime lines + parameter lines + optional preview, with panel top/bottom padding.
    private static double NodeHeight(LayoutNode node)
    {
        var slotRows = Math.Max(node.InputCount, node.OutputCount);
        var detailLines = CountDetailLines(node);
        var lines = slotRows + detailLines + node.ParameterLineCount + node.RuntimeLineCount;
        var height = TitleBarHeight + StripeHeight + CardPadTop + CardPadBottom
                     + slotRows * SlotRowHeight
                     + detailLines * DetailLineHeight
                     + node.ParameterLineCount * ParamLineHeight
                     + node.RuntimeLineCount * RuntimeLineHeight;

        if (node.HasPreview)
            height += PreviewHeight;

        return height;
    }

    // The enhancer always renders a detail block: Summary + NodeFacts (category|typeKey[|flags])
    // + PortKindSummary (in:/out:). When summary is empty it still renders the two fact lines,
    // so the minimum detail body is 3 lines. Detail text is not empty when either summary or
    // detail is present.
    private static int CountDetailLines(LayoutNode node)
    {
        if (node.Compact)
            return string.IsNullOrWhiteSpace(node.Summary) ? 0 : 1;

        var lines = 0;
        if (!string.IsNullOrWhiteSpace(node.Summary))
            lines++;
        // NodeFacts always renders one line.
        lines++;
        // PortKindSummary always renders two lines (in: / out:).
        lines += 2;

        if (!string.IsNullOrWhiteSpace(node.Detail))
            lines++;

        lines += node.ExtraDetailLineCount;

        return lines;
    }

    private static int CountExtraDetailLines(object? providerMetadata, object? executionTraits)
    {
        if (providerMetadata is null && executionTraits is null)
            return 0;

        var lines = 0;
        if (providerMetadata is not null)
        {
            // provider line always present when metadata is non-null.
            lines++;
            var runtime = ReadString(providerMetadata, "RuntimeRequirement");
            if (!string.IsNullOrWhiteSpace(runtime))
                lines++;
        }

        if (executionTraits is not null)
        {
            var traitParts = 0;
            if (ReadBool(executionTraits, "RequiresExternalProcess")) traitParts++;
            if (ReadBool(executionTraits, "RequiresNetwork")) traitParts++;
            if (ReadBool(executionTraits, "RequiresMainThread")) traitParts++;
            if (ReadBool(executionTraits, "SupportsCancellation")) traitParts++;
            if (ReadIntOrNull(executionTraits, "DefaultTimeoutSeconds") is not null) traitParts++;
            if (traitParts > 0)
                lines++;
            if (!string.IsNullOrWhiteSpace(ReadString(executionTraits, "ArtifactShape")))
                lines++;
        }

        return lines;
    }

    private static int CountRuntimeLines(object? runtimeState)
    {
        if (runtimeState is null)
            return 0;

        // Enhancer returns early for Pending with no payload.
        var status = ReadString(runtimeState, "Status");
        var hasPayload = !string.IsNullOrWhiteSpace(ReadString(runtimeState, "InputsJson"))
                          || !string.IsNullOrWhiteSpace(ReadString(runtimeState, "OutputsJson"))
                          || !string.IsNullOrWhiteSpace(ReadString(runtimeState, "ArtifactsJson"))
                          || !string.IsNullOrWhiteSpace(ReadString(runtimeState, "LogsJson"));
        if (status == "Pending" && !hasPayload)
            return 0;

        var lines = 1; // status line
        if (!string.IsNullOrWhiteSpace(ReadString(runtimeState, "InputsJson"))) lines++;
        if (!string.IsNullOrWhiteSpace(ReadString(runtimeState, "OutputsJson"))) lines++;
        if (!string.IsNullOrWhiteSpace(ReadString(runtimeState, "ArtifactsJson"))) lines++;
        if (!string.IsNullOrWhiteSpace(ReadString(runtimeState, "LogsJson"))) lines++;
        return lines;
    }

    private static bool ReadHasPreview(object? item)
        => ReadInt(item, "PreviewWidth") > 0
           && ReadInt(item, "PreviewHeight") > 0
           && ReadByteArray(item, "PreviewRgba") is { Length: > 0 } bytes
           && bytes.Length == ReadInt(item, "PreviewWidth") * ReadInt(item, "PreviewHeight") * 4;

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

    private static int ReadInt(object? item, string propertyName, int fallback = 0)
        => item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item) is int value
            ? value
            : fallback;

    private static int? ReadIntOrNull(object? item, string propertyName)
        => item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item) is int value
            ? value
            : null;

    private static bool ReadBool(object? item, string propertyName)
        => item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item) is bool value && value;

    private static byte[]? ReadByteArray(object? item, string propertyName)
        => item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item) as byte[];

    private static int ReadListCount(object? item, string propertyName)
    {
        var property = item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetValue(item) is not IEnumerable values)
            return 0;

        var count = 0;
        foreach (var _ in values)
            count++;
        return count;
    }

    private sealed record LayoutNode(
        string NodeId,
        string Title,
        int InputCount,
        int OutputCount,
        string Summary,
        string Detail,
        int ParameterLineCount,
        int ExtraDetailLineCount,
        int RuntimeLineCount,
        bool HasPreview,
        bool Compact = false);

    private sealed record LayoutWire(string FromNodeId, string ToNodeId);

    private sealed record PositionedNode(
        string NodeId,
        double X,
        double Y,
        double Width,
        double Height);
}
