using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FantaSim.App.NodeGraph;
using FantaSim.App.World;
using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class WorldGenerationNodeSchemaMetadataTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void VplanetManifest_DeclaresIiiProviderMetadata()
    {
        var manifest = VplanetExternalToolManifest.Build();
        Assert.NotNull(manifest.ProviderMetadata);
        Assert.Equal(FunctionProviderKinds.Iii, manifest.ProviderMetadata.ProviderKind);
        Assert.Equal("vplanet-worker", manifest.ProviderMetadata.ProviderId);
    }

    [Fact]
    public void ExternalToolNodeSchemaProjector_PropagatesProviderMetadataAndExecutionTraits()
    {
        var manifest = VplanetExternalToolManifest.Build();
        var schemas = ExternalToolNodeSchemaProjector.Project(manifest);

        var runSchema = schemas.Single(s => s.TypeId == "vplanet.run");
        Assert.NotNull(runSchema.ProviderMetadata);
        Assert.Equal(FunctionProviderKinds.Iii, runSchema.ProviderMetadata.ProviderKind);

        Assert.NotNull(runSchema.ExecutionTraits);
        Assert.True(runSchema.ExecutionTraits.RequiresExternalProcess);
        Assert.True(runSchema.ExecutionTraits.SupportsCancellation);
        Assert.Equal(300, runSchema.ExecutionTraits.DefaultTimeoutSeconds);
        Assert.Equal("vplanet/run-result", runSchema.ExecutionTraits.ArtifactShape);
        Assert.Equal("adapter-gated", runSchema.ExecutionTraits.CommitEligibility);
    }

    [Fact]
    public void WorldGenerationNodeCatalog_WorldNativeSchemasDeclareCSharpProviderMetadata()
    {
        var catalog = WorldGenerationNodeCatalog.All;
        var nativeTypeIds = new[]
        {
            WorldFunctionProvider.WorldOptions,
            WorldFunctionProvider.BodyFormation,
            WorldFunctionProvider.LayerScope,
            WorldFunctionProvider.CrustGenerate,
        };
        var nativeSchemas = catalog.Where(s => nativeTypeIds.Contains(s.TypeId)).ToList();

        Assert.Equal(nativeTypeIds.Length, nativeSchemas.Count);
        Assert.NotEmpty(nativeSchemas);
        Assert.All(nativeSchemas, schema =>
        {
            Assert.NotNull(schema.ProviderMetadata);
            Assert.Equal(FunctionProviderKinds.CSharp, schema.ProviderMetadata.ProviderKind);
            Assert.NotNull(schema.ExecutionTraits);
            Assert.False(schema.ExecutionTraits.RequiresExternalProcess);
            Assert.False(schema.ExecutionTraits.RequiresNetwork);
            Assert.True(schema.ExecutionTraits.IsDeterministic);
        });
    }

    [Fact]
    public void ComfyManifest_DeclaresIiiProviderMetadataAndComfyGenerateFunction()
    {
        var manifest = ComfyExternalToolManifest.Build();
        Assert.NotNull(manifest.ProviderMetadata);
        Assert.Equal(FunctionProviderKinds.Iii, manifest.ProviderMetadata.ProviderKind);
        Assert.Equal("comfy-worker", manifest.ProviderMetadata.ProviderId);

        var generate = Assert.Single(manifest.Functions);
        Assert.Equal("comfy.generate", generate.FunctionId);
        Assert.True(generate.IsSideEffect);
        Assert.True(generate.IsExpensive);
        Assert.NotNull(generate.ExecutionTraits);
        Assert.True(generate.ExecutionTraits.RequiresExternalProcess);
        Assert.True(generate.ExecutionTraits.RequiresNetwork);
        Assert.Equal("comfy/mesh", generate.ExecutionTraits.ArtifactShape);
    }

    [Fact]
    public void BlenderManifest_DeclaresIiiProviderMetadataAndRefineAndToGltfFunctions()
    {
        var manifest = BlenderExternalToolManifest.Build();
        Assert.NotNull(manifest.ProviderMetadata);
        Assert.Equal(FunctionProviderKinds.Iii, manifest.ProviderMetadata.ProviderKind);
        Assert.Equal("blender-worker", manifest.ProviderMetadata.ProviderId);

        Assert.Equal(2, manifest.Functions.Count);
        var refine = Assert.Single(manifest.Functions, f => f.FunctionId == "blender.refine");
        Assert.True(refine.IsSideEffect);
        Assert.True(refine.IsExpensive);
        Assert.NotNull(refine.ExecutionTraits);
        Assert.True(refine.ExecutionTraits.RequiresExternalProcess);
        Assert.Equal("blender/usd", refine.ExecutionTraits.ArtifactShape);

        var toGltf = Assert.Single(manifest.Functions, f => f.FunctionId == "asset.to_gltf");
        Assert.NotNull(toGltf.ExecutionTraits);
        Assert.Equal("asset/gltf", toGltf.ExecutionTraits.ArtifactShape);
    }

    [Fact]
    public void WorldGenerationNodeCatalog_IncludesComfyAndBlenderProjectedSchemas()
    {
        var catalog = WorldGenerationNodeCatalog.All;
        var comfy = Assert.Single(catalog, s => s.TypeId == "comfy.generate");
        Assert.Equal(FunctionProviderKinds.Iii, comfy.ProviderMetadata.ProviderKind);
        Assert.Equal("external/imagine", comfy.Category);

        var refine = Assert.Single(catalog, s => s.TypeId == "blender.refine");
        Assert.Equal(FunctionProviderKinds.Iii, refine.ProviderMetadata.ProviderKind);
        Assert.Equal("external/geometry", refine.Category);

        var toGltf = Assert.Single(catalog, s => s.TypeId == "asset.to_gltf");
        Assert.Equal(FunctionProviderKinds.Iii, toGltf.ProviderMetadata.ProviderKind);
        Assert.Equal("external/geometry", toGltf.Category);
    }

    [Fact]
    public void WorldGenerationNodeSchema_RoundTripsProviderMetadataWithCamelCaseJson()
    {
        var schema = new WorldGenerationNodeSchema(
            TypeId: "test.schema",
            Label: "Test Schema",
            Category: "test",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: System.Array.Empty<WorldGenerationGraphPort>(),
            Outputs: System.Array.Empty<WorldGenerationGraphPort>(),
            Summary: "Testing serialization",
            ProviderMetadata: new FunctionProviderMetadata(FunctionProviderKinds.CSharp),
            ExecutionTraits: new FunctionExecutionTraits(RequiresExternalProcess: false, IsDeterministic: true));

        var json = JsonSerializer.Serialize(schema, JsonOptions);

        Assert.Contains("\"providerMetadata\"", json);
        Assert.Contains("\"executionTraits\"", json);
        Assert.Contains("\"providerKind\"", json);

        var deserialized = JsonSerializer.Deserialize<WorldGenerationNodeSchema>(json, JsonOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(schema.TypeId, deserialized.TypeId);
        Assert.NotNull(deserialized.ProviderMetadata);
        Assert.Equal(FunctionProviderKinds.CSharp, deserialized.ProviderMetadata.ProviderKind);
        Assert.NotNull(deserialized.ExecutionTraits);
        Assert.False(deserialized.ExecutionTraits.RequiresExternalProcess);
        Assert.True(deserialized.ExecutionTraits.IsDeterministic);
    }
}
