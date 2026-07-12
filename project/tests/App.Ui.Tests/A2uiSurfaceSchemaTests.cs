using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;
using FantaSim.App.Ui.Presentation;
using Xunit;

namespace FantaSim.App.Ui.Tests;

/// <summary>
/// Guards the published <c>a2ui-surface.schema.json</c> against drift from what actually renders.
/// There is no JSON-Schema engine available offline, so instead of evaluating the schema these tests
/// couple its declared vocabulary DIRECTLY to the live <see cref="RuntimeSurfaceCatalog"/> (the runtime
/// source of truth) and push every schema example through the real
/// <see cref="A2uiPresentationNormalizer"/> + <see cref="RuntimeSurfaceValidator"/> pipeline. If the
/// catalog gains/loses a type, property, or event and the schema is not updated in lockstep, one of these
/// fails.
/// </summary>
public sealed class A2uiSurfaceSchemaTests
{
    // Deliberately NOT emittable via the A2UI flat form: its essential `wires` data cannot be expressed,
    // so an emitted nodeGraph would be degenerate. Build node graphs through the canonical surface instead.
    private static readonly HashSet<string> ExcludedFromA2ui =
        new(new[] { "nodeGraph" }, System.StringComparer.OrdinalIgnoreCase);

    private static JsonObject Schema()
        => (JsonObject)JsonNode.Parse(A2uiSurfaceSchema.Json)!;

    private static JsonObject ComponentDef() => (JsonObject)Schema()["$defs"]!["component"]!;

    /// <summary>Maps each type branch's `if.type.const` to its `then.properties` object.</summary>
    private static Dictionary<string, JsonObject> ThenPropertiesByType()
    {
        var result = new Dictionary<string, JsonObject>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var branch in ComponentDef()["allOf"]!.AsArray())
        {
            var type = branch!["if"]!["properties"]!["type"]!["const"]!.GetValue<string>();
            result[type] = (JsonObject)branch["then"]!["properties"]!;
        }

