using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;
using FantaSim.App.Command.Services;
using FantaSim.App.Ui.Presentation;
using Xunit;

namespace FantaSim.App.Command.Tests;

/// <summary>
/// The command pipeline's real A2UI detail card (<see cref="CommandActivityDetail"/>) must always
/// normalize and render — this is the "real domain flow" dogfooding the published A2UI contract. These
/// tests push the builder's output through the SAME normalize→validate pipeline the runtime uses, so a
/// producer can never emit a card the renderer would reject.
/// </summary>
public sealed class CommandActivityDetailTests
{
    private static (JsonObject doc, RuntimeSurfaceValidationResult result) NormalizeAndValidate(string a2uiJson)
    {
        var node = A2uiPresentationNormalizer.Normalize(a2uiJson, "cmd");
        Assert.NotNull(node);

        var document = new JsonObject
        {
            ["protocolVersion"] = "0.1",
            ["surfaceId"] = "command-detail",
            ["catalogId"] = RuntimeSurfaceProtocol.BasicCatalogId,
            ["root"] = node!.DeepClone(),
        }.Deserialize<RuntimeSurfaceDocument>()!;

        var result = RuntimeSurfaceValidator.Validate(document, RuntimeSurfaceCatalog.Basic);
        return ((JsonObject)JsonNode.Parse(a2uiJson)!, result);
    }

    [Fact]
    public void SuccessResult_Validates_AndHasNoErrorPanel()
    {
        var json = CommandActivityDetail.BuildResultDetail(
            command: "world.refresh",
            descriptorTitle: "Refresh world",
            descriptorDescription: "Rebuilds the world from the current sources.",
            category: "world",
            actor: "user:godot",
            correlationId: "ab12cd34ef56aa00",
            causationId: null,
            ok: true,
            errorType: null,
            errorMessage: null);

        var (doc, result) = NormalizeAndValidate(json);
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(d => d.Message)));

        var components = doc["components"]!.AsObject();
        Assert.False(components.ContainsKey("err"), "success card must not carry an error panel");
        Assert.Equal("Rebuilds the world from the current sources.",
            components["hdr"]!["text"]!.GetValue<string>());
        Assert.Equal("by user:godot", components["m_actor"]!["text"]!.GetValue<string>());
        Assert.Equal("corr ab12cd34", components["lineage"]!["text"]!.GetValue<string>()); // short id, no cause
    }

    [Fact]
    public void FailureResult_Validates_AndIncludesErrorPanel()
    {
        var json = CommandActivityDetail.BuildResultDetail(
            command: "world.orchestrate",
            descriptorTitle: "Orchestrate world",
            descriptorDescription: "Delegates to the active orchestrator.",
            category: "orchestration",
            actor: "system",
            correlationId: "corrABCDEF",
            causationId: "causeZ12345",
            ok: false,
            errorType: "orchestration-failed",
            errorMessage: "Inner command 'world.refresh' failed.");

        var (doc, result) = NormalizeAndValidate(json);
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(d => d.Message)));

        var components = doc["components"]!.AsObject();
        Assert.True(components.ContainsKey("err"), "failure card must carry an error panel");
        Assert.Equal("panel", components["err"]!["type"]!.GetValue<string>());
        Assert.Equal("Error", components["err"]!["title"]!.GetValue<string>());

        var errText = components["e_line"]!["text"]!.GetValue<string>();
        Assert.Contains("orchestration-failed", errText);
        Assert.Contains("Inner command 'world.refresh' failed.", errText);

        // Lineage carries both correlation and causation (short ids).
        Assert.Equal("corr corrABCD · cause causeZ12", components["lineage"]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void NullDescriptor_FallsBackToCommandId_AndStillValidates()
    {
        var json = CommandActivityDetail.BuildResultDetail(
            command: "some.raw.command",
            descriptorTitle: null,
            descriptorDescription: null,
            category: null,
            actor: "",
            correlationId: "x",
            causationId: null,
            ok: true,
            errorType: null,
            errorMessage: null);

        var (doc, result) = NormalizeAndValidate(json);
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(d => d.Message)));

        var components = doc["components"]!.AsObject();
        Assert.Equal("some.raw.command", components["hdr"]!["text"]!.GetValue<string>()); // fell back to command id
        Assert.Equal("by system", components["m_actor"]!["text"]!.GetValue<string>());    // blank actor -> system
        Assert.False(components.ContainsKey("m_cat"), "no category -> no category badge");
    }

    [Fact]
    public void OverlongText_IsTruncated_AndStillValidates()
    {
        var longMessage = new string('x', 500);
        var json = CommandActivityDetail.BuildResultDetail(
            command: "c",
            descriptorTitle: null,
            descriptorDescription: new string('d', 500),
            category: null,
            actor: "sys",
            correlationId: "c",
            causationId: null,
            ok: false,
            errorType: "boom",
            errorMessage: longMessage);

        var (doc, result) = NormalizeAndValidate(json);
        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(d => d.Message)));

        var components = doc["components"]!.AsObject();
        Assert.True(components["hdr"]!["text"]!.GetValue<string>().Length <= 201);   // 200 + ellipsis
        Assert.True(components["e_line"]!["text"]!.GetValue<string>().Length <= 201);
    }
}
