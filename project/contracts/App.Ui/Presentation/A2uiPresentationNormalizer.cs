using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FantaSim.App.Ui.Presentation;

/// <summary>
/// Normalizes an A2UI-style <b>adjacency-list</b> UI document — a flat map of components keyed by id,
/// with children referenced by id — into a BoomHud <c>RuntimeComponentNode</c> JSON subtree (the nested
/// tree the runtime-surface renderer consumes). This lets an agent emit UI in the LLM-friendly flat form
/// (easy to generate and stream, no deep bracket-balancing) while the app keeps one canonical tree format.
///
/// Domain-neutral and <b>safe by construction</b>: it only emits generic component nodes, so the
/// downstream <c>RuntimeSurfaceValidator</c> still rejects unknown component types, unknown properties,
/// and oversized/too-deep documents. On any structural problem (missing root, dangling/cyclic id
/// reference, missing type, over-limit) it returns <c>null</c> so the caller falls back to a built-in
/// rendering — untrusted agent input can never inject arbitrary or malformed UI.
///
/// Input shape:
/// <code>
/// { "root": "detail",
///   "components": {
///     "detail": { "type": "container", "layout": "vertical", "children": ["title","meta"] },
///     "title":  { "type": "label", "text": "Reload bundle", "variant": "muted" },
///     "meta":   { "type": "container", "layout": "horizontal", "children": ["b1"] },
///     "b1":     { "type": "badge", "text": "ok", "variant": "success" } } }
/// </code>
/// </summary>
public static class A2uiPresentationNormalizer
{
    private const int MaxComponents = 256;
    private const int MaxDepth = 24;

    // A2UI flat prop name -> BoomHud property name (each becomes a {"literal": value} RuntimeValue).
    private static readonly IReadOnlyDictionary<string, string> ScalarProps = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["text"] = "text",
        ["variant"] = "variant",
        ["tooltip"] = "tooltip",
        ["title"] = "title",
        ["label"] = "label",
        ["value"] = "value",
        ["minimum"] = "minimum",
        ["maximum"] = "maximum",
        ["emptyText"] = "emptyText",
        ["selectedItem"] = "selectedItem",
        ["visible"] = "visible",
        ["enabled"] = "enabled",
        ["size"] = "size",
    };

    private static readonly string[] LayoutKeys =
        { "type", "gap", "align", "justify", "width", "height", "minWidth", "minHeight", "maxWidth", "maxHeight", "padding" };

    /// <summary>
    /// Parse and normalize <paramref name="a2uiJson"/>. Returns the BoomHud subtree root as a
    /// <see cref="JsonObject"/>, or <c>null</c> if the document is missing/malformed/structurally invalid.
    /// Every emitted id is prefixed with <paramref name="idPrefix"/> so multiple normalized subtrees can
    /// coexist in one surface without id collisions.
    /// </summary>
    public static JsonObject? Normalize(string? a2uiJson, string idPrefix)
    {
        if (string.IsNullOrWhiteSpace(a2uiJson))
            return null;

        JsonObject? document;
        try
        {
            document = JsonNode.Parse(a2uiJson) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (document is null)
            return null;

        var rootId = (document["root"] as JsonValue)?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(rootId)
            || document["components"] is not JsonObject components
            || components.Count == 0
            || components.Count > MaxComponents)
        {
            return null;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        return BuildNode(rootId!, components, idPrefix, visited, depth: 0);
    }

    private static JsonObject? BuildNode(
        string componentId, JsonObject components, string idPrefix, HashSet<string> visited, int depth)
    {
        if (depth > MaxDepth)
            return null;
        if (!visited.Add(componentId)) // cycle or reuse — the tree must be acyclic with unique ids
            return null;
        if (components[componentId] is not JsonObject component)
            return null; // dangling reference

        var type = (component["type"] as JsonValue)?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(type))
            return null;

        var node = new JsonObject
        {
            ["id"] = $"{idPrefix}-{componentId}",
            ["type"] = type,
        };

        if (BuildLayout(component["layout"]) is { } layout)
            node["layout"] = layout;

        var properties = BuildProperties(component);
        if (properties.Count > 0)
            node["properties"] = properties;

        if (component["actions"] is JsonArray actions && actions.Count > 0)
            node["actions"] = actions.DeepClone();

        if (component["children"] is JsonArray childIds && childIds.Count > 0)
        {
            var children = new JsonArray();
            foreach (var childIdNode in childIds)
            {
                var childId = (childIdNode as JsonValue)?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(childId))
                    return null;
                if (BuildNode(childId!, components, idPrefix, visited, depth + 1) is not { } childNode)
                    return null;
                children.Add(childNode);
            }

            node["children"] = children;
        }

        return node;
    }

    private static JsonObject? BuildLayout(JsonNode? layoutNode)
    {
        switch (layoutNode)
        {
            case JsonValue value when value.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name):
                return new JsonObject { ["type"] = name };
            case JsonObject obj:
                var layout = new JsonObject();
                foreach (var key in LayoutKeys)
                {
                    if (obj[key] is { } value)
                        layout[key] = value.DeepClone();
                }

                return layout.Count > 0 ? layout : null;
            default:
                return null;
        }
    }

    private static JsonObject BuildProperties(JsonObject component)
    {
        var properties = new JsonObject();

        foreach (var (a2Key, boomProperty) in ScalarProps)
        {
            if (component[a2Key] is { } value)
                properties[boomProperty] = new JsonObject { ["literal"] = value.DeepClone() };
        }

        // `items` (list contents) is an array literal rather than a scalar.
        if (component["items"] is JsonArray items)
            properties["items"] = new JsonObject { ["literal"] = items.DeepClone() };

        return properties;
    }
}
