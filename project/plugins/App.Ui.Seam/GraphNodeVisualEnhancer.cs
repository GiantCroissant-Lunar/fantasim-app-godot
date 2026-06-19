using System.Collections;
using System.Reflection;
using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Ui.Seam;

internal static class GraphNodeVisualEnhancer
{
    private const string MetadataPrefix = "__node_meta_";
    private const string SlotPrefix = "__node_slot_";
    private const float NodeContentWidth = 318f;
    private const float SlotRowHeight = 26f;

    public static bool TryApply(GraphEdit graphEdit, object viewModel, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(graphEdit);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            var nodes = ReadNodes(viewModel).ToDictionary(node => node.NodeId, StringComparer.Ordinal);
            if (nodes.Count == 0)
                return false;

            ApplyGraphStyle(graphEdit);

            var applied = 0;
            foreach (var child in graphEdit.GetChildren())
            {
                if (child is not GraphNode graphNode)
                    continue;

                var nodeId = graphNode.Name.ToString();
                if (!nodes.TryGetValue(nodeId, out var node))
                    continue;

                RemoveMetadata(graphNode);
                ApplyNodeStyle(graphNode, node);
                ApplySlotLabels(graphNode, node);
                AddNodeBody(graphNode, node);
                applied++;
            }

            logger.LogInformation(
                "ViewRenderer: graph node visuals applied to {Applied}/{Nodes} nodes.",
                applied,
                nodes.Count);

            return applied > 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ViewRenderer: graph node visual enrichment failed; keeping default node rendering.");
            return false;
        }
    }

    private static void ApplyGraphStyle(GraphEdit graphEdit)
    {
        graphEdit.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.025f, 0.03f, 0.04f),
            BorderWidthBottom = 0,
            BorderWidthLeft = 0,
            BorderWidthRight = 0,
            BorderWidthTop = 0,
        });
    }

    private static void ApplyNodeStyle(GraphNode graphNode, VisualNode node)
    {
        var color = CategoryColor(node.Category);
        graphNode.Modulate = Colors.White;
        graphNode.AddThemeStyleboxOverride("panel", NodePanelStyle(color, selected: false, node.IsSideEffect));
        graphNode.AddThemeStyleboxOverride("panel_selected", NodePanelStyle(color, selected: true, node.IsSideEffect));
        graphNode.AddThemeStyleboxOverride("titlebar", TitleStyle(color, selected: false));
        graphNode.AddThemeStyleboxOverride("titlebar_selected", TitleStyle(color, selected: true));
    }

    private static void ApplySlotLabels(GraphNode graphNode, VisualNode node)
    {
        RemoveSlotRows(graphNode);

        var rows = Math.Max(node.Inputs.Count, node.Outputs.Count);

        for (var row = 0; row < rows; row++)
        {
            var rowControl = new HBoxContainer
            {
                Name = $"{SlotPrefix}{row}",
                CustomMinimumSize = new Vector2(NodeContentWidth, SlotRowHeight),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };

            var input = BuildPortLabel(
                row < node.Inputs.Count ? node.Inputs[row].Label : string.Empty,
                HorizontalAlignment.Left);
            var output = BuildPortLabel(
                row < node.Outputs.Count ? node.Outputs[row].Label : string.Empty,
                HorizontalAlignment.Right);

            rowControl.AddChild(input);
            rowControl.AddChild(output);
            graphNode.AddChild(rowControl);
            graphNode.MoveChild(rowControl, row);
            graphNode.SetSlot(
                row,
                row < node.Inputs.Count,
                0,
                new Color(0.42f, 0.72f, 1.0f),
                row < node.Outputs.Count,
                0,
                new Color(1.0f, 0.69f, 0.28f));
        }
    }

    private static void AddNodeBody(GraphNode graphNode, VisualNode node)
    {
        var color = CategoryColor(node.Category);
        graphNode.AddChild(new ColorRect
        {
            Name = $"{MetadataPrefix}stripe",
            Color = new Color(color.R, color.G, color.B, 0.55f),
            CustomMinimumSize = new Vector2(0, 5),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });

        var detail = new Label
        {
            Name = $"{MetadataPrefix}detail",
            Text = $"{node.Summary}\n{NodeFacts(node)}\n{PortKindSummary(node)}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipText = false,
            CustomMinimumSize = new Vector2(NodeContentWidth, 0),
        };
        detail.AddThemeColorOverride("font_color", new Color(0.90f, 0.93f, 0.97f, 0.98f));
        detail.AddThemeFontSizeOverride("font_size", 11);
        graphNode.AddChild(detail);

        if (node.ParameterLines is { Count: > 0 })
        {
            foreach (var line in node.ParameterLines)
            {
                var paramLabel = new Label
                {
                    Name = $"{MetadataPrefix}param",
                    Text = line,
                    AutowrapMode = TextServer.AutowrapMode.Off,
                    ClipText = true,
                    CustomMinimumSize = new Vector2(NodeContentWidth, 0),
                };
                paramLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.96f, 0.99f, 0.95f));
                paramLabel.AddThemeFontSizeOverride("font_size", 11);
                graphNode.AddChild(paramLabel);
            }
        }

        if (node.PreviewRgba is { Length: > 0 } && node.PreviewWidth > 0 && node.PreviewHeight > 0
            && node.PreviewRgba.Length == node.PreviewWidth * node.PreviewHeight * 4)
        {
            try
            {
                var image = Image.CreateFromData(node.PreviewWidth, node.PreviewHeight, false, Image.Format.Rgba8, node.PreviewRgba);
                var texture = ImageTexture.CreateFromImage(image);
                var rect = new TextureRect
                {
                    Name = $"{MetadataPrefix}preview",
                    Texture = texture,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    CustomMinimumSize = new Vector2(0, 72),
                };
                graphNode.AddChild(rect);
            }
            catch
            {
                // Malformed preview bytes - skip silently.
            }
        }
    }

    private static void RemoveMetadata(GraphNode graphNode)
    {
        foreach (var child in graphNode.GetChildren()
                     .Where(child => child.Name.ToString().StartsWith(MetadataPrefix, StringComparison.Ordinal))
                     .ToList())
        {
            graphNode.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void RemoveSlotRows(GraphNode graphNode)
    {
        foreach (var child in graphNode.GetChildren()
                     .Where(IsSlotRowChild)
                     .ToList())
        {
            graphNode.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static bool IsSlotRowChild(Node child)
    {
        var name = child.Name.ToString();
        if (name.StartsWith(SlotPrefix, StringComparison.Ordinal))
            return true;
        if (name.StartsWith(MetadataPrefix, StringComparison.Ordinal))
            return false;
        return child is Label;
    }

    private static Label BuildPortLabel(string value, HorizontalAlignment alignment)
    {
        var label = new Label
        {
            Text = Shorten(value, 19),
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            ClipText = true,
            CustomMinimumSize = new Vector2(NodeContentWidth * 0.5f, SlotRowHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeColorOverride("font_color", new Color(0.96f, 0.97f, 0.99f, 0.99f));
        label.AddThemeFontSizeOverride("font_size", 12);
        return label;
    }

    private static string Shorten(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        value = value.Trim();
        return value.Length <= maxLength ? value : value[..Math.Max(1, maxLength - 1)] + ".";
    }

    private static string PortKindSummary(VisualNode node)
    {
        var inputs = node.Inputs.Count == 0 ? "none" : string.Join(", ", node.Inputs.Select(port => port.KindHint).Distinct().Take(2));
        var outputs = node.Outputs.Count == 0 ? "none" : string.Join(", ", node.Outputs.Select(port => port.KindHint).Distinct().Take(2));
        return $"in: {Shorten(inputs, 36)}\nout: {Shorten(outputs, 36)}";
    }

    private static string NodeFacts(VisualNode node)
    {
        var flags = new[]
        {
            node.IsExpensive ? "expensive" : null,
            node.IsSideEffect ? "side-effect" : null,
        }.Where(flag => !string.IsNullOrWhiteSpace(flag));

        var flagText = string.Join(", ", flags);
        return string.IsNullOrWhiteSpace(flagText)
            ? $"{node.Category} | {node.TypeKey}"
            : $"{node.Category} | {node.TypeKey} | {flagText}";
    }

    private static StyleBoxFlat NodePanelStyle(Color color, bool selected, bool sideEffect)
        => new()
        {
            BgColor = new Color(0.035f, 0.042f, 0.052f, selected ? 0.92f : 0.82f),
            BorderColor = new Color(color.R, color.G, color.B, sideEffect ? 0.95f : selected ? 0.88f : 0.72f),
            BorderWidthBottom = sideEffect ? 2 : 1,
            BorderWidthLeft = sideEffect ? 2 : 1,
            BorderWidthRight = sideEffect ? 2 : 1,
            BorderWidthTop = sideEffect ? 2 : 1,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            ContentMarginBottom = 8,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
        };

    private static StyleBoxFlat TitleStyle(Color color, bool selected)
        => new()
        {
            BgColor = new Color(color.R, color.G, color.B, selected ? 0.86f : 0.72f),
            BorderWidthBottom = 0,
            BorderWidthLeft = 0,
            BorderWidthRight = 0,
            BorderWidthTop = 0,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            ContentMarginBottom = 5,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 5,
        };

    private static Color CategoryColor(string category)
        => category switch
        {
            "source" => new Color(0.45f, 0.72f, 1.00f),
            "geometry" => new Color(0.38f, 0.86f, 0.68f),
            "geosphere" => new Color(0.76f, 0.66f, 0.42f),
            "truth" => new Color(0.95f, 0.66f, 0.32f),
            "truthstream" => new Color(1.00f, 0.53f, 0.42f),
            "materialize" => new Color(0.58f, 0.74f, 1.00f),
            "cartography" => new Color(0.58f, 0.88f, 0.52f),
            "ecs" => new Color(0.82f, 0.62f, 1.00f),
            "timeline" => new Color(0.88f, 0.78f, 0.48f),
            _ => new Color(0.66f, 0.70f, 0.78f),
        };

    private static IEnumerable<VisualNode> ReadNodes(object viewModel)
    {
        var property = viewModel.GetType().GetProperty("Nodes", BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetValue(viewModel) is not IEnumerable items)
            yield break;

        foreach (var item in items)
        {
            var nodeId = ReadString(item, "NodeId");
            if (string.IsNullOrWhiteSpace(nodeId))
                continue;

            yield return new VisualNode(
                nodeId,
                ReadString(item, "TypeId") ?? nodeId,
                ReadString(item, "Category") ?? "node",
                ReadString(item, "TypeKey") ?? nodeId,
                ReadString(item, "Summary") ?? string.Empty,
                ReadBool(item, "IsSideEffect"),
                ReadBool(item, "IsExpensive"),
                ReadPorts(item, "Inputs").ToList(),
                ReadPorts(item, "Outputs").ToList(),
                ReadStringList(item, "ParameterLines"),
                ReadInt(item, "PreviewWidth"),
                ReadInt(item, "PreviewHeight"),
                ReadByteArray(item, "PreviewRgba"));
        }
    }

    private static IEnumerable<VisualPort> ReadPorts(object? item, string propertyName)
    {
        var property = item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetValue(item) is not IEnumerable ports)
            yield break;

        foreach (var port in ports)
        {
            yield return new VisualPort(
                ReadString(port, "Label") ?? "port",
                ReadString(port, "KindHint") ?? "value");
        }
    }

    private static string? ReadString(object? item, string propertyName)
        => item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item)?.ToString();

    private static bool ReadBool(object? item, string propertyName)
        => item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item) is bool value && value;

    private static int ReadInt(object? item, string propertyName)
        => item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item) is int value ? value : 0;

    private static byte[]? ReadByteArray(object? item, string propertyName)
        => item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item) as byte[];

    private static IReadOnlyList<string>? ReadStringList(object? item, string propertyName)
    {
        var val = item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item);
        if (val is IEnumerable<string> list)
            return list.ToList();
        if (val is IEnumerable enumerable)
            return enumerable.Cast<object>().Select(o => o?.ToString() ?? string.Empty).ToList();
        return null;
    }

    private sealed record VisualNode(
        string NodeId,
        string Title,
        string Category,
        string TypeKey,
        string Summary,
        bool IsSideEffect,
        bool IsExpensive,
        IReadOnlyList<VisualPort> Inputs,
        IReadOnlyList<VisualPort> Outputs,
        IReadOnlyList<string>? ParameterLines,
        int PreviewWidth,
        int PreviewHeight,
        byte[]? PreviewRgba);

    private sealed record VisualPort(string Label, string KindHint);
}
