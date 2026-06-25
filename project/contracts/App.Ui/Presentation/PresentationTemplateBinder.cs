using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;

namespace FantaSim.App.Ui.Presentation;

/// <summary>
/// Caller-supplied inputs that drive <see cref="PresentationTemplateBinder.Bind"/>.
/// Domain-neutral: no Activity, ledger, Godot, NodeGraph, Timeline, or bundle types.
/// </summary>
/// <param name="Placeholders">Maps <c>${key}</c> placeholders to replacement strings.</param>
/// <param name="Slots">Maps a <c>"slot"</c> name to the array that replaces the slot object in its parent array. Inserted nodes are deep-cloned.</param>
/// <param name="RemoveTopLevelProperties">Top-level template properties to strip (e.g. <c>activityTemplates</c>).</param>
public sealed record PresentationTemplateBinding(
    IReadOnlyDictionary<string, string>? Placeholders = null,
    IReadOnlyDictionary<string, JsonArray>? Slots = null,
    IReadOnlySet<string>? RemoveTopLevelProperties = null);

/// <summary>
/// Generic, reusable presentation-template binder for hot-reloadable UI bundles.
/// </summary>
public static class PresentationTemplateBinder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Parse <paramref name="templateJson"/>, clone it, stamp <paramref name="revision"/>,
    /// strip <see cref="PresentationTemplateBinding.RemoveTopLevelProperties"/>, replace
    /// <c>${key}</c> placeholders, expand <c>slot</c> objects, and deserialize into
    /// a <see cref="RuntimeSurfaceDocument"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Empty/malformed JSON, non-object root, or deserialization failure.</exception>
    public static RuntimeSurfaceDocument Bind(string templateJson, PresentationTemplateBinding binding, int revision)
    {
        if (string.IsNullOrWhiteSpace(templateJson))
            throw new InvalidOperationException("Presentation template is empty.");

        JsonObject template;
        try
        {
            template = JsonNode.Parse(templateJson) as JsonObject
                ?? throw new InvalidOperationException("Presentation template root must be a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Presentation template is malformed JSON.", ex);
        }

        var document = template.DeepClone().AsObject();
        document["revision"] = revision;

        if (binding.RemoveTopLevelProperties is { Count: > 0 } remove)
        {
            foreach (var name in remove)
                document.Remove(name);
        }

        var values = binding.Placeholders;
        if (values is { Count: > 0 })
            ReplacePlaceholders(document, values);

        var slots = binding.Slots;
        if (slots is { Count: > 0 })
            ReplaceSlots(document, slots);

        try
        {
            return document.Deserialize<RuntimeSurfaceDocument>(JsonOptions)
                ?? throw new InvalidOperationException("Presentation document failed to deserialize into RuntimeSurfaceDocument.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Presentation document failed to deserialize into RuntimeSurfaceDocument.", ex);
        }
    }

    /// <summary>
    /// Deep-clone the named template from <paramref name="templates"/> and substitute
    /// <c>${key}</c> placeholders from <paramref name="values"/>. The caller owns the returned node.
    /// </summary>
    /// <exception cref="InvalidOperationException">No template named <paramref name="name"/>.</exception>
    public static JsonNode CloneTemplate(JsonObject templates, string name, IReadOnlyDictionary<string, string> values)
    {
        if (templates[name] is not JsonObject template)
            throw new InvalidOperationException($"Presentation template is missing '{name}'.");

        var clone = template.DeepClone();
        if (values is { Count: > 0 })
            ReplacePlaceholders(clone, values);
        return clone;
    }

    private static void ReplaceSlots(JsonNode node, IReadOnlyDictionary<string, JsonArray> slots)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (property.Value is JsonArray array)
                    ReplaceSlots(array, slots);
                else if (property.Value is not null)
                    ReplaceSlots(property.Value, slots);
            }
            return;
        }

        if (node is not JsonArray children)
            return;

        for (var index = children.Count - 1; index >= 0; index--)
        {
            if (children[index] is JsonObject child
                && child.TryGetPropertyValue("slot", out var slotNode)
                && ReadString(slotNode) is string slotName
                && slots.TryGetValue(slotName, out var slotRows))
            {
                children.RemoveAt(index);
                for (var rowIndex = slotRows.Count - 1; rowIndex >= 0; rowIndex--)
                    children.Insert(index, slotRows[rowIndex]!.DeepClone());
                continue;
            }

            if (children[index] is not null)
                ReplaceSlots(children[index]!, slots);
        }
    }

    private static void ReplacePlaceholders(JsonNode node, IReadOnlyDictionary<string, string> values)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (property.Value is JsonValue valueNode && valueNode.TryGetValue<string>(out var text))
                {
                    obj[property.Key] = Replace(text ?? string.Empty, values);
                }
                else if (property.Value is not null)
                {
                    ReplacePlaceholders(property.Value, values);
                }
            }
            return;
        }

        if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue valueNode && valueNode.TryGetValue<string>(out var text))
                {
                    array[index] = Replace(text ?? string.Empty, values);
                }
                else if (array[index] is not null)
                {
                    ReplacePlaceholders(array[index]!, values);
                }
            }
        }
    }

    private static string Replace(string text, IReadOnlyDictionary<string, string> values)
    {
        foreach (var (key, value) in values)
            text = text.Replace("${" + key + "}", value, StringComparison.Ordinal);
        return text;
    }

    private static string ReadString(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return text ?? string.Empty;
        return node?.ToString() ?? string.Empty;
    }
}
