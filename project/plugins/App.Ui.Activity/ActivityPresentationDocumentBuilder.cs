using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;
using FantaSim.App.Activity;
using FantaSim.App.Ui.Presentation;

namespace FantaSim.App.Ui.Activity;

internal static class ActivityPresentationDocumentBuilder
{
    public static RuntimeSurfaceDocument Build(
        string templateJson,
        IReadOnlyList<ActivityEntry> entries,
        bool ledgerAvailable,
        int revision)
    {
        if (string.IsNullOrWhiteSpace(templateJson))
            throw new InvalidOperationException("Activity presentation template is empty.");

        JsonObject template;
        try
        {
            template = JsonNode.Parse(templateJson) as JsonObject
                ?? throw new InvalidOperationException("Activity presentation template root must be a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Activity presentation template is malformed JSON.", ex);
        }

        var templates = template["activityTemplates"] as JsonObject
            ?? throw new InvalidOperationException("Activity presentation template is missing 'activityTemplates'.");

        var rows = BuildRows(templates, entries, ledgerAvailable);
        var shellRows = BuildShellRows(entries, ledgerAvailable);
        var shellBindings = ReadShellBindings(template);

        var userCount = entries.Count(entry => string.Equals(entry.Actor.Kind, "user", StringComparison.OrdinalIgnoreCase));
        var systemCount = entries.Count(entry => string.Equals(entry.Actor.Kind, "system", StringComparison.OrdinalIgnoreCase));
        var commandCount = entries.Count(entry =>
            entry.Kind is ActivityEntryKind.DomainCommand or ActivityEntryKind.CommandResult);
        var failureCount = entries.Count(entry => !string.IsNullOrWhiteSpace(entry.Error));
        var title =
            $"ACTIVITY LEDGER  -  {entries.Count} shown  -  user {userCount} / system {systemCount}  -  commands {commandCount}  -  failures {failureCount}";

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["activity.title"] = title,
        };

        var slots = new Dictionary<string, JsonArray>(StringComparer.Ordinal)
        {
            ["activityRows"] = rows
        };

        var removeTopLevelProperties = new HashSet<string>(StringComparer.Ordinal) { "activityTemplates", "shellBindings" };

        var binding = new PresentationTemplateBinding(
            Placeholders: placeholders,
            Slots: slots,
            RemoveTopLevelProperties: removeTopLevelProperties);

