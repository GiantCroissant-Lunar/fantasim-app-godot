using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;

namespace FantaSim.App.Ui.ExternalTools;

/// <summary>
/// BoomHUD-native presentation source for iii/external-tool results. This is presentation only:
/// accepted world values still need a world-side DTO/field/truth converter.
/// </summary>
public sealed class ExternalToolResultViewSource : IViewSource
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const int MaxPreviewRows = 25;

    private JsonObject _result;
    private long _revision;

    public ExternalToolResultViewSource(string viewId, string title, JsonObject result)
    {
        if (string.IsNullOrWhiteSpace(viewId))
            throw new ArgumentException("View id is required.", nameof(viewId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));

        ViewId = viewId;
        Title = title;
        _result = CloneObject(result ?? throw new ArgumentNullException(nameof(result)));
    }

    public string ViewId { get; }

    public string Title { get; }

    public event Action? Changed;

    public void UpdateResult(JsonObject result)
    {
        _result = CloneObject(result ?? throw new ArgumentNullException(nameof(result)));
        Changed?.Invoke();
    }

    public RuntimeSurfaceDocument BuildDocument()
    {
        var projection = BuildProjection(_result);
        var root = new JsonObject
        {
            ["protocolVersion"] = RuntimeSurfaceProtocol.CurrentVersion,
            ["surfaceId"] = ViewId,
            ["catalogId"] = RuntimeSurfaceProtocol.BasicCatalogId,
            ["revision"] = ++_revision,
            ["dataModel"] = new JsonObject { ["toolResult"] = projection.DeepClone() },
            ["root"] = BuildRoot(projection),
        };

        return root.Deserialize<RuntimeSurfaceDocument>(JsonOptions)
            ?? throw new InvalidOperationException("external tool result view document failed to deserialize.");
    }

    public void Dispatch(string action, string? componentId)
    {
        if (string.Equals(action, "refresh", StringComparison.Ordinal))
            Changed?.Invoke();
    }

    public JsonObject BuildActivityPayload()
    {
        var projection = BuildProjection(_result);
        var payload = new JsonObject
        {
            ["viewId"] = ViewId,
            ["title"] = Title,
            ["kind"] = ReadString(projection, "kind"),
            ["jobId"] = ReadString(projection, "jobId"),
        };

        if (projection.TryGetPropertyValue("table", out var tableNode) && tableNode is JsonObject table)
        {
            payload["bodyName"] = ReadString(table, "bodyName");
            payload["rowCount"] = ReadInt(table, "rowCount");
            payload["sourcePath"] = ReadString(table, "sourcePath");
        }

        return payload;
    }

    private JsonObject BuildProjection(JsonObject result)
    {
        var projection = new JsonObject
        {
            ["title"] = Title,
            ["jobId"] = ReadString(result, "job_id"),
            ["rawResult"] = result.DeepClone(),
            ["rawPreview"] = Truncate(result.ToJsonString(), 1200),
        };

        if (result.TryGetPropertyValue("outputTable", out var tableNode)
            && tableNode is JsonObject outputTable)
        {
            projection["kind"] = "table";
            projection["table"] = ProjectTable(outputTable);
        }
        else
        {
            projection["kind"] = "raw";
        }

        projection["inspectorSections"] = BuildInspectorSections(projection);
        return projection;
    }

    private static JsonObject ProjectTable(JsonObject outputTable)
    {
        var columns = ReadStringArray(outputTable, "columns");
        var rows = outputTable.TryGetPropertyValue("rows", out var rowsNode) && rowsNode is JsonArray rowArray
            ? rowArray
            : new JsonArray();

        var displayRows = new JsonArray();
        for (var i = 0; i < rows.Count && i < MaxPreviewRows; i++)
        {
            var display = rows[i] is JsonArray row
                ? string.Join(" | ", row.Select(FormatCell))
                : FormatCell(rows[i]);
            displayRows.Add(new JsonObject
            {
                ["index"] = i,
                ["display"] = display,
            });
        }

        return new JsonObject
        {
            ["bodyName"] = ReadString(outputTable, "bodyName"),
            ["fallback"] = ReadBool(outputTable, "fallback"),
            ["sourcePath"] = ReadString(outputTable, "sourcePath"),
            ["columns"] = BuildStringArray(columns),
            ["rows"] = rows.DeepClone(),
            ["rowCount"] = rows.Count,
            ["previewRowCount"] = displayRows.Count,
            ["omittedRowCount"] = Math.Max(0, rows.Count - displayRows.Count),
            ["headerLine"] = columns.Count > 0 ? string.Join(" | ", columns) : "(no columns)",
            ["displayRows"] = displayRows,
        };
    }

    private JsonObject BuildRoot(JsonObject projection)
    {
        var children = new JsonArray
        {
            Label("title", ReadString(projection, "title")),
            Panel("inspector", "Inspector", BuildInspectorChildren(projection)),
            Panel("summary", "Summary", BuildSummaryChildren(projection)),
        };

        if (projection.TryGetPropertyValue("table", out var tableNode) && tableNode is JsonObject table)
        {
            children.Add(Panel("table", "Output table", BuildTableChildren(table)));
        }

        children.Add(Panel("raw", "Raw result", BuildRawChildren()));

        return new JsonObject
        {
            ["id"] = "root",
            ["type"] = "container",
            ["layout"] = new JsonObject { ["type"] = "vertical", ["gap"] = 8 },
            ["children"] = children,
        };
    }

    private static JsonArray BuildInspectorSections(JsonObject projection)
    {
        var sections = new JsonArray
        {
            InspectorSection("identity", "Identity", new JsonArray
            {
                InspectorField("view", ReadString(projection, "title")),
                InspectorField("kind", ReadString(projection, "kind")),
                InspectorField("job", ReadString(projection, "jobId", "n/a")),
            }),
        };

        if (projection.TryGetPropertyValue("table", out var tableNode) && tableNode is JsonObject table)
        {
            sections.Add(InspectorSection("output-table", "Output table", new JsonArray
            {
                InspectorField("body", ReadString(table, "bodyName", "unknown")),
                InspectorField("rows", ReadInt(table, "rowCount").ToString(CultureInfo.InvariantCulture)),
                InspectorField("columns", ReadString(table, "headerLine")),
                InspectorField("fallback", ReadBool(table, "fallback").ToString(CultureInfo.InvariantCulture).ToLowerInvariant()),
            }));

            var sourcePath = ReadString(table, "sourcePath");
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                sections.Add(InspectorSection("provenance", "Provenance", new JsonArray
                {
                    InspectorField("source", CompactPath(sourcePath)),
                }));
            }
        }

        return sections;
    }

    private static JsonObject InspectorSection(string id, string title, JsonArray fields)
        => new()
        {
            ["id"] = id,
            ["title"] = title,
            ["fields"] = fields,
        };

    private static JsonObject InspectorField(string key, string value)
        => new()
        {
            ["key"] = key,
            ["value"] = value,
        };

    private static JsonArray BuildInspectorChildren(JsonObject projection)
    {
        var children = new JsonArray();
        if (!projection.TryGetPropertyValue("inspectorSections", out var sectionsNode) || sectionsNode is not JsonArray sections)
            return children;

        foreach (var sectionNode in sections.OfType<JsonObject>())
        {
            var sectionId = ReadString(sectionNode, "id", "section");
            var sectionChildren = new JsonArray
            {
                Label($"inspector-{sectionId}-title", ReadString(sectionNode, "title")),
            };
            if (sectionNode.TryGetPropertyValue("fields", out var fieldsNode) && fieldsNode is JsonArray fields)
            {
                foreach (var fieldNode in fields.OfType<JsonObject>())
                {
                    var key = ReadString(fieldNode, "key");
                    var value = ReadString(fieldNode, "value");
                    sectionChildren.Add(Label($"inspector-{sectionId}-{SafeId(key)}", $"{key}: {value}"));
                }
            }

            children.Add(Container($"inspector-{sectionId}", sectionChildren));
        }

        return children;
    }

    private static JsonArray BuildSummaryChildren(JsonObject projection)
    {
        var rows = new JsonArray
        {
            Label("summary-kind", $"kind: {ReadString(projection, "kind")}"),
        };

        var jobId = ReadString(projection, "jobId");
        if (!string.IsNullOrWhiteSpace(jobId))
            rows.Add(Label("summary-job", $"job: {jobId}"));

        if (projection.TryGetPropertyValue("table", out var tableNode) && tableNode is JsonObject table)
        {
            rows.Add(Label("summary-body", $"body: {ReadString(table, "bodyName", "unknown")}"));
            rows.Add(Label("summary-rows", $"rows: {ReadInt(table, "rowCount")}"));
            rows.Add(Label("summary-fallback", $"fallback: {ReadBool(table, "fallback").ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}"));
        }

        return rows;
    }

    private static JsonArray BuildTableChildren(JsonObject table)
    {
        var children = new JsonArray
        {
            Label("table-header", ReadString(table, "headerLine")),
        };

        if (table.TryGetPropertyValue("displayRows", out var rowsNode) && rowsNode is JsonArray rows)
        {
            foreach (var rowNode in rows.OfType<JsonObject>())
            {
                var index = ReadInt(rowNode, "index");
                children.Add(Label($"row-{index}", ReadString(rowNode, "display")));
            }
        }

        var omitted = ReadInt(table, "omittedRowCount");
        if (omitted > 0)
            children.Add(Label("table-omitted", $"{omitted} more rows omitted from preview"));

        var sourcePath = ReadString(table, "sourcePath");
        if (!string.IsNullOrWhiteSpace(sourcePath))
            children.Add(Label("table-source", $"source: {CompactPath(sourcePath)}"));

        return children;
    }

    private static JsonArray BuildRawChildren()
        => new()
        {
            Label("raw-preview", "raw payload retained in dataModel.toolResult.rawResult"),
        };

    private static string CompactPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 2)
            return normalized;

        return $".../{segments[^2]}/{segments[^1]}";
    }

    private static JsonObject Panel(string id, string title, JsonArray children)
        => new()
        {
            ["id"] = id,
            ["type"] = "panel",
            ["layout"] = new JsonObject { ["type"] = "vertical", ["gap"] = 4 },
            ["properties"] = new JsonObject { ["title"] = new JsonObject { ["literal"] = title } },
            ["children"] = children,
        };

    private static JsonObject Container(string id, JsonArray children)
        => new()
        {
            ["id"] = id,
            ["type"] = "container",
            ["layout"] = new JsonObject { ["type"] = "vertical", ["gap"] = 2 },
            ["children"] = children,
        };

    private static JsonObject Label(string id, string text)
        => new()
        {
            ["id"] = id,
            ["type"] = "label",
            ["properties"] = new JsonObject { ["text"] = new JsonObject { ["literal"] = text } },
        };

    private static string SafeId(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray();
        var id = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(id) ? "field" : id;
    }

    private static JsonArray BuildStringArray(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonArray array)
            return Array.Empty<string>();

        return array.Select(item => FormatCell(item)).ToArray();
    }

    private static string FormatCell(JsonNode? node)
    {
        if (node is null)
            return string.Empty;

        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var intValue))
                return intValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<long>(out var longValue))
                return longValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<double>(out var doubleValue))
                return doubleValue.ToString("G15", CultureInfo.InvariantCulture);
            if (value.TryGetValue<decimal>(out var decimalValue))
                return decimalValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<bool>(out var boolValue))
                return boolValue.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
            if (value.TryGetValue<string>(out var stringValue))
                return stringValue;
        }

        return node.ToJsonString();
    }

    private static string ReadString(JsonObject obj, string key, string fallback = "")
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null)
            return fallback;
        return FormatCell(node);
    }

    private static bool ReadBool(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue value)
            return false;
        if (value.TryGetValue<bool>(out var boolValue))
            return boolValue;
        if (value.TryGetValue<string>(out var text)
            && bool.TryParse(text, out var parsed))
            return parsed;
        return false;
    }

    private static int ReadInt(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is not JsonValue value)
            return 0;
        if (value.TryGetValue<int>(out var intValue))
            return intValue;
        if (value.TryGetValue<long>(out var longValue))
            return longValue > int.MaxValue ? int.MaxValue : (int)longValue;
        return 0;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";

    private static JsonObject CloneObject(JsonObject source)
        => source.DeepClone().AsObject();
}
