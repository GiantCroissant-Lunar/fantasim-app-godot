using System;
using System.Text.Json.Nodes;

namespace FantaSim.App.Command.Services;

/// <summary>
/// Builds the A2UI adjacency-list detail document that a command-result activity entry carries in its
/// <c>DetailDocumentJson</c>. This is the "real domain flow" for the agent-UI pilot: the command
/// pipeline emits its OWN structured detail card (context line + actor/category + lineage + an error
/// panel on failure) from real execution data, replacing the demo <c>activity.emit_detail</c> command.
///
/// The output conforms to <c>a2ui-surface.schema.json</c> — only catalog component types, only
/// catalog-allowed properties, and documented variants — so it always normalizes and renders
/// (asserted end-to-end by <c>CommandActivityDetailTests</c>). The builder produces plain JSON in the
/// A2UI shape; it does not depend on the normalizer/renderer.
/// </summary>
public static class CommandActivityDetail
{
    private const int MaxText = 200;
    private const int ShortIdLength = 8;

    /// <summary>Build the detail document for a command-result entry. Returns an A2UI JSON string.</summary>
    public static string BuildResultDetail(
        string command,
        string? descriptorTitle,
        string? descriptorDescription,
        string? category,
        string actor,
        string correlationId,
        string? causationId,
        bool ok,
        string? errorType,
        string? errorMessage)
    {
        var components = new JsonObject();
        var rootChildren = new JsonArray();

        // Context line: the human descriptor description (falls back to the title, then the command id).
        var heading = FirstNonBlank(descriptorDescription, descriptorTitle, command);
        components["hdr"] = Label(Truncate(heading), "muted");
        rootChildren.Add("hdr");

        // Meta row: who ran it, and the descriptor category.
        var metaChildren = new JsonArray();
        components["m_actor"] = Badge("by " + Blank(actor, "system"), "neutral");
        metaChildren.Add("m_actor");
        if (!string.IsNullOrWhiteSpace(category))
        {
            components["m_cat"] = Badge(Truncate(category!), "info");
            metaChildren.Add("m_cat");
        }

        components["meta"] = Container("horizontal", metaChildren);
        rootChildren.Add("meta");

        // Lineage: correlation (+ causation), compact and muted.
        var lineage = "corr " + Short(correlationId);
        if (!string.IsNullOrWhiteSpace(causationId))
            lineage += " · cause " + Short(causationId!);
        components["lineage"] = Label(lineage, "muted");
        rootChildren.Add("lineage");

        // Error panel: only on failure.
        if (!ok)
        {
            var errText = FirstNonBlank(Join(errorType, errorMessage), "The command failed.");
            components["e_line"] = Label(Truncate(errText), "danger");
            components["err"] = Panel("Error", "danger", new JsonArray { "e_line" });
            rootChildren.Add("err");
        }

        components["d"] = Container("vertical", rootChildren);

        return new JsonObject
        {
            ["root"] = "d",
            ["components"] = components,
        }.ToJsonString();
    }

    private static JsonObject Container(string layout, JsonArray childIds) => new()
    {
        ["type"] = "container",
        ["layout"] = layout,
        ["children"] = childIds,
    };

    private static JsonObject Panel(string title, string variant, JsonArray childIds) => new()
    {
        ["type"] = "panel",
        ["title"] = title,
        ["variant"] = variant,
        ["children"] = childIds,
    };

    private static JsonObject Label(string text, string variant) => new()
    {
        ["type"] = "label",
        ["text"] = text,
        ["variant"] = variant,
    };

    private static JsonObject Badge(string text, string variant) => new()
    {
        ["type"] = "badge",
        ["text"] = text,
        ["variant"] = variant,
    };

    private static string Short(string? id) =>
        string.IsNullOrWhiteSpace(id) ? "?" : (id!.Length <= ShortIdLength ? id : id[..ShortIdLength]);

    private static string Truncate(string s) =>
        s.Length <= MaxText ? s : s[..MaxText] + "…";

    private static string Join(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b ?? string.Empty;
        if (string.IsNullOrWhiteSpace(b)) return a!;
        return a + ": " + b;
    }

    private static string Blank(string? s, string fallback) =>
        string.IsNullOrWhiteSpace(s) ? fallback : s!;

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v!;
        }

        return string.Empty;
    }
}