        var document = PresentationTemplateBinder.Bind(templateJson, binding, revision);
        return document with
        {
            DataModel = new JsonObject
            {
                ["title"] = title,
                ["rows"] = shellRows,
                ["shell"] = new JsonObject
                {
                    ["bindings"] = shellBindings,
                },
            },
        };
    }

    private static JsonArray BuildRows(JsonObject templates, IReadOnlyList<ActivityEntry> entries, bool ledgerAvailable)
    {
        var rows = new JsonArray();
        if (!ledgerAvailable)
        {
            rows.Add(PresentationTemplateBinder.CloneTemplate(templates, "unavailable", EmptyValues));
            return rows;
        }

        if (entries.Count == 0)
        {
            rows.Add(PresentationTemplateBinder.CloneTemplate(templates, "empty", EmptyValues));
            return rows;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["row.id"] = index.ToString(),
                ["row.text"] = FormatEntry(entry),
                ["row.detail"] = FormatPayloadDetails(entry) ?? string.Empty,
                ["row.error"] = entry.Error ?? string.Empty,
                ["row.outcome"] = entry.Outcome ?? string.Empty,
            };

            rows.Add(PresentationTemplateBinder.CloneTemplate(templates, "entry", values));
            AppendCommandCardTemplates(rows, templates, entry, values);
            if (!string.IsNullOrWhiteSpace(values["row.detail"]))
                rows.Add(PresentationTemplateBinder.CloneTemplate(templates, "detail", values));
            if (!string.IsNullOrWhiteSpace(values["row.error"]))
                rows.Add(PresentationTemplateBinder.CloneTemplate(templates, "error", values));
            else if (!string.IsNullOrWhiteSpace(values["row.outcome"]))
                rows.Add(PresentationTemplateBinder.CloneTemplate(templates, "outcome", values));
        }

        return rows;
    }

    private static JsonArray BuildShellRows(IReadOnlyList<ActivityEntry> entries, bool ledgerAvailable)
    {
        var rows = new JsonArray();
        if (!ledgerAvailable)
        {
            rows.Add(new JsonObject { ["text"] = "(activity ledger unavailable)" });
            return rows;
        }

        if (entries.Count == 0)
        {
            rows.Add(new JsonObject { ["text"] = "(no activity recorded yet - entries appear as commands are dispatched)" });
            return rows;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var row = new JsonObject
            {
                ["id"] = index,
                ["text"] = FormatEntry(entry),
                ["detail"] = FormatPayloadDetails(entry) ?? string.Empty,
                ["error"] = entry.Error ?? string.Empty,
                ["outcome"] = entry.Outcome ?? string.Empty,
            };
            if (TryReadCommandCard(entry) is { } card)
                row["command"] = card;
            rows.Add(row);
        }

        return rows;
    }

    private static JsonArray ReadShellBindings(JsonObject template)
        => template["shellBindings"]?.DeepClone() as JsonArray ?? new JsonArray();

    private static JsonObject? TryReadCommandCard(ActivityEntry entry)
    {
        if (entry.Kind is not (ActivityEntryKind.DomainCommand or ActivityEntryKind.CommandResult))
            return null;
        if (string.IsNullOrWhiteSpace(entry.PayloadJson))
            return null;

        try
        {
            if (JsonNode.Parse(entry.PayloadJson) is not JsonObject payload)
                return null;

            return payload.TryGetPropertyValue("command", out var commandNode) && commandNode is not null
                ? payload
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void AppendCommandCardTemplates(
        JsonArray rows, JsonObject templates, ActivityEntry entry, Dictionary<string, string> values)
    {
        if (TryReadCommandCard(entry) is not { } card)
            return;

        var title = ReadString(card, "descriptorTitle");
        var description = ReadString(card, "descriptorDescription");
        var status = FormatCommandStatus(entry, card);

        values["row.commandTitle"] = title;
        values["row.commandDescription"] = description;
        values["row.commandStatus"] = status;

        if (!string.IsNullOrWhiteSpace(title))
            rows.Add(PresentationTemplateBinder.CloneTemplate(templates, "commandTitle", values));
        if (!string.IsNullOrWhiteSpace(description))
            rows.Add(PresentationTemplateBinder.CloneTemplate(templates, "commandDescription", values));
        if (!string.IsNullOrWhiteSpace(status))
            rows.Add(PresentationTemplateBinder.CloneTemplate(templates, "commandStatus", values));
    }

    private static string FormatCommandStatus(ActivityEntry entry, JsonObject card)
    {
        if (entry.Kind != ActivityEntryKind.CommandResult)
            return "requested";

        var errorType = ReadString(card, "errorType");
        return string.IsNullOrWhiteSpace(errorType) ? "ok" : $"failed: {errorType}";
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyValues =
        new Dictionary<string, string>(StringComparer.Ordinal);

    // "HH:mm:ss  [kind]  Name  -  actor  -  category"
    private static string FormatEntry(ActivityEntry entry)
    {
        var time = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        var actor = string.IsNullOrEmpty(entry.Actor?.Id)
            ? entry.Actor?.Kind ?? "?"
            : $"{entry.Actor!.Kind}:{entry.Actor.Id}";
        var category = string.IsNullOrWhiteSpace(entry.Category) ? "" : $"  -  {entry.Category}";
        return $"{time}  [{KindTag(entry.Kind)}]  {entry.Name}  -  {actor}{category}";
    }

    private static string? FormatPayloadDetails(ActivityEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.PayloadJson))
            return null;

        try
        {
            if (JsonNode.Parse(entry.PayloadJson) is not JsonObject payload)
                return null;

            var parts = new List<string>();
            foreach (var key in DetailKeys)
            {
                var value = ReadString(payload, key);
                if (!string.IsNullOrWhiteSpace(value))
                    AddPart(parts, LabelFor(key), key.EndsWith("Path", StringComparison.OrdinalIgnoreCase) ? CompactPath(value) : value);
            }

            if (ReadInt(payload, "rowCount") is { } rowCount)
                parts.Add($"rows: {rowCount}");
            if (ReadInt(payload, "nodeCount") is { } nodeCount)
                parts.Add($"nodes: {nodeCount}");
            if (ReadInt(payload, "wireCount") is { } wireCount)
                parts.Add($"wires: {wireCount}");
            if (ReadInt(payload, "activeScenes") is { } activeScenes)
                parts.Add($"active scenes: {activeScenes}");

            return parts.Count == 0 ? null : string.Join("  -  ", parts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void AddPart(List<string> parts, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parts.Add($"{key}: {value}");
    }

    private static readonly string[] DetailKeys =
    {
        "command",
        "viewId",
        "recipe",
        "runId",
        "nodeId",
        "functionId",
        "status",
        "sceneId",
        "parentSceneId",
        "bundleId",
        "title",
        "jobId",
        "bodyName",
        "sourcePath",
        "artifactPath",
        "glb_path",
        "usd_path",
        "path",
    };

    private static string LabelFor(string key) => key switch
    {
        "viewId" => "view",
        "runId" => "run",
        "nodeId" => "node",
        "functionId" => "fn",
        "sceneId" => "scene",
        "parentSceneId" => "parent",
        "bundleId" => "bundle",
        "title" => "tool",
        "jobId" => "job",
        "bodyName" => "body",
        "sourcePath" => "source",
        "artifactPath" => "artifact",
        "glb_path" => "glb",
        "usd_path" => "usd",
        _ => key,
    };

    private static string ReadString(JsonObject payload, string key)
    {
        if (!payload.TryGetPropertyValue(key, out var value) || value is null)
            return string.Empty;
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
            return text ?? string.Empty;
        return value.ToString();
    }

    private static int? ReadInt(JsonObject payload, string key)
    {
        if (!payload.TryGetPropertyValue(key, out var value) || value is not JsonValue jsonValue)
            return null;
        if (jsonValue.TryGetValue<int>(out var intValue))
            return intValue;
        if (jsonValue.TryGetValue<long>(out var longValue))
            return (int)longValue;
        if (jsonValue.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed))
            return parsed;
        return null;
    }

    private static string CompactPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 2)
            return normalized;

        return $".../{segments[^2]}/{segments[^1]}";
    }

    private static string KindTag(ActivityEntryKind kind) => kind switch
    {
        ActivityEntryKind.UiOperation => "ui ",
        ActivityEntryKind.DomainCommand => "cmd",
        ActivityEntryKind.CommandResult => "res",
        ActivityEntryKind.Log => "log",
        _ => "?  ",
    };
}
