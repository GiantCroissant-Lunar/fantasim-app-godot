using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;
using FantaSim.App.Activity;
using FantaSim.App.Ui.Presentation;

namespace FantaSim.App.Ui.Activity;

/// <summary>
/// Builds the activity-ledger runtime surface as a list of BoomHud "cards": each ledger entry becomes
/// an <c>item</c>-variant <c>panel</c> holding a header row (kind badge · name · time) and a meta row of
/// status pills, instead of a flat stack of indented labels. The versatile vocabulary (panel / badge /
/// container variants) is already in the <c>boomhud.runtime.basic.v1</c> catalog and the resident theme;
/// this builder just opts the activity surface into it.
///
/// The BoomHud renderer path has no scroll container (that lived only in the old hand-authored shell
/// scene), so only the newest <see cref="MaxCards"/> entries are rendered as cards; the remainder is
/// reported honestly in a footer line rather than silently dropped. Full per-entry detail lives in each
/// card's tooltip.
/// </summary>
internal static class ActivityPresentationDocumentBuilder
{
    private const int MaxCards = 15;

    public static RuntimeSurfaceDocument Build(
        string templateJson,
        IReadOnlyList<ActivityEntry> entries,
        bool ledgerAvailable,
        int revision)
    {
        if (string.IsNullOrWhiteSpace(templateJson))
            throw new InvalidOperationException("Activity presentation template is empty.");

        var rows = BuildRows(entries, ledgerAvailable);

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["activity.title"] = BuildTitle(entries, ledgerAvailable),
        };

        var slots = new Dictionary<string, JsonArray>(StringComparer.Ordinal)
        {
            ["activityRows"] = rows,
        };

