using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;
using FantaSim.App.Ui.Presentation;
using Xunit;

namespace FantaSim.App.Ui.Tests;

public sealed class A2uiPresentationNormalizerTests
{
    private const string SampleA2ui = """
    {
      "root": "detail",
      "components": {
        "detail": { "type": "container", "layout": "vertical", "children": ["title", "meta"] },
        "title":  { "type": "label", "text": "Reload bundle", "variant": "muted" },
        "meta":   { "type": "container", "layout": "horizontal", "children": ["b1", "b2"] },
        "b1":     { "type": "badge", "text": "resource", "variant": "neutral" },
        "b2":     { "type": "badge", "text": "ok", "variant": "success" }
      }
    }
    """;

    [Fact]
    public void Normalize_BuildsNestedTreeWithPrefixedIds()
    {
        var node = A2uiPresentationNormalizer.Normalize(SampleA2ui, "card9");

        Assert.NotNull(node);
        Assert.Equal("card9-detail", node!["id"]!.GetValue<string>());
        Assert.Equal("container", node["type"]!.GetValue<string>());
        Assert.Equal("vertical", node["layout"]!["type"]!.GetValue<string>());

        var children = node["children"]!.AsArray();
        Assert.Equal(2, children.Count);

        var title = children[0]!.AsObject();
        Assert.Equal("card9-title", title["id"]!.GetValue<string>());
        Assert.Equal("label", title["type"]!.GetValue<string>());
        Assert.Equal("Reload bundle", title["properties"]!["text"]!["literal"]!.GetValue<string>());
        Assert.Equal("muted", title["properties"]!["variant"]!["literal"]!.GetValue<string>());

        var meta = children[1]!.AsObject();
        Assert.Equal("horizontal", meta["layout"]!["type"]!.GetValue<string>());
        var badges = meta["children"]!.AsArray();
        Assert.Equal("card9-b1", badges[0]!["id"]!.GetValue<string>());
        Assert.Equal("success", badges[1]!["properties"]!["variant"]!["literal"]!.GetValue<string>());
    }

    [Fact]
    public void Normalize_OutputIsAValidRenderableSurface()
    {
        // Safety by construction: whatever the normalizer emits must pass the runtime-surface validator.
        var node = A2uiPresentationNormalizer.Normalize(SampleA2ui, "d");
        Assert.NotNull(node);

        var documentJson = new JsonObject
        {
            ["protocolVersion"] = "0.1",
            ["surfaceId"] = "test",
            ["catalogId"] = RuntimeSurfaceProtocol.BasicCatalogId,
            ["root"] = node!.DeepClone(),
        };
        var document = documentJson.Deserialize<RuntimeSurfaceDocument>()!;

        var result = RuntimeSurfaceValidator.Validate(document, RuntimeSurfaceCatalog.Basic);
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{}")]                                                             // no root/components
    [InlineData("""{"root":"x"}""")]                                              // no components
    [InlineData("""{"root":"x","components":{}}""")]                              // empty components
    [InlineData("""{"root":"x","components":{"y":{"type":"label"}}}""")]          // dangling root reference
    public void Normalize_ReturnsNullForInvalidInput(string input)
    {
        Assert.Null(A2uiPresentationNormalizer.Normalize(input, "p"));
    }

    [Fact]
    public void Normalize_ReturnsNullOnDanglingChildReference()
    {
        var json = """{ "root":"a", "components": { "a": {"type":"container","children":["missing"]} } }""";
        Assert.Null(A2uiPresentationNormalizer.Normalize(json, "p"));
    }

    [Fact]
    public void Normalize_ReturnsNullOnCycle()
    {
        var json = """
        { "root":"a", "components": {
            "a": {"type":"container","children":["b"]},
            "b": {"type":"container","children":["a"]} } }
        """;
        Assert.Null(A2uiPresentationNormalizer.Normalize(json, "p"));
    }

    [Fact]
    public void Normalize_ReturnsNullWhenComponentMissingType()
    {
        var json = """{ "root":"a", "components": { "a": {"text":"no type"} } }""";
        Assert.Null(A2uiPresentationNormalizer.Normalize(json, "p"));
    }

    [Fact]
    public void Normalize_PassesThroughActionsAndLayoutObject()
    {
        var json = """
        { "root":"btn", "components": {
            "btn": { "type":"button", "text":"Go", "layout": {"type":"horizontal","gap":8},
                     "actions":[{"event":"pressed","command":"do.it"}] } } }
        """;
        var node = A2uiPresentationNormalizer.Normalize(json, "p");

        Assert.NotNull(node);
        Assert.Equal(8, node!["layout"]!["gap"]!.GetValue<int>());
        Assert.Equal("do.it", node["actions"]![0]!["command"]!.GetValue<string>());
    }
}
