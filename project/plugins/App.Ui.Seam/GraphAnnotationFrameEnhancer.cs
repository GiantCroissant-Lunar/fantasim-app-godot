using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Ui.Seam;

internal static class GraphAnnotationFrameEnhancer
{
    private const string FramePrefix = "__annotation_frame_";
    private const int AutoshrinkMargin = 52;
    private const float MinFrameWidth = 120f;
    private const float MinFrameHeight = 80f;
    private static readonly ConditionalWeakTable<GraphEdit, FrameMoveSyncState> MoveSyncStates = new();

    public static bool TryApply(GraphEdit graphEdit, object viewModel, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(graphEdit);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            var annotations = ReadAnnotations(viewModel).ToList();
            RemoveExistingFrames(graphEdit);
            var moveSync = MoveSyncFor(graphEdit);
            moveSync.Clear();

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
                var frameSize = ClampFrameSize(annotation.Bounds.Size);
                // Persisted comment boundaries are explicit rectangles; autoshrink would rewrite user-authored bounds.
                // Source: https://docs.godotengine.org/en/stable/classes/class_graphframe.html
                var frame = new GraphFrame
                {
                    Name = frameName,
                    Title = string.IsNullOrWhiteSpace(annotation.Label) ? annotation.AnnotationId : annotation.Label,
                    PositionOffset = annotation.Bounds.Position,
                    Size = frameSize,
                    CustomMinimumSize = new Vector2(MinFrameWidth, MinFrameHeight),
                    Resizable = true,
                    Draggable = true,
                    AutoshrinkEnabled = false,
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

                BindFrameBoundsRoundTrip(moveSync, frame, annotation.AnnotationId, viewModel, logger);
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
                ReadBounds(item),
                ReadStringList(item, "NodeIds"),
                ReadString(item, "Text") ?? string.Empty,
                ReadString(item, "Color") ?? string.Empty);
        }
    }

    private static void BindFrameBoundsRoundTrip(
        FrameMoveSyncState moveSync,
        GraphFrame frame,
        string annotationId,
        object viewModel,
        ILogger logger)
    {
        var binding = new FrameBoundsBinding(annotationId, frame, viewModel, logger);
        moveSync.Register(binding);

        // Godot's GraphElement resize signals request a size; the caller must apply it.
        // Source: https://docs.godotengine.org/en/4.7/classes/class_graphelement.html
        frame.ResizeRequest += newSize => frame.Size = ClampFrameSize(newSize);
        frame.ResizeEnd += _ => binding.PersistIfChanged();
    }

    private static FrameMoveSyncState MoveSyncFor(GraphEdit graphEdit)
        => MoveSyncStates.GetValue(graphEdit, graph =>
        {
            var state = new FrameMoveSyncState();
            // Persist moves once at drag completion instead of on every position offset update.
            // Source: https://docs.godotengine.org/en/stable/classes/class_graphedit.html
            graph.EndNodeMove += state.PersistMovedFrames;
            return state;
        });

    private static void SetAnnotationBounds(object viewModel, string annotationId, Vector2 position, Vector2 size, ILogger logger)
    {
        if (!IsFinite(position.X) || !IsFinite(position.Y) || !IsFinite(size.X) || !IsFinite(size.Y))
            return;

        var method = viewModel.GetType().GetMethod(
            "SetAnnotationBounds",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[]
            {
                typeof(string),
                typeof(float),
                typeof(float),
                typeof(float),
                typeof(float)
            },
            modifiers: null);

        if (method is null)
            return;

        try
        {
            method.Invoke(viewModel, new object[] { annotationId, position.X, position.Y, size.X, size.Y });
        }
        catch (TargetInvocationException ex)
        {
            logger.LogWarning(ex.InnerException ?? ex, "ViewRenderer: graph annotation frame bounds update failed for {AnnotationId}.", annotationId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ViewRenderer: graph annotation frame bounds update failed for {AnnotationId}.", annotationId);
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

    private static VisualBounds ReadBounds(object? item)
    {
        var bounds = item?.GetType().GetProperty("Bounds", BindingFlags.Public | BindingFlags.Instance)?.GetValue(item);
        if (bounds is null)
            return VisualBounds.Default;

        return new VisualBounds(
            ReadFloat(bounds, "X", VisualBounds.Default.X),
            ReadFloat(bounds, "Y", VisualBounds.Default.Y),
            ReadPositiveFloat(bounds, "Width", VisualBounds.Default.Width),
            ReadPositiveFloat(bounds, "Height", VisualBounds.Default.Height));
    }

    private static string? ReadString(object? item, string propertyName)
        => item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item)?.ToString();

    private static float ReadPositiveFloat(object? item, string propertyName, float fallback)
    {
        var value = ReadFloat(item, propertyName, fallback);
        return value > 0f ? value : fallback;
    }

    private static float ReadFloat(object? item, string propertyName, float fallback)
    {
        var value = item?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item);
        if (value is null)
            return fallback;

        try
        {
            var converted = Convert.ToSingle(value, CultureInfo.InvariantCulture);
            return IsFinite(converted) ? converted : fallback;
        }
        catch (FormatException)
        {
            return fallback;
        }
        catch (InvalidCastException)
        {
            return fallback;
        }
        catch (OverflowException)
        {
            return fallback;
        }
    }

    private static bool SameBounds(Vector2 position, Vector2 size, Vector2 lastPosition, Vector2 lastSize)
        => NearlyEqual(position.X, lastPosition.X)
           && NearlyEqual(position.Y, lastPosition.Y)
           && NearlyEqual(size.X, lastSize.X)
           && NearlyEqual(size.Y, lastSize.Y);

    private static bool NearlyEqual(float left, float right)
        => Math.Abs(left - right) < 0.01f;

    private static Vector2 ClampFrameSize(Vector2 size)
        => new(Math.Max(MinFrameWidth, size.X), Math.Max(MinFrameHeight, size.Y));

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);

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
        VisualBounds Bounds,
        IReadOnlyList<string> NodeIds,
        string Text,
        string Color);

    private sealed record VisualBounds(float X, float Y, float Width, float Height)
    {
        public static VisualBounds Default { get; } = new(0f, 0f, 320f, 180f);

        public Vector2 Position => new(X, Y);

        public Vector2 Size => new(Width, Height);
    }

    private sealed class FrameMoveSyncState
    {
        private readonly Dictionary<string, FrameBoundsBinding> _bindings = new(StringComparer.Ordinal);

        public void Clear() => _bindings.Clear();

        public void Register(FrameBoundsBinding binding) => _bindings[binding.AnnotationId] = binding;

        public void PersistMovedFrames()
        {
            foreach (var binding in _bindings.Values.ToArray())
                binding.PersistIfChanged();
        }
    }

    private sealed class FrameBoundsBinding
    {
        private readonly GraphFrame _frame;
        private readonly object _viewModel;
        private readonly ILogger _logger;
        private Vector2 _lastPosition;
        private Vector2 _lastSize;

        public FrameBoundsBinding(string annotationId, GraphFrame frame, object viewModel, ILogger logger)
        {
            AnnotationId = annotationId;
            _frame = frame;
            _viewModel = viewModel;
            _logger = logger;
            _lastPosition = frame.PositionOffset;
            _lastSize = ClampFrameSize(frame.Size);
        }

        public string AnnotationId { get; }

        public void PersistIfChanged()
        {
            if (!GodotObject.IsInstanceValid(_frame))
                return;

            var position = _frame.PositionOffset;
            var size = ClampFrameSize(_frame.Size);
            if (SameBounds(position, size, _lastPosition, _lastSize))
                return;

            _lastPosition = position;
            _lastSize = size;
            SetAnnotationBounds(_viewModel, AnnotationId, position, size, _logger);
        }
    }
}
