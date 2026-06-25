using System;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;
using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Ui.Seam;

/// <summary>
/// Data-driven binder for optional Godot shell scenes. Feature bundles supply
/// binding rules in RuntimeSurfaceDocument.DataModel.shell.bindings; this class
/// only interprets the generic rule vocabulary.
/// </summary>
internal sealed class PresentationShellBinder
{
    private readonly ILogger _logger;

    public PresentationShellBinder(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Bind(Control shell, RuntimeSurfaceDocument document, Action<string, string?> dispatch)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(dispatch);

        var model = document.DataModel;
        var bindings = Select(model, "shell.bindings") as JsonArray;
        if (bindings is null || bindings.Count == 0)
        {
            _logger.LogDebug("No shell bindings declared for {SurfaceId}.", document.SurfaceId);
            return;
        }

        foreach (var bindingNode in bindings)
        {
            if (bindingNode is not JsonObject binding)
                continue;

            var kind = ReadString(binding, "kind");
            try
            {
                switch (kind)
                {
                    case "text":
                        BindText(shell, model, binding);
                        break;
                    case "command":
                        BindCommand(shell, model, binding, dispatch);
                        break;
                    case "list":
                        BindList(shell, model, binding);
                        break;
                    default:
                        _logger.LogWarning("Unknown shell binding kind '{Kind}' for {SurfaceId}.", kind, document.SurfaceId);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Shell binding failed for kind '{Kind}' on {SurfaceId}.", kind, document.SurfaceId);
            }
        }

        _logger.LogInformation("Shell bindings applied for {SurfaceId}: {BindingCount}", document.SurfaceId, bindings.Count);
    }

    private static void BindText(Node shell, JsonNode? model, JsonObject binding)
    {
        var nodeName = ReadString(binding, "node");
        if (string.IsNullOrWhiteSpace(nodeName))
            return;

        var text = ReadBoundText(model, binding);
        if (Find<Label>(shell, nodeName) is { } label)
        {
            label.Text = text;
            return;
        }

        if (Find<Button>(shell, nodeName) is { } button)
            button.Text = text;
    }

    private static void BindCommand(Node shell, JsonNode? model, JsonObject binding, Action<string, string?> dispatch)
    {
        var nodeName = ReadString(binding, "node");
        var command = ReadString(binding, "command");
        if (string.IsNullOrWhiteSpace(nodeName) || string.IsNullOrWhiteSpace(command))
            return;

        if (Find<Button>(shell, nodeName) is not { } button)
            return;

        if (!string.IsNullOrWhiteSpace(ReadString(binding, "text")) || !string.IsNullOrWhiteSpace(ReadString(binding, "textPath")))
            button.Text = ReadBoundText(model, binding, valueKey: "text", pathKey: "textPath");

        button.Pressed += () => dispatch(command, button.Name.ToString());
    }

    private static void BindList(Node shell, JsonNode? model, JsonObject binding)
    {
        var nodeName = ReadString(binding, "node");
        var path = ReadString(binding, "path");
        if (string.IsNullOrWhiteSpace(nodeName) || string.IsNullOrWhiteSpace(path))
            return;

        if (Find<Container>(shell, nodeName) is not { } list)
            return;

        ClearChildren(list);
        var items = Select(model, path) as JsonArray ?? new JsonArray();
        var lineRules = binding["lines"] as JsonArray;

        if (items.Count == 0)
        {
            var emptyText = ReadString(binding, "emptyText");
            if (!string.IsNullOrWhiteSpace(emptyText))
                list.AddChild(MakeLabel(emptyText, "muted"));
            return;
        }

        foreach (var item in items)
        {
            if (lineRules is null || lineRules.Count == 0)
            {
                list.AddChild(MakeLabel(Text(item, string.Empty), "normal"));
                continue;
            }

            foreach (var lineNode in lineRules)
            {
                if (lineNode is not JsonObject line)
                    continue;

                var lineText = ReadBoundText(item, line, pathKey: "path");
                var skipEmpty = ReadBool(line, "skipEmpty", fallback: true);
                if (skipEmpty && string.IsNullOrWhiteSpace(lineText))
                    continue;

                var prefix = ReadString(line, "prefix");
                var suffix = ReadString(line, "suffix");
                var style = ReadString(line, "style", "normal");
                list.AddChild(MakeLabel(prefix + lineText + suffix, style));
            }
        }
    }

    private static Label MakeLabel(string text, string style)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipText = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };

        label.AddThemeColorOverride("font_color", style switch
        {
            "error" => new Color(1.0f, 0.46f, 0.48f),
            "muted" => new Color(0.66f, 0.70f, 0.74f),
            _ => new Color(0.90f, 0.92f, 0.95f),
        });
        return label;
    }

    private static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static T? Find<T>(Node root, string nodeName) where T : Node
        => root.FindChild(nodeName, recursive: true, owned: false) as T;

    private static string ReadBoundText(JsonNode? model, JsonObject binding, string valueKey = "value", string pathKey = "path")
    {
        var literal = ReadString(binding, valueKey);
        if (!string.IsNullOrEmpty(literal))
            return literal;

        var path = ReadString(binding, pathKey);
        return string.IsNullOrWhiteSpace(path) ? string.Empty : Text(model, path);
    }

    private static JsonNode? Select(JsonNode? node, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return node;

        var current = node;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is JsonObject obj && obj.TryGetPropertyValue(segment, out current))
                continue;
            return null;
        }

        return current;
    }

    private static string Text(JsonNode? node, string path, string fallback = "")
    {
        var value = string.IsNullOrWhiteSpace(path) ? node : Select(node, path);
        if (value is null || value.GetValueKind() == System.Text.Json.JsonValueKind.Null)
            return fallback;

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var text))
                return text ?? fallback;
            if (jsonValue.TryGetValue<int>(out var intValue))
                return intValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<long>(out var longValue))
                return longValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<double>(out var doubleValue))
                return doubleValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<bool>(out var boolValue))
                return boolValue ? "true" : "false";
        }

        return value.ToJsonString();
    }

    private static string ReadString(JsonObject obj, string key, string fallback = "")
        => obj.TryGetPropertyValue(key, out var node) ? Text(node, string.Empty, fallback) : fallback;

    private static bool ReadBool(JsonObject obj, string key, bool fallback = false)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue value)
            return fallback;
        if (value.TryGetValue<bool>(out var boolValue))
            return boolValue;
        return value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed) ? parsed : fallback;
    }
}
