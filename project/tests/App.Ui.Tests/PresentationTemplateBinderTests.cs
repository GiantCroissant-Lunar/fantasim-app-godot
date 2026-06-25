using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using FantaSim.App.Ui.Presentation;
using Xunit;

namespace FantaSim.App.Ui.Tests;

public sealed class PresentationTemplateBinderTests
{
    private static readonly string DomainNeutralTemplate =
        """
        {
          "protocolVersion": "0.1",
          "surfaceId": "demo",
          "catalogId": "boomhud.runtime.basic.v1",
          "root": {
            "id": "root",
            "type": "container",
            "layout": { "type": "vertical" },
            "children": [
              { "id": "title", "type": "label", "properties": { "text": { "literal": "${title}" } } },
              { "id": "rows", "type": "slot", "slot": "rows" },
              { "id": "footer", "type": "label", "properties": { "text": { "literal": "${footer}" } } }
            ]
          },
          "demoTemplates": {
            "row": { "id": "row-${row.id}", "type": "label", "properties": { "text": { "literal": "${row.text}" } } }
          }
        }
        """;

    [Fact]
    public void Bind_StampsRevisionAndStripsExtensionPropertiesAndDeserializes()
    {
        var slots = new Dictionary<string, JsonArray>(StringComparer.Ordinal)
        {
            ["rows"] = new JsonArray(
                PresentationTemplateBinder.CloneTemplate(
                    JsonNode.Parse("""{"row": {"id": "row-1", "type": "label", "properties": {"text": {"literal": "hello"}}}}""")!.AsObject(),
                    "row",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["row.id"] = "1", ["row.text"] = "hello" })),
        };

        var binding = new PresentationTemplateBinding(
            Placeholders: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["title"] = "Demo Title",
                ["footer"] = "Demo Footer",
            },
            Slots: slots,
            RemoveTopLevelProperties: new HashSet<string>(StringComparer.Ordinal) { "demoTemplates" });

        var document = PresentationTemplateBinder.Bind(DomainNeutralTemplate, binding, revision: 7);

        Assert.Equal("demo", document.SurfaceId);
        Assert.Equal("boomhud.runtime.basic.v1", document.CatalogId);
        Assert.Equal(7, document.Revision);

        var title = document.Root.Children[0];
        Assert.Equal("Demo Title", title.Properties["text"].Literal!.GetValue<string>());

        var row = document.Root.Children[1];
        Assert.Equal("row-1", row.Id);
        Assert.Equal("hello", row.Properties["text"].Literal!.GetValue<string>());

        var footer = document.Root.Children[2];
        Assert.Equal("Demo Footer", footer.Properties["text"].Literal!.GetValue<string>());
    }

    [Fact]
    public void Bind_DoesNotConsumeCallerSlotArray()
    {
        var callerRow = new JsonObject
        {
            ["id"] = "row-99",
            ["type"] = "label",
            ["properties"] = new JsonObject { ["text"] = new JsonObject { ["literal"] = "owned" } },
        };
        var callerArray = new JsonArray(callerRow);

        var binding = new PresentationTemplateBinding(
            Placeholders: null,
            Slots: new Dictionary<string, JsonArray>(StringComparer.Ordinal) { ["rows"] = callerArray },
            RemoveTopLevelProperties: new HashSet<string>(StringComparer.Ordinal) { "demoTemplates" });

        var document = PresentationTemplateBinder.Bind(DomainNeutralTemplate, binding, revision: 1);

        Assert.Single(callerArray);
        Assert.Same(callerRow, callerArray[0]);

        var row = document.Root.Children[1];
        Assert.Equal("row-99", row.Id);
    }

    [Fact]
    public void Bind_ReplacesPlaceholdersInNestedObjects()
    {
        var template =
            """
            {
              "surfaceId": "nested",
              "catalogId": "boomhud.runtime.basic.v1",
              "root": {
                "id": "root",
                "type": "container",
                "children": [
                  {
                    "id": "a",
                    "type": "label",
                    "properties": {
                      "text": { "literal": "${a}" },
                      "hint": { "literal": "${b}" }
                    }
                  }
                ]
              }
            }
            """;

        var binding = new PresentationTemplateBinding(
            Placeholders: new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "A-val", ["b"] = "B-val" });

        var document = PresentationTemplateBinder.Bind(template, binding, revision: 3);

        var child = document.Root.Children[0];
        Assert.Equal("A-val", child.Properties["text"].Literal!.GetValue<string>());
        Assert.Equal("B-val", child.Properties["hint"].Literal!.GetValue<string>());
    }

    [Fact]
    public void Bind_ThrowsForEmptyJson()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => PresentationTemplateBinder.Bind("   ", new PresentationTemplateBinding(), 1));
        Assert.Contains("empty", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_ThrowsForMalformedJson()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => PresentationTemplateBinder.Bind("{ not json", new PresentationTemplateBinding(), 1));
        Assert.Contains("malformed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_ThrowsForNonObjectRoot()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => PresentationTemplateBinder.Bind("[1,2,3]", new PresentationTemplateBinding(), 1));
        Assert.Contains("object", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CloneTemplate_ThrowsForMissingNamedTemplate()
    {
        var templates = JsonNode.Parse("""{"present": {}}""")!.AsObject();
        var ex = Assert.Throws<InvalidOperationException>(
            () => PresentationTemplateBinder.CloneTemplate(templates, "absent", new Dictionary<string, string>()));
        Assert.Contains("absent", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CloneTemplate_DeepClonesAndSubstitutesPlaceholders()
    {
        var templates = JsonNode.Parse(
            """{"row": {"id": "row-${row.id}", "type": "label", "properties": {"text": {"literal": "${row.text}"}}}}""")!.AsObject();
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["row.id"] = "42",
            ["row.text"] = "hello",
        };

        var clone = PresentationTemplateBinder.CloneTemplate(templates, "row", values);
        var obj = clone.AsObject();

        Assert.Equal("row-42", obj["id"]!.GetValue<string>());
        Assert.Equal("hello", obj["properties"]!["text"]!["literal"]!.GetValue<string>());

        Assert.NotSame(clone, templates["row"]);
        Assert.Equal("row-${row.id}", templates["row"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void CloneTemplate_ReplacesPlaceholdersInNestedArrays()
    {
        var templates = JsonNode.Parse(
            """{"row": {"id": "row", "type": "label", "metadata": ["${row.kind}", "static"]}}""")!.AsObject();
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["row.kind"] = "activity",
        };

        var clone = PresentationTemplateBinder.CloneTemplate(templates, "row", values);
        var metadata = clone["metadata"]!.AsArray();

        Assert.Equal("activity", metadata[0]!.GetValue<string>());
        Assert.Equal("static", metadata[1]!.GetValue<string>());
        Assert.Equal("${row.kind}", templates["row"]!["metadata"]![0]!.GetValue<string>());
    }
}
