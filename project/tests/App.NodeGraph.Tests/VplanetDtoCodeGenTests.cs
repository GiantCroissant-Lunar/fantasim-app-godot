using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using App.ExternalTools.CodeGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace FantaSim.App.NodeGraph.Tests;

public sealed class VplanetDtoCodeGenTests
{
    private static readonly string SchemaDirectory = Path.Combine(
        AppContext.BaseDirectory, "Schemas", "vplanet");

    private static string GenerateToTemp()
    {
        var outDir = Path.Combine(Path.GetTempPath(), $"fantasim-vplanet-dto-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outDir);
        new VplanetDtoGenerator().Generate(SchemaDirectory, outDir);
        return outDir;
    }

    [Fact]
    public void GeneratedCode_ContainsExpectedTypesAndJsonPropertyNames()
    {
        var outDir = GenerateToTemp();
        var filePath = Directory.EnumerateFiles(outDir, "*.cs").Single();
        var code = File.ReadAllText(filePath);

        try
        {
            Directory.Delete(outDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; temp is fine.
        }

        Assert.Contains("namespace FantaSim.App.NodeGraph.ExternalTools.Vplanet", code);
        Assert.Contains("public sealed class VplanetInputBuildRequest", code);
        Assert.Contains("public sealed class VplanetInputBuildResponse", code);
        Assert.Contains("public sealed class VplanetInputBundle", code);
        Assert.Contains("public sealed class VplanetRunRequest", code);
        Assert.Contains("public sealed class VplanetRunResponse", code);
        Assert.Contains("public sealed class VplanetRunResult", code);
        Assert.Contains("public sealed class VplanetOutputParseRequest", code);
        Assert.Contains("public sealed class VplanetOutputParseResponse", code);
        Assert.Contains("public sealed class VplanetOutputTable", code);
        Assert.Contains("public sealed class VplanetStatusResponse", code);
        Assert.Contains("public sealed class VplanetStatusResponseStatus", code);

        Assert.Contains("[JsonPropertyName(\"job_id\")]", code);
        Assert.Contains("public string JobId", code);
        Assert.Contains("[JsonPropertyName(\"systemName\")]", code);
        Assert.Contains("public string SystemName", code);
        Assert.Contains("[JsonPropertyName(\"stopTimeYears\")]", code);
        Assert.Contains("public double StopTimeYears", code);
        Assert.Contains("[JsonPropertyName(\"timeoutSeconds\")]", code);
        Assert.Contains("public int TimeoutSeconds", code);
        Assert.Contains("[JsonPropertyName(\"returnCode\")]", code);
        Assert.Contains("public int ReturnCode", code);
        Assert.Contains("[JsonPropertyName(\"fallback\")]", code);
        Assert.Contains("public bool Fallback", code);
        Assert.Contains("[JsonPropertyName(\"bodyPaths\")]", code);
        Assert.Contains("public System.Collections.Generic.Dictionary<string, string> BodyPaths", code);
        Assert.Contains("[JsonPropertyName(\"rows\")]", code);
        Assert.Contains("public System.Collections.Generic.List<System.Collections.Generic.List<System.Text.Json.JsonElement>> Rows", code);
        Assert.Contains("[JsonPropertyName(\"binPath\")]", code);
        Assert.Contains("public string? BinPath", code);
        Assert.Contains("[JsonPropertyName(\"version\")]", code);
        Assert.Contains("public string? Version", code);
    }

    [Fact]
    public void GeneratedCode_IsUtf8WithoutBom()
    {
        var outDir = GenerateToTemp();
        var filePath = Directory.EnumerateFiles(outDir, "*.cs").Single();
        var bytes = File.ReadAllBytes(filePath);

        try
        {
            Directory.Delete(outDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; temp is fine.
        }

        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "Generated source must be UTF-8 without BOM so ASCII-only generated output stays clean.");
    }

    [Fact]
    public void GeneratedCode_CompilesWithRoslyn()
    {
        var outDir = GenerateToTemp();
        var filePath = Directory.EnumerateFiles(outDir, "*.cs").Single();
        var code = File.ReadAllText(filePath);

        var syntaxTree = CSharpSyntaxTree.ParseText(code, path: filePath);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .Concat(new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonElement).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.Dictionary<,>).Assembly.Location),
            })
            .ToList();

        var compilation = CSharpCompilation.Create(
            $"VplanetDtoCompilation-{Guid.NewGuid():N}",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        try
        {
            Directory.Delete(outDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; temp is fine.
        }

        if (!result.Success)
        {
            var diagnostics = string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
            Assert.Fail($"Generated VPLanet DTO code failed to compile:{Environment.NewLine}{diagnostics}");
        }
    }

    [Fact]
    public void GeneratedCode_ResolvesTransitiveSchemaRefs()
    {
        var schemaDir = Path.Combine(Path.GetTempPath(), $"fantasim-vplanet-ref-schemas-{Guid.NewGuid():N}");
        var outDir = Path.Combine(Path.GetTempPath(), $"fantasim-vplanet-ref-dtos-{Guid.NewGuid():N}");
        Directory.CreateDirectory(schemaDir);
        Directory.CreateDirectory(outDir);

        File.WriteAllText(Path.Combine(schemaDir, "vplanet.ref-root.schema.json"), """
        {
          "title": "VPLanet ref root",
          "type": "object",
          "required": ["middle"],
          "properties": {
            "middle": { "$ref": "vplanet.ref-middle.schema.json" }
          }
        }
        """);
        File.WriteAllText(Path.Combine(schemaDir, "vplanet.ref-middle.schema.json"), """
        {
          "title": "VPLanet ref middle",
          "type": "object",
          "required": ["leaf"],
          "properties": {
            "leaf": { "$ref": "vplanet.ref-leaf.schema.json" }
          }
        }
        """);
        File.WriteAllText(Path.Combine(schemaDir, "vplanet.ref-leaf.schema.json"), """
        {
          "title": "VPLanet ref leaf",
          "type": "object",
          "required": ["value"],
          "properties": {
            "value": { "type": "string" }
          }
        }
        """);

        var result = new VplanetDtoGenerator().Generate(schemaDir, outDir);

        try
        {
            Assert.Contains("public sealed class VplanetRefRoot", result.Source);
            Assert.Contains("public VplanetRefMiddle Middle { get; set; } = new();", result.Source);
            Assert.Contains("public sealed class VplanetRefMiddle", result.Source);
            Assert.Contains("public VplanetRefLeaf Leaf { get; set; } = new();", result.Source);
            Assert.Contains("public sealed class VplanetRefLeaf", result.Source);
            Assert.Contains("public string Value { get; set; } = string.Empty;", result.Source);
        }
        finally
        {
            TryDelete(schemaDir);
            TryDelete(outDir);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; temp is fine.
        }
    }
}
