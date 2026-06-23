using System.Collections;
using System.Reflection;
using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Ui.Seam;

internal static class GraphAnnotationFrameEnhancer
{
    private const string FramePrefix = "__annotation_frame_";
    private const int AutoshrinkMargin = 52;

    public static bool TryApply(GraphEdit graphEdit, object viewModel, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(graphEdit);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            var annotations = ReadAnnotations(viewModel).ToList();
            RemoveExistingFrames(graphEdit);

            if (annotations.Count == 0)
                return false;

            var graphNodes = graphEdit.GetChildren()
                .OfType<GraphNode>()
                .ToDictionary(node => node.Name.ToString(), StringComparer.Ordinal);

            var frames = 0;
            var attachments = 0;
            var missingNodes = 0;

            foreach (var annotation in annotations)
            {
                var nodeIds = annotation.NodeIds
                    .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (nodeIds.Count == 0)
                    continue;

                var attachedNodes = nodeIds
                    .Select(nodeId => graphNodes.TryGetValue(nodeId, out var graphNode) ? graphNode : null)
                    .Where(graphNode => graphNode is not null)
                    .Cast<GraphNode>()
                    .ToList();
                missingNodes += nodeIds.Count - attachedNodes.Count;
                if (attachedNodes.Count == 0)
                    continue;

                var frameName = new StringName($"{FramePrefix}{SafeName(annotation.AnnotationId)}");
                var frame = new GraphFrame
                {
                    Name = frameName,
                    Title = string.IsNullOrWhiteSpace(annotation.Label) ? annotation.AnnotationId : annotation.Label,
                    AutoshrinkEnabled = true,
                    AutoshrinkMargin = AutoshrinkMargin,
                    TintColorEnabled = true,
                    TintColor = ParseTint(annotation.Color),
                    TooltipText = string.IsNullOrWhiteSpace(annotation.Text) ? annotation.Kind : annotation.Text,
                };
                frame.AddThemeFontSizeOverride("title_font_size", 12);

                graphEdit.AddChild(frame);
                frames++;

                foreach (var graphNode in attachedNodes)
                {
                    graphEdit.AttachGraphElementToFrame(graphNode.Name, frameName);
                    attachments++;
                }
            }

            logger.LogInformation(
                "ViewRenderer: graph annotation frames applied to {Frames}/{Annotations} annotations ({Attachments} node attachments, {MissingNodes} missing nodes).",
                frames,
                annotations.Count,
                attachments,
                missingNodes);

            return frames > 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ViewRenderer: graph annotation frame rendering failed; keeping node graph without frames.");
            return false;
        }
    }

    private static void RemoveExistingFrames(GraphEdit graphEdit)
    {
        foreach (var child in graphEdit.GetChildren()
                     .Where(child => child is GraphFrame && child.Name.ToString().StartsWith(FramePrefix, StringComparison.Ordinal))
                     .ToList())
        {
            graphEdit.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static IEnumerable<VisualAnnotation> ReadAnnotations(object viewModel)
    {
        var property = viewModel.GetType().GetProperty("Annotations", BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetValue(viewModel) is not IEnumerable items)
            yield break;

        foreach (var item in items)
        {
            var annotationId = ReadString(item, "AnnotationId");
            if (string.IsNullOrWhiteSpace(annotationId))
                continue;

            yield return new VisualAnnotation(
                annotationId,
                ReadString(item, "Kind") ?? "comment",
                ReadString(item, "Label") ?? annotationId,
                ReadStringList(item, "NodeIds"),
                ReadString(item, "Text") ?? string.Empty,
                ReadString(item, "Color") ?? string.Empty);
        }
    }

    private static Color ParseTint(string value)
    {
        var fallback = new Color(0.34f, 0.54f, 0.96f, 0.18f);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var color = Color.FromString(value.Trim(), fallback);
        color.A = color.A <= 0f ? 0.18f : Math.Min(color.A, 0.28f);
        return color;
    }

    private static string SafeName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray();
        var safe = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(safe) ? "annotation" : safe;
    }

    private static string? ReadString(object? item, string propertyName)
        => item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item)?.ToString();

    private static IReadOnlyList<string> ReadStringList(object? item, string propertyName)
    {
        var val = item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item);
        if (val is IEnumerable<string> list)
            return list.ToList();
        if (val is IEnumerable enumerable)
            return enumerable.Cast<object>().Select(o => o?.ToString() ?? string.Empty).ToList();
        return Array.Empty<string>();
    }

    private sealed record VisualAnnotation(
        string AnnotationId,
        string Kind,
        string Label,
        IReadOnlyList<string> NodeIds,
        string Text,
        string Color);
}