        return result;
    }

    [Fact]
    public void Schema_DeclaresDraft2020_12()
    {
        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            Schema()["$schema"]!.GetValue<string>());
    }

    [Fact]
    public void Schema_TypeEnum_IsCatalogMinusExcluded()
    {
        var schemaTypes = new HashSet<string>(
            ComponentDef()["properties"]!["type"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()),
            System.StringComparer.OrdinalIgnoreCase);

        var expected = new HashSet<string>(
            RuntimeSurfaceCatalog.Basic.Components.Keys.Where(k => !ExcludedFromA2ui.Contains(k)),
            System.StringComparer.OrdinalIgnoreCase);

        Assert.True(
            schemaTypes.SetEquals(expected),
            $"Schema type enum drifted from catalog. Only in schema: [{string.Join(", ", schemaTypes.Except(expected))}]; " +
            $"only in catalog (excluding {string.Join(",", ExcludedFromA2ui)}): [{string.Join(", ", expected.Except(schemaTypes))}]");
    }

    [Fact]
    public void Schema_HasABranchForEveryDeclaredType()
    {
        var enumTypes = new HashSet<string>(
            ComponentDef()["properties"]!["type"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()),
            System.StringComparer.OrdinalIgnoreCase);

        Assert.True(
            enumTypes.SetEquals(ThenPropertiesByType().Keys),
            "Every enumerated type must have exactly one allOf/if-then property branch.");
    }

    [Fact]
    public void Schema_BaseComponent_AllowsStructuralKeys()
    {
        var baseProps = (JsonObject)ComponentDef()["properties"]!;
        Assert.True(baseProps.ContainsKey("type"));
        Assert.True(baseProps.ContainsKey("layout"));
        Assert.True(baseProps.ContainsKey("children"));
        Assert.False(ComponentDef()["unevaluatedProperties"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("container")]
    [InlineData("scroll")]
    [InlineData("panel")]
    [InlineData("label")]
    [InlineData("badge")]
    [InlineData("button")]
    [InlineData("progressBar")]
    [InlineData("list")]
    [InlineData("spacer")]
    public void Schema_PerType_FlatProperties_MatchCatalogEffectiveSet(string type)
    {
        var spec = RuntimeSurfaceCatalog.Basic.Components[type];
        // The validator allows a static property if it is a static Property OR a BindableProperty
        // (RuntimeSurfaceValidator: Contains(Properties) || Contains(BindableProperties)).
        var expected = new HashSet<string>(
            spec.Properties.Concat(spec.BindableProperties),
            System.StringComparer.OrdinalIgnoreCase);

        var schemaProps = new HashSet<string>(
            ThenPropertiesByType()[type].Select(kvp => kvp.Key).Where(k => k != "actions"),
            System.StringComparer.OrdinalIgnoreCase);

        Assert.True(
            schemaProps.SetEquals(expected),
            $"'{type}' property allow-list drifted from catalog. Only in schema: [{string.Join(", ", schemaProps.Except(expected))}]; " +
            $"only in catalog: [{string.Join(", ", expected.Except(schemaProps))}]");
    }

    [Theory]
    [InlineData("container")]
    [InlineData("scroll")]
    [InlineData("panel")]
    [InlineData("label")]
    [InlineData("badge")]
    [InlineData("button")]
    [InlineData("progressBar")]
    [InlineData("list")]
    [InlineData("spacer")]
    public void Schema_PerType_ActionEvents_MatchCatalog(string type)
    {
        var catalogEvents = RuntimeSurfaceCatalog.Basic.Components[type].Events;
        var then = ThenPropertiesByType()[type];

        if (!catalogEvents.Any())
        {
            Assert.False(then.ContainsKey("actions"), $"'{type}' has no catalog events but the schema allows `actions`.");
            return;
        }

        Assert.True(then.ContainsKey("actions"), $"'{type}' has catalog events {string.Join(",", catalogEvents)} but the schema forbids `actions`.");

        var refName = then["actions"]!["$ref"]!.GetValue<string>().Split('/').Last();
        var eventConst = Schema()["$defs"]![refName]!["items"]!["properties"]!["event"]!["const"]!.GetValue<string>();

        // Every A2UI-emittable event type in the catalog happens to expose a single event; assert the
        // schema pins exactly that one. (If a type ever gains multiple events, this branch must widen.)
        Assert.Equal(new HashSet<string>(catalogEvents, System.StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(new[] { eventConst }, System.StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Schema_Examples_NormalizeAndRenderCleanly()
    {
        var examples = Schema()["examples"]!.AsArray();
        Assert.NotEmpty(examples);

        var index = 0;
        foreach (var example in examples)
        {
            var json = example!.ToJsonString();
            var node = A2uiPresentationNormalizer.Normalize(json, $"ex{index}");
            Assert.True(node is not null, $"example[{index}] failed to normalize");

            var document = new JsonObject
            {
                ["protocolVersion"] = "0.1",
                ["surfaceId"] = $"schema-example-{index}",
                ["catalogId"] = RuntimeSurfaceProtocol.BasicCatalogId,
                ["root"] = node!.DeepClone(),
            }.Deserialize<RuntimeSurfaceDocument>()!;

            var result = RuntimeSurfaceValidator.Validate(document, RuntimeSurfaceCatalog.Basic);
            Assert.True(result.IsValid,
                $"example[{index}] did not validate: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");
            index++;
        }
    }

    [Fact]
    public void Validator_RejectsPropertyThatSchemaAlsoForbidsPerType()
    {
        // `text` is normalizer-mappable, but the catalog does NOT allow it on `container` — and neither does
        // the schema (container's branch omits it; unevaluatedProperties:false rejects it). This anchors the
        // schema's per-type strictness to a REAL validator rejection, not just an authored assumption.
        var json = """{ "root":"a", "components": { "a": {"type":"container","text":"nope"} } }""";
        var node = A2uiPresentationNormalizer.Normalize(json, "p");
        Assert.NotNull(node); // normalizer is permissive; it maps `text` through...

        var document = new JsonObject
        {
            ["protocolVersion"] = "0.1",
            ["surfaceId"] = "reject",
            ["catalogId"] = RuntimeSurfaceProtocol.BasicCatalogId,
            ["root"] = node!.DeepClone(),
        }.Deserialize<RuntimeSurfaceDocument>()!;

        var result = RuntimeSurfaceValidator.Validate(document, RuntimeSurfaceCatalog.Basic);
        Assert.False(result.IsValid, "...but the validator must reject `text` on a container.");
    }
}
