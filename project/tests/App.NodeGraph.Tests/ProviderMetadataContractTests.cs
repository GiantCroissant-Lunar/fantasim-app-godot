using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.NodeGraph.Tests;

public sealed class ProviderMetadataContractTests
{
    [Fact]
    public void FunctionProviderKinds_HasExpectedConstantValues()
    {
        Assert.Equal("csharp", FunctionProviderKinds.CSharp);
        Assert.Equal("iii", FunctionProviderKinds.Iii);
        Assert.Equal("akka", FunctionProviderKinds.Akka);
        Assert.Equal("remote", FunctionProviderKinds.Remote);
        Assert.Equal("godot-import", FunctionProviderKinds.GodotImport);
    }

    [Fact]
    public void ExternalToolManifest_CarriesManifestLevelProviderMetadata()
    {
        var metadata = new FunctionProviderMetadata(
            ProviderKind: FunctionProviderKinds.Iii,
            ProviderId: "vplanet-worker",
            RuntimeRequirement: "python3:vplanet>=2.5",
            Determinism: "versioned",
            TrustLevel: "external-service");

        var manifest = new ExternalToolManifest(
            ToolId: "demo",
            ToolVersion: "1.0.0",
            Provider: "iii",
            License: null,
            SourceUrl: null,
            Functions: System.Array.Empty<ExternalToolFunctionManifest>(),
            ProviderMetadata: metadata);

        Assert.NotNull(manifest.ProviderMetadata);
        Assert.Equal(FunctionProviderKinds.Iii, manifest.ProviderMetadata.ProviderKind);
        Assert.Equal("vplanet-worker", manifest.ProviderMetadata.ProviderId);
        Assert.Equal("python3:vplanet>=2.5", manifest.ProviderMetadata.RuntimeRequirement);
        Assert.Equal("versioned", manifest.ProviderMetadata.Determinism);
        Assert.Equal("external-service", manifest.ProviderMetadata.TrustLevel);
    }

    [Fact]
    public void ExternalToolFunctionManifest_CarriesFunctionSpecificExecutionTraits()
    {
        var traits = new FunctionExecutionTraits(
            RequiresExternalProcess: true,
            RequiresNetwork: false,
            RequiresMainThread: false,
            IsDeterministic: false,
            SupportsCancellation: true,
            DefaultTimeoutSeconds: 300,
            CacheKeyShape: "sha256-input-bundle",
            ArtifactShape: "vplanet/run-result",
            CommitEligibility: "adapter-gated");

        var function = new ExternalToolFunctionManifest(
            FunctionId: "demo.run",
            Label: "Run",
            Category: "demo/science",
            Summary: "Run a demo scenario.",
            IsSideEffect: true,
            IsExpensive: true,
            Inputs: System.Array.Empty<ExternalToolPortManifest>(),
            Outputs: System.Array.Empty<ExternalToolPortManifest>(),
            ExecutionTraits: traits);

        Assert.NotNull(function.ExecutionTraits);
        Assert.True(function.ExecutionTraits.RequiresExternalProcess);
        Assert.False(function.ExecutionTraits.RequiresNetwork);
        Assert.False(function.ExecutionTraits.RequiresMainThread);
        Assert.False(function.ExecutionTraits.IsDeterministic);
        Assert.True(function.ExecutionTraits.SupportsCancellation);
        Assert.Equal(300, function.ExecutionTraits.DefaultTimeoutSeconds);
        Assert.Equal("sha256-input-bundle", function.ExecutionTraits.CacheKeyShape);
        Assert.Equal("vplanet/run-result", function.ExecutionTraits.ArtifactShape);
        Assert.Equal("adapter-gated", function.ExecutionTraits.CommitEligibility);
    }

    [Fact]
    public void VplanetExternalToolManifest_RemainsBuildable()
    {
        var manifest = VplanetExternalToolManifest.Build();

        Assert.NotNull(manifest);
        Assert.Equal("vplanet", manifest.ToolId);
        Assert.Equal("iii", manifest.Provider);
        Assert.Equal(manifest.Provider, manifest.ProviderMetadata?.ProviderKind);
        Assert.NotEmpty(manifest.Functions);
    }
}