        var binding = new PresentationTemplateBinding(Placeholders: placeholders, Slots: slots);
        return PresentationTemplateBinder.Bind(templateJson, binding, revision);
    }

    private static string BuildTitle(IReadOnlyList<ActivityEntry> entries, bool ledgerAvailable)
    {
        if (!ledgerAvailable)
            return "Activity ledger  -  unavailable";

        var shown = Math.Min(MaxCards, entries.Count);
        var commandCount = entries.Count(entry =>
            entry.Kind is ActivityEntryKind.DomainCommand or ActivityEntryKind.CommandResult);
        var failureCount = entries.Count(IsFailure);
        return $"Activity ledger  -  {shown} of {entries.Count} recent  -  {commandCount} cmd  -  {failureCount} failed";
    }

    private static JsonArray BuildRows(IReadOnlyList<ActivityEntry> entries, bool ledgerAvailable)
    {
        var rows = new JsonArray();

        if (!ledgerAvailable)
        {
            rows.Add(InfoLabel("activity-unavailable", "(activity ledger unavailable)", "danger"));
            return rows;
        }

        if (entries.Count == 0)
        {
            rows.Add(InfoLabel(
                "activity-empty",
                "(no activity recorded yet - entries appear as commands are dispatched)",
                "muted"));
            return rows;
        }

        var shown = Math.Min(MaxCards, entries.Count);
        for (var index = 0; index < shown; index++)
            rows.Add(BuildCard(index, entries[index]));

        if (entries.Count > shown)
            rows.Add(InfoLabel(
                "activity-more",
                $"+ {entries.Count - shown} earlier entries (scrolling not yet available in card view)",
                "muted"));

        return rows;
    }

    // A ledger entry as a card: item-variant panel > [ header row, optional descriptor line, meta pills ].
    private static JsonObject BuildCard(int index, ActivityEntry entry)
    {
        var id = $"activity-card-{index}";
        var tooltip = BuildTooltip(entry);

        // Header packs left — kind · name · time — with a trailing spacer absorbing the slack, so the
        // time never gets pushed to (and clipped at) the panel's right edge.
        var header = Node(
            $"{id}-header",
            "container",
            layout: Layout("horizontal", gap: 8, align: "center"),
            children: new JsonArray
            {
                Badge($"{id}-kind", KindTag(entry.Kind).Trim(), KindVariant(entry)),
                Label($"{id}-name", Truncate(entry.Name, 46)),
                Label($"{id}-time", entry.Timestamp.ToLocalTime().ToString("HH:mm:ss"), variant: "muted"),
                Node($"{id}-header-spacer", "spacer"),
            });

        var children = new JsonArray { header };

        var descriptorTitle = ReadDescriptorTitle(entry);
        if (!string.IsNullOrWhiteSpace(descriptorTitle))
            children.Add(Label($"{id}-desc", Truncate(descriptorTitle!, 64), variant: "muted", tooltip: tooltip));

        var meta = BuildMetaRow(id, entry);
        if (meta is not null)
            children.Add(meta);

        var properties = new JsonObject { ["variant"] = Val("item") };
        if (!string.IsNullOrWhiteSpace(tooltip))
            properties["tooltip"] = Val(tooltip);

        return Node(id, "panel", properties: properties, layout: Layout("vertical", gap: 4), children: children);
    }

    private static JsonObject? BuildMetaRow(string id, ActivityEntry entry)
    {
        var pills = new JsonArray();

        var actor = FormatActor(entry.Actor);
        if (!string.IsNullOrWhiteSpace(actor))
            pills.Add(Badge($"{id}-actor", Truncate(actor, 16), "neutral"));

        if (!string.IsNullOrWhiteSpace(entry.Category))
            pills.Add(Badge($"{id}-category", Truncate(entry.Category!, 18), "neutral"));

        var (status, statusVariant) = FormatStatus(entry);
        if (!string.IsNullOrWhiteSpace(status))
            pills.Add(Badge($"{id}-status", Truncate(status!, 24), statusVariant));

        if (pills.Count == 0)
            return null;

        // Trailing spacer packs the pills to the left instead of stretching them to fill the row.
        pills.Add(Node($"{id}-meta-spacer", "spacer"));
        return Node($"{id}-meta", "container", layout: Layout("horizontal", gap: 6, align: "center"), children: pills);
    }

    // ----- component-node construction (RuntimeComponentNode JSON shape) -----

    private static JsonObject Node(
        string id,
        string type,
        JsonObject? properties = null,
        JsonObject? layout = null,
        JsonArray? children = null)
    {
        var node = new JsonObject { ["id"] = id, ["type"] = type };
        if (layout is not null) node["layout"] = layout;
        if (properties is not null) node["properties"] = properties;
        if (children is not null) node["children"] = children;
        return node;
    }

    private static JsonObject Layout(string type, int? gap = null, string? align = null)
    {
        var layout = new JsonObject { ["type"] = type };
        if (gap is not null) layout["gap"] = gap;
        if (align is not null) layout["align"] = align;
        return layout;
    }

    private static JsonObject Val(string literal) => new() { ["literal"] = literal };

    private static JsonObject Badge(string id, string text, string variant)
        => Node(id, "badge", new JsonObject { ["text"] = Val(text), ["variant"] = Val(variant) });

    private static JsonObject Label(string id, string text, string? variant = null, string? tooltip = null)
    {
        var properties = new JsonObject { ["text"] = Val(text) };
        if (variant is not null) properties["variant"] = Val(variant);
        if (tooltip is not null) properties["tooltip"] = Val(tooltip);
        return Node(id, "label", properties);
    }

    private static JsonObject InfoLabel(string id, string text, string variant)
        => Label(id, text, variant);

    // ----- entry → presentation mapping -----

    private static string KindVariant(ActivityEntry entry) => entry.Kind switch
    {
        ActivityEntryKind.UiOperation => "neutral",
        ActivityEntryKind.DomainCommand => "info",
        ActivityEntryKind.CommandResult => IsFailure(entry) ? "danger" : "success",
        ActivityEntryKind.Log => IsFailure(entry) ? "danger" : "neutral",
        _ => "neutral",
    };

    private static (string? Status, string Variant) FormatStatus(ActivityEntry entry)
    {
        if (IsFailure(entry))
        {
            var errorType = TryReadCommandCard(entry) is { } card ? ReadString(card, "errorType") : string.Empty;
            return (string.IsNullOrWhiteSpace(errorType) ? "failed" : $"failed: {errorType}", "danger");
        }

        if (!string.IsNullOrWhiteSpace(entry.Outcome))
        {
            var variant = entry.Outcome!.Trim().ToLowerInvariant() switch
            {
                "ok" => "success",
                "requested" => "warning",
                _ => "neutral",
            };
            return (entry.Outcome, variant);
        }

        return entry.Kind == ActivityEntryKind.CommandResult ? ("ok", "success") : (null, "neutral");
    }

    private static bool IsFailure(ActivityEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Error))
            return true;
        return TryReadCommandCard(entry) is { } card && !string.IsNullOrWhiteSpace(ReadString(card, "errorType"));
    }

    // Short actor tag for the pill (e.g. "http", "user", "system"); the full "kind:id" is in the tooltip.
    private static string FormatActor(ActivityActor? actor)
        => actor?.Kind ?? string.Empty;

    private static string? ReadDescriptorTitle(ActivityEntry entry)
        => TryReadCommandCard(entry) is { } card ? ReadString(card, "descriptorTitle") : null;

    private static string BuildTooltip(ActivityEntry entry)
    {
        var lines = new List<string> { FormatEntry(entry) };

        if (TryReadCommandCard(entry) is { } card)
        {
            var title = ReadString(card, "descriptorTitle");
            if (!string.IsNullOrWhiteSpace(title))
                lines.Add(title);
            var description = ReadString(card, "descriptorDescription");
            if (!string.IsNullOrWhiteSpace(description))
                lines.Add(description);
        }

        var details = FormatPayloadDetails(entry);
        if (!string.IsNullOrWhiteSpace(details))
            lines.Add(details);

        if (!string.IsNullOrWhiteSpace(entry.Error))
            lines.Add($"error: {entry.Error}");
        else if (!string.IsNullOrWhiteSpace(entry.Outcome))
            lines.Add($"outcome: {entry.Outcome}");

        return string.Join("\n", lines);
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;
        return value[..Math.Max(1, max - 1)].TrimEnd() + "…";
    }

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

    // "HH:mm:ss  [kind]  Name  -  actor  -  category" (tooltip header line)
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
