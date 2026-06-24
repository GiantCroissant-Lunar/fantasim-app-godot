using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;
using CrosscutFoundation.Messaging;
using FantaSim.App.Activity;
using FantaSim.App.Ui;
using LedgerService = FantaSim.App.Activity.IService;

namespace FantaSim.App.Ui.Activity;

/// <summary>
/// A live view of the activity ledger: the most recent <see cref="MaxEntries"/> entries, newest first
/// (the same stream the <c>activity.recent</c> remote command returns). Pure C# — no Godot, no ReactiveUI.
///
/// It refreshes itself: it subscribes to <see cref="ActivityEntry"/> on the resident crosscut bus and
/// raises <see cref="Changed"/> on each new entry, so the resident <c>ViewRenderer</c> re-reads it. That
/// bus subscription is the ONE resident root into this bundle's ALC, so it is dropped in
/// <see cref="Dispose"/> (the plugin disposes the source on unload) — otherwise the collectible ALC
/// would leak across reloads.
/// </summary>
public sealed class ActivityViewSource : IViewSource, IDisposable
{
    private const int MaxEntries = 60;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly LedgerService? _ledger;
    private readonly IMessageBus? _bus;
    private readonly IDisposable? _entrySubscription;
    private int _revision;
    private bool _disposed;

    public ActivityViewSource(LedgerService? ledger, IMessageBus? bus)
    {
        _ledger = ledger;
        _bus = bus;
        _entrySubscription = bus?.Subscribe<ActivityEntry>(_ => Changed?.Invoke());
    }

    public string ViewId => "activity";

    /// <summary>Raised when a new ledger entry arrives (off the main thread) or on an explicit refresh.</summary>
    public event Action? Changed;

    public RuntimeSurfaceDocument BuildDocument()
    {
        var children = new JsonArray();
        var id = 0;
        JsonObject Label(string text) => new()
        {
            ["id"] = $"n{id++}",
            ["type"] = "label",
            ["properties"] = new JsonObject { ["text"] = new JsonObject { ["literal"] = text } },
        };
        JsonObject Button(string text, string command) => new()
        {
            ["id"] = $"n{id++}",
            ["type"] = "button",
            ["properties"] = new JsonObject { ["text"] = new JsonObject { ["literal"] = text } },
            ["actions"] = new JsonArray { new JsonObject { ["event"] = "pressed", ["command"] = command } },
        };

        var entries = _ledger?.QueryLatest(MaxEntries) ?? (IReadOnlyList<ActivityEntry>)Array.Empty<ActivityEntry>();

        children.Add(Label($"ACTIVITY LEDGER  (newest first)   —   {entries.Count} shown"));
        children.Add(Button("↻ Refresh", "refresh"));
        children.Add(Label(""));

        if (_ledger is null)
        {
            children.Add(Label("   (activity ledger unavailable)"));
        }
        else if (entries.Count == 0)
        {
            children.Add(Label("   (no activity recorded yet — entries appear as commands are dispatched)"));
        }
        else
        {
            foreach (var e in entries)
            {
                children.Add(Label(FormatEntry(e)));
                var payloadDetails = FormatPayloadDetails(e);
                if (!string.IsNullOrWhiteSpace(payloadDetails))
                    children.Add(Label($"        {payloadDetails}"));
                if (!string.IsNullOrWhiteSpace(e.Error))
                    children.Add(Label($"        ⚠ {e.Error}"));
                else if (!string.IsNullOrWhiteSpace(e.Outcome))
                    children.Add(Label($"        ↳ {e.Outcome}"));
            }
        }

        children.Add(Label(""));
        children.Add(Button("Hide", "hide"));

        var doc = new JsonObject
        {
            ["protocolVersion"] = "0.1",
            ["surfaceId"] = "activity",
            ["catalogId"] = "boomhud.runtime.basic.v1",
            ["revision"] = ++_revision,
            ["root"] = new JsonObject
            {
                ["id"] = "root",
                ["type"] = "container",
                ["layout"] = new JsonObject { ["type"] = "vertical" },
                ["children"] = children,
            },
        };

        return doc.Deserialize<RuntimeSurfaceDocument>(JsonOptions)
            ?? throw new InvalidOperationException("activity view document failed to deserialize.");
    }

    public void Dispatch(string action, string? componentId)
    {
        switch (action)
        {
            case "refresh":
                Changed?.Invoke();
                break;
            case "hide":
                _bus?.Publish(new HideViewMessage("activity"));
                break;
        }
    }

    // "HH:mm:ss  [kind]  Name  ·  actor  ·  category"
    private static string FormatEntry(ActivityEntry e)
    {
        var time = e.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        var actor = string.IsNullOrEmpty(e.Actor?.Id) ? e.Actor?.Kind ?? "?" : $"{e.Actor!.Kind}:{e.Actor.Id}";
        var category = string.IsNullOrWhiteSpace(e.Category) ? "" : $"  ·  {e.Category}";
        return $"{time}  [{KindTag(e.Kind)}]  {e.Name}  ·  {actor}{category}";
    }

    private static string? FormatPayloadDetails(ActivityEntry entry)
    {
        if (!string.Equals(entry.Category, "external-tool", StringComparison.Ordinal)
            && !string.Equals(entry.Name, "external-tool.inspect", StringComparison.Ordinal))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(entry.PayloadJson))
            return null;

        try
        {
            if (JsonNode.Parse(entry.PayloadJson) is not JsonObject payload)
                return null;

            var parts = new List<string>();
            AddPart(parts, "tool", ReadString(payload, "title"));
            AddPart(parts, "job", ReadString(payload, "jobId"));
            AddPart(parts, "body", ReadString(payload, "bodyName"));
            if (ReadInt(payload, "rowCount") is { } rowCount)
                parts.Add($"rows: {rowCount}");

            var sourcePath = ReadString(payload, "sourcePath");
            if (!string.IsNullOrWhiteSpace(sourcePath))
                parts.Add($"source: {CompactPath(sourcePath)}");

            return parts.Count == 0 ? null : string.Join("  ·  ", parts);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _entrySubscription?.Dispose();
    }
}
