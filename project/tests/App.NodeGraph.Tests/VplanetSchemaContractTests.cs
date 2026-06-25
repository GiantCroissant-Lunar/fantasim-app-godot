using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.NodeGraph.Tests;

public sealed class VplanetSchemaContractTests
{
    private static readonly string SchemaDirectory = Path.Combine(
        AppContext.BaseDirectory, "Schemas", "vplanet");

    private static readonly string[] ExpectedSchemaFiles =
    {
        "vplanet.status.response.schema.json",
        "vplanet.input-build.request.schema.json",
        "vplanet.input-build.response.schema.json",
        "vplanet.run.request.schema.json",
        "vplanet.run.response.schema.json",
        "vplanet.output-parse.request.schema.json",
        "vplanet.output-parse.response.schema.json",
        "vplanet.output-table.schema.json",
        "vplanet.input-bundle.schema.json",
        "vplanet.run-result.schema.json",
    };

    public static IEnumerable<object[]> SchemaFileNames() =>
        ExpectedSchemaFiles.Select(name => new object[] { name });

    private static JsonNode LoadSchema(string fileName)
    {
        var path = Path.Combine(SchemaDirectory, fileName);
        var json = File.ReadAllText(path);
        return JsonNode.Parse(json)!;
    }

    [Theory]
    [MemberData(nameof(SchemaFileNames))]
    public void SchemaFile_IsParseable_AndHasMetadata(string fileName)
    {
        var schema = LoadSchema(fileName);

        Assert.NotNull(schema["$schema"]);
        Assert.Contains("json-schema.org/draft/2020-12", schema["$schema"]!.GetValue<string>());

        Assert.NotNull(schema["$id"]);
        Assert.StartsWith("https://schemas.fantasim.local/external-tools/vplanet/", schema["$id"]!.GetValue<string>());

        Assert.NotNull(schema["title"]);
        Assert.False(string.IsNullOrWhiteSpace(schema["title"]!.GetValue<string>()));

        Assert.NotNull(schema["type"]);
        Assert.Equal("object", schema["type"]!.GetValue<string>());
    }

    [Fact]
    public void InputBuildRequest_PropertiesMatchManifestParameterKeys()
    {
        var schema = LoadSchema("vplanet.input-build.request.schema.json");
        var manifest = VplanetExternalToolManifest.Build();
        var function = manifest.Functions.Single(f => f.FunctionId == "vplanet.input.build");

        var expectedKeys = function.Parameters!.Select(p => p.Key).OrderBy(k => k).ToArray();
        var actualKeys = schema["properties"]!
            .AsObject()
            .Select(p => p.Key)
            .Where(k => k != "job_id")
            .OrderBy(k => k)
            .ToArray();

        Assert.Equal(expectedKeys, actualKeys);
        Assert.Contains("job_id", schema["properties"]!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public void RunRequest_PropertiesMatchManifestInputsAndParameters()
    {
        var schema = LoadSchema("vplanet.run.request.schema.json");
        var manifest = VplanetExternalToolManifest.Build();
        var function = manifest.Functions.Single(f => f.FunctionId == "vplanet.run");

        var expectedKeys = function.Inputs
            .Select(p => p.PortId)
            .Concat(function.Parameters!.Select(p => p.Key))
            .OrderBy(k => k)
            .ToArray();

        var actualKeys = schema["properties"]!
            .AsObject()
            .Select(p => p.Key)
            .Where(k => k != "job_id")
            .OrderBy(k => k)
            .ToArray();

        Assert.Equal(expectedKeys, actualKeys);
        Assert.Contains("job_id", schema["properties"]!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public void OutputParseRequest_PropertiesMatchManifestInputsAndParameters()
    {
        var schema = LoadSchema("vplanet.output-parse.request.schema.json");
        var manifest = VplanetExternalToolManifest.Build();
        var function = manifest.Functions.Single(f => f.FunctionId == "vplanet.output.parse");

        var expectedKeys = function.Inputs
            .Select(p => p.PortId)
            .Concat(function.Parameters!.Select(p => p.Key))
            .OrderBy(k => k)
            .ToArray();

        var actualKeys = schema["properties"]!
            .AsObject()
            .Select(p => p.Key)
            .Where(k => k != "job_id")
            .OrderBy(k => k)
            .ToArray();

        Assert.Equal(expectedKeys, actualKeys);
        Assert.Contains("job_id", schema["properties"]!.AsObject().Select(p => p.Key));
    }

    [Theory]
    [InlineData("vplanet.status.response.schema.json", "status", "ok")]
    [InlineData("vplanet.input-build.response.schema.json", "inputBundle", "job_id")]
    [InlineData("vplanet.run.response.schema.json", "runResult", "job_id")]
    [InlineData("vplanet.output-parse.response.schema.json", "outputTable", "job_id")]
    public void ResponseSchema_HasExpectedRequiredTopLevelKeys(string fileName, string first, string second)
    {
        var schema = LoadSchema(fileName);
        var required = schema["required"]!
            .AsArray()
            .Select(node => node!.GetValue<string>())
            .OrderBy(k => k)
            .ToArray();

        Assert.Equal(new[] { first, second }.OrderBy(k => k).ToArray(), required);
    }

    [Fact]
    public void InputBundleSchema_HasExpectedRequiredKeys()
    {
        var schema = LoadSchema("vplanet.input-bundle.schema.json");
        var required = schema["required"]!
            .AsArray()
            .Select(node => node!.GetValue<string>())
            .OrderBy(k => k)
            .ToArray();

        Assert.Equal(
            new[] { "bodyPaths", "job_id", "manifestPath", "planetBodyName", "primaryPath", "rootPath", "starBodyName", "systemName" },
            required);
    }

    [Fact]
    public void RunResultSchema_HasExpectedRequiredKeys()
    {
        var schema = LoadSchema("vplanet.run-result.schema.json");
        var required = schema["required"]!
            .AsArray()
            .Select(node => node!.GetValue<string>())
            .OrderBy(k => k)
            .ToArray();

        Assert.Equal(
            new[] { "available", "fallback", "job_id", "outputPath", "returnCode", "rootPath", "stderrPath", "stdoutPath" },
            required);
    }

    [Fact]
    public void OutputTableSchema_HasExpectedRequiredKeys()
    {
        var schema = LoadSchema("vplanet.output-table.schema.json");
        var required = schema["required"]!
            .AsArray()
            .Select(node => node!.GetValue<string>())
            .OrderBy(k => k)
            .ToArray();

        Assert.Equal(
            new[] { "bodyName", "columns", "fallback", "rows", "sourcePath" },
            required);
    }
}
